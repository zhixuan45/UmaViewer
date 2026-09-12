using Gallop.Live.Cutt;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// Director 的多机位分屏（Multi-Camera）扩展分部类
    /// 负责根据当前 Live 的 LiveTimelineMultiCameraSettings 或工作表多机位轨道配置动态实例化分屏相机节点、
    /// 分配各机位独立的离屏双缓冲 RenderTexture、为每台 MultiCamera 挂载独立的 MultiCameraComposite 合成组件，
    /// 实现多机位参数隔离与精确按需休眠/唤醒调度，并在 Live 退出或切换时进行无残留安全销毁与内存清理。
    /// </summary>
    public partial class Director
    {
        [Header("多机位分屏运行时管理")]
        [SerializeField] private MultiCameraComposite[] _multiCameraComposites;
        [SerializeField] private GameObject _multiCameraRoot;
        private readonly List<MultiCamera> _multiCameraList = new List<MultiCamera>();
        private RenderTexture[] _multiCameraRTs;
        private LiveTimelineControl _boundMultiCameraTimeline;

        [Header("多机位 URP 全屏呈现层")]
        [SerializeField] private GameObject _multiCameraOverlayRoot;
        [SerializeField] private Canvas _multiCameraOverlayCanvas;
        [SerializeField] private UnityEngine.UI.RawImage _multiCameraOverlayImage;
        private RenderTexture _finalDisplayRT;

        /// <summary>
        /// 多机位分屏合成器数组
        /// </summary>
        public MultiCameraComposite[] MultiCameraComposites => _multiCameraComposites;

        /// <summary>
        /// 兼容保留原单例访问器：默认返回第 0 路机位合成器
        /// </summary>
        public MultiCameraComposite MultiCamComposite => GetMultiCameraComposite(0);

        /// <summary>
        /// 根据通道索引获取对应的多机位合成器组件
        /// </summary>
        /// <param name="index">机位通道索引</param>
        /// <returns>多机位合成器实例，越界时返回 null</returns>
        public MultiCameraComposite GetMultiCameraComposite(int index)
        {
            if (_multiCameraComposites != null && index >= 0 && index < _multiCameraComposites.Length)
            {
                return _multiCameraComposites[index];
            }
            return null;
        }

        /// <summary>
        /// 根据时间轴配置或多机位轨道初始化多机位分屏相机系统与独立合成器
        /// </summary>
        /// <param name="control">当前 Live 的时间轴控制器</param>
        public void InitializeMultiCamera(LiveTimelineControl control)
        {
            // 首先清理可能残留的历史机位与纹理
            CleanupMultiCamera();

            if (control == null || control.data == null)
            {
                return;
            }

            // 1. 获取机位总数：先尝试从 multiCameraSettings 获取
            int cameraCount = 0;
            if (control.data.multiCameraSettings != null)
            {
                cameraCount = control.data.multiCameraSettings.cameraNum;
            }

            // 移除直接 return 阻断：当 multiCameraSettings 为 null 或 cameraNum <= 0 时，检查 worksheetList[0].multiCameraPosKeys.Count
            if (cameraCount <= 0)
            {
                if (control.data.worksheetList != null &&
                    control.data.worksheetList.Count > 0 &&
                    control.data.worksheetList[0] != null &&
                    control.data.worksheetList[0].multiCameraPosKeys != null)
                {
                    int posKeysCount = control.data.worksheetList[0].multiCameraPosKeys.Count;
                    if (posKeysCount > 0)
                    {
                        cameraCount = posKeysCount;
                        Debug.Log($"[Director.MultiCamera] multiCameraSettings 为空或 cameraNum 无效，从 worksheetList[0].multiCameraPosKeys 推断多机位数：{cameraCount}");
                    }
                }
            }

            if (cameraCount <= 0)
            {
                Debug.Log("[Director.MultiCamera] 当前 Live 未启用多机位配置 (cameraCount <= 0)");
                return;
            }

            Debug.Log($"[Director.MultiCamera] 开始初始化多机位分屏系统，机位总数：{cameraCount}");

            _boundMultiCameraTimeline = control;

            // 2. 创建多机位根节点并挂载至时间轴控制器下
            _multiCameraRoot = new GameObject("MultiCameras");
            _multiCameraRoot.transform.SetParent(control.transform, false);

            // 3. 计算离屏渲染分辨率（优先使用屏幕分辨率，兜底 1920x1080）
            int rtWidth = Screen.width > 0 ? Screen.width : 1920;
            int rtHeight = Screen.height > 0 ? Screen.height : 1080;

            _multiCameraRTs = new RenderTexture[cameraCount];
            _multiCameraComposites = new MultiCameraComposite[cameraCount];
            MultiCamera[] cameras = new MultiCamera[cameraCount];
            _multiCameraList.Clear();

            // 确保多机位录制帧列表容量就绪
            if (control.MultiRecordFrames == null)
            {
                control.MultiRecordFrames = new List<List<LiveCameraFrame>>();
            }
            control.MultiRecordFrames.Clear();

            for (int i = 0; i < cameraCount; i++)
            {
                // 创建各机位子节点（如 MultiCamera_0、MultiCamera_1）
                GameObject camObj = new GameObject($"MultiCamera_{i}");
                camObj.transform.SetParent(_multiCameraRoot.transform, false);

                // 挂载并初始化 MultiCamera 组件
                MultiCamera multiCam = camObj.AddComponent<MultiCamera>();
                multiCam.Initialize();
                cameras[i] = multiCam;
                _multiCameraList.Add(multiCam);

                // 为每台 MultiCamera 独立挂载 MultiCameraComposite 组件
                MultiCameraComposite composite = camObj.AddComponent<MultiCameraComposite>();
                composite.MultiCameraNo = i;
                _multiCameraComposites[i] = composite;

                control.MultiRecordFrames.Add(new List<LiveCameraFrame>());

                // 为每路机位创建独立的深度与颜色离屏 RenderTexture
                RenderTexture rt = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"MultiCam_RT_{i}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                rt.Create();
                _multiCameraRTs[i] = rt;

                // 配置相机渲染目标与深度参数
                Camera cam = multiCam.GetCamera();
                if (cam != null)
                {
                    cam.targetTexture = rt;
                    cam.clearFlags = CameraClearFlags.Color;
                    cam.backgroundColor = Color.clear;
                    cam.depth = -2 + i;
                    // 核心性能治理：初始化阶段强制所有分屏相机处于休眠状态 (Camera.enabled = false)，彻底消除多相机常开重绘导致的 17fps 性能黑洞
                    cam.enabled = false;
                }
            }

            // 4. 为每台机位的 MultiCameraComposite 分配独立的输入纹理与主副相机
            Camera mainCam = cameras.Length > 0 ? cameras[0].GetCamera() : null;
            RenderTexture mainRT = _multiCameraRTs.Length > 0 ? _multiCameraRTs[0] : null;

            for (int i = 0; i < cameraCount; i++)
            {
                MultiCameraComposite composite = _multiCameraComposites[i];
                if (composite != null)
                {
                    composite.CameraMain = mainCam;
                    composite.CameraSub = cameras[i].GetCamera();
                    composite.SetCameraTextures(mainRT, _multiCameraRTs[i]);
                    composite.ResetParameters();
                }
            }

            // 5. 订阅时间轴多机位图层更新事件，实现参数毫秒级实时驱动
            control.OnUpdateMultiCameraLayer += OnTimelineMultiCameraLayerUpdated;

            // 6. 将相机数组注入时间轴控制器
            control.SetMultiCamera(cameras);

            // 7. 初始状态绝对休眠守卫：确保所有相机 Camera.enabled = false
            SetMultiCamerasEnabled(false);

            // 8. 确保主摄像机挂载 MultiCameraFinalComposite 最终屏幕后处理合成组件
            EnsureFinalCompositeAttached();

            // 9. 确保 URP 全屏分屏呈现层初始化并注册渲染管线完成回调
            EnsureOverlayCreated();
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndCameraRendering;

            Debug.Log("[Director.MultiCamera] 多机位独立合成器与 URP 全屏呈现层装配就绪（初始状态已强制置为休眠 Camera.enabled = false）。");
        }

        /// <summary>
        /// 动态按需控制指定多机位相机的休眠与激活状态
        /// 仅在分屏激活且淡入权重有效时唤醒相机渲染，单镜头阶段全面休眠，消除冗余 DrawCall 与场景重绘（掉帧根因治理）
        /// </summary>
        /// <param name="index">机位通道索引</param>
        /// <param name="enabled">是否激活该机位相机渲染</param>
        public void SetMultiCameraEnabled(int index, bool enabled)
        {
            if (_multiCameraList == null || index < 0 || index >= _multiCameraList.Count)
            {
                return;
            }

            MultiCamera multiCam = _multiCameraList[index];
            if (multiCam != null)
            {
                Camera cam = multiCam.GetCamera();
                if (cam != null && cam.enabled != enabled)
                {
                    cam.enabled = enabled;
                }
            }

            // 同步联动对应合成器的相机休眠状态
            MultiCameraComposite composite = GetMultiCameraComposite(index);
            if (composite != null && !enabled)
            {
                composite.SetCamerasEnabled(false);
            }
        }

        /// <summary>
        /// 批量控制所有多机位分屏相机的休眠与激活状态
        /// </summary>
        /// <param name="enabled">是否激活全部机位相机渲染</param>
        public void SetMultiCamerasEnabled(bool enabled)
        {
            if (_multiCameraList == null || _multiCameraList.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _multiCameraList.Count; i++)
            {
                SetMultiCameraEnabled(i, enabled);
            }
        }

        /// <summary>
        /// 响应时间轴下发的多机位分屏图层事件，按需动态唤醒/休眠对应分屏相机并传递给对应机位合成器
        /// </summary>
        private void OnTimelineMultiCameraLayerUpdated(
            int cameraNo,
            MultiCameraComposite.DivideLineType lineType,
            float lineThickness,
            Color lineColor,
            float fadeValue,
            Vector4 transformParameter,
            float maskRoll,
            Vector3 offsetMinPos,
            Vector3 offsetMaxPos
        )
        {
            // 性能治理与掉帧根因修复：仅在时间轴下发有效分屏且 fadeValue > 0.001f 时才被唤醒，在淡变归零时立即休眠！
            bool isSplitScreenActive = fadeValue > 0.001f;

            MultiCameraComposite composite = GetMultiCameraComposite(cameraNo);
            if (composite != null)
            {
                composite.UpdateLayerParameters(
                    cameraNo,
                    lineType,
                    lineThickness,
                    lineColor,
                    fadeValue,
                    transformParameter,
                    maskRoll,
                    offsetMinPos,
                    offsetMaxPos
                );

                // 双重休眠保障：淡变归零时绝对强制休眠对应机位
                if (!isSplitScreenActive)
                {
                    composite.SetCamerasEnabled(false);
                }
            }

            // 联动控制相机休眠状态
            SetMultiCameraEnabled(cameraNo, isSplitScreenActive);
            if (isSplitScreenActive)
            {
                // 分屏激活时确保主机位 0 同时处于工作状态
                SetMultiCameraEnabled(0, true);
            }
        }

        /// <summary>
        /// 安全清理并销毁所有多机位运行时资源与离屏纹理，杜绝内存泄漏
        /// </summary>
        public void CleanupMultiCamera()
        {
            // 0. 立即强制休眠所有多机位相机并注销管线渲染监听
            SetMultiCamerasEnabled(false);
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

            // 1. 取消时间轴事件订阅
            if (_boundMultiCameraTimeline != null)
            {
                _boundMultiCameraTimeline.OnUpdateMultiCameraLayer -= OnTimelineMultiCameraLayerUpdated;
                _boundMultiCameraTimeline = null;
            }

            // 2. 释放并销毁所有多机位离屏 RenderTexture
            if (_multiCameraRTs != null)
            {
                for (int i = 0; i < _multiCameraRTs.Length; i++)
                {
                    if (_multiCameraRTs[i] != null)
                    {
                        _multiCameraRTs[i].Release();
                        if (Application.isPlaying)
                        {
                            Destroy(_multiCameraRTs[i]);
                        }
                        else
                        {
                            DestroyImmediate(_multiCameraRTs[i]);
                        }
                        _multiCameraRTs[i] = null;
                    }
                }
                _multiCameraRTs = null;
            }

            // 3. 释放最终显示 RenderTexture
            if (_finalDisplayRT != null)
            {
                _finalDisplayRT.Release();
                if (Application.isPlaying)
                {
                    Destroy(_finalDisplayRT);
                }
                else
                {
                    DestroyImmediate(_finalDisplayRT);
                }
                _finalDisplayRT = null;
            }

            // 4. 销毁全屏呈现层 Canvas 节点
            if (_multiCameraOverlayRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_multiCameraOverlayRoot);
                }
                else
                {
                    DestroyImmediate(_multiCameraOverlayRoot);
                }
                _multiCameraOverlayRoot = null;
                _multiCameraOverlayCanvas = null;
                _multiCameraOverlayImage = null;
            }

            // 5. 销毁多机位根节点树与相机组件
            if (_multiCameraRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_multiCameraRoot);
                }
                else
                {
                    DestroyImmediate(_multiCameraRoot);
                }
                _multiCameraRoot = null;
            }

            _multiCameraComposites = null;
            _multiCameraList.Clear();
        }

        /// <summary>
        /// 确保 URP 兼容的多机位全屏分屏呈现层 (ScreenSpaceOverlay Canvas) 初始化
        /// sortingOrder 设为 -1 确保其覆盖在 3D 摄像机画面上方，但位于所有业务 UI（UI 容器预制体均为 0 或以上）之下，绝不遮挡歌词与播放控制条
        /// </summary>
        private void EnsureOverlayCreated()
        {
            if (_multiCameraOverlayRoot != null)
            {
                return;
            }

            _multiCameraOverlayRoot = new GameObject("MultiCameraScreenOverlay");
            _multiCameraOverlayRoot.transform.SetParent(transform, false);

            _multiCameraOverlayCanvas = _multiCameraOverlayRoot.AddComponent<Canvas>();
            _multiCameraOverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _multiCameraOverlayCanvas.sortingOrder = -1;

            GameObject imgObj = new GameObject("MultiCameraDisplayImage");
            imgObj.transform.SetParent(_multiCameraOverlayRoot.transform, false);
            _multiCameraOverlayImage = imgObj.AddComponent<UnityEngine.UI.RawImage>();
            _multiCameraOverlayImage.raycastTarget = false;

            RectTransform rect = _multiCameraOverlayImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _multiCameraOverlayRoot.SetActive(false);
        }

        /// <summary>
        /// URP 摄像机渲染完成回调：当次机位 (MultiCamera_1) 完成几何渲染后立即触发全屏分屏合成
        /// </summary>
        private void OnEndCameraRendering(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
        {
            if (_multiCameraComposites == null || _multiCameraComposites.Length == 0) return;
            if (_multiCameraRTs == null || _multiCameraRTs.Length < 2) return;

            Camera subCam = _multiCameraList.Count > 1 ? _multiCameraList[1]?.GetCamera() : null;
            if (camera != subCam) return;

            MultiCameraComposite comp = _multiCameraComposites[0];
            if (comp == null || !comp.IsCompositeActive || comp.FadeValue <= 0.001f) return;

            if (_finalDisplayRT != null && _multiCameraRTs[0] != null && _multiCameraRTs[1] != null)
            {
                comp.CompositeTextures(_multiCameraRTs[0], _multiCameraRTs[1], _finalDisplayRT);
                if (_multiCameraOverlayImage != null)
                {
                    _multiCameraOverlayImage.texture = _finalDisplayRT;
                }
            }
        }

        /// <summary>
        /// 每帧在 Director.LateUpdate 末尾调用：根据分屏激活状态实时更新呈现层与离屏纹理
        /// </summary>
        public void UpdateMultiCameraDisplay()
        {
            if (_multiCameraComposites == null || _multiCameraComposites.Length == 0) return;
            MultiCameraComposite comp = _multiCameraComposites[0];
            bool isSplitScreenActive = comp != null && comp.IsCompositeActive && comp.FadeValue > 0.001f;

            if (!isSplitScreenActive)
            {
                if (_multiCameraOverlayRoot != null && _multiCameraOverlayRoot.activeSelf)
                {
                    _multiCameraOverlayRoot.SetActive(false);
                }
                SetMultiCamerasEnabled(false);
                return;
            }

            // 分屏激活：唤醒两路分屏相机
            SetMultiCameraEnabled(0, true);
            SetMultiCameraEnabled(1, true);

            EnsureOverlayCreated();

            int screenW = Screen.width > 0 ? Screen.width : 1920;
            int screenH = Screen.height > 0 ? Screen.height : 1080;

            // 动态自适应屏幕分辨率变更
            if (_finalDisplayRT == null || _finalDisplayRT.width != screenW || _finalDisplayRT.height != screenH)
            {
                if (_finalDisplayRT != null)
                {
                    _finalDisplayRT.Release();
                    DestroyImmediate(_finalDisplayRT);
                }
                _finalDisplayRT = new RenderTexture(screenW, screenH, 0, RenderTextureFormat.ARGB32)
                {
                    name = "MultiCam_FinalDisplay_RT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                _finalDisplayRT.Create();
            }

            // 确保离屏双路 RT 也匹配最新屏幕分辨率
            if (_multiCameraRTs != null)
            {
                for (int i = 0; i < _multiCameraRTs.Length; i++)
                {
                    if (_multiCameraRTs[i] != null && (_multiCameraRTs[i].width != screenW || _multiCameraRTs[i].height != screenH))
                    {
                        _multiCameraRTs[i].Release();
                        DestroyImmediate(_multiCameraRTs[i]);
                        _multiCameraRTs[i] = new RenderTexture(screenW, screenH, 24, RenderTextureFormat.ARGB32)
                        {
                            name = $"MultiCam_RT_{i}",
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp,
                            hideFlags = HideFlags.DontSave
                        };
                        _multiCameraRTs[i].Create();
                        if (i < _multiCameraList.Count && _multiCameraList[i] != null)
                        {
                            Camera c = _multiCameraList[i].GetCamera();
                            if (c != null) c.targetTexture = _multiCameraRTs[i];
                        }
                    }
                }
            }

            // 执行合成并投射至全屏 Overlay
            if (_multiCameraRTs != null && _multiCameraRTs.Length >= 2 && _multiCameraRTs[0] != null && _multiCameraRTs[1] != null)
            {
                comp.CompositeTextures(_multiCameraRTs[0], _multiCameraRTs[1], _finalDisplayRT);
                if (_multiCameraOverlayImage != null)
                {
                    _multiCameraOverlayImage.texture = _finalDisplayRT;
                }
                if (_multiCameraOverlayRoot != null && !_multiCameraOverlayRoot.activeSelf)
                {
                    _multiCameraOverlayRoot.SetActive(true);
                }
            }
        }

        /// <summary>
        /// 确保主摄像机（Camera.main 与 _cameraObjects 中的各主镜头相机）挂载 MultiCameraFinalComposite 最终屏幕后处理合成组件
        /// </summary>
        public void EnsureFinalCompositeAttached()
        {
            // 1. 尝试为主相机 Camera.main 挂载并配置合成器
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                MultiCameraFinalComposite finalComp = mainCam.GetComponent<MultiCameraFinalComposite>();
                if (finalComp == null)
                {
                    finalComp = mainCam.gameObject.AddComponent<MultiCameraFinalComposite>();
                }
                if (finalComp != null && _multiCameraComposites != null && _multiCameraComposites.Length > 0)
                {
                    finalComp.TargetComposite = _multiCameraComposites[0];
                }
            }

            // 2. 遍历 _cameraObjects，确保每一个主镜头相机（切镜头时）均具备最终后处理合成能力
            if (_cameraObjects != null)
            {
                for (int i = 0; i < _cameraObjects.Length; i++)
                {
                    Camera cam = _cameraObjects[i];
                    if (cam != null)
                    {
                        MultiCameraFinalComposite comp = cam.GetComponent<MultiCameraFinalComposite>();
                        if (comp == null)
                        {
                            comp = cam.gameObject.AddComponent<MultiCameraFinalComposite>();
                        }
                        if (comp != null && _multiCameraComposites != null && _multiCameraComposites.Length > 0)
                        {
                            comp.TargetComposite = _multiCameraComposites[0];
                        }
                    }
                }
            }
        }
    }
}
