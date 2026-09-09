using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gallop.Live;

namespace Gallop.Live.Cutt
{
    /// <summary>
    /// Live 时间轴角色动作序列驱动器，负责按帧采样动作关键帧并应用到角色模型 Animation 组件
    /// </summary>
    public class LiveTimelineMotionSequence
    {
        private ILiveTimelineMSQTarget _target; // 0x10
        private Animation[] _animationArray; // 0x18
        private int _targetIndex; // 0x20
        private LiveTimelineKeyCharaMotionSeqDataList[] _keyArray; // 0x28
        private LiveTimelineKeyCharaMotionSeqDataList _currentKey; // 0x30
        private int _indexOfPlayingKey; // 0x38
        private LiveTimelineControl _timelineControl; // 0x40
        private float _heightRate; // 0x48
        private LiveTimelineDefine.SheetIndex _currentSheetIndex; // 0x4C
        //private LivePlayableAnimator[] _playableAnimatorArray; // 0x50
        private int _prevSysTextId; // 0x58
        private const float OVERRIDE_ANIMATOR_FACIAL_WAIT = 1;
        private int _prevFrame; // 0x5C
        private float _prevFrameAnimationTime; // 0x60

        //Testing
        private Transform _tempTarget;
        private Animation _tempAnim;

        private bool _needChange;
        private LiveTimelineKeyCharaMotionData _prevKey;

        private AnimationClip _targetAnim = null;

        private bool _motionSetup = false;

        private int _curIndex = -1;
        private int _prevIndex = -1;

        public int charaIndex = -1;

