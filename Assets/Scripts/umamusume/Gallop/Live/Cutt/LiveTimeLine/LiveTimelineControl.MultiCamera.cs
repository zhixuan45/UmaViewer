using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// 多机位时间轴：layer 给遮罩/分割线/fade，Switcher 用 fadeTime 做权重过渡。
    /// 多路权重大于 0 时同时出画，由 Driver 合并后再交给 Director 合成。
    /// </summary>
    public partial class LiveTimelineControl : MonoBehaviour
    {
        public delegate void MultiCameraLayerUpdateDelegate(
            int cameraNo,
            MultiCameraComposite.DivideLineType lineType,
            float lineThickness,
            Color lineColor,
            float fadeValue,
            Vector4 transformParameter,
            float maskRoll,
            Vector3 offsetMinPos,
            Vector3 offsetMaxPos
        );

        public event MultiCameraLayerUpdateDelegate OnUpdateMultiCameraLayer;

        public delegate void MultiCameraPositionUpdateDelegate(
            int cameraIndex,
            Vector3 position,
            float fov,
            float roll,
            Vector2 maskOffset,
            float maskRoll,
            LiveTimelineKeyMultiCameraPositionData.MaskType maskType,
            bool enableMultiCamera
        );

        public event MultiCameraPositionUpdateDelegate OnUpdateMultiCameraPosition;

        public delegate void MultiCameraLookAtUpdateDelegate(
            int cameraIndex,
            Vector3 lookAtPosition
        );

        public event MultiCameraLookAtUpdateDelegate OnUpdateMultiCameraLookAt;

        /// <summary>
        /// 是否启用「按 fadeTime 推进的多机位切换淡变」。关掉可立刻对比排查。
        /// </summary>
        public static bool UseFadeTimeSwitcher = true;

        private static Vector3 GetMultiCameraPositionValue(
            LiveTimelineKeyCameraPositionData keyData,
            LiveTimelineControl timelineControl,
            FindTimelineConfig config
        )
        {
            if (keyData is LiveTimelineKeyMultiCameraPositionData multiPosKey)
            {
                return multiPosKey.GetValue(timelineControl);
            }
            return Vector3.zero;
        }

        private static Vector3 GetMultiCameraLookAtValue(
            LiveTimelineKeyCameraLookAtData keyData,
            LiveTimelineControl timelineControl,
            Vector3 camPos,
            FindTimelineConfig config
        )
        {
            if (keyData is LiveTimelineKeyMultiCameraLookAtData multiLookAtKey)
            {
                return multiLookAtKey.GetValue(timelineControl, camPos);
            }
            return Vector3.zero;
        }

        public bool CalculateMultiCameraPos(
            out Vector3 pos,
            LiveTimelineWorkSheet sheet,
            LiveTimelineKey curKey,
            LiveTimelineKey nextKey,
            float currentFrame,
            int timelineIndex
        )
        {
            if (sheet == null || sheet.multiCameraPosKeys == null || sheet.multiCameraPosKeys.Count <= timelineIndex)
            {
                pos = Vector3.zero;
                return false;
            }

            if (_multiCameraCache == null || timelineIndex >= _multiCameraCache.Length)
            {
                pos = Vector3.zero;
                return false;
            }

            FindTimelineConfig config = default;
            config.curKey = curKey;
            config.nextKey = nextKey;
            config.keyType = FindTimelineConfig.KeyType.KeyDirect;
            config.posKeys = sheet.multiCameraPosKeys[timelineIndex].keys;
            config.lookAtKeys = null;
            config.extraCameraIndex = timelineIndex;

            return CalculateCameraPos(
                out pos,
                sheet,
                currentFrame,
                _multiCameraCache[timelineIndex],
                ref config,
                ref fnGetMultiCameraPositionValueFunc
            );
        }

        public bool CalculateMultiCameraLookAt(
            out Vector3 pos,
            LiveTimelineWorkSheet sheet,
            LiveTimelineKey curKey,
            LiveTimelineKey nextKey,
            float currentFrame,
            int timelineIndex = 0
        )
        {
            if (sheet == null || sheet.multiCameraPosKeys == null || sheet.multiCameraLookAtKeys == null ||
                sheet.multiCameraPosKeys.Count <= timelineIndex || sheet.multiCameraLookAtKeys.Count <= timelineIndex)
            {
                pos = Vector3.zero;
                return false;
            }

            if (_multiCameraCache == null || timelineIndex >= _multiCameraCache.Length)
            {
                pos = Vector3.zero;
                return false;
            }

            FindTimelineConfig config = default;
            config.curKey = curKey;
            config.nextKey = nextKey;
            config.keyType = FindTimelineConfig.KeyType.KeyDirect;
            config.posKeys = sheet.multiCameraPosKeys[timelineIndex].keys;
            config.lookAtKeys = sheet.multiCameraLookAtKeys[timelineIndex].keys;
            config.extraCameraIndex = timelineIndex;

            return CalculateCameraLookAt(
                out pos,
                sheet,
                currentFrame,
                _multiCameraCache[timelineIndex],
                ref config,
                ref fnGetMultiCameraLookAtValueFunc,
                ref fnGetMultiCameraPositionValueFunc
            );
        }

        public void AlterUpdate_MultiCameraLayer(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null || sheet.multiCameraLayerKeys == null || sheet.multiCameraLayerKeys.Count == 0)
            {
                return;
            }

            LiveCameraTransitionDriver driver = GetTransitionDriver();
            int count = sheet.multiCameraLayerKeys.Count;
            for (int i = 0; i < count; i++)
            {
                LiveTimelineMultiCameraLayerData layerData = sheet.multiCameraLayerKeys[i];
                if (layerData == null || layerData.keys == null || layerData.keys.Count == 0)
                {
                    continue;
                }

                LiveTimelineKeyMultiCameraLayerDataList keys = layerData.keys;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    continue;
                }

                FindTimelineKey(out LiveTimelineKey curKeyBase, out LiveTimelineKey nextKeyBase, keys, currentFrame);
                if (curKeyBase == null)
                {
                    continue;
                }

                LiveTimelineKeyMultiCameraLayerData curKey = curKeyBase as LiveTimelineKeyMultiCameraLayerData;
                if (curKey == null)
                {
                    continue;
                }

                LiveTimelineKeyMultiCameraLayerData nextKey = nextKeyBase as LiveTimelineKeyMultiCameraLayerData;

                int cameraNo = curKey.MultiCameraNo;
                MultiCameraComposite.DivideLineType lineType = curKey.LineType;
                float lineThickness = curKey.LineThickness;
                Color lineColor = curKey.LineColor;
                float fadeValue = curKey.FadeValue;
                Vector4 transformParameter = curKey.TransformParameter;
                float maskRoll = curKey.MaskRoll;
                Vector3 offsetMinPos = curKey.offsetMinPosition;
                Vector3 offsetMaxPos = curKey.offsetMaxPosition;

                if (nextKey != null && nextKey.IsInterpolateKey())
                {
                    float t = CalculateInterpolationValue(curKey, nextKey, currentFrame);
                    lineThickness = Mathf.LerpUnclamped(curKey.LineThickness, nextKey.LineThickness, t);
                    lineColor = Color.LerpUnclamped(curKey.LineColor, nextKey.LineColor, t);
                    fadeValue = Mathf.LerpUnclamped(curKey.FadeValue, nextKey.FadeValue, t);
                    transformParameter = Vector4.LerpUnclamped(curKey.TransformParameter, nextKey.TransformParameter, t);
                    maskRoll = Mathf.LerpUnclamped(curKey.MaskRoll, nextKey.MaskRoll, t);
                    offsetMinPos = Vector3.LerpUnclamped(curKey.offsetMinPosition, nextKey.offsetMinPosition, t);
                    offsetMaxPos = Vector3.LerpUnclamped(curKey.offsetMaxPosition, nextKey.offsetMaxPosition, t);
                }

                if (driver != null)
                {
                    driver.ApplyLayer(cameraNo, fadeValue);
                }

                OnUpdateMultiCameraLayer?.Invoke(
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
            }
        }

        private float GetMultiCameraMaskRollFromMaskType(LiveTimelineKeyMultiCameraPositionData.MaskType maskType)
        {
            switch (maskType)
            {
                case LiveTimelineKeyMultiCameraPositionData.MaskType.Down:
                    return 180f;
                case LiveTimelineKeyMultiCameraPositionData.MaskType.Left:
                    return -90f;
                case LiveTimelineKeyMultiCameraPositionData.MaskType.Right:
                    return 90f;
                case LiveTimelineKeyMultiCameraPositionData.MaskType.LeftUp:
                    return -45f;
                case LiveTimelineKeyMultiCameraPositionData.MaskType.RightUp:
                    return 45f;
                case LiveTimelineKeyMultiCameraPositionData.MaskType.LeftDown:
                    return -135f;
                case LiveTimelineKeyMultiCameraPositionData.MaskType.RightDown:
                    return 135f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 多机位 Switcher：只把 fadeTime 换成权重，不再 SetActive 把别的路掐掉。
        /// </summary>
        private bool AlterUpdate_MultiCameraSwitcher(
            LiveTimelineWorkSheet sheet,
            LiveTimelineKeyMultiCameraPositionData curData,
            LiveTimelineKeyMultiCameraPositionData nextData,
            int currentFrame,
            int multiCameraIndex,
            out bool isFading)
        {
            isFading = false;
            if (curData == null)
            {
                return false;
            }

            LiveCameraTransitionDriver driver = GetTransitionDriver();
            if (driver == null)
            {
                return false;
            }

            bool isSingleMask = curData.maskType == LiveTimelineKeyMultiCameraPositionData.MaskType.Single;
            bool isScreenDivide = curData.maskType != LiveTimelineKeyMultiCameraPositionData.MaskType.All && !isSingleMask;
            bool updated = driver.ApplySwitcher(
                multiCameraIndex,
                curData.frame,
                curData.fadeTime,
                curData.enableMultiCamera,
                isSingleMask,
                isScreenDivide,
                currentFrame,
                _oldFrame,
                UseFadeTimeSwitcher
            );

            LiveCameraTransitionDriver.ChannelState channel = driver.GetChannel(multiCameraIndex);
            isFading = channel.IsFading;
            return updated;
        }

        private bool UpdateMultiCameraSwitcher(
            LiveTimelineKeyMultiCameraPositionData curData,
            float currentFrame,
            out bool isFading,
            out float fadingValue)
        {
            bool ret = AlterUpdate_MultiCameraSwitcher(null, curData, null, (int)currentFrame, 0, out isFading);
            LiveCameraTransitionDriver driver = GetTransitionDriver();
            fadingValue = driver != null ? driver.GetDisplayWeight(0) : (curData != null && curData.enableMultiCamera ? 1f : 0f);
            return ret;
        }

        public void AlterUpdate_MultiCameraPosition(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null || sheet.multiCameraPosKeys == null || _multiCameraCache == null)
            {
                return;
            }

            int count = sheet.multiCameraPosKeys.Count;
            if (count == 0)
            {
                return;
            }

            LiveCameraTransitionDriver driver = GetTransitionDriver();
            Director director = Director.instance;

            for (int i = 0; i < count; i++)
            {
                int timelineIndex = i;
                if (timelineIndex >= _multiCameraCache.Length)
                {
                    break;
                }

                LiveTimelineKeyMultiCameraPositionDataList keys = sheet.multiCameraPosKeys[timelineIndex].keys;
                if (keys == null || keys.Count == 0)
                {
                    continue;
                }

                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    continue;
                }

                MultiCameraComposite multiCameraComposition = director != null ? director.GetMultiCameraComposite(timelineIndex) : null;
                if (multiCameraComposition != null && multiCameraComposition.RenderCamera != null)
                {
                    multiCameraComposition.RenderCamera.depth = timelineIndex + 1f;
                }

                FindTimelineKey(out LiveTimelineKey curKey, out LiveTimelineKey nextKey, keys, currentFrame);
                if (curKey == null)
                {
                    continue;
                }

                LiveTimelineKeyMultiCameraPositionData curPosKey = curKey as LiveTimelineKeyMultiCameraPositionData;
                LiveTimelineKeyMultiCameraPositionData nextPosKey = nextKey as LiveTimelineKeyMultiCameraPositionData;
                if (curPosKey == null)
                {
                    continue;
                }

                bool isFading;
                bool updated = AlterUpdate_MultiCameraSwitcher(sheet, curPosKey, nextPosKey, (int)currentFrame, timelineIndex, out isFading);

                bool isSingleMask = curPosKey.maskType == LiveTimelineKeyMultiCameraPositionData.MaskType.Single;
                bool isScreenDivide = curPosKey.maskType != LiveTimelineKeyMultiCameraPositionData.MaskType.All && !isSingleMask;
                if (driver != null)
                {
                    driver.ApplyScreenDivide(timelineIndex, isScreenDivide, isSingleMask);
                }
                if (multiCameraComposition != null)
                {
                    multiCameraComposition.IsScreenDivide = isScreenDivide;
                }

                if (!curPosKey.enableMultiCamera && !updated && !isFading)
                {
                    continue;
                }

                CacheCamera cacheCamera = _multiCameraCache[timelineIndex];
                if (cacheCamera == null || cacheCamera.camera == null)
                {
                    continue;
                }

                Camera camera = cacheCamera.camera;
                if (curPosKey.IsEnabledBgColor)
                {
                    camera.backgroundColor = curPosKey.GetBgColor();
                }

                _isMultiCameraEnable = true;

                float zAngle;
                float fieldOfView;
                Vector3 maskOffset;
                float maskRoll;

                if (nextPosKey != null && nextPosKey.interpolateType != 0)
                {
                    float t = CalculateInterpolationValue(curPosKey, nextPosKey, currentFrame);
                    fieldOfView = LerpWithoutClamp(curPosKey.fov, nextPosKey.fov, t);
                    maskOffset = LerpWithoutClamp(curPosKey.maskOffset, nextPosKey.maskOffset, t);
                    maskRoll = LerpWithoutClamp(curPosKey.maskRoll, nextPosKey.maskRoll, t);
                    zAngle = LerpWithoutClamp(curPosKey.roll, nextPosKey.roll, t);
                    if (multiCameraComposition != null)
                    {
                        multiCameraComposition.LineThickness = LerpWithoutClamp(curPosKey.lineThickness, nextPosKey.lineThickness, t);
                        multiCameraComposition.LineColor = Color.LerpUnclamped(curPosKey.LineColor, nextPosKey.LineColor, t);
                        multiCameraComposition.LineAntialiasing = LerpWithoutClamp(curPosKey.LineAntialiasing, nextPosKey.LineAntialiasing, t);
                    }
                }
                else
                {
                    fieldOfView = curPosKey.fov;
                    maskOffset = curPosKey.maskOffset;
                    maskRoll = curPosKey.maskRoll;
                    zAngle = curPosKey.roll;
                    if (multiCameraComposition != null)
                    {
                        multiCameraComposition.LineThickness = curPosKey.lineThickness;
                        multiCameraComposition.LineColor = curPosKey.LineColor;
                        multiCameraComposition.LineAntialiasing = curPosKey.LineAntialiasing;
                    }
                }

                if (multiCameraComposition != null)
                {
                    multiCameraComposition.LineType = curPosKey.LineType;
                }

                camera.nearClipPlane = curPosKey.nearClip;
                camera.farClipPlane = curPosKey.farClip;
                camera.fieldOfView = fieldOfView;

                float baseMaskRoll = GetMultiCameraMaskRollFromMaskType(curPosKey.maskType);
                float normalizedMaskRoll = (baseMaskRoll + maskRoll) / 360f;

                if (multiCameraComposition != null)
                {
                    multiCameraComposition.TransformParameter = new Vector4(
                        maskOffset.x,
                        maskOffset.y,
                        normalizedMaskRoll,
                        multiCameraComposition.TransformParameter.w
                    );
                    multiCameraComposition.MaskRoll = baseMaskRoll + maskRoll;
                }

                if (CalculateMultiCameraPos(out Vector3 pos, sheet, curKey, nextKey, currentFrame, timelineIndex))
                {
                    if (cacheCamera.cacheTransform != null)
                    {
                        cacheCamera.cacheTransform.position = pos;
                        cacheCamera.cacheTransform.localRotation = Quaternion.Euler(0f, 0f, zAngle);
                    }
                }
                else if (cacheCamera.cacheTransform != null)
                {
                    cacheCamera.cacheTransform.localRotation = Quaternion.Euler(0f, 0f, zAngle);
                }

                if (_multiCamera != null && timelineIndex < _multiCamera.Length && _multiCamera[timelineIndex] != null)
                {
                    if (_multiCamera[timelineIndex].maskIndex >= 0)
                    {
                        _multiCamera[timelineIndex].MaskOffset = maskOffset;
                        _multiCamera[timelineIndex].MaskRoll = baseMaskRoll + maskRoll;
                    }
                }

                OnUpdateMultiCameraPosition?.Invoke(
                    timelineIndex,
                    cacheCamera.cacheTransform != null ? cacheCamera.cacheTransform.position : Vector3.zero,
                    fieldOfView,
                    zAngle,
                    maskOffset,
                    maskRoll,
                    curPosKey.maskType,
                    curPosKey.enableMultiCamera
                );
            }

        }

        public void AlterUpdate_MultiCameraLookAt(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null || sheet.multiCameraLookAtKeys == null || _multiCameraCache == null)
            {
                return;
            }

            int count = sheet.multiCameraLookAtKeys.Count;
            for (int i = 0; i < count; i++)
            {
                if (i >= _multiCameraCache.Length)
                {
                    break;
                }

                LiveTimelineKeyMultiCameraLookAtDataList keys = sheet.multiCameraLookAtKeys[i].keys;
                if (keys == null || keys.Count == 0)
                {
                    continue;
                }

                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    continue;
                }

                FindTimelineKey(out LiveTimelineKey curKey, out LiveTimelineKey nextKey, keys, currentFrame);
                if (curKey != null && CalculateMultiCameraLookAt(out Vector3 lookAtPos, sheet, curKey, nextKey, currentFrame, i))
                {
                    CacheCamera cacheCamera = _multiCameraCache[i];
                    if (cacheCamera != null && cacheCamera.cacheTransform != null)
                    {
                        cacheCamera.cacheTransform.LookAt(lookAtPos);
                    }

                    OnUpdateMultiCameraLookAt?.Invoke(i, lookAtPos);
                }
            }
        }

        public void AlterUpdate_MultiCamera(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null)
            {
                return;
            }

            LiveCameraTransitionDriver driver = GetTransitionDriver();
            if (driver != null)
            {
                int channelCount = 0;
                if (sheet.multiCameraPosKeys != null)
                {
                    channelCount = sheet.multiCameraPosKeys.Count;
                }
                driver.EnsureChannelCount(channelCount);
                driver.BeginFrame((int)currentFrame);
            }

            AlterUpdate_MultiCameraLayer(sheet, currentFrame);

            if (_multiCameraCache != null)
            {
                AlterUpdate_MultiCameraPosition(sheet, currentFrame);
                if (_isMultiCameraEnable)
                {
                    AlterUpdate_MultiCameraLookAt(sheet, currentFrame);
                }
            }

            if (driver != null)
            {
                driver.FinalizeFrame((int)currentFrame);
            }
            if (Director.instance != null)
            {
                Director.instance.ApplyMultiCameraTransitionState();
            }
        }

        private LiveCameraTransitionDriver GetTransitionDriver()
        {
            return Director.instance != null ? Director.instance.CameraTransitionDriver : null;
        }
    }
}
