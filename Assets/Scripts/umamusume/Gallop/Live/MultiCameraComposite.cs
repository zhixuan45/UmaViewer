using System;
using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// 全新自研多机位分屏合成渲染组件 (MultiCameraComposite)
    /// 完全遵循 Clean-room 自主研发规范，负责接收 Live 时间轴多机位图层分割线、羽化、遮罩旋转与位移参数，
    /// 通过带符号距离场 (SDF) 算法与 RenderTexture 双缓冲管线完成左右分屏、倾斜分屏与平滑淡入淡出合成渲染。
    /// </summary>
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    public class MultiCameraComposite : MonoBehaviour
    {
        /// <summary>
        /// 分割线渲染类型枚举
        /// </summary>
        public enum DivideLineType
        {
            /// <summary>
            /// 0：渐变过渡/羽化融合模式
            /// </summary>
            Fade = 0,

            /// <summary>
            /// 1：纯色高亮分割线模式
            /// </summary>
            Color = 1
        }

        #region 分屏渲染核心参数
        [Header("多机位图层配置")]
        [Tooltip("当前受控的多机位通道编号")]
        [SerializeField] private int _multiCameraNo = 0;

        [Tooltip("分割线绘制类型（Fade 渐变融合 或 Color 纯色分割线）")]
        [SerializeField] private DivideLineType _lineType = DivideLineType.Color;

        [Tooltip("分割线物理厚度/宽度（基于屏幕归一化坐标系）")]
        [Range(0f, 0.2f)]
        [SerializeField] private float _lineThickness = 0.015f;

        [Tooltip("分割线颜色（带透明度）")]
        [SerializeField] private Color _lineColor = Color.white;

        [Tooltip("分屏图层淡入淡出权重 (0: 完全透出主画面, 1: 完整分屏呈现)")]
        [Range(0f, 1f)]
        [SerializeField] private float _fadeValue = 1f;

        [Tooltip("分屏遮罩与 UV 变换参数 (X: OffsetX, Y: OffsetY, Z: RollDeg, W: 扩展参数)")]
        [SerializeField] private Vector4 _transformParameter = Vector4.zero;

        [Tooltip("分屏分割线旋转欧拉角 (度)")]
        [SerializeField] private float _maskRoll = 0f;

        [Tooltip("分屏视图在空间上的最小坐标偏移范围")]
        [SerializeField] private Vector3 _offsetMinPosition = Vector3.zero;

        [Tooltip("分屏视图在空间上的最大坐标偏移范围")]
        [SerializeField] private Vector3 _offsetMaxPosition = Vector3.zero;

        [Tooltip("是否启用分屏模式（false 表示不分屏，次机位全屏覆盖并混合）")]
        [SerializeField] private bool _isScreenDivide = true;

        [Tooltip("分割线边缘羽化/抗锯齿平滑带宽度")]
        [SerializeField] private float _lineAntialiasing = 0.002f;

        [Tooltip("分屏合成器当前是否处于激活合成状态")]
        [SerializeField] private bool _isCompositeActive = false;
        #endregion

        #region 相机与离屏纹理引用
        [Header("多机位输入源")]
        [Tooltip("主相机画面输入 RenderTexture")]
        [SerializeField] private RenderTexture _mainTexture;

        [Tooltip("次机位画面输入 RenderTexture")]
        [SerializeField] private RenderTexture _subTexture;

        [Tooltip("关联的主机位相机组件")]
        [SerializeField] private Camera _cameraMain;

        [Tooltip("关联的分屏机位相机组件")]
        [SerializeField] private Camera _cameraSub;
        #endregion

        #region 内部缓存与后处理材质
        private Material _compositeMaterial;
        private RenderTexture[] _doubleBufferRT = new RenderTexture[2];
        private int _currentBufferIndex = 0;

        // Shader 属性 ID 静态预热，消灭热循环中的字符串哈希损耗
        private static readonly int PropMainTex = Shader.PropertyToID("_MainTex");
        private static readonly int PropSubTex = Shader.PropertyToID("_SubTex");
        private static readonly int PropDivideLineColor = Shader.PropertyToID("_DivideLineColor");
        private static readonly int PropDivideLineParam = Shader.PropertyToID("_DivideLineParam");
        private static readonly int PropDivideLineSetting = Shader.PropertyToID("_DivideLineSetting");

        private const string ShaderName = "Hidden/UmaViewer/MultiCameraComposite";
        #endregion

        #region 公开属性访问器
        /// <summary>
        /// 是否启用分屏模式（false 表示不分屏，次机位全屏覆盖并混合）
        /// </summary>
        public bool IsScreenDivide
        {
            get => _isScreenDivide;
            set => _isScreenDivide = value;
        }

        /// <summary>
        /// 当前分屏/次机位所关联的 Camera 组件
        /// </summary>
        public Camera RenderCamera => _cameraSub;

        /// <summary>
        /// 分割线边缘羽化/抗锯齿平滑带宽度
        /// </summary>
        public float LineAntialiasing
        {
            get => _lineAntialiasing;
            set => _lineAntialiasing = value;
        }

        public int MultiCameraNo
        {
            get => _multiCameraNo;
            set => _multiCameraNo = value;
        }

        public DivideLineType LineType
        {
            get => _lineType;
            set => _lineType = value;
        }

        public float LineThickness
        {
            get => _lineThickness;
            set => _lineThickness = Mathf.Max(0f, value);
        }

        public Color LineColor
        {
            get => _lineColor;
            set => _lineColor = value;
        }

        public float FadeValue
        {
            get => _fadeValue;
            set => _fadeValue = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 由多机位切换器（关键帧 fadeTime 淡变）下发权重。
        ///
        /// 为什么不直接写 FadeValue：激活判定 _isCompositeActive 只在
        /// UpdateLayerParameters / SetCameraTextures 里重算，而多机位图层轨道每帧都会先跑一遍
        /// UpdateLayerParameters，按图层自己的 fadeValue 把 _isCompositeActive 复位（通常为 0）。
        /// 于是即使事后把 FadeValue 写成 0.9，渲染守卫
        /// `if (!_isCompositeActive || _fadeValue &lt;= 0.001f) return;` 仍会直接退出，
        /// 画面上完全看不出区别 —— 这正是"切换淡变补了却零效果"的原因。
        /// 因此切换器必须走这个入口：权重与激活判定一起更新。
        /// </summary>
        /// <param name="fadeValue">淡变权重 0~1</param>
        public void ApplySwitcherFade(float fadeValue)
        {
            _fadeValue = Mathf.Clamp01(fadeValue);

            bool active = _subTexture != null && _fadeValue > 0.001f;
            _isCompositeActive = active;
            SetCamerasEnabled(active);
        }

        public Vector4 TransformParameter
        {
            get => _transformParameter;
            set => _transformParameter = value;
        }

        public float MaskRoll
        {
            get => _maskRoll;
            set => _maskRoll = value;
        }

        public Vector3 OffsetMinPosition
        {
            get => _offsetMinPosition;
            set => _offsetMinPosition = value;
        }

        public Vector3 OffsetMaxPosition
        {
            get => _offsetMaxPosition;
            set => _offsetMaxPosition = value;
        }

        public bool IsCompositeActive
        {
            get => _isCompositeActive;
            set => _isCompositeActive = value;
        }

        public RenderTexture MainTexture
        {
            get => _mainTexture;
            set => _mainTexture = value;
        }

        public RenderTexture SubTexture
        {
            get => _subTexture;
            set => _subTexture = value;
        }

        public Camera CameraMain
        {
            get => _cameraMain;
            set => _cameraMain = value;
        }

        public Camera CameraSub
        {
            get => _cameraSub;
            set => _cameraSub = value;
        }
        #endregion

        #region 生命周期
        private void Awake()
        {
            EnsureMaterial();
        }

        private void OnEnable()
        {
            EnsureMaterial();
        }

        private void OnDisable()
        {
            ReleaseDoubleBuffers();
        }

        private void OnDestroy()
        {
            ReleaseDoubleBuffers();
            if (_compositeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_compositeMaterial);
                }
                else
                {
                    DestroyImmediate(_compositeMaterial);
                }
                _compositeMaterial = null;
            }
        }
        #endregion

        #region 材质与双缓冲管理
        /// <summary>
        /// 确保屏幕后处理合成材质已正确初始化
        /// </summary>
        private bool EnsureMaterial()
        {
            if (_compositeMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                // 容错搜索备用 Shader
                shader = Shader.Find("Hidden/MultiCameraComposite");
            }

            if (shader != null && shader.isSupported)
            {
                _compositeMaterial = new Material(shader)
                {
                    name = "Mtl_MultiCameraComposite_Runtime",
                    hideFlags = HideFlags.DontSave
                };
                return true;
            }

            return false;
        }

        /// <summary>
        /// 确保双缓冲 RenderTexture 与当前目标屏幕尺寸相匹配
        /// </summary>
        /// <param name="width">目标宽度</param>
        /// <param name="height">目标高度</param>
        public void EnsureDoubleBuffers(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                if (_doubleBufferRT[i] == null || _doubleBufferRT[i].width != width || _doubleBufferRT[i].height != height)
                {
                    if (_doubleBufferRT[i] != null)
                    {
                        _doubleBufferRT[i].Release();
                        DestroyImmediate(_doubleBufferRT[i]);
                    }

                    _doubleBufferRT[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                    {
                        name = $"MultiCam_DoubleBuffer_{i}",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.DontSave
                    };
                    _doubleBufferRT[i].Create();
                }
            }
        }

        /// <summary>
        /// 安全释放双缓冲纹理资源
        /// </summary>
        private void ReleaseDoubleBuffers()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_doubleBufferRT[i] != null)
                {
                    _doubleBufferRT[i].Release();
                    if (Application.isPlaying)
                    {
                        Destroy(_doubleBufferRT[i]);
                    }
                    else
                    {
                        DestroyImmediate(_doubleBufferRT[i]);
                    }
                    _doubleBufferRT[i] = null;
                }
            }
        }
        #endregion

        #region 参数同步与接口驱动
        /// <summary>
        /// 设置主机位与分屏机位的输入渲染纹理
        /// </summary>
        /// <param name="mainTex">第一路/主机位纹理</param>
        /// <param name="subTex">第二路/分屏机位纹理</param>
        public void SetCameraTextures(RenderTexture mainTex, RenderTexture subTex)
        {
            _mainTexture = mainTex;
            _subTexture = subTex;
            // 严禁在仅绑定纹理时盲目激活分屏，严格由时间轴 fadeValue 动态判定
            _isCompositeActive = subTex != null && _fadeValue > 0.001f;
        }

        /// <summary>
        /// 接收时间轴下发的多机位图层与分割线动态关键帧参数
        /// </summary>
        public void UpdateLayerParameters(
            int cameraNo,
            DivideLineType lineType,
            float lineThickness,
            Color lineColor,
            float fadeValue,
            Vector4 transformParameter,
            float maskRoll,
            Vector3 offsetMinPos,
            Vector3 offsetMaxPos
        )
        {
            _multiCameraNo = cameraNo;
            _lineType = lineType;
            _lineThickness = Mathf.Max(0f, lineThickness);
            _lineColor = lineColor;
            _fadeValue = Mathf.Clamp01(fadeValue);
            _transformParameter = transformParameter;
            _maskRoll = maskRoll;
            _offsetMinPosition = offsetMinPos;
            _offsetMaxPosition = offsetMaxPos;

            // 当淡入权重高于阈值且已分配次机位画面时，激活分屏混合渲染，否则保持休眠
            _isCompositeActive = _fadeValue > 0.001f;
            SetCamerasEnabled(_isCompositeActive);
        }

        /// <summary>
        /// 控制关联的主机位与副机位相机的启用/休眠状态
        /// 强化休眠逻辑：确保当 !_isCompositeActive 或 _fadeValue <= 0.001f 时，
        /// 关联的次机位 Camera.enabled 绝对为 false，杜绝单机位阶段持续重绘 8 人高模舞台导致的掉帧（17fps 性能黑洞）
        /// </summary>
        /// <param name="enabled">是否开启相机渲染</param>
        public void SetCamerasEnabled(bool enabled)
        {
            // 防御式休眠守卫：当未激活分屏或淡变权重低于阈值时，关联的次机位 Camera.enabled 绝对为 false
            bool shouldEnableSub = enabled && _isCompositeActive && _fadeValue > 0.001f;
            bool shouldEnableMain = enabled && (_isCompositeActive || _fadeValue > 0.001f);

            if (_cameraMain != null && _cameraMain.enabled != shouldEnableMain)
            {
                _cameraMain.enabled = shouldEnableMain;
            }
            if (_cameraSub != null && _cameraSub.enabled != shouldEnableSub)
            {
                _cameraSub.enabled = shouldEnableSub;
            }
        }

        /// <summary>
        /// 重置所有多机位图层参数至初始默认状态，并强制将关联相机置为休眠
        /// </summary>
        public void ResetParameters()
        {
            _multiCameraNo = 0;
            _lineType = DivideLineType.Color;
            _lineThickness = 0.015f;
            _lineColor = Color.white;
            _fadeValue = 0f; // 重置为 0，杜绝残存权重导致误唤醒
            _isScreenDivide = true;
            _lineAntialiasing = 0.002f;
            _transformParameter = Vector4.zero;
            _maskRoll = 0f;
            _offsetMinPosition = Vector3.zero;
            _offsetMaxPosition = Vector3.zero;
            _isCompositeActive = false;
            SetCamerasEnabled(false);
        }
        /// <summary>
        /// 屏幕后处理合成材质（供外部后处理组件或预览使用）
        /// </summary>
        public Material CompositeMaterial
        {
            get
            {
                EnsureMaterial();
                return _compositeMaterial;
            }
        }
        #endregion

        #region 屏幕后处理与通用合成管线
        /// <summary>
        /// Unity 原生屏幕后处理回调：当挂载在 Camera 节点上时自动执行
        /// </summary>
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            Composite(source, destination);
        }

        /// <summary>
        /// 显式传入两路纹理的多机位合成执行接口：供挂载在主相机上的 MultiCameraFinalComposite 最终后处理调用
        /// </summary>
        /// <param name="mainTex">第一路/主机位画面输入（通常为主相机的渲染源纹理）</param>
        /// <param name="subTex">第二路/次机位画面输入（通常为次机位离屏 RenderTexture）</param>
        /// <param name="dest">合成后输出目标纹理（若为 null 则直接渲染至帧缓冲）</param>
        public void CompositeTextures(RenderTexture mainTex, RenderTexture subTex, RenderTexture dest)
        {
            // 防御分支 1：若未处于分屏激活状态或淡入权重低于阈值，直接将主画面直通 Blit，并强制休眠次机位
            if (!_isCompositeActive || _fadeValue <= 0.001f)
            {
                if (_cameraSub != null && _cameraSub.enabled)
                {
                    _cameraSub.enabled = false;
                }
                RenderTexture fallbackTex = (mainTex != null) ? mainTex : _mainTexture;
                if (fallbackTex != null)
                {
                    Graphics.Blit(fallbackTex, dest);
                }
                return;
            }

            // 防御分支 2：若次机位输入纹理为空，无法执行分屏合成，安全回退至主画面
            RenderTexture effectiveSub = (subTex != null) ? subTex : _subTexture;
            RenderTexture effectiveMain = (mainTex != null) ? mainTex : _mainTexture;

            if (effectiveSub == null)
            {
                if (_cameraSub != null && _cameraSub.enabled)
                {
                    _cameraSub.enabled = false;
                }
                if (effectiveMain != null)
                {
                    Graphics.Blit(effectiveMain, dest);
                }
                return;
            }

            if (effectiveMain == null)
            {
                // 若仅有次机位画面有效，直接输出次机位
                Graphics.Blit(effectiveSub, dest);
                return;
            }

            // 防御分支 3：确保后处理材质有效，若 Shader 缺失则安全回退
            if (!EnsureMaterial() || _compositeMaterial == null)
            {
                Graphics.Blit(effectiveMain, dest);
                return;
            }

            // 计算分割线的方向角弧度（优先使用 MaskRoll，其次使用 TransformParameter.z）
            float effectiveRoll = Mathf.Abs(_maskRoll) > 0.0001f ? _maskRoll : _transformParameter.z;
            float rollRad = effectiveRoll * Mathf.Deg2Rad;
            float cosRoll = Mathf.Cos(rollRad);
            float sinRoll = Mathf.Sin(rollRad);

            // 提取 UV 偏移（来自 TransformParameter.x 与 y）
            float offsetX = _transformParameter.x;
            float offsetY = _transformParameter.y;

            // 组装 Shader 输入参数
            _compositeMaterial.SetTexture(PropMainTex, effectiveMain);
            _compositeMaterial.SetTexture(PropSubTex, effectiveSub);
            _compositeMaterial.SetColor(PropDivideLineColor, _lineColor);
            _compositeMaterial.SetVector(PropDivideLineParam, new Vector4(offsetX, offsetY, cosRoll, sinRoll));
            _compositeMaterial.SetVector(PropDivideLineSetting, new Vector4(
                (float)_lineType,
                _isScreenDivide ? Mathf.Max(0.001f, _lineThickness) : -1f,
                _fadeValue,
                _lineAntialiasing > 0f ? _lineAntialiasing : 0.002f
            ));

            // 执行多机位双缓冲/帧缓冲合成 Blit
            Graphics.Blit(effectiveMain, dest, _compositeMaterial);
        }

        /// <summary>
        /// 通用分屏合成执行方法：支持被外部或离屏流程主动调用，具备全场景空指针防御与安全回退
        /// </summary>
        /// <param name="source">输入源纹理（通常为主相机画面）</param>
        /// <param name="destination">输出目标纹理（若为 null 则直接渲染至帧缓冲）</param>
        public void Composite(RenderTexture source, RenderTexture destination)
        {
            CompositeTextures(source != null ? source : _mainTexture, _subTexture, destination);
        }
        #endregion

        #region 静态纯数学视口与带符号距离场计算工具（供断言与逻辑判定使用）
        /// <summary>
        /// 计算屏幕归一化坐标点 (uv) 相对于倾斜中心分割线的带符号垂直距离 (Signed Distance)
        /// 正值表示该点属于次机位图层区域，负值表示属于主机位区域，绝对值小于半厚度表示位于分割线上。
        /// </summary>
        /// <param name="uv">屏幕归一化 UV 坐标 [0, 1]</param>
        /// <param name="offset">中心点在 UV 空间的偏移量</param>
        /// <param name="rollDeg">分割线旋转角度（度）</param>
        /// <returns>像素点到分割线的垂直距离</returns>
        public static float CalculateDistanceToDivideLine(Vector2 uv, Vector2 offset, float rollDeg)
        {
            Vector2 center = new Vector2(0.5f, 0.5f) + offset;
            Vector2 diff = uv - center;

            float rad = rollDeg * Mathf.Deg2Rad;
            float cosRoll = Mathf.Cos(rad);
            float sinRoll = Mathf.Sin(rad);

            // 投影点到分割线法向量的点积 (diff · n)
            return diff.x * cosRoll + diff.y * sinRoll;
        }

        /// <summary>
        /// 判定指定 UV 像素点是否落在分屏图层所在侧
        /// </summary>
        /// <param name="uv">屏幕归一化 UV 坐标</param>
        /// <param name="offset">中心偏移量</param>
        /// <param name="rollDeg">旋转欧拉角</param>
        /// <returns>若处于图层区域内则返回 true</returns>
        public static bool IsPointInLayer(Vector2 uv, Vector2 offset, float rollDeg)
        {
            return CalculateDistanceToDivideLine(uv, offset, rollDeg) >= 0f;
        }

        /// <summary>
        /// 计算像素点上分割线的羽化与透明度 Alpha 因子 (0~1)
        /// 采用带反走样平滑阶跃 (SmoothStep) 算法，确保斜线与高分辨率下无锯齿撕裂。
        /// </summary>
        /// <param name="uv">屏幕归一化 UV 坐标</param>
        /// <param name="offset">中心偏移量</param>
        /// <param name="rollDeg">分割线旋转角度</param>
        /// <param name="thickness">分割线宽度</param>
        /// <param name="antialiasing">抗锯齿平滑带宽度</param>
        /// <returns>分割线 Alpha 强度值</returns>
        public static float CalculateDivideLineAlpha(Vector2 uv, Vector2 offset, float rollDeg, float thickness, float antialiasing = 0.002f)
        {
            float dist = Mathf.Abs(CalculateDistanceToDivideLine(uv, offset, rollDeg));
            float halfThickness = Mathf.Max(0.0001f, thickness * 0.5f);
            float aa = Mathf.Max(0.0001f, antialiasing);

            if (dist <= halfThickness)
            {
                return 1f;
            }
            else if (dist < halfThickness + aa)
            {
                return 1f - Mathf.SmoothStep(halfThickness, halfThickness + aa, dist);
            }
            else
            {
                return 0f;
            }
        }

        /// <summary>
        /// 针对左右等分等标准分屏情形，快速估算机位视口 Rect
        /// </summary>
        /// <param name="cameraIndex">机位编号 (0: 左/主, 1: 右/次)</param>
        /// <param name="totalCameras">总机位数</param>
        /// <param name="offset">水平中心偏移</param>
        /// <returns>视口归一化 Rect</returns>
        public static Rect CalculateSplitViewport(int cameraIndex, int totalCameras, Vector2 offset)
        {
            if (totalCameras <= 1)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            float splitX = Mathf.Clamp01(0.5f + offset.x);
            if (cameraIndex == 0)
            {
                return new Rect(0f, 0f, splitX, 1f);
            }
            else
            {
                return new Rect(splitX, 0f, 1f - splitX, 1f);
            }
        }
        #endregion
    }
}
