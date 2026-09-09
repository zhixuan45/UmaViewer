using UnityEngine;
using Gallop.ImageEffect;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    /// <summary>
    /// Director 画面后处理效果分部类，负责获取主相机 GallopImageEffect 并响应时间轴光晕/扩散等后处理参数更新
    /// </summary>
    public partial class Director
    {
        [SerializeField]
        private GallopImageEffect _mainGallopImageEffect;

        /// <summary>
        /// 获取或动态挂载主相机的 GallopImageEffect 后处理组件
        /// </summary>
        private GallopImageEffect GetActivePostEffect()
        {
            if (_mainGallopImageEffect != null)
                return _mainGallopImageEffect;

            Camera mainCamera = null;

            if (_cameraObjects != null &&
                _activeCameraIndex >= 0 &&
                _activeCameraIndex < _cameraObjects.Length)
            {
                mainCamera = _cameraObjects[_activeCameraIndex];
            }

            if (mainCamera == null)
                mainCamera = Camera.main;

            if (mainCamera == null)
                return null;

            _mainGallopImageEffect =
                mainCamera.GetComponent<GallopImageEffect>();

            if (_mainGallopImageEffect == null)
            {
                _mainGallopImageEffect =
                    mainCamera.gameObject
                        .AddComponent<GallopImageEffect>();
            }

            return _mainGallopImageEffect;
        }

        /// <summary>
        /// 时间轴 Bloom 与 Diffusion 泛光扩散参数驱动回调
        /// </summary>
        private void OnUpdatePostEffect_BloomDiffusion(PostEffectUpdateInfo_BloomDiffusion updateInfo)
        {
            GallopImageEffect imageEffect = GetActivePostEffect();

            if (imageEffect == null) return;

            DofDiffusionBloomOverlayParam param =
                imageEffect.DofDiffusionBloomOverlayParam;

            param.IsEnableBloom =
                updateInfo.IsEnabledBloom;

            param.BloomDofWeight =
                updateInfo.bloomDofWeight;

            param.BloomThreshold =
                updateInfo.threshold;

            param.BloomIntensity =
                updateInfo.intensity;

            param.BloomBlurSize =
                updateInfo.BloomBlurSize;

            param.BloomBlendMode =
                updateInfo.BloomBlendMode;

            param.IsEnableDiffusion =
                updateInfo.IsEnabledDiffusion;

            param.DiffusionBlurSize =
                updateInfo.diffusionBlurSize;

            param.DiffusionBright =
                updateInfo.diffusionBright;

            param.DiffusionThreshold =
                updateInfo.diffusionThreshold;

            param.DiffusionSaturation =
                updateInfo.diffusionSaturation;

            param.DiffusionContrast =
                updateInfo.diffusionContrast;
        }

        /// <summary>
        /// 取消时间轴后处理事件订阅
        /// </summary>
        private void UnbindTimelineEvents()
        {
            if (_liveTimelineControl == null)
                return;

            _liveTimelineControl.OnUpdatePostEffect_BloomDiffusion -=
                OnUpdatePostEffect_BloomDiffusion;
        }
    }
}