        /// <summary>
        /// 初始化角色的动作序列配置与动画组件，并执行严格的组件与关键帧动作资源校验
        /// </summary>
        /// <param name="target">角色根变换节点</param>
        /// <param name="targetIndex">角色索引编号（0-indexed）</param>
        /// <param name="seqDataIndex">时间轴动作数据序列索引</param>
        /// <param name="timelineControl">Live 时间轴总控制器</param>
        /// <param name="animclips">当处于 LiveMode 0（纯动作模式）时传入的整首动作剪辑列表</param>
        public void Initialize(Transform target, int targetIndex, int seqDataIndex, LiveTimelineControl timelineControl, List<AnimationClip> animclips = null)
        {
            _tempTarget = target;
            _targetIndex = targetIndex;
            charaIndex = targetIndex;
            _timelineControl = timelineControl;

            var director = Director.instance;

            // 1. 优先获取角色的 Animation 组件
            if (director != null && director.charaAnims != null && targetIndex < director.charaAnims.Count)
            {
                _tempAnim = director.charaAnims[targetIndex];
            }

            // 2. 校验 Animation 动画组件是否存在，若缺失则记录严重错误并弹窗警告，防止静默失败
            if (_tempAnim == null)
            {
                Debug.LogError($"[LiveMotion] 角色 {targetIndex + 1} 动画组件 Animation 丢失！");
                UmaErrorManager.ShowUIMessage($"[Live动作异常] 角色 {targetIndex + 1} 缺少动作组件 Animation，将无法播放动作", UIMessageType.Error);
            }

            // 3. 分支判断：LiveMode 0 独立动作播放模式
            bool isLiveMode0 = (director != null && director.liveMode == 0) || animclips != null;
            if (isLiveMode0)
            {
                if (animclips == null || animclips.Count == 0)
                {
                    Debug.LogError($"[LiveMotion] LiveMode 0 模式下角色 {targetIndex + 1} 传入动作剪辑列表为空！(歌曲: {director?.live?.MusicId})");
                    UmaErrorManager.ShowUIMessage($"[Live动作异常] 角色 {targetIndex + 1} 动作数据缺失，角色将保持初始姿态(T-pose)，请检查对应 Live 动作资源是否下载完整", UIMessageType.Error);
                }
                else if (targetIndex >= animclips.Count)
                {
                    Debug.LogError($"[LiveMotion] LiveMode 0 动作剪辑数量不足 ({animclips.Count})，无法覆盖角色 {targetIndex + 1} 的站位 (歌曲: {director?.live?.MusicId})");
                    UmaErrorManager.ShowUIMessage($"[Live动作异常] 角色 {targetIndex + 1} 动作数据缺失，角色将保持初始姿态(T-pose)，请检查对应 Live 动作资源是否下载完整", UIMessageType.Error);
                }
                else
                {
                    _targetAnim = animclips[targetIndex];
                    if (_targetAnim == null)
                    {
                        Debug.LogError($"[LiveMotion] LiveMode 0 角色 {targetIndex + 1} 动作剪辑为空！(歌曲: {director?.live?.MusicId})");
                        UmaErrorManager.ShowUIMessage($"[Live动作异常] 角色 {targetIndex + 1} 动作数据缺失，角色将保持初始姿态(T-pose)，请检查对应 Live 动作资源是否下载完整", UIMessageType.Error);
                    }
                    else if (_tempAnim != null)
                    {
                        _tempAnim.AddClip(_targetAnim, _targetAnim.name);
                    }
                }

                // 增强容错：安全配置动画循环模式，彻底防止空指针异常导致后续所有角色中断初始化
                if (_tempAnim != null)
                {
                    _tempAnim.wrapMode = WrapMode.Clamp;
                    _tempAnim.enabled = false;
                }
                return;
            }

            // 4. 分支判断：Timeline 序列帧动作模式
            _keyArray = timelineControl != null ? timelineControl._keyArray : null;

            if (_keyArray != null && seqDataIndex >= 0 && seqDataIndex < _keyArray.Length)
            {
                _currentKey = _keyArray[seqDataIndex];
            }

            // 校验 _currentKey 是否为 null
            if (_currentKey == null)
            {
                Debug.LogError($"[LiveMotion] 角色 {targetIndex + 1} 动作关键帧序列为空！(序列索引: {seqDataIndex}, 歌曲: {director?.live?.MusicId})");
                UmaErrorManager.ShowUIMessage($"[Live动作异常] 角色 {targetIndex + 1} 动作数据缺失，角色将保持初始姿态(T-pose)，请检查对应 Live 动作资源是否下载完整", UIMessageType.Error);
            }
            else if (_currentKey.thisList != null)
            {
                // 遍历统计有效动作剪辑数量，并收集记录缺失动作剪辑的关键帧
                int validClipCount = 0;
                foreach (var key in _currentKey.thisList)
                {
                    if (key == null)
                        continue;

                    if (key.clip != null)
                    {
                        validClipCount++;
                        if (_tempAnim != null)
                        {
                            _tempAnim.AddClip(key.clip, key.clip.name);
                        }
                    }
                    else
                    {
                        string missingMotionName = !string.IsNullOrEmpty(key.motionName) ? key.motionName : "(未知动作名)";
                        Debug.LogError($"[LiveMotion] 角色 {targetIndex + 1} 动作剪辑缺失: {missingMotionName} (歌曲: {director?.live?.MusicId})");
                    }
                }

                // 如果整个轨道中没有任何有效的 key.clip，弹出明确错误弹窗警告 T-pose 风险
                if (validClipCount == 0)
                {
                    UmaErrorManager.ShowUIMessage($"[Live动作异常] 角色 {targetIndex + 1} 动作数据缺失，角色将保持初始姿态(T-pose)，请检查对应 Live 动作资源是否下载完整", UIMessageType.Error);
                }
            }

            // 增强容错：在结尾设置 wrapMode 与 enabled 前必须校验 _tempAnim != null
            if (_tempAnim != null)
            {
                _tempAnim.wrapMode = WrapMode.Clamp;
                _tempAnim.enabled = false;
            }
        }

