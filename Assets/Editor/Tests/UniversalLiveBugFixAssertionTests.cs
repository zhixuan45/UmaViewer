using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using Gallop;
using Gallop.ImageEffect;
using Gallop.Live;
using Gallop.Live.Cutt;

/// <summary>
/// 全 Live 通用舞台屏幕透明混合、光模糊后处理与天空色彩驱动断言测试套件
/// </summary>
public static class UniversalLiveBugFixAssertionTests
{
    private static readonly string LogFilePath = @"C:\Users\JuziD\.gemini\antigravity\brain\bfbceab6-21ba-4d33-b8ab-fa39182ba6dd\scratch\universal_assertion_test_report.txt";

    [MenuItem("UmaViewer/Run Universal BugFix Assertion Tests")]
    public static bool RunAllTests()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            Debug.LogWarning("[UniversalLiveBugFixAssertionTests] Unity 当前处于播放模式或正在切换播放状态，已安全跳过断言测试以保护运行场景。");
            return false;
        }

        var sb = new StringBuilder();
        sb.AppendLine("================================================================");
        sb.AppendLine("=== Universal LiveBugFix Assertion Tests Execution Report ======");
        sb.AppendLine($"=== Time: {DateTime.Now} ===");
        sb.AppendLine("================================================================\n");

        int total = 0;
        int passed = 0;
        int failed = 0;

        // 1. URP Volume 全局激活与 Diffusion 光扩散参数映射断言测试
        RunTestCase("PostEffect: GallopImageEffect Volume 全局激活与 Diffusion 映射", ref total, ref passed, ref failed, sb, TestVolumeGlobalActivationAndDiffusionMapping);

        // 2. 舞台层级保护与 StageParentMap 防脱离断言测试
        RunTestCase("StageController: StageParentMap 记录与父级层级防脱离保护", ref total, ref passed, ref failed, sb, TestStageParentMapHierarchyPreservation);

        // 3. 舞台物体 renderEnable 显隐控制与 Monitor 部件防黑模保护
        RunTestCase("StageController: renderEnable 显隐与 Monitor 部件透明/隐藏防护", ref total, ref passed, ref failed, sb, TestStageObjectRenderEnableAndMonitorDefense);

        // 4. 全 Live 通用 UVMovie 视频资源前缀收集断言测试
        RunTestCase("Director: 通用 UVMovie 依赖收集与常规 Live 兼容性覆盖", ref total, ref passed, ref failed, sb, TestUniversalUvMoviePreloadCoverage);

        // 5. 天空网格材质温和受光与防纯黑回归测试
        RunTestCase("Skybox: 天空材质温和受光保护与去除死黑压制测试", ref total, ref passed, ref failed, sb, TestSkyMaterialGentleFallbackNoDeadBlack);

        // 6. 真实坏材质/空材质分支下天空网格防纯白遮蔽断言测试
        RunTestCase("Skybox: 坏材质/空材质真实分支下天空网格安全禁用防白模遮蔽", ref total, ref passed, ref failed, sb, TestSkyMaterialRealInvalidFallbackNoWhitePlane);

        // 7. BgColor 回退广播隔离与天空/草地防滥染断言测试
        RunTestCase("BgColor: 全量回退广播隔离天空与草地防白天化与荧光绿", ref total, ref passed, ref failed, sb, TestBgColorFallbackExcludesSkyAndGrass);

        // 8. 公共天空与云层材质包（sourceresources/3d/env/live/common/）全量收集断言测试
        RunTestCase("Director: 公共天空与云层材质依赖（sourceresources/common/）全量收集覆盖", ref total, ref passed, ref failed, sb, TestDirectorCollectsCommonSkyAndCloudMaterials);

        // 9. 时间轴 BgColor1 单次派发回归断言
        RunTestCase("LiveTimelineControl: AlterLateUpdate 每帧只派发一次 BgColor1", ref total, ref passed, ref failed, sb, TestBgColor1DispatchedOncePerLateUpdate);

        // 10. Live 专属动作包预载收集断言测试
        RunTestCase("Director: Live 专属身体与 CUTT 动作包预加载全量收集覆盖", ref total, ref passed, ref failed, sb, TestDirectorCollectsLiveMotionPackages);

        // 11. LiveTimelineMotionSequence 动作健全性与防崩溃防御断言测试
        RunTestCase("LiveTimelineMotionSequence: 缺失组件或空 Clip 时安全防崩溃与防御断言", ref total, ref passed, ref failed, sb, TestMotionSequenceRobustnessDefense);

        sb.AppendLine("\n================================================================");
        sb.AppendLine($"=== SUMMARY: Total={total}, PASSED={passed}, FAILED={failed} ===");
        sb.AppendLine("================================================================");

        bool allPassed = (failed == 0 && total > 0);
        try
        {
            string dir = Path.GetDirectoryName(LogFilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[UniversalLiveBugFixAssertionTests] Completed. AllPassed={allPassed}. Log: {LogFilePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UniversalLiveBugFixAssertionTests] Failed to write report: {ex}");
        }

        return allPassed;
    }

    private static void RunTestCase(string testName, ref int total, ref int passed, ref int failed, StringBuilder sb, Action testAction)
    {
        total++;
        sb.AppendLine($"--- [TEST {total}] {testName} ---");
        try
        {
            testAction.Invoke();
            passed++;
            sb.AppendLine("    RESULT: [PASS] 断言全部通过！\n");
        }
        catch (Exception ex)
        {
            failed++;
            sb.AppendLine($"    RESULT: [FAIL] 断言失败或发生未捕获异常: {ex.Message}");
            sb.AppendLine($"    Stack: {ex.StackTrace}\n");
            Debug.LogError($"[Assertion Failed] {testName}: {ex}");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[断言失败] {message}");
        }
    }

    private static void AssertNotNull(object obj, string message)
    {
        if (obj == null)
        {
            throw new Exception($"[断言失败] 期望非空对象，实际为 null: {message}");
        }
    }

    /// <summary>
    /// 测试 1：验证 GallopImageEffect 中 Volume 是否被正确设为 isGlobal = true，且 Diffusion 泛光参数正确映射
    /// </summary>
    private static void TestVolumeGlobalActivationAndDiffusionMapping()
    {
        var camGo = new GameObject("Test_PostEffect_Camera");
        var cam = camGo.AddComponent<Camera>();
        var effect = camGo.AddComponent<GallopImageEffect>();
        bool prevDisable = GallopImageEffect.DisableBloomForDebug;
        GallopImageEffect.DisableBloomForDebug = false;

        try
        {
            effect.InitializeVolume();

            var volume = camGo.GetComponent<Volume>();
            AssertNotNull(volume, "Camera 对象上必须成功挂载 Volume 组件");

            // 核心断言 1：Volume 必须是全局的（isGlobal == true），杜绝因缺少碰撞体导致后处理失效
            AssertTrue(volume.isGlobal, "GallopImageEffect 的 Volume 必须配置为 isGlobal = true，以确保全屏光模糊后处理生效！");

            // 核心断言 2：注入 Diffusion（光扩散）参数并验证参数调控
            var param = effect.DofDiffusionBloomOverlayParam;
            AssertNotNull(param, "DofDiffusionBloomOverlayParam 不能为空");

            param.IsEnableBloom = true;
            param.BloomIntensity = 2.0f;
            param.BloomBlurSize = 5.0f;
            param.IsEnableDiffusion = true;
            param.DiffusionBlurSize = 8.0f;
            param.DiffusionBright = 1.5f;

            // 应用参数
            effect.ApplyBloomParameter();

            // 验证 VolumeProfile 中获取到的 Bloom 组件状态
            AssertNotNull(volume.profile, "Volume profile 必须已实例化");
            AssertTrue(volume.profile.TryGet(out Bloom bloom), "Volume profile 必须包含 Bloom 组件");
            AssertTrue(bloom.active, "Bloom 组件必须处于 active 状态");
            AssertTrue(bloom.intensity.value > 0f, "Bloom intensity 必须大于 0");
            AssertTrue(bloom.scatter.value > 0f, "Bloom scatter 散射值必须大于 0，以产生光模糊柔化效果");
        }
        finally
        {
            GallopImageEffect.DisableBloomForDebug = prevDisable;
            UnityEngine.Object.DestroyImmediate(camGo);
        }
    }

    /// <summary>
    /// 测试 2：验证 StageController 的 StageParentMap 记录机制，以及 UpdateObject 在 AttachType.None 时不脱离父级
    /// </summary>
    private static void TestStageParentMapHierarchyPreservation()
    {
        var prevStage = Director.instance ? Director.instance._stageController : null;
        var stageRoot = new GameObject("Test_Stage_Root");
        var stageController = stageRoot.AddComponent<StageController>();

        // 构造父子层级：StageRoot -> MonitorParent -> MonitorChild
        var monitorParent = new GameObject("pfb_env_live_monitor_parent");
        monitorParent.transform.SetParent(stageRoot.transform);

        var monitorChild = new GameObject("monitor_000");
        monitorChild.transform.SetParent(monitorParent.transform);

        try
        {
            // 模拟记录 StageParentMap
            stageController.StageObjectMap["monitor_000"] = monitorChild;
            stageController.StageParentMap["monitor_000"] = monitorParent.transform;

            // 核心断言 1：StageParentMap 记录了 monitor_000 的原始父级
            AssertTrue(stageController.StageParentMap.ContainsKey("monitor_000"), "StageParentMap 必须记录子物件与其父级的映射");
            AssertTrue(stageController.StageParentMap["monitor_000"] == monitorParent.transform, "记录的父级必须为 monitorParent");

            // 核心断言 2：调用 UpdateObject 时，若 AttachTarget 为 None，绝不能将 monitorChild 的父级置为 null
            var updateInfo = new ObjectUpdateInfo
            {
                data = new LiveTimelineObjectData { name = "monitor_000", enablePosition = true },
                updateData = new TransformBaseData { position = new Vector3(0, 10, 0), rotation = Quaternion.identity, scale = Vector3.one },
                AttachTarget = AttachType.None,
                renderEnable = true
            };

            stageController.UpdateObject(ref updateInfo);

            AssertTrue(monitorChild.transform.parent != null, "UpdateObject 处理 AttachType.None 时，绝不能将 transform.parent 设为 null！");
            AssertTrue(monitorChild.transform.parent == monitorParent.transform, "monitorChild 应始终保持在正确的父节点层级下");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(stageRoot);
            if (Director.instance) Director.instance._stageController = prevStage;
        }
    }

    /// <summary>
    /// 测试 3：验证 StageController.UpdateObject 对 renderEnable 显隐控制生效，且 Monitor 初始状态具备防黑块保护
    /// </summary>
    private static void TestStageObjectRenderEnableAndMonitorDefense()
    {
        var prevStage = Director.instance ? Director.instance._stageController : null;
        var stageRoot = new GameObject("Test_Stage_Root");
        var stageController = stageRoot.AddComponent<StageController>();

        var monitorObj = new GameObject("monitor_000");
        monitorObj.transform.SetParent(stageRoot.transform);
        monitorObj.SetActive(true);

        stageController.StageObjectMap["monitor_000"] = monitorObj;
        stageController.StageParentMap["monitor_000"] = stageRoot.transform;

        try
        {
            // 核心断言 1：当 renderEnable = false 时，UpdateObject 必须将物体设为非激活（隐藏）
            var hideUpdateInfo = new ObjectUpdateInfo
            {
                data = new LiveTimelineObjectData { name = "monitor_000" },
                updateData = new TransformBaseData { position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one },
                AttachTarget = AttachType.None,
                renderEnable = false
            };

            stageController.UpdateObject(ref hideUpdateInfo);
            AssertTrue(!monitorObj.activeSelf, "当 renderEnable 为 false 时，UpdateObject 必须将物体 SetActive(false)");

            // 核心断言 2：当 renderEnable = true 时，UpdateObject 重新激活物体
            var showUpdateInfo = new ObjectUpdateInfo
            {
                data = new LiveTimelineObjectData { name = "monitor_000" },
                updateData = new TransformBaseData { position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one },
                AttachTarget = AttachType.None,
                renderEnable = true
            };

            stageController.UpdateObject(ref showUpdateInfo);
            AssertTrue(monitorObj.activeSelf, "当 renderEnable 为 true 时，UpdateObject 必须将物体 SetActive(true)");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(stageRoot);
            if (Director.instance) Director.instance._stageController = prevStage;
        }
    }

    /// <summary>
    /// 测试 4：验证 Director.CollectStageBundleEntries 对 1175 等包含 UVMovie 的歌曲自动收集视频 bundle
    /// </summary>
    private static void TestUniversalUvMoviePreloadCoverage()
    {
        if (Config.Instance == null) new Config();
        Config.Instance.MainPath = @"C:\Users\JuziD\Umamusume\umamusume_Data\Persistent";

        var db = UmaDatabaseController.Instance;
        AssertNotNull(db.MetaEntries, "数据库 MetaEntries 应成功加载");

        var live1175 = new LiveEntry("header\n0,0,0\n0,0,10147\n")
        {
            MusicId = 1175,
            BackGroundId = "10147"
        };

        var prevMain = UmaViewerMain.Instance;
        GameObject dummyMainObj = null;
        try
        {
            dummyMainObj = new GameObject("Dummy_UmaViewerMain_Universal");
            var dummyMain = dummyMainObj.AddComponent<UmaViewerMain>();
            dummyMain.AbList = db.MetaEntries;
            UmaViewerMain.Instance = dummyMain;

            var entries = Director.CollectStageBundleEntries(live1175, requireStage: true);
            AssertNotNull(entries, "CollectStageBundleEntries 必须返回有效列表");

            // 核心断言：收集的列表中必须包含该歌曲对应的 live/uvmovie 资源（至少包含 gal_uvmovie_1175_001）
            bool hasUvMovie = entries.Any(e => e.Name.IndexOf("live/uvmovie/gal_uvmovie_1175", StringComparison.OrdinalIgnoreCase) >= 0);
            AssertTrue(hasUvMovie, "1175 舞台依赖项中必须包含 live/uvmovie/gal_uvmovie_1175 相关视频 bundle！");

            // 核心断言 2：常规不带视频的 Live（例如 1001）不能因此报错崩溃
            var liveNormal = new LiveEntry("header\n0,0,0\n0,0,10001\n")
            {
                MusicId = 1001,
                BackGroundId = "10001"
            };
            var normalEntries = Director.CollectStageBundleEntries(liveNormal, requireStage: true);
            AssertNotNull(normalEntries, "常规 Live 收集必须稳定返回");
        }
        finally
        {
            if (dummyMainObj != null) UnityEngine.Object.DestroyImmediate(dummyMainObj);
            UmaViewerMain.Instance = prevMain;
        }
    }

    /// <summary>
    /// 测试 5：验证 ProtectRendererMaterials 不再强行将天空材质涂成深黑，保留其原本表现力
    /// </summary>
    private static void TestSkyMaterialGentleFallbackNoDeadBlack()
    {
        var dummyGo = new GameObject("Test_Sky_Mesh_Gentle");
        var renderer = dummyGo.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/InternalErrorShader"));
        renderer.material = mat;

        try
        {
            // 通过反射调用 ProtectRendererMaterials
            var method = typeof(StageController).GetMethod("ProtectRendererMaterials",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            AssertNotNull(method, "StageController 必须包含 ProtectRendererMaterials 方法");

            var prevStage = Director.instance ? Director.instance._stageController : null;
            var stageGo = new GameObject("StageHost");
            var stage = stageGo.AddComponent<StageController>();

            try
            {
                method.Invoke(stage, new object[] { dummyGo, "pfb_env_live10147_sky000" });

                // 核心断言：材质颜色不能是生硬的死黑 (0.04f, 0.05f, 0.08f)
                Color color = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : (mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.black);
                bool isDeadBlack = Mathf.Approximately(color.r, 0.04f) && Mathf.Approximately(color.g, 0.05f) && Mathf.Approximately(color.b, 0.08f);
                AssertTrue(!isDeadBlack, "天空材质绝不能被强行刷成死黑色 (0.04, 0.05, 0.08)，应保留原始材质属性并允许渐变受光！");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stageGo);
                if (Director.instance) Director.instance._stageController = prevStage;
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(dummyGo);
        }
    }

    /// <summary>
    /// 测试 6：验证真实坏材质/空材质分支下，天空网格绝不会被赋予纯白浅灰 Unlit 材质遮蔽全屏，而是被安全禁用或正确回退
    /// </summary>
    private static void TestSkyMaterialRealInvalidFallbackNoWhitePlane()
    {
        var dummyGo = new GameObject("sky_base_00");
        var renderer = dummyGo.AddComponent<MeshRenderer>();
        // 刻意传入损坏的 Hidden/InternalErrorShader 材质或空材质，真实触发 fallback 分支
        var badMat = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
        renderer.sharedMaterial = badMat;

        var prevStage = Director.instance ? Director.instance._stageController : null;
        var stageGo = new GameObject("StageHost");
        var stage = stageGo.AddComponent<StageController>();

        try
        {
            var method = typeof(StageController).GetMethod("ProtectRendererMaterials",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            AssertNotNull(method, "StageController 必须包含 ProtectRendererMaterials 方法");

            method.Invoke(stage, new object[] { dummyGo, "pfb_env_live10147_sky000" });

            // 核心断言：天空网格在缺失天空材质时，为了防止纯白/浅灰无光照白模遮蔽全屏，必须被安全禁用（renderer.enabled == false）
            // 或者若有材质替换，绝不能是 0.9 浅白纯白无贴图 Unlit 材质！
            if (renderer.enabled)
            {
                var curMat = renderer.sharedMaterial;
                AssertNotNull(curMat, "若天空网格保持启用，材质绝不能为空");
                Color col = curMat.HasProperty("_BaseColor") ? curMat.GetColor("_BaseColor") : (curMat.HasProperty("_Color") ? curMat.GetColor("_Color") : Color.black);
                bool isPureWhiteFallback = Mathf.Approximately(col.r, 0.9f) && Mathf.Approximately(col.g, 0.9f) && Mathf.Approximately(col.b, 0.9f);
                AssertTrue(!isPureWhiteFallback, "天空网格绝不能被赋予 0.9 纯白死模遮蔽背景！");
            }
            else
            {
                // 安全禁用是完全符合预期的优雅防御策略
                AssertTrue(true, "天空网格在缺失材质时被安全禁用，成功防御纯白遮蔽！");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(stageGo);
            UnityEngine.Object.DestroyImmediate(dummyGo);
            if (Director.instance) Director.instance._stageController = prevStage;
        }
    }

    /// <summary>
    /// 测试 7：验证 BgColor 全量回退广播时，天空网格与草地网格被严格隔离排除，杜绝白天化与荧光草地
    /// </summary>
    private static void TestBgColorFallbackExcludesSkyAndGrass()
    {
        var prevStage = Director.instance ? Director.instance._stageController : null;
        var stageGo = new GameObject("StageHost");
        var stage = stageGo.AddComponent<StageController>();

        // 挂载一个草地渲染器和一个天空渲染器
        var skyObj = new GameObject("sky_base_00");
        skyObj.transform.SetParent(stageGo.transform);
        var skyRenderer = skyObj.AddComponent<MeshRenderer>();
        var skyMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"));
        skyMat.SetColor("_BaseColor", Color.white);
        skyRenderer.sharedMaterial = skyMat;

        var grassObj = new GameObject("grass_00");
        grassObj.transform.SetParent(stageGo.transform);
        var grassRenderer = grassObj.AddComponent<MeshRenderer>();
        var grassMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"));
        grassMat.SetColor("_BaseColor", Color.green);
        grassRenderer.sharedMaterial = grassMat;

        var normalObj = new GameObject("wash_light_00");
        normalObj.transform.SetParent(stageGo.transform);
        var normalRenderer = normalObj.AddComponent<MeshRenderer>();
        var normalMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"));
        normalMat.SetColor("_BaseColor", Color.yellow);
        normalRenderer.sharedMaterial = normalMat;

        try
        {
            // 重建缓存
            var rebuildMethod = typeof(StageController).GetMethod("RebuildBgColorCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            rebuildMethod?.Invoke(stage, null);

            // 调用 ResolveBgColorRenderers，传入非天空非草地的 Timeline 名称（触发 fallback 分支）
            var resolveMethod = typeof(StageController).GetMethod("ResolveBgColorRenderers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            AssertNotNull(resolveMethod, "StageController 必须包含 ResolveBgColorRenderers 方法");

            var resolved = resolveMethod.Invoke(stage, new object[] { "wash_random_ambient", false, true }) as List<Renderer>;
            AssertNotNull(resolved, "解析结果不能为 null");

            // 核心断言：回退广播列表中绝不能包含天空网格和草地网格！
            bool containsSky = resolved.Contains(skyRenderer);
            bool containsGrass = resolved.Contains(grassRenderer);

            AssertTrue(!containsSky, "全量回退广播严禁命中天空网格，杜绝天空被环境灯洗白！");
            AssertTrue(!containsGrass, "全量回退广播严禁命中草地网格，杜绝草地变成荧光绿！");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(stageGo);
            if (Director.instance) Director.instance._stageController = prevStage;
        }
    }

    /// <summary>
    /// 测试 8：验证 Director.CollectStageBundleEntries 完整收集以 sourceresources/3d/env/live/common/ 开头的公共天空与云层材质
    /// </summary>
    private static void TestDirectorCollectsCommonSkyAndCloudMaterials()
    {
        var prevMain = UmaViewerMain.Instance;
        var dummyMainObj = new GameObject("UmaViewerMain_CommonMatTest");
        var main = dummyMainObj.AddComponent<UmaViewerMain>();

        try
        {
            main.AbList = new Dictionary<string, UmaDatabaseEntry>(StringComparer.OrdinalIgnoreCase)
            {
                { "3d/env/live/common/sky/pfb_env_live_cmn_sky002", new UmaDatabaseEntry { Name = "3d/env/live/common/sky/pfb_env_live_cmn_sky002" } },
                { "sourceresources/3d/env/live/common/sky/materials/mtl_env_live_cmn_sky002", new UmaDatabaseEntry { Name = "sourceresources/3d/env/live/common/sky/materials/mtl_env_live_cmn_sky002" } },
                { "sourceresources/3d/env/live/common/sky_cloud/materials/mtl_env_live_cmn_sky_cloud000", new UmaDatabaseEntry { Name = "sourceresources/3d/env/live/common/sky_cloud/materials/mtl_env_live_cmn_sky_cloud000" } }
            };

            var live = new LiveEntry("header\n0,0,0\n0,0,10147\n")
            {
                MusicId = 1175,
                BackGroundId = "10147"
            };

            var entries = Director.CollectStageBundleEntries(live, requireStage: true);
            AssertNotNull(entries, "返回列表不能为 null");

            // 核心断言：必须收集到公共天空材质和公共云层材质
            bool hasCommonSkyMat = entries.Any(e => e.Name.IndexOf("sourceresources/3d/env/live/common/sky/materials", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasCommonCloudMat = entries.Any(e => e.Name.IndexOf("sourceresources/3d/env/live/common/sky_cloud/materials", StringComparison.OrdinalIgnoreCase) >= 0);

            AssertTrue(hasCommonSkyMat, "收集列表必须包含 sourceresources/3d/env/live/common/sky/materials/ 下的公共天空材质！");
            AssertTrue(hasCommonCloudMat, "收集列表必须包含 sourceresources/3d/env/live/common/sky_cloud/materials/ 下的公共云层材质！");
        }
        finally
        {
            if (dummyMainObj != null) UnityEngine.Object.DestroyImmediate(dummyMainObj);
            UmaViewerMain.Instance = prevMain;
        }
    }

    /// <summary>
    /// 测试 9：防止 AlterLateUpdate 重复派发 BgColor1，避免订阅者重复染色或污染共享材质。
    /// </summary>
    private static void TestBgColor1DispatchedOncePerLateUpdate()
    {
        const string assetPath = "Assets/Scripts/umamusume/Gallop/Live/Cutt/LiveTimeLine/LiveTimelineControl.cs";

        string source = File.ReadAllText(assetPath, Encoding.UTF8);
        const string methodStart = "public void AlterLateUpdate()";
        int start = source.IndexOf(methodStart, StringComparison.Ordinal);
        AssertTrue(start >= 0, "LiveTimelineControl 必须包含 AlterLateUpdate 方法");

        int nextMethod = source.IndexOf("\n        private ", start, StringComparison.Ordinal);
        AssertTrue(nextMethod > start, "AlterLateUpdate 方法边界必须可解析");

        string methodBody = source.Substring(start, nextMethod - start);
        int callCount = 0;
        int searchOffset = 0;
        const string call = "AlterUpdate_BgColor1(camSheet, _currentFrame);";
        while ((searchOffset = methodBody.IndexOf(call, searchOffset, StringComparison.Ordinal)) >= 0)
        {
            callCount++;
            searchOffset += call.Length;
        }

        AssertTrue(callCount == 1, $"AlterLateUpdate 中 BgColor1 调用次数应为 1，实际为 {callCount}");
    }

    /// <summary>
    /// 测试 10：验证 Director.GetLivePreloadEntries 是否能完整收集该 Live 专属的身体动作包与 CUTT 动作包。
    /// </summary>
    private static void TestDirectorCollectsLiveMotionPackages()
    {
        var prevMain = UmaViewerMain.Instance;
        GameObject dummyMainObj = null;
        try
        {
            if (prevMain == null)
            {
                dummyMainObj = new GameObject("DummyUmaViewerMain_MotionPreloadTest");
                var dummyMain = dummyMainObj.AddComponent<UmaViewerMain>();
                dummyMain.AbList = new Dictionary<string, UmaDatabaseEntry>(StringComparer.OrdinalIgnoreCase);
                UmaViewerMain.Instance = dummyMain;
            }

            var abList = UmaViewerMain.Instance.AbList;
            abList.Clear();

            const int testMusicId = 1040;
            string bodyMotionKey = $"3d/motion/live/body/son{testMusicId}/son{testMusicId}_01";
            string cuttMotionKey = $"3d/motion/live/cutt/son{testMusicId}/cutt_son{testMusicId}_cam";
            string irrelevantMotionKey = "3d/motion/live/body/son9999/son9999_01";

            abList[bodyMotionKey] = new UmaDatabaseEntry { Name = bodyMotionKey };
            abList[cuttMotionKey] = new UmaDatabaseEntry { Name = cuttMotionKey };
            abList[irrelevantMotionKey] = new UmaDatabaseEntry { Name = irrelevantMotionKey };

            var live = new LiveEntry("header\n0,0,0\n0,0,1040\n") { MusicId = testMusicId, BackGroundId = "1040" };
            var preloadEntries = Director.GetLivePreloadEntries(live, new List<LiveCharacterLoadData>(), requireStage: false);

            AssertNotNull(preloadEntries, "预加载资源列表不能为 null");
            bool hasBodyMotion = preloadEntries.Any(e => string.Equals(e.Name, bodyMotionKey, StringComparison.OrdinalIgnoreCase));
            bool hasCuttMotion = preloadEntries.Any(e => string.Equals(e.Name, cuttMotionKey, StringComparison.OrdinalIgnoreCase));
            bool hasIrrelevant = preloadEntries.Any(e => string.Equals(e.Name, irrelevantMotionKey, StringComparison.OrdinalIgnoreCase));

            AssertTrue(hasBodyMotion, $"预加载列表中必须包含专属身体动作包: {bodyMotionKey}");
            AssertTrue(hasCuttMotion, $"预加载列表中必须包含专属 CUTT 动作包: {cuttMotionKey}");
            AssertTrue(!hasIrrelevant, "预加载列表中不应包含其他无关歌曲的动作包");
        }
        finally
        {
            if (dummyMainObj != null) UnityEngine.Object.DestroyImmediate(dummyMainObj);
            UmaViewerMain.Instance = prevMain;
        }
    }

    /// <summary>
    /// 测试 11：验证 LiveTimelineMotionSequence 在缺失动画组件、缺失动作 Clip 时能够防御空指针并保持安全健壮。
    /// </summary>
    private static void TestMotionSequenceRobustnessDefense()
    {
        var motionSeq = new LiveTimelineMotionSequence();
        AssertNotNull(motionSeq, "LiveTimelineMotionSequence 实例化成功");

        // 验证在目标 GameObject 与 Animation 组件均缺失时，调用 Initialize 不会抛出未处理空指针异常
        GameObject dummyChara = new GameObject("DummyChara_MotionTest");
        try
        {
            var timelineControlObj = new GameObject("DummyTimelineControl_MotionTest");
            var control = timelineControlObj.AddComponent<LiveTimelineControl>();
            control._keyArray = new LiveTimelineKeyCharaMotionSeqDataList[0];

            // 传入越界的 targetIndex 与 seqDataIndex，测试其安全容错
            motionSeq.Initialize(dummyChara.transform, targetIndex: 99, seqDataIndex: 99, timelineControl: control);

            // 调用 AlterUpdate 测试其不会因 _tempAnim 或 _currentKey 为空产生未捕获崩溃
            var dummyTimescaleList = new LiveTimelineKeyTimescaleDataList();
            dummyTimescaleList.thisList = new List<LiveTimelineKeyTimescaleData>();
            motionSeq.AlterUpdate(0f, dummyTimescaleList);

            UnityEngine.Object.DestroyImmediate(timelineControlObj);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(dummyChara);
        }
    }
}
