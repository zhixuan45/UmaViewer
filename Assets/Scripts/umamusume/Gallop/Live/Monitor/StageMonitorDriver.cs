using System;
using System.Collections.Generic;
using System.Reflection;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop.Live
{
    public class StageMonitorDriver : MonoBehaviour
    {
        [Header("绑定与调试")]
        public bool verboseLog = false, includeInactiveRenderers = true, rebuildCacheOnEnable = true, rebuildCacheWhenTargetMissing = true, autoInitializeProvider = true;

        [Header("播放与匹配")]
        public bool assignMaskTextureToFilterTex = false, clearFadeTextureWhenUnused = true, applyRenderQueue = true, applyBlendModeProperties = true;
        public string monitorShaderName = "Gallop/3D/Live/Stage/Monitor";

        [Header("着色器属性名称")]
        public string mainTexProperty = "_MainTex", filterTexProperty = "_FilterTex", fadeTexProperty = "_FadeTex";
        public string alphaProperty = "_Alpha", colorFadeProperty = "_ColorFade", baseColorProperty = "_BaseColor";
        public string monitorWidthProperty = "_MonitorWidth", monitorHeightProperty = "_MonitorHeight", crossFadeRateProperty = "_CrossFadeRate";
        public string srcBlendModeProperty = "_SrcBlendMode", dstBlendModeProperty = "_DstBlendMode", srcBlendProperty = "_SrcBlend", dstBlendProperty = "_DstBlend", zWriteProperty = "_ZWrite";

        private sealed class MonitorMaterialBinding
        {
            public Renderer renderer;
            public Material material;
            public string rendererKey, materialKey, rendererCompact, materialCompact, groupKey;
            public Vector2 baseFilterScale = Vector2.one, baseFilterOffset = Vector2.zero;
            public float baseAlpha = 1f;
            public Color baseColor = Color.white, baseColorFade = Color.clear;
            public bool hasSrcBlendMode, hasDstBlendMode, hasAppliedState;
            public float baseSrcBlendMode, baseDstBlendMode;
            public MonitorShaderState appliedState;
        }

        private struct MonitorTextureState
        {
            public Texture2D texture, maskTexture;
            public int imageIndex;
            public Vector2 offset, scale;
        }

        private struct MonitorShaderState
        {
            public MonitorTextureState main, fade;
            public Texture2D filterTexture;
            public float alpha, width, height, crossFadeRate, filterTexScale;
            public Color colorFade, baseColor;
            public int srcBlendMode, dstBlendMode, renderQueue;
            public bool hasRenderQueue, hasMainTexture, hasFadeTexture, useBlendMode, useBaseColor;
        }

        private LiveTimelineControl _ctl;
        private StageController _stage;
        private MonitorUvMovieProvider _provider;
        private bool _hasBuiltCache, _hasMonitorTimelineData, _providerContextReady;
        private int _lastPreparedMusicId = -1, _lastPreparedStageInstanceId = int.MinValue;

        private readonly List<MonitorMaterialBinding> _bindings = new List<MonitorMaterialBinding>(32);
        private readonly Dictionary<string, List<MonitorMaterialBinding>> _bindingCache = new Dictionary<string, List<MonitorMaterialBinding>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missingBindingLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missingClipLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _rebuildAttemptedForMissingBinding = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MonitorMaterialBinding> _resolveBuffer = new List<MonitorMaterialBinding>(8);
        private readonly HashSet<MonitorMaterialBinding> _activeBindingsThisFrame = new HashSet<MonitorMaterialBinding>();
        private static readonly List<MonitorMaterialBinding> EmptyBindingList = new List<MonitorMaterialBinding>(0);

        private void OnEnable()
        {
            BindIfPossible();
            if (rebuildCacheOnEnable)
                RebuildCache();
        }

        private void OnDisable()
        {
            // 退出或禁用时将所有屏幕恢复完全透明并隐藏，杜绝遮挡夕阳与残留黑屏
            SetAllBindingsIdle();
            Unbind();
            ClearCaches();
        }

        private void LateUpdate()
        {
            if (_ctl == null || _stage == null || _provider == null)
                BindIfPossible();

            // 若控制器、舞台或 Provider 未就绪，或无时间轴监视器数据，确保全部监视器处于透明隐藏空闲状态
            if (_ctl == null || _stage == null || _provider == null || !_hasMonitorTimelineData)
            {
                SetAllBindingsIdle();
                return;
            }

            // Provider 尚未准备就绪时同样保持空闲透明，杜绝出现初始大黑板遮挡夕阳
            if (!EnsureProviderReady())
            {
                SetAllBindingsIdle();
                return;
            }

            if (!_hasBuiltCache)
                RebuildCache();

            ApplyMonitorTimeline();
        }

        private void BindIfPossible()
        {
            Director dir = Director.instance;
            if (!dir)
                return;

            LiveTimelineControl newCtl = dir._liveTimelineControl;
            StageController newStage = dir._stageController;
            if (newCtl == null || newStage == null) return;

            MonitorUvMovieProvider newProvider = GetComponent<MonitorUvMovieProvider>();
            if (newProvider == null) newProvider = dir.GetComponent<MonitorUvMovieProvider>();
            if (newProvider == null) newProvider = FindObjectOfType<MonitorUvMovieProvider>();
            if (newProvider == null && autoInitializeProvider)
            {
                newProvider = dir.gameObject.GetComponent<MonitorUvMovieProvider>();
                if (newProvider == null) newProvider = dir.gameObject.AddComponent<MonitorUvMovieProvider>();
            }

            bool changed = _ctl != newCtl || _stage != newStage || _provider != newProvider;
            _ctl = newCtl; _stage = newStage; _provider = newProvider;
            if (!changed) return;

            ClearCaches();
            _hasMonitorTimelineData = HasMonitorTimelineData(_ctl);
            _providerContextReady = false;
            _lastPreparedMusicId = -1;
            _lastPreparedStageInstanceId = int.MinValue;

            if (verboseLog) Debug.Log($"[StageMonitorDriver] bound, hasMonitorTimelineData={_hasMonitorTimelineData}");
        }

        private void Unbind()
        {
            _ctl = null; _stage = null; _provider = null;
            _hasMonitorTimelineData = false; _providerContextReady = false;
            _lastPreparedMusicId = -1; _lastPreparedStageInstanceId = int.MinValue;
        }

        private void ClearCaches()
        {
            _bindings.Clear();
            _bindingCache.Clear();
            _missingBindingLogged.Clear();
            _missingClipLogged.Clear();
            _rebuildAttemptedForMissingBinding.Clear();
            _resolveBuffer.Clear();
            _activeBindingsThisFrame.Clear();
            _hasBuiltCache = false;
        }

        private bool EnsureProviderReady()
        {
            if (_provider == null || !autoInitializeProvider) return _provider != null;
            int musicId = Director.instance?.live?.MusicId ?? 0;
            if (musicId <= 0) return false;

            bool musicChanged = _lastPreparedMusicId != musicId || _provider.LoadedMusicId != musicId;
            bool stageChanged = _stage != null && _lastPreparedStageInstanceId != _stage.GetInstanceID();
            bool alreadyLoaded = _provider.LoadedMusicId == musicId && _provider.HasTriedLoad;
            if (!alreadyLoaded || musicChanged)
            {
                bool forceReload = _provider.LoadedMusicId > 0 && _provider.LoadedMusicId != musicId;
                bool loaded = _provider.InitializeForMusicId(musicId, forceReload);
                _providerContextReady = false;
                if (verboseLog) Debug.Log($"[StageMonitorDriver] provider initialize musicId={musicId}, loaded={loaded}, clips={_provider.clips?.Count ?? 0}");
            }

            _lastPreparedMusicId = musicId;
            bool synced = false;
            if (!_providerContextReady || stageChanged || _provider.ContextSlotCount == 0)
            {
                synced = _provider.RebuildContextSlotsFromLiveSettings(_ctl, musicId, force: stageChanged || musicChanged);
                _providerContextReady = synced || _provider.ContextSlotCount > 0;
            }

            if (_stage != null) _lastPreparedStageInstanceId = _stage.GetInstanceID();
            return _provider.ContextSlotCount > 0;
        }

        private static bool HasMonitorTimelineData(LiveTimelineControl timelineControl)
        {
            if (timelineControl?.data?.worksheetList == null) return false;
            List<LiveTimelineWorkSheet> worksheets = timelineControl.data.worksheetList;
            for (int i = 0; i < worksheets.Count; i++)
            {
                LiveTimelineWorkSheet workSheet = worksheets[i];
                if (workSheet?.monitorControlList != null && workSheet.monitorControlList.Count > 0) return true;
            }
            return false;
        }

        public void RebuildCache()
        {
            _bindings.Clear();
            _bindingCache.Clear();
            _missingBindingLogged.Clear();
            _activeBindingsThisFrame.Clear();
            _hasBuiltCache = true;
            if (_stage == null) return;

            Renderer[] renderers = _stage.GetComponentsInChildren<Renderer>(includeInactiveRenderers);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;

                Material[] materials;
                try { materials = renderer.materials; }
                catch (Exception ex)
                {
                    if (verboseLog) Debug.LogWarning($"[StageMonitorDriver] failed to read materials from {renderer.name}: {ex.Message}");
                    continue;
                }
                if (materials == null || materials.Length == 0) continue;

                for (int j = 0; j < materials.Length; j++)
                {
                    Material material = materials[j];
                    if (!IsMonitorMaterial(material)) continue;

                    // 目标 2：如果材质名称包含 StageMonitorBlendTransparent 或 monitor，确保具备半透明混合能力
                    EnsureTransparentBlend(material);

                    MonitorMaterialBinding binding = new MonitorMaterialBinding
                    {
                        renderer = renderer, material = material,
                        rendererKey = NormalizeName(renderer.name), materialKey = NormalizeName(material.name),
                        rendererCompact = CompactName(renderer.name), materialCompact = CompactName(material.name),
                    };
                    binding.groupKey = BuildGroupKey(binding);

                    if (!string.IsNullOrEmpty(filterTexProperty) && material.HasProperty(filterTexProperty))
                    {
                        binding.baseFilterScale = material.GetTextureScale(filterTexProperty);
                        binding.baseFilterOffset = material.GetTextureOffset(filterTexProperty);
                    }

                    // 记录原始基准透明度，若初始为 0 则保底为 1f，便于后续视频播放时正确显示
                    float initialAlpha = 1f;
                    if (TryHasProperty(material, alphaProperty))
                    {
                        initialAlpha = material.GetFloat(alphaProperty);
                        if (initialAlpha <= 0.001f) initialAlpha = 1f;
                    }
                    binding.baseAlpha = initialAlpha;

                    if (TryHasProperty(material, colorFadeProperty)) binding.baseColorFade = material.GetColor(colorFadeProperty);
                    if (TryHasProperty(material, baseColorProperty)) binding.baseColor = material.GetColor(baseColorProperty);

                    if (TryHasProperty(material, srcBlendModeProperty)) { binding.hasSrcBlendMode = true; binding.baseSrcBlendMode = material.GetFloat(srcBlendModeProperty); }
                    else if (TryHasProperty(material, srcBlendProperty)) { binding.hasSrcBlendMode = true; binding.baseSrcBlendMode = material.GetFloat(srcBlendProperty); }

                    if (TryHasProperty(material, dstBlendModeProperty)) { binding.hasDstBlendMode = true; binding.baseDstBlendMode = material.GetFloat(dstBlendModeProperty); }
                    else if (TryHasProperty(material, dstBlendProperty)) { binding.hasDstBlendMode = true; binding.baseDstBlendMode = material.GetFloat(dstBlendProperty); }

                    // 目标 1：初始状态下杜绝黑模，强制 _Alpha = 0f 并隐藏 Renderer，确保 100% 透明透光绝不遮挡夕阳
                    TrySetFloat(material, alphaProperty, 0f);
                    if (renderer != null) renderer.enabled = false;

                    _bindings.Add(binding);
                }
            }

            _bindings.Sort((a, b) =>
            {
                int cmp = string.Compare(a.groupKey, b.groupKey, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                cmp = string.Compare(a.materialKey, b.materialKey, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                return string.Compare(a.rendererKey, b.rendererKey, StringComparison.OrdinalIgnoreCase);
            });

            if (verboseLog) Debug.Log($"[StageMonitorDriver] cache rebuilt: bindings={_bindings.Count}");
        }

        /// <summary>
        /// 增强半透明混合兼容：
        /// 在材质绑定或初始化阶段，如果材质名称包含 StageMonitorBlendTransparent 或 monitor，
        /// 确保其具备透明混合能力（_SrcBlend = SrcAlpha, _DstBlend = OneMinusSrcAlpha, _ZWrite = 0），杜绝退化为不透明黑色。
        /// </summary>
        private void EnsureTransparentBlend(Material material)
        {
            if (material == null) return;
            string matName = material.name ?? string.Empty;
            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            bool isTransparentMonitor = matName.IndexOf("StageMonitorBlendTransparent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        matName.IndexOf("monitor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        shaderName.IndexOf("StageMonitorBlendTransparent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        shaderName.IndexOf("Monitor", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isTransparentMonitor)
            {
                // 配置半透明混合模式：SrcBlend = SrcAlpha (5), DstBlend = OneMinusSrcAlpha (10)
                TrySetFloat(material, srcBlendModeProperty, (float)BlendMode.SrcAlpha);
                TrySetFloat(material, srcBlendProperty, (float)BlendMode.SrcAlpha);
                TrySetFloat(material, dstBlendModeProperty, (float)BlendMode.OneMinusSrcAlpha);
                TrySetFloat(material, dstBlendProperty, (float)BlendMode.OneMinusSrcAlpha);

                // 深度写入置 0，防止遮挡后方天空背景与景物
                TrySetFloat(material, zWriteProperty, 0f);

                // 确保渲染队列位于 Transparent 层级
                if (material.renderQueue < (int)RenderQueue.Transparent)
                    material.renderQueue = (int)RenderQueue.Transparent;
            }
        }

        /// <summary>
        /// 将指定监视器绑定置于空闲未播放状态：
        /// 将 Renderer.enabled 设为 false，材质 _Alpha 设为 0f，确保屏幕 100% 透明透光。
        /// </summary>
        private void SetBindingIdle(MonitorMaterialBinding binding)
        {
            if (binding == null) return;
            if (binding.renderer != null && binding.renderer.enabled) binding.renderer.enabled = false;
            if (binding.material != null)
            {
                TrySetFloat(binding.material, alphaProperty, 0f);
                EnsureTransparentBlend(binding.material);
            }
            if (binding.hasAppliedState)
            {
                MonitorShaderState state = binding.appliedState;
                state.alpha = 0f;
                binding.appliedState = state;
            }
        }

        /// <summary>
        /// 将所有监视器绑定置于空闲透明状态
        /// </summary>
        private void SetAllBindingsIdle()
        {
            for (int i = 0; i < _bindings.Count; i++) SetBindingIdle(_bindings[i]);
        }

        private void ApplyMonitorTimeline()
        {
            if (_provider == null)
                return;

            bool hasPool = _provider.clips != null && _provider.clips.Count > 0;
            bool hasSlots = _provider.ContextSlotCount > 0;
            if (!hasPool && !hasSlots)
            {
                SetAllBindingsIdle();
                return;
            }

            LiveTimelineData data = _ctl.data;
            if (data == null || data.worksheetList == null)
            {
                SetAllBindingsIdle();
                return;
            }

            float currentLiveTime = _ctl.currentLiveTime;
            float currentFrame = currentLiveTime * LiveTimelineControl.kTargetFpsF;

            _activeBindingsThisFrame.Clear();

            List<LiveTimelineWorkSheet> worksheets = data.worksheetList;
            for (int wsIndex = 0; wsIndex < worksheets.Count; wsIndex++)
            {
                LiveTimelineWorkSheet workSheet = worksheets[wsIndex];
                if (workSheet == null || workSheet.monitorControlList == null)
                    continue;

                List<LiveTimelineMonitorControlData> monitorList = workSheet.monitorControlList;
                for (int i = 0; i < monitorList.Count; i++)
                {
                    LiveTimelineMonitorControlData monitorData = monitorList[i];
                    if (monitorData == null || monitorData.keys == null)
                        continue;

                    LiveTimelineKeyMonitorControlDataList keys = monitorData.keys;
                    if (keys.Count <= 0)
                        continue;

                    if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                        continue;

                    if (!keys.EnablePlayModeTimeline(_ctl.PlayMode))
                        continue;

                    LiveTimelineControl.FindTimelineKey(out LiveTimelineKey curKeyBase, out LiveTimelineKey nextKeyBase, keys, currentFrame);
                    LiveTimelineKeyMonitorControlData curKey = curKeyBase as LiveTimelineKeyMonitorControlData;
                    if (curKey == null) continue;
                    LiveTimelineKeyMonitorControlData nextKey = nextKeyBase as LiveTimelineKeyMonitorControlData;
                    string bindingName = !string.IsNullOrWhiteSpace(monitorData.SafeName) ? monitorData.SafeName : monitorData.name;

                    if (verboseLog)
                    {
                        Debug.Log($"[StageMonitorDriver] timeline hit: monitor='{bindingName}', frame={currentFrame:F2}, dispID={curKey.dispID}");
                    }

                    List<MonitorMaterialBinding> targets = ResolveBindings(bindingName);
                    if (targets.Count == 0 && rebuildCacheWhenTargetMissing && _bindings.Count > 0 && _rebuildAttemptedForMissingBinding.Add(bindingName))
                    {
                        RebuildCache();
                        targets = ResolveBindings(bindingName);
                    }
                    if (targets.Count == 0) continue;

                    // 目标 1：严格检查有效视频播放条件（dispID > 0 且存在有效主纹理）
                    MonitorShaderState state = default;
                    bool isPlayable = curKey.dispID > 0 &&
                                     TryBuildShaderState(monitorData, curKey, nextKey, currentFrame, out state) &&
                                     state.hasMainTexture && state.main.texture != null;

                    if (isPlayable)
                    {
                        // 触发有效视频播放：恢复 Renderer.enabled = true，并按时间轴参数写入主纹理与 Alpha
                        for (int j = 0; j < targets.Count; j++)
                        {
                            MonitorMaterialBinding target = targets[j];
                            if (target == null) continue;
                            if (target.renderer != null && !target.renderer.enabled) target.renderer.enabled = true;
                            ApplyShaderState(target, state);
                            _activeBindingsThisFrame.Add(target);
                        }
                    }
                    else
                    {
                        // 当前机位/关键帧未播放视频（dispID <= 0，或者 hasMainTexture 为 false，或者尚无有效纹理）：
                        // 对应监视器屏幕保持或切换为 100% 透明透光与隐藏，绝不遮挡夕阳
                        for (int j = 0; j < targets.Count; j++)
                        {
                            MonitorMaterialBinding target = targets[j];
                            if (target != null && !_activeBindingsThisFrame.Contains(target))
                                SetBindingIdle(target);
                        }
                    }
                }
            }

            // 对所有在当前时间轴帧中未激活有效播放的监视器部件统一收底，执行透明与隐藏保护
            for (int i = 0; i < _bindings.Count; i++)
            {
                MonitorMaterialBinding binding = _bindings[i];
                if (binding != null && !_activeBindingsThisFrame.Contains(binding))
                    SetBindingIdle(binding);
            }
        }

        private bool TryBuildShaderState(
            LiveTimelineMonitorControlData monitorData,
            LiveTimelineKeyMonitorControlData curKey,
            LiveTimelineKeyMonitorControlData nextKey,
            float currentFrame,
            out MonitorShaderState state)
        {
            state = default;
            bool canInterpolate = nextKey != null && nextKey.interpolateType != LiveCameraInterpolateType.None;
            float ratio = canInterpolate ? LiveTimelineControl.CalculateInterpolationValue(curKey, nextKey, currentFrame) : 0f;

            Vector2 size = canInterpolate ? Vector2.Lerp(curKey.size, nextKey.size, ratio) : curKey.size;
            float blendFactor = canInterpolate ? Mathf.Lerp(curKey.blendFactor, nextKey.blendFactor, ratio) : curKey.blendFactor;
            Color colorFade = canInterpolate ? Color.Lerp(curKey.colorFade, nextKey.colorFade, ratio) : curKey.colorFade;
            Color baseColor = canInterpolate ? Color.Lerp(curKey.BaseColor, nextKey.BaseColor, ratio) : curKey.BaseColor;
            float crossFadeRate = canInterpolate ? Mathf.Lerp(curKey.CrossFadeRate, nextKey.CrossFadeRate, ratio) : curKey.CrossFadeRate;
            float filterTexScale = canInterpolate ? Mathf.Lerp(curKey.FilterTexScale, nextKey.FilterTexScale, ratio) : curKey.FilterTexScale;

            float localTime = (currentFrame - curKey.frame) * LiveTimelineControl.kFrameToSec;
            bool isReversePlay = curKey.IsReversePlayFlag();
            if (curKey.speed < 0f) isReversePlay = !isReversePlay;
            float playbackSpeed = Mathf.Abs(curKey.speed) <= 0f ? 1f : Mathf.Abs(curKey.speed);

            MonitorUvMovieContextSlot primarySlot = ResolvePrimarySlot(monitorData, curKey);
            MonitorUvMovieContextSlot fadeSlot = ResolveFadeSlot(monitorData, curKey);

            if (primarySlot?.clip != null && TryBuildTextureState(primarySlot, primarySlot.clip, localTime, playbackSpeed, isReversePlay, curKey.playStartOffsetFrame, curKey.LightImageNo, out MonitorTextureState mainTexture))
            {
                state.main = mainTexture;
                state.hasMainTexture = true;
            }

            if (fadeSlot?.clip != null && TryBuildTextureState(fadeSlot, fadeSlot.clip, localTime, playbackSpeed, isReversePlay, curKey.playStartOffsetFrame, curKey.LightImageNo2, out MonitorTextureState fadeTexture))
            {
                state.fade = fadeTexture;
                state.hasFadeTexture = true;
            }

            if (assignMaskTextureToFilterTex && state.hasMainTexture)
                state.filterTexture = state.main.maskTexture;

            // 目标 1：计算时间轴透明度，优先使用插值后的 blendFactor，如果有效播放且未配置则保底为 1f
            float timelineAlpha = blendFactor;
            if (timelineAlpha <= 0.0001f && curKey.dispID > 0 && curKey.blendFactor <= 0.0001f && (nextKey == null || nextKey.blendFactor <= 0.0001f))
            {
                timelineAlpha = 1f;
            }
            state.alpha = Mathf.Clamp01(timelineAlpha);
            state.colorFade = colorFade;
            state.useBaseColor = !IsColorEffectivelyClear(baseColor);
            state.baseColor = state.useBaseColor ? baseColor : Color.white;
            state.width = size.x;
            state.height = size.y;
            state.crossFadeRate = crossFadeRate;
            state.filterTexScale = Mathf.Max(0.0001f, filterTexScale <= 0f ? 1f : filterTexScale);
            state.srcBlendMode = curKey.SrcBlendMode;
            state.dstBlendMode = curKey.DstBlendMode;
            state.useBlendMode = curKey.IsEnabledBlendMode;
            state.renderQueue = curKey.RenderQueueNo;
            state.hasRenderQueue = curKey.IsRenderQueue != 0;

            return state.hasMainTexture;
        }

        private MonitorUvMovieContextSlot ResolvePrimarySlot(LiveTimelineMonitorControlData monitorData, LiveTimelineKeyMonitorControlData key)
        {
            if (_provider == null || key == null) return null;
            int originalDispId = key.dispID;
            int effectiveDispId = ResolveEffectivePrimaryDispId(key);

            if (TryGetPlayableContextSlot(effectiveDispId, out MonitorUvMovieContextSlot slot)) return slot;
            if (effectiveDispId != originalDispId && TryGetPlayableContextSlot(originalDispId, out slot)) return slot;
            if ((effectiveDispId > 0 || originalDispId > 0) && TryGetFallbackContextSlot(out slot)) return slot;

            string timelineName = monitorData != null ? (!string.IsNullOrWhiteSpace(monitorData.SafeName) ? monitorData.SafeName : monitorData.name) : "<unnamed>";
            string missKey = $"{timelineName}|primary|slotId={effectiveDispId}";
            if (_missingClipLogged.Add(missKey) && verboseLog)
                Debug.LogWarning($"[StageMonitorDriver] primary official slot not found for '{timelineName}' (slotId={effectiveDispId})");
            return null;
        }

        private int ResolveEffectivePrimaryDispId(LiveTimelineKeyMonitorControlData key)
        {
            if (key?.ChangeUVSettingArray == null || key.ChangeUVSettingArray.Length == 0) return key?.dispID ?? -1;
            for (int i = 0; i < key.ChangeUVSettingArray.Length; i++)
            {
                LiveTimelineMonitorChangeUVSetting change = key.ChangeUVSettingArray[i];
                if (change != null && change.IsEnabled && change.DispID > 0 && DoesChangeConditionMatchCurrentCharacters(change.ConditionArray))
                    return change.DispID;
            }
            return key.dispID;
        }

        private MonitorUvMovieContextSlot ResolveFadeSlot(LiveTimelineMonitorControlData monitorData, LiveTimelineKeyMonitorControlData key)
        {
            if (_provider == null || key == null || key.DispID2 <= 0) return null;
            if (TryGetPlayableContextSlot(key.DispID2, out MonitorUvMovieContextSlot slot)) return slot;
            if (TryGetFallbackContextSlot(out slot)) return slot;

            string timelineName = monitorData != null ? (!string.IsNullOrWhiteSpace(monitorData.SafeName) ? monitorData.SafeName : monitorData.name) : "<unnamed>";
            string missKey = $"{timelineName}|fade|slotId={key.DispID2}";
            if (_missingClipLogged.Add(missKey) && verboseLog)
                Debug.LogWarning($"[StageMonitorDriver] fade slot not found for '{timelineName}' (slotId={key.DispID2})");
            return null;
        }

        private bool TryGetPlayableContextSlot(int slotId, out MonitorUvMovieContextSlot slot)
        {
            slot = null;
            if (_provider == null || slotId <= 0) return false;
            return _provider.TryGetContextSlot(slotId, out slot) && slot != null && slot.isEnabledLoad && slot.clip != null;
        }

        private bool TryGetFallbackContextSlot(out MonitorUvMovieContextSlot slot) => TryGetPlayableContextSlot(1, out slot);

        private bool DoesChangeConditionMatchCurrentCharacters(LiveTimelineMonitorDressCondition[] conditions)
        {
            if (conditions == null || conditions.Length == 0) return true;
            for (int i = 0; i < conditions.Length; i++)
            {
                LiveTimelineMonitorDressCondition condition = conditions[i];
                if (condition != null && condition.IsEnabled && !DoesSingleConditionMatchCurrentCharacters(condition))
                    return false;
            }
            return true;
        }

        private bool DoesSingleConditionMatchCurrentCharacters(LiveTimelineMonitorDressCondition condition)
        {
            if (condition == null || !condition.IsEnabled) return true;
            Director director = Director.instance;
            if (director?.CharaContainerScript == null || director.CharaContainerScript.Count == 0) return false;

            for (int i = 0; i < director.CharaContainerScript.Count; i++)
            {
                UmaContainerCharacter container = director.CharaContainerScript[i];
                if (container == null) continue;
                int charaId = GetContainerCharaId(container);
                int dressId = GetContainerDressId(container);
                if ((condition.CharaId <= 0 || condition.CharaId == charaId) && (condition.DressId <= 0 || condition.DressId == dressId))
                    return true;
            }
            return false;
        }

        private static int GetContainerCharaId(UmaContainerCharacter container)
        {
            if (container == null) return 0;
            if (container.CharaEntry != null && container.CharaEntry.Id > 0) return container.CharaEntry.Id;
            if (container.CharaData != null)
            {
                try
                {
                    object idValue = container.CharaData["id"];
                    if (idValue != null && int.TryParse(idValue.ToString(), out int charaId)) return charaId;
                }
                catch { }
            }
            return 0;
        }

        private static int GetContainerDressId(UmaContainerCharacter container)
        {
            if (container == null) return 0;
            if (TryParseDressIdPrefix(container.VarCostumeIdLong, out int dressId)) return dressId;
            if (TryParseDressIdPrefix(container.VarCostumeIdShort, out dressId)) return dressId;
            return 0;
        }

        private static bool TryParseDressIdPrefix(string costumeId, out int dressId)
        {
            dressId = 0;
            if (string.IsNullOrWhiteSpace(costumeId)) return false;
            string[] parts = costumeId.Split('_');
            return parts.Length > 0 && int.TryParse(parts[0], out dressId);
        }

        private bool TryBuildTextureState(MonitorUvMovieContextSlot slot, MonitorUvMovieClipData clip, float localTime, float playbackSpeed,
            bool isReversePlay, int startOffsetFrame, int lightImageNo, out MonitorTextureState state)
        {
            state = default;
            if (clip == null) return false;

            if (slot != null && !slot.useStandardMode && clip.lightTexture != null)
            {
                state.texture = clip.lightTexture;
                state.maskTexture = clip.lightMaskTexture;
                state.imageIndex = Mathf.Max(0, lightImageNo);
                state.offset = clip.texturePixelOffset;
                state.scale = Vector2.one - clip.texturePixelScale;
                return true;
            }

            if (clip.metadata == null) return false;
            MonitorUvMovieFrameInfo frameInfo = clip.metadata.FrameInfo ?? new MonitorUvMovieFrameInfo();
            int totalFrameCount = Mathf.Max(clip.FrameCount, 0);
            if (totalFrameCount <= 0) return false;

            float fps = Mathf.Max(clip.Fps, 1f);
            float moviePlaySec = totalFrameCount / fps;
            float startOffsetSec = Mathf.Max(0f, frameInfo.StartOffsetSec);
            float startLoopSec = Mathf.Max(0f, frameInfo.StartLoopSec);
            float endLoopSec = frameInfo.EndLoopSec > 0f ? frameInfo.EndLoopSec : moviePlaySec;
            endLoopSec = Mathf.Clamp(endLoopSec, 0f, moviePlaySec);
            if (endLoopSec <= startLoopSec) endLoopSec = moviePlaySec;

            float playStartSec = startOffsetFrame > 0 ? startOffsetFrame / fps : 0f;
            float setTime = Mathf.Max(0f, localTime) * Mathf.Max(0f, playbackSpeed);
            if (playStartSec > 0f) setTime += playStartSec;
            setTime = Mathf.Max(0f, setTime - startOffsetSec);

            float sampleTime = ResolveSampleTime(setTime, moviePlaySec, frameInfo.IsLoop, startLoopSec, endLoopSec, frameInfo.LoopCount, isReversePlay);
            BuildFrameUv(clip, sampleTime, fps, totalFrameCount, out int imageIndex, out int atlasIndex, out Vector2 frameOffset, out Vector2 frameScale);

            if (!clip.TryGetFrameTexture(imageIndex, out Texture2D texture) || texture == null) return false;
            clip.TryGetMaskTexture(imageIndex, out Texture2D maskTexture);

            state.texture = texture;
            state.maskTexture = maskTexture;
            state.imageIndex = imageIndex;
            state.offset = frameOffset + clip.texturePixelOffset;
            state.scale = frameScale - clip.texturePixelScale;
            return true;
        }

        private static float ResolveSampleTime(float setTime, float moviePlaySec, bool isLoop, float startLoopSec, float endLoopSec, int loopCount, bool isReversePlay)
        {
            float loopStart = isReversePlay ? Mathf.Max(0f, moviePlaySec - endLoopSec) : startLoopSec;
            float loopEnd = isReversePlay ? Mathf.Max(loopStart, moviePlaySec - startLoopSec) : endLoopSec;
            float time = setTime;

            if (isLoop)
            {
                float loopLength = Mathf.Max(loopEnd - loopStart, 1f / 60f);
                if (time > loopEnd)
                {
                    float after = time - loopEnd;
                    if (loopCount > 0)
                    {
                        float totalLoopSec = loopLength * loopCount;
                        if (after > totalLoopSec) after -= totalLoopSec;
                    }
                    time = loopStart + Mathf.Repeat(after, loopLength);
                }
            }
            else if (time > moviePlaySec)
            {
                time = moviePlaySec;
            }

            return isReversePlay ? Mathf.Max(0f, moviePlaySec - time) : time;
        }

        private static void BuildFrameUv(MonitorUvMovieClipData clip, float sampleTime, float fps, int totalFrameCount,
            out int imageIndex, out int atlasIndex, out Vector2 frameOffset, out Vector2 frameScale)
        {
            int frameIndex = Mathf.Clamp((int)(sampleTime * fps), 0, totalFrameCount - 1);
            int framesPerImage = clip.metadata != null ? Mathf.Max(clip.metadata.EffectiveFramePerImage, 1) : 1;
            int framesPerWidth = clip.metadata != null ? Mathf.Max(clip.metadata.EffectiveFramePerWidth, 1) : 1;

            imageIndex = Mathf.Clamp(frameIndex / framesPerImage, 0, Mathf.Max(clip.frameTextures.Count - 1, 0));
            atlasIndex = Mathf.Clamp(frameIndex - imageIndex * framesPerImage, 0, Mathf.Max(framesPerImage - 1, 0));

            frameScale = (clip.metadata?.FrameInfo != null && clip.metadata.FrameInfo.Size.x > 0f && clip.metadata.FrameInfo.Size.y > 0f)
                ? clip.metadata.FrameInfo.Size
                : new Vector2(1f / framesPerWidth, 1f / Mathf.Max(1, Mathf.CeilToInt((float)framesPerImage / framesPerWidth)));

            int column = atlasIndex % framesPerWidth;
            int row = atlasIndex / framesPerWidth;
            frameOffset = new Vector2(column * frameScale.x, row * frameScale.y);
        }

        private void ApplyShaderState(MonitorMaterialBinding binding, MonitorShaderState state)
        {
            Material material = binding.material;
            if (material == null) return;

            if (state.hasMainTexture && HasTextureProperty(material, mainTexProperty))
            {
                if (!binding.hasAppliedState || binding.appliedState.main.texture != state.main.texture ||
                    !Approximately(binding.appliedState.main.scale, state.main.scale) || !Approximately(binding.appliedState.main.offset, state.main.offset))
                {
                    material.SetTexture(mainTexProperty, state.main.texture);
                    material.SetTextureScale(mainTexProperty, state.main.scale);
                    material.SetTextureOffset(mainTexProperty, state.main.offset);
                }
            }

            if (HasTextureProperty(material, fadeTexProperty))
            {
                if (state.hasFadeTexture)
                {
                    if (!binding.hasAppliedState || !binding.appliedState.hasFadeTexture || binding.appliedState.fade.texture != state.fade.texture ||
                        !Approximately(binding.appliedState.fade.scale, state.fade.scale) || !Approximately(binding.appliedState.fade.offset, state.fade.offset))
                    {
                        material.SetTexture(fadeTexProperty, state.fade.texture);
                        material.SetTextureScale(fadeTexProperty, state.fade.scale);
                        material.SetTextureOffset(fadeTexProperty, state.fade.offset);
                    }
                }
                else if (clearFadeTextureWhenUnused && (!binding.hasAppliedState || binding.appliedState.hasFadeTexture))
                {
                    material.SetTexture(fadeTexProperty, null);
                    material.SetTextureScale(fadeTexProperty, Vector2.one);
                    material.SetTextureOffset(fadeTexProperty, Vector2.zero);
                }
            }

            if (HasTextureProperty(material, filterTexProperty))
            {
                Vector2 filterScale = binding.baseFilterScale * state.filterTexScale;
                if (assignMaskTextureToFilterTex && (!binding.hasAppliedState || binding.appliedState.filterTexture != state.filterTexture))
                    material.SetTexture(filterTexProperty, state.filterTexture);
                if (!binding.hasAppliedState || !Approximately(binding.appliedState.filterTexScale, state.filterTexScale))
                    material.SetTextureScale(filterTexProperty, filterScale);
                if (!binding.hasAppliedState)
                    material.SetTextureOffset(filterTexProperty, binding.baseFilterOffset);
            }

            // 目标 1：根据时间轴计算出的 Alpha 与材质基准透明度合成，确保有效视频播放时显示正常画面
            float effectiveBaseAlpha = binding.baseAlpha > 0.001f ? binding.baseAlpha : 1f;
            float appliedAlpha = Mathf.Clamp01(effectiveBaseAlpha * (state.alpha > 0f ? state.alpha : 1f));
            Color appliedColorFade = state.colorFade;
            Color appliedBaseColor = state.useBaseColor ? state.baseColor : binding.baseColor;

            if (!binding.hasAppliedState || !Approximately(binding.appliedState.alpha, appliedAlpha)) TrySetFloat(material, alphaProperty, appliedAlpha);
            if (!binding.hasAppliedState || !Approximately(binding.appliedState.colorFade, appliedColorFade)) TrySetColor(material, colorFadeProperty, appliedColorFade);
            if (!binding.hasAppliedState || !Approximately(binding.appliedState.baseColor, appliedBaseColor)) TrySetColor(material, baseColorProperty, appliedBaseColor);
            if (!binding.hasAppliedState || !Approximately(binding.appliedState.width, state.width)) TrySetFloat(material, monitorWidthProperty, state.width);
            if (!binding.hasAppliedState || !Approximately(binding.appliedState.height, state.height)) TrySetFloat(material, monitorHeightProperty, state.height);
            if (!binding.hasAppliedState || !Approximately(binding.appliedState.crossFadeRate, state.crossFadeRate)) TrySetFloat(material, crossFadeRateProperty, state.crossFadeRate);

            if (applyBlendModeProperties)
            {
                if (state.useBlendMode)
                {
                    if (!binding.hasAppliedState || !binding.appliedState.useBlendMode || binding.appliedState.srcBlendMode != state.srcBlendMode)
                    {
                        if (!TrySetFloat(material, srcBlendModeProperty, state.srcBlendMode)) TrySetFloat(material, srcBlendProperty, state.srcBlendMode);
                    }
                    if (!binding.hasAppliedState || !binding.appliedState.useBlendMode || binding.appliedState.dstBlendMode != state.dstBlendMode)
                    {
                        if (!TrySetFloat(material, dstBlendModeProperty, state.dstBlendMode)) TrySetFloat(material, dstBlendProperty, state.dstBlendMode);
                    }
                }
                else
                {
                    // 默认确保半透明混合能力并禁用 ZWrite，杜绝退化为不透明黑板
                    EnsureTransparentBlend(material);

                    if (binding.hasSrcBlendMode && (!binding.hasAppliedState || binding.appliedState.useBlendMode || binding.appliedState.srcBlendMode != Mathf.RoundToInt(binding.baseSrcBlendMode)))
                    {
                        if (!TrySetFloat(material, srcBlendModeProperty, binding.baseSrcBlendMode)) TrySetFloat(material, srcBlendProperty, binding.baseSrcBlendMode);
                    }
                    if (binding.hasDstBlendMode && (!binding.hasAppliedState || binding.appliedState.useBlendMode || binding.appliedState.dstBlendMode != Mathf.RoundToInt(binding.baseDstBlendMode)))
                    {
                        if (!TrySetFloat(material, dstBlendModeProperty, binding.baseDstBlendMode)) TrySetFloat(material, dstBlendProperty, binding.baseDstBlendMode);
                    }
                }
            }

            if (applyRenderQueue && state.hasRenderQueue && (!binding.hasAppliedState || !binding.appliedState.hasRenderQueue || binding.appliedState.renderQueue != state.renderQueue))
            {
                material.renderQueue = state.renderQueue;
            }

            MonitorShaderState storedState = state;
            storedState.alpha = appliedAlpha;
            storedState.baseColor = appliedBaseColor;
            binding.appliedState = storedState;
            binding.hasAppliedState = true;
        }

        private List<MonitorMaterialBinding> ResolveBindings(string timelineName)
        {
            string normalized = NormalizeName(timelineName);
            if (string.IsNullOrEmpty(normalized)) return EmptyBindingList;
            if (_bindingCache.TryGetValue(normalized, out List<MonitorMaterialBinding> cached)) return cached;

            _resolveBuffer.Clear();
            string compact = CompactName(normalized);
            AddMatchesExact(normalized, compact, _resolveBuffer);
            if (_resolveBuffer.Count == 0) AddMatchesContains(normalized, compact, _resolveBuffer);
            if (_resolveBuffer.Count == 0 && TryExtractMonitorIndex(normalized, out int numericIndex)) AddMatchesByNumericIndex(numericIndex, _resolveBuffer);
            if (_resolveBuffer.Count == 0 && TryExtractMonitorLetterIndex(normalized, out int letterIndex)) AddMatchesByOrdinal(letterIndex, _resolveBuffer);

            List<MonitorMaterialBinding> resolved = new List<MonitorMaterialBinding>(_resolveBuffer.Count);
            for (int i = 0; i < _resolveBuffer.Count; i++)
            {
                MonitorMaterialBinding binding = _resolveBuffer[i];
                if (binding != null && !resolved.Contains(binding)) resolved.Add(binding);
            }
            _bindingCache[normalized] = resolved;
            return resolved;
        }

        private void AddMatchesExact(string normalized, string compact, List<MonitorMaterialBinding> result)
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                MonitorMaterialBinding b = _bindings[i];
                if (b == null) continue;
                if (b.materialKey == normalized || b.rendererKey == normalized || b.materialCompact == compact || b.rendererCompact == compact || b.groupKey == normalized || b.groupKey == compact)
                    result.Add(b);
            }
        }

        private void AddMatchesContains(string normalized, string compact, List<MonitorMaterialBinding> result)
        {
            for (int i = 0; i < _bindings.Count; i++)
            {
                MonitorMaterialBinding b = _bindings[i];
                if (b == null) continue;
                if (b.materialKey.Contains(normalized) || b.rendererKey.Contains(normalized) || (!string.IsNullOrEmpty(compact) && (b.materialCompact.Contains(compact) || b.rendererCompact.Contains(compact))))
                    result.Add(b);
            }
        }

        private void AddMatchesByNumericIndex(int monitorIndex, List<MonitorMaterialBinding> result)
        {
            string groupKey = $"monitor{monitorIndex:D3}", compactKey = CompactName(groupKey), relaxedKey = $"monitor{monitorIndex}";
            for (int i = 0; i < _bindings.Count; i++)
            {
                MonitorMaterialBinding b = _bindings[i];
                if (b == null) continue;
                if (b.groupKey == groupKey || b.materialKey.Contains(groupKey) || b.rendererKey.Contains(groupKey) ||
                    b.materialCompact.Contains(compactKey) || b.rendererCompact.Contains(compactKey) ||
                    b.materialCompact.Contains(relaxedKey) || b.rendererCompact.Contains(relaxedKey))
                    result.Add(b);
            }
        }

        private void AddMatchesByOrdinal(int ordinal, List<MonitorMaterialBinding> result)
        {
            if (ordinal < 0 || _bindings.Count == 0) return;
            List<string> groups = new List<string>(_bindings.Count);
            for (int i = 0; i < _bindings.Count; i++)
            {
                string groupKey = _bindings[i]?.groupKey;
                if (!string.IsNullOrEmpty(groupKey) && !groups.Contains(groupKey)) groups.Add(groupKey);
            }
            groups.Sort(StringComparer.OrdinalIgnoreCase);
            if (ordinal >= groups.Count) return;

            string targetGroup = groups[ordinal];
            for (int i = 0; i < _bindings.Count; i++)
            {
                MonitorMaterialBinding b = _bindings[i];
                if (b != null && b.groupKey == targetGroup) result.Add(b);
            }
        }

        private bool IsMonitorMaterial(Material material)
        {
            if (material == null) return false;
            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            if (!string.IsNullOrEmpty(shaderName) && shaderName.IndexOf(monitorShaderName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if ((material.name ?? string.Empty).IndexOf("monitor", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            int score = 0;
            if (HasTextureProperty(material, mainTexProperty)) score++;
            if (HasTextureProperty(material, filterTexProperty)) score++;
            if (HasTextureProperty(material, fadeTexProperty)) score++;
            if (TryHasProperty(material, alphaProperty)) score++;
            if (TryHasProperty(material, colorFadeProperty)) score++;
            return score >= 3;
        }

        private static bool HasTextureProperty(Material material, string propertyName) => material != null && !string.IsNullOrEmpty(propertyName) && material.HasProperty(propertyName);
        private static bool TryHasProperty(Material material, string propertyName) => material != null && !string.IsNullOrEmpty(propertyName) && material.HasProperty(propertyName);
        private static bool TrySetFloat(Material material, string propertyName, float value) { if (!TryHasProperty(material, propertyName)) return false; material.SetFloat(propertyName, value); return true; }
        private static bool TrySetColor(Material material, string propertyName, Color value) { if (!TryHasProperty(material, propertyName)) return false; material.SetColor(propertyName, value); return true; }
        private static bool IsColorEffectivelyClear(Color value) => value.a <= 0.0001f && value.r <= 0.0001f && value.g <= 0.0001f && value.b <= 0.0001f;
        private static bool Approximately(float a, float b) => Mathf.Abs(a - b) <= 0.0001f;
        private static bool Approximately(Vector2 a, Vector2 b) => Approximately(a.x, b.x) && Approximately(a.y, b.y);
        private static bool Approximately(Color a, Color b) => Approximately(a.r, b.r) && Approximately(a.g, b.g) && Approximately(a.b, b.b) && Approximately(a.a, b.a);

        private static string NormalizeName(string value) => string.IsNullOrEmpty(value) ? string.Empty : value.Replace("(Instance)", string.Empty).Replace("(Clone)", string.Empty).Trim().ToLowerInvariant();

        private static string CompactName(string value)
        {
            string normalized = NormalizeName(value);
            if (string.IsNullOrEmpty(normalized)) return string.Empty;
            char[] buffer = new char[normalized.Length];
            int count = 0;
            for (int i = 0; i < normalized.Length; i++) { char c = normalized[i]; if (char.IsLetterOrDigit(c)) buffer[count++] = c; }
            return count > 0 ? new string(buffer, 0, count) : string.Empty;
        }

        private static string BuildGroupKey(MonitorMaterialBinding binding)
        {
            if (binding == null) return string.Empty;
            if (TryExtractMonitorIndex(binding.materialKey, out int matIdx)) return $"monitor{matIdx:D3}";
            if (TryExtractMonitorIndex(binding.rendererKey, out int renIdx)) return $"monitor{renIdx:D3}";
            if (!string.IsNullOrEmpty(binding.materialCompact) && binding.materialCompact.Contains("monitor")) return binding.materialCompact;
            if (!string.IsNullOrEmpty(binding.rendererCompact) && binding.rendererCompact.Contains("monitor")) return binding.rendererCompact;
            return !string.IsNullOrEmpty(binding.materialCompact) ? binding.materialCompact : binding.rendererCompact;
        }

        private static bool TryExtractMonitorIndex(string value, out int index)
        {
            index = -1;
            string compact = CompactName(value);
            if (string.IsNullOrEmpty(compact)) return false;
            int monitorIndex = compact.IndexOf("monitor", StringComparison.OrdinalIgnoreCase);
            if (monitorIndex < 0) return false;
            monitorIndex += "monitor".Length;
            int start = monitorIndex;
            while (monitorIndex < compact.Length && char.IsDigit(compact[monitorIndex])) monitorIndex++;
            return monitorIndex > start && int.TryParse(compact.Substring(start, monitorIndex - start), out index);
        }

        private static bool TryExtractMonitorLetterIndex(string value, out int index)
        {
            index = -1;
            string compact = CompactName(value);
            if (string.IsNullOrEmpty(compact) || !compact.StartsWith("monitor", StringComparison.OrdinalIgnoreCase) || compact.Length != "monitor".Length + 1) return false;
            char c = compact[compact.Length - 1];
            if (c < 'a' || c > 'z') return false;
            index = c - 'a';
            return true;
        }
    }
}