        /// <summary>
        /// 每帧根据当前 Live 时间更新动作采样并应用到角色的动画组件上
        /// </summary>
        /// <param name="currentTime">当前音乐/时间轴时间（秒）</param>
        /// <param name="timescaleKeys">全局时间缩放关键帧列表</param>
        public void AlterUpdate(float currentTime, LiveTimelineKeyTimescaleDataList timescaleKeys)
        {
            var director = Director.instance;

            // LiveMode 0 独立动作播放模式更新
            if (director != null && director.liveMode == 0)
            {
                if (_targetAnim != null && _tempAnim != null)
                {
                    var state = _tempAnim[_targetAnim.name];
                    if (state != null)
                    {
                        if ((director.sliderControl != null && director.sliderControl.is_Touched) || director.IsRecordVMD)
                        {
                            state.time = currentTime;
                            _tempAnim.Play(_targetAnim.name);
                            _motionSetup = false;
                        }
                        else if (!_motionSetup)
                        {
                            state.time = currentTime;
                            _tempAnim.Play(_targetAnim.name);
                            _motionSetup = true;
                        }
                    }
                }
                return;
            }

            // Timeline 模式关键帧动作更新
            if (_currentKey != null && _tempAnim != null)
            {
                LiveTimelineKeyIndex curKey = LiveTimelineControl.AlterUpdate_Key(_currentKey, currentTime);

                _curIndex = curKey.index;
                LiveTimelineKeyCharaMotionData arg = curKey.key as LiveTimelineKeyCharaMotionData;
                if (arg == null)
                    return;

                AnimationClip anim = arg.clip;

                // 增加对 anim 为空时的安全跳过与容错记录，防止静默空指针并仅在关键帧切换时输出告警
                if (anim == null)
                {
                    if (_curIndex != _prevIndex)
                    {
                        Debug.LogWarning($"[LiveMotion] 角色 {_targetIndex + 1} 关键帧索引 {_curIndex} 的动作剪辑为空 (动作名: {arg.motionName})，跳过动作采样");
                        _prevIndex = _curIndex;
                    }
                    _prevFrameAnimationTime = currentTime;
                    return;
                }

                double start;
                if (arg.isMotionHeadFrameAll)
                {
                    start = (double)arg.motionHeadFrame / 60;
                }
                else
                {
                    start = (charaIndex >= 0 && arg.motionHeadFrameSeparetes != null && charaIndex < arg.motionHeadFrameSeparetes.Length)
                        ? (double)arg.motionHeadFrameSeparetes[charaIndex] / 60
                        : 0;
                }

                double interval = 0;
                double last_current_time = currentTime;
                if (timescaleKeys != null && timescaleKeys.thisList != null && timescaleKeys.thisList.Count > 0)
                {
                    var has_key = false;
                    // 应用时间缩放关键帧
                    for (int i = timescaleKeys.thisList.Count - 1; i >= 0; i--)
                    {
                        var scaleKey = timescaleKeys.thisList[i];
                        if (scaleKey.FrameSecond <= currentTime)
                        {
                            has_key = true;
                            if (scaleKey.FrameSecond <= arg.FrameSecond)
                            {
                                interval += (last_current_time - arg.FrameSecond) * scaleKey.Timescale * arg.playSpeed;
                                break;
                            }
                            else
                            {
                                interval += (last_current_time - scaleKey.FrameSecond) * scaleKey.Timescale * arg.playSpeed;
                                last_current_time = scaleKey.FrameSecond;
                            }
                        }
                    }
                    if (!has_key)
                    {
                        interval = (currentTime - arg.FrameSecond) * arg.playSpeed; // 无时间缩放关键帧，使用默认速度
                    }
                }
                else
                {
                    interval = (currentTime - arg.FrameSecond) * arg.playSpeed;
                }
                float currentAnimationTime = (float)(start + interval);

                var state = _tempAnim[anim.name];
                if (state != null)
                {
                    state.enabled = true;
                    state.weight = 1;
                    state.time = arg.loop ? Mathf.Repeat(currentAnimationTime, state.length) : currentAnimationTime;
                    _tempAnim.Sample();
                    state.enabled = false;
                }
                else
                {
                    if (_curIndex != _prevIndex)
                    {
                        Debug.LogWarning($"[LiveMotion] 角色 {_targetIndex + 1} 动画组件中未注册动作剪辑: {anim.name}");
                    }
                }
                _prevIndex = _curIndex;
            }
            _prevFrameAnimationTime = currentTime;
        }
    }
}
