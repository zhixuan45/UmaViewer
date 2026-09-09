using Gallop.ImageEffect;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop
{
    [DisallowMultipleComponent]
    public class GallopImageEffect : MonoBehaviour
    {
        [SerializeField]
        private Volume _volume;

        [SerializeField]
        private VolumeProfile _runtimeProfile;

        [SerializeField]
        private DofDiffusionBloomOverlayParam
            _dofDiffusionBloomOverlayParam =
                new DofDiffusionBloomOverlayParam();

        private Bloom _bloom;

        public DofDiffusionBloomOverlayParam
            DofDiffusionBloomOverlayParam
        {
            get
            {
                return _dofDiffusionBloomOverlayParam;
            }
        }

        /// <summary>
        /// 第二步隔离测试调试开关：控制是否临时全局禁用 URP Bloom / Diffusion 泛光，排查是否导致天空压白与荧光绿
        /// </summary>
        public static bool DisableBloomForDebug = false;

        private void Awake()
        {
            InitializeVolume();
        }

        private void LateUpdate()
        {
            ApplyBloomParameter();
        }

        public void InitializeVolume()
        {
            if (_volume == null)
                _volume = GetComponent<Volume>();

            if (_volume == null)
                _volume = gameObject.AddComponent<Volume>();

            // 将 Volume 设置为全局生效 (isGlobal = true)。
            // 在 URP 管线下，若 isGlobal 为 false，Volume 必须依赖 Collider 触发器且需要相机进入其碰撞范围才能生效。
            // 马娘演出中机位频繁切换或挂载在没有碰撞体的对象上时，局部 Volume 会导致后处理被管线直接忽略。
            // 全局化后，全屏任意机位均能全局执行后处理，并通过高优先级 (100f) 与完全权重 (1f) 保证后处理效果正确覆盖。
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.weight = DisableBloomForDebug ? 0f : 1f;

            if (_volume.sharedProfile != null)
                _runtimeProfile =
                    Instantiate(_volume.sharedProfile);
            else
                _runtimeProfile =
                    ScriptableObject.CreateInstance<VolumeProfile>();

            _runtimeProfile.name =
                $"{name}_RuntimePostEffectProfile";

            _volume.profile = _runtimeProfile;

            if (!_runtimeProfile.TryGet(out _bloom))
                _bloom = _runtimeProfile.Add<Bloom>(true);
        }

        /// <summary>
        /// 应用 Bloom 与 Diffusion（光扩散模糊）参数映射至 URP Bloom 组件。
        /// 马娘原版时间轴包含 Bloom（泛光）与 Diffusion（大范围柔光扩散）两套参数。
        /// 在 URP 渲染管线中，我们将 Diffusion 深度融入 Bloom 的 intensity、scatter 与 threshold 属性中：
        /// 1. 激活判断：当 IsEnableBloom 或 IsEnableDiffusion 为 true 且具备有效亮度时激活组件；
        /// 2. 强度映射 (intensity)：综合 BloomIntensity 与 DiffusionBright 叠加调优；
        /// 3. 散射度映射 (scatter)：取 BloomBlurSize 与 DiffusionBlurSize 的较大值映射至 [0, 1] 散射范围，保证强光背景（如夕阳晚霞、舞台发光物）能产生柔和的大范围光扩散模糊漫射；
        /// 4. 阈值映射 (threshold)：兼顾 BloomThreshold 与 DiffusionThreshold，取有效较低阈值使 Diffusion 能够捕捉到更广的中高亮度漫射区域。
        /// </summary>
        public void ApplyBloomParameter()
        {
            if (_bloom == null)
                InitializeVolume();

            if (_bloom == null)
                return;

            if (DisableBloomForDebug)
            {
                _bloom.active = false;
                if (_volume != null) _volume.weight = 0f;
                return;
            }

            var param = _dofDiffusionBloomOverlayParam;
            if (param == null)
                return;

            float bloomIntensity = param.IsEnableBloom ? Mathf.Max(0f, param.BloomIntensity) : 0f;
            float diffusionFactor = param.IsEnableDiffusion ? Mathf.Max(0f, param.DiffusionBright) : 0f;
            float totalIntensity = bloomIntensity + diffusionFactor;

            // 当 IsEnableBloom 或 IsEnableDiffusion 为 true 时，若存在有效强度则激活 Bloom 组件
            bool enabled = (param.IsEnableBloom || param.IsEnableDiffusion) && totalIntensity > 0f;
            _bloom.active = enabled;

            _bloom.threshold.overrideState = true;
            _bloom.intensity.overrideState = true;
            _bloom.scatter.overrideState = true;

            _bloom.intensity.value = totalIntensity;

            // 2. 散射度映射 (scatter)：
            // 马娘原版 BloomBlurSize 与 DiffusionBlurSize 范围通常为 0~10。
            // URP Bloom scatter 范围为 0~1，控制泛光向四周蔓延扩散的模糊半径与漫射程度。
            // 综合二者并取最大值映射至 [0, 1]，确保夕阳晚霞、舞台高光产生柔和的大范围光模糊效果。
            float bloomBlur = param.IsEnableBloom ? Mathf.Max(0f, param.BloomBlurSize) : 0f;
            float diffusionBlur = param.IsEnableDiffusion ? Mathf.Max(0f, param.DiffusionBlurSize) : 0f;
            float maxBlurSize = Mathf.Max(bloomBlur, diffusionBlur);
            _bloom.scatter.value = Mathf.Clamp01(maxBlurSize / 10f);

            // 3. 亮度阈值映射 (threshold)：
            // 兼顾 BloomThreshold 与 DiffusionThreshold。
            // 当两者同时开启时，取较低阈值以确保扩散光能够提取到更丰富的中高亮漫射区域；单项开启时则使用对应项的阈值。
            float threshold;
            if (param.IsEnableBloom && param.IsEnableDiffusion)
            {
                threshold = Mathf.Min(Mathf.Max(0f, param.BloomThreshold), Mathf.Max(0f, param.DiffusionThreshold));
            }
            else if (param.IsEnableDiffusion)
            {
                threshold = Mathf.Max(0f, param.DiffusionThreshold);
            }
            else
            {
                threshold = Mathf.Max(0f, param.BloomThreshold);
            }
            _bloom.threshold.value = threshold;
        }
    }
}