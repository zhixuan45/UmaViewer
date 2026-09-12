using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// LiveTimelineControl 分部类：Live 多机位（Multi-Camera）分屏插值控制器与图层调度器
    /// 负责解析多机位分屏图层分割线、多路分屏相机的空间坐标、视野 FOV、旋转 Roll、朝向 LookAt 及掩码位移插值
    /// </summary>
    public partial class LiveTimelineControl : MonoBehaviour
    {
        /// <summary>
        /// 多机位分屏图层与分割线参数更新委托
        /// </summary>
        /// <param name="cameraNo">多机位通道编号</param>
        /// <param name="lineType">分割线类型（Fade 渐变或 Color 纯色）</param>
        /// <param name="lineThickness">分割线宽度/厚度</param>
        /// <param name="lineColor">分割线颜色</param>
        /// <param name="fadeValue">淡入淡出透明度权重 (0~1)</param>
        /// <param name="transformParameter">分屏遮罩与 UV 变换参数 (X, Y: 偏移; Z: 旋转; W: 缩放/扩展)</param>
        /// <param name="maskRoll">分屏遮罩旋转欧拉角</param>
        /// <param name="offsetMinPos">分屏视图最小边界坐标偏移</param>
        /// <param name="offsetMaxPos">分屏视图最大边界坐标偏移</param>
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

        /// <summary>
        /// 多机位图层分割线及分屏材质参数更新事件
        /// </summary>
        public event MultiCameraLayerUpdateDelegate OnUpdateMultiCameraLayer;

        /// <summary>
        /// 多机位相机位置与投影参数更新委托
        /// </summary>
        /// <param name="cameraIndex">多机位通道索引</param>
        /// <param name="position">相机局部空间坐标</param>
        /// <param name="fov">相机垂直视野角 (Field of View)</param>
        /// <param name="roll">相机 Z 轴倾斜旋转角 (Roll)</param>
        /// <param name="maskOffset">分屏遮罩 UV 偏移</param>
        /// <param name="maskRoll">分屏遮罩局部旋转角</param>
        /// <param name="maskType">分屏遮罩布局预设类型</param>
        /// <param name="enableMultiCamera">多机位是否启用激活</param>
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

        /// <summary>
        /// 多机位相机位置、FOV 与遮罩参数更新事件
        /// </summary>
        public event MultiCameraPositionUpdateDelegate OnUpdateMultiCameraPosition;

        /// <summary>
        /// 多机位相机目标注视点更新委托
        /// </summary>
        /// <param name="cameraIndex">多机位通道索引</param>
        /// <param name="lookAtPosition">相机目标注视的世界/基准坐标</param>
        public delegate void MultiCameraLookAtUpdateDelegate(
            int cameraIndex,
            Vector3 lookAtPosition
        );

        /// <summary>
        /// 多机位相机目标注视点 (LookAt) 更新事件
        /// </summary>
        public event MultiCameraLookAtUpdateDelegate OnUpdateMultiCameraLookAt;

        /// <summary>
        /// 获取多机位相机位置关键帧的计算基准坐标值
        /// </summary>
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

        /// <summary>
        /// 获取多机位相机注视点关键帧的计算基准坐标值
        /// </summary>
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

        /// <summary>
        /// 计算指定通道多机位相机的空间坐标（支持路径定位器与相对偏移）
        /// </summary>
        /// <param name="pos">输出计算后的空间坐标</param>
        /// <param name="sheet">当前时间轴工作表</param>
        /// <param name="curKey">当前关键帧</param>
        /// <param name="nextKey">下一关键帧</param>
        /// <param name="currentFrame">当前时间轴帧</param>
        /// <param name="timelineIndex">机位通道索引</param>
        /// <returns>计算成功则返回 true</returns>
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

        /// <summary>
        /// 计算指定通道多机位相机的注视点空间坐标
        /// </summary>
        /// <param name="pos">输出注视点世界坐标</param>
        /// <param name="sheet">当前时间轴工作表</param>
        /// <param name="curKey">当前关键帧</param>
        /// <param name="nextKey">下一关键帧</param>
        /// <param name="currentFrame">当前时间轴帧</param>
        /// <param name="timelineIndex">机位通道索引</param>
        /// <returns>计算成功则返回 true</returns>
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

        /// <summary>
        /// 调度并更新所有多机位分屏图层（MultiCameraLayer）参数与分割线插值
        /// </summary>
        /// <param name="sheet">当前时间轴工作表</param>
        /// <param name="currentFrame">当前时间轴帧</param>
        public void AlterUpdate_MultiCameraLayer(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null || sheet.multiCameraLayerKeys == null || sheet.multiCameraLayerKeys.Count == 0)
            {
                return;
            }

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

                // 若下一关键帧启用插值，则计算两帧之间的平滑过渡
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

                // 派发多机位图层分割线与遮罩参数更新事件
                _multiCameraLayerDrove = true;
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

        /// <summary>
        /// 是否启用「按 fadeTime 推进的多机位切换淡变」。
        /// 官方在每路机位的切换器里用关键帧上的 fadeTime 换算淡变帧数，
        /// 这是原版机位切换时那段短渐变过渡的来源。关掉可立刻对比排查。
        /// </summary>
        public static bool UseFadeTimeSwitcher = true;

        // 定位用的有界日志计数（确认 fadeTime 到底有没有值）
        private static int _switcherNonZeroLogCount;
        private static int _switcherZeroLogCount;
        private static int _switcherCompositeNullLogCount;

        // 本帧多机位图层轨道是否真的下发过参数（下发了就由它主导分屏权重，切换器不抢）
        private bool _multiCameraLayerDrove;

        /// <summary>
        /// 根据分屏遮罩预设类型获取基准旋转欧拉角（度数）
        /// 参考官方 LiveTimelineControl.GetMultiCameraMaskRollFromMaskType (:4695)
        /// </summary>
        /// <param name="maskType">分屏遮罩类型</param>
        /// <returns>基准旋转欧拉角度数</returns>
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
        /// 多机位切换器（对应官方 AlterUpdate_MultiCameraSwitcher 的语义及实现 :4267-4342）。
        /// 负责按关键帧 fadeTime 换算淡变帧数推进淡入淡出权重插值、控制合成对象的激活状态 SetActive，
        /// 并在淡变结束后同步稳态权重。
        /// </summary>
        /// <param name="sheet">当前时间轴工作表</param>
        /// <param name="curData">当前机位位置关键帧</param>
        /// <param name="nextData">下一机位位置关键帧</param>
        /// <param name="currentFrame">当前帧号</param>
        /// <param name="multiCameraIndex">多机位通道索引</param>
        /// <param name="isFading">输出是否处于淡变中或刚跨越淡变边界</param>
        /// <returns>本帧是否刚刚跨过淡变结束帧边界（用于判定本帧是否有状态突变）</returns>
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

            // 消除多机位互踩：通过 multiCameraIndex 获取当前机位专用的 MultiCameraComposite 实例
            MultiCameraComposite multiCameraComposition = Director.instance != null ? Director.instance.GetMultiCameraComposite(multiCameraIndex) : null;
            if (multiCameraComposition == null)
            {
                return false;
            }

            int fadeFrame = (int)Math.Round(curData.fadeTime * kTargetFpsF);
            int fadeEndFrame = curData.frame + fadeFrame;

            // 淡变已经结束
            if (currentFrame >= fadeEndFrame)
            {
                float fadeValue = curData.enableMultiCamera ? 1f : 0f;

                if (curData.maskType != LiveTimelineKeyMultiCameraPositionData.MaskType.Single)
                {
                    multiCameraComposition.gameObject.SetActive(curData.enableMultiCamera);
                }
                else
                {
                    multiCameraComposition.gameObject.SetActive(false);
                }

                multiCameraComposition.FadeValue = fadeValue;
                multiCameraComposition.ApplySwitcherFade(fadeValue);

                // 本帧刚刚跨过淡变结束帧
                if (_oldFrame < fadeEndFrame)
                {
                    isFading = true;
                    return true;
                }

                return false;
            }

            // 淡变进行中
            isFading = true;

            float fadeFrom;
            float fadeTo;

            if (curData.enableMultiCamera && curData.maskType != LiveTimelineKeyMultiCameraPositionData.MaskType.Single)
            {
                fadeFrom = 0f;
                fadeTo = 1f;
            }
            else
            {
                fadeFrom = 1f;
                fadeTo = 0f;
            }

            float rate = fadeFrame > 0 ? (float)(currentFrame - curData.frame) / fadeFrame : 1f;

            // 播放头本帧刚进入这个 Key 时，先启用合成对象
            // 即使是淡出，也需要先保持对象启用才能显示淡出过程
            if (_oldFrame < curData.frame)
            {
                multiCameraComposition.gameObject.SetActive(true);
            }

            float currentFade = Mathf.Lerp(fadeFrom, fadeTo, Mathf.Clamp01(rate));
            multiCameraComposition.FadeValue = currentFade;
            multiCameraComposition.ApplySwitcherFade(currentFade);

            return false;
        }

        /// <summary>
        /// 兼容保留辅助方法：转调 AlterUpdate_MultiCameraSwitcher
        /// </summary>
        private bool UpdateMultiCameraSwitcher(
            LiveTimelineKeyMultiCameraPositionData curData,
            float currentFrame,
            out bool isFading,
            out float fadingValue)
        {
            bool ret = AlterUpdate_MultiCameraSwitcher(null, curData, null, (int)currentFrame, 0, out isFading);
            MultiCameraComposite comp = Director.instance != null ? Director.instance.GetMultiCameraComposite(0) : null;
            fadingValue = comp != null ? comp.FadeValue : (curData != null && curData.enableMultiCamera ? 1f : 0f);
            return ret;
        }

        /// <summary>
        /// 驱动并计算所有多机位相机的空间位置、FOV、Roll 角度及分屏遮罩偏移
        /// 严格对齐官方 LiveTimelineControl.AlterUpdate_MultiCameraPosition (:3998-4220)
        /// </summary>
        /// <param name="sheet">当前时间轴工作表</param>
        /// <param name="currentFrame">当前时间轴帧</param>
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

            bool isSingle = false;
            float singleFadeValue = 0f;
            bool captureFrame = false;

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

                // 核心修复：通过 timelineIndex 获取当前机位独立的 MultiCameraComposite 实例，彻底消除 Track 0 与 Track 1 互相覆写踩踏
                MultiCameraComposite multiCameraComposition = Director.instance != null ? Director.instance.GetMultiCameraComposite(timelineIndex) : null;

                // 官方 :4053 设置当前机位相机深度：RenderCamera.depth = timelineIndex + 1f;
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

                // 官方多机位切换器：按 timelineIndex 独立推进淡变状态与 SetActive 处理
                bool isFading;
                bool updated = AlterUpdate_MultiCameraSwitcher(sheet, curPosKey, nextPosKey, (int)currentFrame, timelineIndex, out isFading);

                // 官方 :4067 如果未启用多机位、本帧未刚跨过淡变结束、且不在淡变中，则跳过
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

                // 官方 :4082 修复背景色应用：if (curPosKey.IsEnabledBgColor) camera.backgroundColor = curPosKey.GetBgColor();
                if (curPosKey.IsEnabledBgColor)
                {
                    camera.backgroundColor = curPosKey.GetBgColor();
                }

                // 官方 :4094-4116 修复分屏开关 IsScreenDivide 与单画面淡变
                if (multiCameraComposition != null)
                {
                    if (curPosKey.maskType == LiveTimelineKeyMultiCameraPositionData.MaskType.All)
                    {
                        multiCameraComposition.IsScreenDivide = false;
                    }
                    else if (curPosKey.maskType == LiveTimelineKeyMultiCameraPositionData.MaskType.Single)
                    {
                        int fadeEndFrame = curPosKey.frame + (int)Math.Round(curPosKey.fadeTime * kTargetFpsF);
                        if (currentFrame < fadeEndFrame)
                        {
                            isSingle = true;
                            singleFadeValue = multiCameraComposition.FadeValue;
                            if (_oldFrame < curPosKey.frame)
                            {
                                captureFrame = true;
                            }
                        }
                    }
                    else
                    {
                        multiCameraComposition.IsScreenDivide = true;
                    }
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
                }
                else
                {
                    fieldOfView = curPosKey.fov;
                    maskOffset = curPosKey.maskOffset;
                    maskRoll = curPosKey.maskRoll;
                    zAngle = curPosKey.roll;
                }

                camera.nearClipPlane = curPosKey.nearClip;
                camera.farClipPlane = curPosKey.farClip;
                camera.fieldOfView = fieldOfView;

                // 官方 :4157 遮罩角度归一化：float baseMaskRoll = GetMultiCameraMaskRollFromMaskType(curPosKey.maskType); float normalizedMaskRoll = (baseMaskRoll + maskRoll) / 360f; 并将 normalizedMaskRoll 传入 TransformParameter.z
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

                // 官方 :4179 计算相机坐标
                if (CalculateMultiCameraPos(out Vector3 pos, sheet, curKey, nextKey, currentFrame, timelineIndex))
                {
                    // 官方 :4154 修复相机旋转：绝对欧拉角旋转 Quaternion.Euler(0f, 0f, zAngle)，杜绝每帧累加
                    // 官方 :4197 修复相机坐标：使用绝对世界坐标 position = pos，杜绝 localPosition 相对父节点偏移误差
                    if (cacheCamera.cacheTransform != null)
                    {
                        cacheCamera.cacheTransform.position = pos;
                        cacheCamera.cacheTransform.localRotation = Quaternion.Euler(0f, 0f, zAngle);
                    }
                }
                else
                {
                    if (cacheCamera.cacheTransform != null)
                    {
                        cacheCamera.cacheTransform.localRotation = Quaternion.Euler(0f, 0f, zAngle);
                    }
                }

                // 同步遮罩偏移和旋转属性到对应的 MultiCamera 组件
                if (_multiCamera != null && timelineIndex < _multiCamera.Length && _multiCamera[timelineIndex] != null)
                {
                    if (_multiCamera[timelineIndex].maskIndex >= 0)
                    {
                        _multiCamera[timelineIndex].MaskOffset = maskOffset;
                        _multiCamera[timelineIndex].MaskRoll = baseMaskRoll + maskRoll;
                    }
                }

                // 派发多机位相机位置与投影事件
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

            // 官方 :4217-4236 循环结束后处理单画面 (isSingle) 或各多机位激活与休眠状态
            if (Director.instance != null)
            {
                if (isSingle)
                {
                    // 单画面淡变期间或结束时，禁用所有多机位合成器并强制休眠相机
                    MultiCameraComposite[] composites = Director.instance.MultiCameraComposites;
                    if (composites != null)
                    {
                        for (int c = 0; c < composites.Length; c++)
                        {
                            if (composites[c] != null)
                            {
                                composites[c].gameObject.SetActive(false);
                            }
                        }
                    }
                    Director.instance.SetMultiCamerasEnabled(false);
                }
                else
                {
                    // 检查各机位合成器的独立激活状态与权重，按需唤醒或休眠
                    MultiCameraComposite[] composites = Director.instance.MultiCameraComposites;
                    if (composites != null)
                    {
                        bool anyActive = false;
                        for (int c = 0; c < composites.Length; c++)
                        {
                            MultiCameraComposite comp = composites[c];
                            if (comp != null)
                            {
                                bool compActive = comp.gameObject.activeSelf && (comp.IsCompositeActive || comp.FadeValue > 0.001f);
                                Director.instance.SetMultiCameraEnabled(c, compActive);
                                if (compActive)
                                {
                                    anyActive = true;
                                }
                            }
                        }
                        if (!anyActive)
                        {
                            Director.instance.SetMultiCamerasEnabled(false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 驱动并计算所有多机位相机的目标注视点 (LookAt)
        /// </summary>
        /// <param name="sheet">当前时间轴工作表</param>
        /// <param name="currentFrame">当前时间轴帧</param>
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

                    // 派发多机位注视点事件
                    OnUpdateMultiCameraLookAt?.Invoke(i, lookAtPos);
                }
            }
        }

        /// <summary>
        /// 统一驱动多机位主调度入口：包含分屏图层分割线、各相机位姿及注视点
        /// </summary>
        /// <param name="sheet">当前时间轴工作表</param>
        /// <param name="currentFrame">当前时间轴帧</param>
        public void AlterUpdate_MultiCamera(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null)
            {
                return;
            }

            // 1. 优先驱动多机位分屏图层分割线与渲染参数
            _multiCameraLayerDrove = false;
            AlterUpdate_MultiCameraLayer(sheet, currentFrame);

            // 2. 驱动各路分屏相机位置与朝向注视
            if (_multiCameraCache != null)
            {
                AlterUpdate_MultiCameraPosition(sheet, currentFrame);
                if (_isMultiCameraEnable)
                {
                    AlterUpdate_MultiCameraLookAt(sheet, currentFrame);
                }
            }
        }
    }
}
