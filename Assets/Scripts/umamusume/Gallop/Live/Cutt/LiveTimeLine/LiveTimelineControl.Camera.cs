using System;
using System.Collections.Generic;
using UnityEngine;
using Gallop.Live;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// LiveTimelineControl 分部类：负责时间轴镜头切换、机位定位与注视点、FOV以及多机位控制
    /// </summary>
    public partial class LiveTimelineControl : MonoBehaviour
    {
        public Quaternion GetMultiCameraWorldRotation(int index)
        {
            if (_multiCameraCache == null || _multiCameraCache.Length <= index)
            {
                return Quaternion.identity;
            }
            return _multiCameraCache[index].cacheTransform.rotation;
        }

        public Vector3 GetMultiCameraWorldPosition(int index)
        {
            if (_multiCameraCache == null || _multiCameraCache.Length <= index)
            {
                return Vector3.zero;
            }
            return _multiCameraCache[index].cacheTransform.position;
        }


        public bool ExistsMultiCamera(int index)
        {
            if (_multiCameraCache != null && index < _multiCameraCache.Length)
            {
                return _multiCameraCache[index] != null;
            }
            return false;
        }

        public float GetCharacterHeight(LiveCharaPosition position)
        {
            return liveCharactorLocators[(int)position].liveCharaHeightValue;
        }

        public Vector3 GetPositionWithCharacters(LiveCharaPositionFlag posFlags, LiveCameraCharaParts parts, Vector3 charaPos, Vector3 cameraOffset)
        {
            Vector3 retPos = Vector3.zero;
            Vector3 tmpPos = Vector3.zero;
            if (posFlags == 0)
            {
                retPos = liveStageCenterPos + charaPos;
                tmpPos = liveStageCenterPos + charaPos;
            }
            else
            {
                int num = 0;
                switch (parts)
                {
                    case LiveCameraCharaParts.Face:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    Vector3 headPos = liveCharactorLocators[i].liveCharaHeadPosition;
                                    if (headPos.sqrMagnitude < 0.01f || (Mathf.Abs(headPos.x) < 0.01f && Mathf.Abs(headPos.z) < 0.01f))
                                    {
                                        headPos += charaPos;
                                    }
                                    retPos += headPos;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.Waist:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    Vector3 waistPos = liveCharactorLocators[i].liveCharaWaistPosition;
                                    if (waistPos.sqrMagnitude < 0.01f || (Mathf.Abs(waistPos.x) < 0.01f && Mathf.Abs(waistPos.z) < 0.01f))
                                    {
                                        waistPos += charaPos;
                                    }
                                    retPos += waistPos;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.LeftHandWrist:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos += liveCharactorLocators[i].liveCharaLeftHandWristPosition;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.LeftHandAttach:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos += liveCharactorLocators[i].liveCharaLeftHandAttachPosition;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.RightHandWrist:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos += liveCharactorLocators[i].liveCharaRightHandWristPosition;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.RightHandAttach:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos += liveCharactorLocators[i].liveCharaRightHandAttachPosition;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.Chest:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    Vector3 chestPos = liveCharactorLocators[i].liveCharaChestPosition;
                                    if (chestPos.sqrMagnitude < 0.01f || (Mathf.Abs(chestPos.x) < 0.01f && Mathf.Abs(chestPos.z) < 0.01f))
                                    {
                                        chestPos += charaPos;
                                    }
                                    retPos += chestPos;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.Foot:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos += liveCharactorLocators[i].liveCharaFootPosition;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.ConstFaceHeight:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos.y += liveCharactorLocators[i].liveCharaConstHeightHeadPosition.y;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    retPos += charaPos;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.ConstWaistHeight:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos.y += liveCharactorLocators[i].liveCharaConstHeightWaistPosition.y;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    retPos += charaPos;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.ConstChestHeight:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos.y += liveCharactorLocators[i].liveCharaConstHeightChestPosition.y;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    retPos += charaPos;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.ConstFootHeight:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    retPos.y += liveCharactorLocators[i].liveCharaFootPosition.y;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    retPos += charaPos;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.InitFaceHeight:
                        {
                            // 核心修复：对齐官方原版反编译实现，直接获取角色头部骨骼世界坐标，并在骨骼尚未就绪时以 charaPos 作为补偿
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    Vector3 headPos = liveCharactorLocators[i].liveCharaHeadPosition;
                                    if (headPos.sqrMagnitude < 0.01f || (Mathf.Abs(headPos.x) < 0.01f && Mathf.Abs(headPos.z) < 0.01f))
                                    {
                                        headPos += charaPos;
                                    }
                                    retPos += headPos;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.InitChestHeight:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    Vector3 chestPos = liveCharactorLocators[i].liveCharaChestPosition;
                                    if (chestPos.sqrMagnitude < 0.01f || (Mathf.Abs(chestPos.x) < 0.01f && Mathf.Abs(chestPos.z) < 0.01f))
                                    {
                                        chestPos += charaPos;
                                    }
                                    retPos += chestPos;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                    case LiveCameraCharaParts.InitWaistHeight:
                        {
                            for (int i = 0; i < liveCharaPositionMax; i++)
                            {
                                if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                                {
                                    Vector3 waistPos = liveCharactorLocators[i].liveCharaWaistPosition;
                                    if (waistPos.sqrMagnitude < 0.01f || (Mathf.Abs(waistPos.x) < 0.01f && Mathf.Abs(waistPos.z) < 0.01f))
                                    {
                                        waistPos += charaPos;
                                    }
                                    retPos += waistPos;
                                    retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    num++;
                                }
                            }
                            break;
                        }
                }
                if (num > 1)
                {
                    retPos /= (float)num;
                }
                else if (num == 0)
                {
                    // 若未命中任何有效角色定位器，安全回退至舞台中心叠加关键帧站位，杜绝归零导致镜头跌入虚空
                    retPos = liveStageCenterPos + charaPos;
                }
                /*
                if ((uint)(parts - 11) <= 2u)
                {
                    if (flag)
                    {
                        tmpPos /= (float)num;
                    }
                    retPos.y = tmpPos.y;
                }
                */
            }
            return retPos;
        }

        public Vector3 GetPositionWithCharacters(LiveCharaPositionFlag posFlags, LiveCameraCharaParts parts, Vector3 charaPos)
        {
            return GetPositionWithCharacters(posFlags, parts, charaPos, _cameraLayerOffset);
        }

        public static float CalculateInterpolationValue(LiveTimelineKey curKey, LiveTimelineKeyWithInterpolate nextKey, float frame)
        {
            float result = 0f;
            switch (nextKey.interpolateType)
            {
                case LiveCameraInterpolateType.Linear:
                    result = LinearInterpolateKeyframes(curKey, nextKey, frame);
                    break;
                case LiveCameraInterpolateType.Curve:
                    result = CurveInterpolateKeyframes(curKey, nextKey, frame);
                    break;
                case LiveCameraInterpolateType.Ease:
                    result = EaseInterpolateKeyframes(curKey, nextKey, frame);
                    break;
            }
            return result;
        }

        public void SetTimelineCamera(Camera cam, int index)
        {
            if (index < _cameraArray.Length)
            {
                if (_cameraArray[index] == null)
                {
                    _cameraArray[index] = new CacheCamera(cam);
                }
                else
                {
                    _cameraArray[index].Set(cam);
                }
                LiveTimelineCamera liveTimelineCamera = cam.gameObject.GetComponent<LiveTimelineCamera>();
                if (liveTimelineCamera == null)
                {
                    liveTimelineCamera = cam.gameObject.AddComponent<LiveTimelineCamera>();
                }
                _cameraScriptArray[index] = liveTimelineCamera;
                if (liveTimelineCamera != null)
                {
                    liveTimelineCamera.AlterAwake();
                }
            }
        }

        private void AlterUpdate_CameraSwitcher(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet.cameraSwitcherKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !sheet.cameraSwitcherKeys.EnablePlayModeTimeline(_playMode))
            {
                return;
            }
            LiveTimelineKey curKey = null;
            FindTimelineKeyCurrent(out curKey, sheet.cameraSwitcherKeys, currentFrame);
            if (curKey == null)
            {
                return;
            }
            LiveTimelineKeyCameraSwitcherData liveTimelineKeyCameraSwitcherData = curKey as LiveTimelineKeyCameraSwitcherData;
            if (this.OnUpdateCameraSwitcher != null)
            {
                this.OnUpdateCameraSwitcher(liveTimelineKeyCameraSwitcherData.cameraIndex);
            }
            else
            {
                if (liveTimelineKeyCameraSwitcherData.cameraIndex >= cameraArray.Length)
                {
                    return;
                }
                for (int i = 0; i < cameraArray.Length; i++)
                {
                    if (cameraArray[i] == null)
                    {
                        continue;
                    }
                    if (i == liveTimelineKeyCameraSwitcherData.cameraIndex)
                    {
                        if (!cameraArray[i].camera.enabled)
                        {
                            cameraArray[i].camera.enabled = true;
                            cameraScriptArray[i].enabled = true;
                        }
                    }
                    else if (cameraArray[i].camera.enabled)
                    {
                        cameraArray[i].camera.enabled = false;
                        cameraScriptArray[i].enabled = false;
                    }
                }
            }
        }

        private void AlterUpdate_CameraPos(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet.cameraPosKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !sheet.cameraPosKeys.EnablePlayModeTimeline(_playMode))
            {
                return;
            }

            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            if (camera == null)
            {
                return;
            }

            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            FindTimelineKey(out curKey, out nextKey, sheet.cameraPosKeys, currentFrame);
            if (curKey == null)
            {
                return;
            }
            LiveTimelineKeyCameraPositionData liveTimelineKeyCameraPositionData = curKey as LiveTimelineKeyCameraPositionData;
            camera.camera.nearClipPlane = liveTimelineKeyCameraPositionData.nearClip;
            camera.camera.farClipPlane = liveTimelineKeyCameraPositionData.farClip;
            if (CalculateCameraPos(out var pos, sheet, curKey, nextKey, currentFrame))
            {
                camera.cacheTransform.position = pos;

                // TODO �����õĻ���CGSS��layerö�٣�Ҫ�����滻
                /*
                int num = liveTimelineKeyCameraPositionData.GetCullingMask();
                if (num == 0)
                {
                    num = LiveTimelineKeyCameraPositionData.GetDefaultCullingMask();
                }
                camera.camera.cullingMask = num;
                if (OnUpdateCameraPos != null)
                {
                    CameraPosUpdateInfo updateInfo = default(CameraPosUpdateInfo);
                    updateInfo.outlineZOffset = liveTimelineKeyCameraPositionData.outlineZOffset;
                    updateInfo.characterLODMask = (int)liveTimelineKeyCameraPositionData.characterLODMask;
                    OnUpdateCameraPos(ref updateInfo);
                }
                */
            }
        }

        private static Vector3 GetCameraPosValue(LiveTimelineKeyCameraPositionData keyData, LiveTimelineControl timelineControl, FindTimelineConfig config)
        {
            return keyData.GetValue(timelineControl);
        }

        public bool CalculateCameraPos(out Vector3 pos, LiveTimelineWorkSheet sheet, float currentFrame)
        {
            FindTimelineConfig config = default(FindTimelineConfig);
            config.curKey = null;
            config.nextKey = null;
            config.keyType = FindTimelineConfig.KeyType.CurrentFrame;
            config.posKeys = sheet.cameraPosKeys;
            config.lookAtKeys = null;
            config.extraCameraIndex = 0;
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            return CalculateCameraPos(out pos, sheet, currentFrame, camera, ref config, ref fnGetCameraPosValue);
        }

        public bool CalculateCameraPos(out Vector3 pos, LiveTimelineWorkSheet sheet, LiveTimelineKey curKey, LiveTimelineKey nextKey, float currentFrame)
        {
            FindTimelineConfig config = default(FindTimelineConfig);
            config.curKey = curKey;
            config.nextKey = nextKey;
            config.keyType = FindTimelineConfig.KeyType.KeyDirect;
            config.posKeys = sheet.cameraPosKeys;
            config.lookAtKeys = null;
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            config.extraCameraIndex = 0;
            return CalculateCameraPos(out pos, sheet, currentFrame, camera, ref config, ref fnGetCameraPosValue);
        }

        public bool CalculateCameraPos(out Vector3 pos, LiveTimelineWorkSheet sheet, float currentFrame, CacheCamera targetCamera, ref FindTimelineConfig config, ref Func<LiveTimelineKeyCameraPositionData, LiveTimelineControl, FindTimelineConfig, Vector3> getFunc)
        {
            pos = Vector3.zero;
            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            if (config.posKeys == null)
            {
                return false;
            }
            if (config.keyType == FindTimelineConfig.KeyType.CurrentFrame)
            {
                FindTimelineKey(out curKey, out nextKey, config.posKeys, currentFrame);
            }
            else
            {
                curKey = config.curKey;
                nextKey = config.nextKey;
            }
            if (curKey == null)
            {
                return false;
            }
            LiveTimelineKeyCameraPositionData liveTimelineKeyCameraPositionData = curKey as LiveTimelineKeyCameraPositionData;
            LiveTimelineKeyCameraPositionData liveTimelineKeyCameraPositionData2 = nextKey as LiveTimelineKeyCameraPositionData;
            if (liveTimelineKeyCameraPositionData2 != null && liveTimelineKeyCameraPositionData2.interpolateType != 0)
            {
                float t = CalculateInterpolationValue(liveTimelineKeyCameraPositionData, liveTimelineKeyCameraPositionData2, currentFrame);
                int bezierPointCount = liveTimelineKeyCameraPositionData2.GetBezierPointCount();
                if (bezierPointCount == 0)
                {
                    pos = LerpWithoutClamp(getFunc(liveTimelineKeyCameraPositionData, this, config), getFunc(liveTimelineKeyCameraPositionData2, this, config), t);
                }
                else
                {
                    BezierCalcWork.cameraPos.Set(getFunc(liveTimelineKeyCameraPositionData, this, config), getFunc(liveTimelineKeyCameraPositionData2, this, config), bezierPointCount);
                    BezierCalcWork.cameraPos.UpdatePoints(liveTimelineKeyCameraPositionData2, this);
                    BezierCalcWork.cameraPos.Calc(bezierPointCount, t, out pos);
                }
            }
            else
            {
                pos = getFunc(liveTimelineKeyCameraPositionData, this, config);
            }
            if (_isNowAlterUpdate && liveTimelineKeyCameraPositionData.attribute.hasFlag(LiveTimelineKeyAttribute.CameraDelayEnable) && (_oldFrame >= liveTimelineKeyCameraPositionData.frame || currentFrame < liveTimelineKeyCameraPositionData.frame || liveTimelineKeyCameraPositionData.attribute.hasFlag(LiveTimelineKeyAttribute.CameraDelayInherit)))
            {
                if (targetCamera == null)
                {
                    return false;
                }
                float t2 = liveTimelineKeyCameraPositionData.traceSpeed * _deltaTimeRatio;
                pos = Vector3.Slerp(targetCamera.cacheTransform.position, pos, t2);
            }
            return true;
        }

        private void AlterUpdate_CameraLookAt(LiveTimelineWorkSheet sheet, float currentFrame, ref Vector3 outLookAt)
        {
            if (!sheet.cameraLookAtKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) && sheet.cameraLookAtKeys.EnablePlayModeTimeline(_playMode))
            {
                CacheCamera camera = GetCamera(sheet.targetCameraIndex);
                if (camera != null && CalculateCameraLookAt(out var lookAtPos, sheet, currentFrame))
                {
                    camera.cacheTransform.LookAt(lookAtPos, Vector3.up);
                    outLookAt = lookAtPos;
                }
            }
        }

        private static Vector3 GetCameraLookAtValue(LiveTimelineKeyCameraLookAtData keyData, LiveTimelineControl timelineControl, Vector3 camPos, FindTimelineConfig config)
        {
            return keyData.GetValue(timelineControl, camPos);
        }

        public bool CalculateCameraLookAt(out Vector3 lookAtPos, LiveTimelineWorkSheet sheet, float currentFrame)
        {
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            FindTimelineConfig config = default(FindTimelineConfig);
            config.curKey = null;
            config.nextKey = null;
            config.keyType = FindTimelineConfig.KeyType.CurrentFrame;
            config.posKeys = sheet.cameraPosKeys;
            config.lookAtKeys = sheet.cameraLookAtKeys;
            config.extraCameraIndex = 0;
            return CalculateCameraLookAt(out lookAtPos, sheet, currentFrame, camera, ref config, ref fnGetCameraLookAtValue, ref fnGetCameraPosValue);
        }

        private bool CalculateCameraLookAt(out Vector3 lookAtPos, LiveTimelineWorkSheet sheet, float currentFrame, CacheCamera targetCamera, ref FindTimelineConfig config, ref Func<LiveTimelineKeyCameraLookAtData, LiveTimelineControl, Vector3, FindTimelineConfig, Vector3> getLookAtValueFunc, ref Func<LiveTimelineKeyCameraPositionData, LiveTimelineControl, FindTimelineConfig, Vector3> getPosValueFunc)
        {
            lookAtPos = Vector3.zero;
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            if (camera == null || config.lookAtKeys == null || config.posKeys == null)
            {
                return false;
            }
            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            FindTimelineKey(out curKey, out nextKey, config.lookAtKeys, currentFrame);
            if (curKey == null)
            {
                return false;
            }
            Vector3 position = camera.cacheTransform.position;
            LiveTimelineKeyCameraLookAtData liveTimelineKeyCameraLookAtData = curKey as LiveTimelineKeyCameraLookAtData;
            LiveTimelineKeyCameraLookAtData liveTimelineKeyCameraLookAtData2 = nextKey as LiveTimelineKeyCameraLookAtData;
            if (liveTimelineKeyCameraLookAtData2 != null && liveTimelineKeyCameraLookAtData2.interpolateType != 0)
            {
                float t = CalculateInterpolationValue(liveTimelineKeyCameraLookAtData, liveTimelineKeyCameraLookAtData2, currentFrame);
                int bezierPointCount = liveTimelineKeyCameraLookAtData2.GetBezierPointCount();
                if (bezierPointCount == 0)
                {
                    lookAtPos = LerpWithoutClamp(getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config), getLookAtValueFunc(liveTimelineKeyCameraLookAtData2, this, position, config), t);
                }
                else if (liveTimelineKeyCameraLookAtData2.necessaryToUseNewBezierCalcMethod)
                {
                    Vector3 zero = Vector3.zero;
                    BezierCalcWork.cameraLookAt.Set(getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config), getLookAtValueFunc(liveTimelineKeyCameraLookAtData2, this, zero, config), bezierPointCount);
                    BezierCalcWork.cameraLookAt.UpdatePoints(liveTimelineKeyCameraLookAtData2, this, zero);
                    BezierCalcWork.cameraLookAt.Calc(bezierPointCount, t, out lookAtPos);
                }
                else
                {
                    Vector3 zero2 = Vector3.zero;
                    Vector3 end = getLookAtValueFunc(liveTimelineKeyCameraLookAtData2, this, zero2, config);
                    Vector3 cp = liveTimelineKeyCameraLookAtData2.GetBezierPoint(0, this, zero2);
                    Vector3 cp2 = liveTimelineKeyCameraLookAtData2.GetBezierPoint(1, this, zero2);
                    Vector3 cp3 = liveTimelineKeyCameraLookAtData2.GetBezierPoint(2, this, zero2);
                    switch (liveTimelineKeyCameraLookAtData2.GetBezierPointCount())
                    {
                        default:
                            lookAtPos = LerpWithoutClamp(getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config), getLookAtValueFunc(liveTimelineKeyCameraLookAtData2, this, position, config), t);
                            break;
                        case 1:
                            {
                                Vector3 start3 = getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config);
                                BezierUtil.Calc(ref start3, ref end, ref cp, t, out lookAtPos);
                                break;
                            }
                        case 2:
                            {
                                Vector3 start2 = getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config);
                                BezierUtil.Calc(ref start2, ref end, ref cp, ref cp2, t, out lookAtPos);
                                break;
                            }
                        case 3:
                            {
                                Vector3 start = getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config);
                                BezierUtil.Calc(ref start, ref end, ref cp, ref cp2, ref cp3, t, out lookAtPos);
                                break;
                            }
                    }
                }
            }
            else
            {
                lookAtPos = getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config);
            }
            if (_isNowAlterUpdate && liveTimelineKeyCameraLookAtData.attribute.hasFlag(LiveTimelineKeyAttribute.CameraDelayEnable) && (_oldFrame >= liveTimelineKeyCameraLookAtData.frame || currentFrame < liveTimelineKeyCameraLookAtData.frame || liveTimelineKeyCameraLookAtData.attribute.hasFlag(LiveTimelineKeyAttribute.CameraDelayInherit)))
            {
                Vector3 b = lookAtPos - camera.cacheTransform.position;
                float magnitude = b.magnitude;
                if (magnitude >= float.Epsilon)
                {
                    b /= magnitude;
                    float t2 = liveTimelineKeyCameraLookAtData.traceSpeed * _deltaTimeRatio;
                    lookAtPos = position + Vector3.Slerp(camera.cacheTransform.forward, b, t2) * magnitude;
                }
            }
            return true;
        }

        private void AlterUpdate_CameraFov(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet.cameraFovKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !sheet.cameraFovKeys.EnablePlayModeTimeline(_playMode))
            {
                return;
            }
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            if (camera == null)
            {
                return;
            }
            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            FindTimelineKey(out curKey, out nextKey, sheet.cameraFovKeys, currentFrame);
            if (curKey == null)
            {
                return;
            }
            LiveTimelineKeyCameraFovData liveTimelineKeyCameraFovData = curKey as LiveTimelineKeyCameraFovData;
            LiveTimelineKeyCameraFovData liveTimelineKeyCameraFovData2 = nextKey as LiveTimelineKeyCameraFovData;
            float num = 80f;
            if (liveTimelineKeyCameraFovData2 != null && liveTimelineKeyCameraFovData2.interpolateType != 0)
            {
                float t = CalculateInterpolationValue(liveTimelineKeyCameraFovData, liveTimelineKeyCameraFovData2, currentFrame);
                num = LerpWithoutClamp(liveTimelineKeyCameraFovData.fov, liveTimelineKeyCameraFovData2.fov, t);
            }
            else if (liveTimelineKeyCameraFovData.fovType == LiveCameraFovType.Direct)
            {
                num = liveTimelineKeyCameraFovData.fov;
            }
            if (_limitFovForWidth)
            {
                float num2 = (float)camera.camera.pixelWidth / (float)camera.camera.pixelHeight;
                if (num2 > _baseCameraAspectRatio)
                {
                    float num3 = num2 / _baseCameraAspectRatio;
                    num /= num3;
                }
            }
            camera.camera.fieldOfView = num;
        }

        private void AlterUpdate_CameraRoll(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet.cameraRollKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !sheet.cameraRollKeys.EnablePlayModeTimeline(_playMode))
            {
                return;
            }
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            if (camera == null)
            {
                return;
            }
            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            FindTimelineKey(out curKey, out nextKey, sheet.cameraRollKeys, currentFrame);
            if (curKey != null)
            {
                LiveTimelineKeyCameraRollData liveTimelineKeyCameraRollData = curKey as LiveTimelineKeyCameraRollData;
                LiveTimelineKeyCameraRollData liveTimelineKeyCameraRollData2 = nextKey as LiveTimelineKeyCameraRollData;
                float num = 80f;
                if (liveTimelineKeyCameraRollData2 != null && liveTimelineKeyCameraRollData2.interpolateType != 0)
                {
                    float t = CalculateInterpolationValue(liveTimelineKeyCameraRollData, liveTimelineKeyCameraRollData2, currentFrame);
                    num = LerpWithoutClamp(liveTimelineKeyCameraRollData.degree, liveTimelineKeyCameraRollData2.degree, t);
                }
                else
                {
                    num = liveTimelineKeyCameraRollData.degree;
                }
                camera.cacheTransform.Rotate(0f, 0f, num);
            }
        }

        //public void SetMultiCamera(MultiCameraManager manager, MultiCamera[] multiCamera)
        public void SetMultiCamera(MultiCamera[] multiCamera)
        {
            _multiCamera = multiCamera;
            //_multiCameraManager = manager;
            if (multiCamera != null)
            {
                _multiCameraCache = new CacheCamera[multiCamera.Length];
                for (int i = 0; i < multiCamera.Length; i++)
                {
                    _multiCameraCache[i] = new CacheCamera(multiCamera[i].GetCamera());
                }
            }
        }
    }
}

