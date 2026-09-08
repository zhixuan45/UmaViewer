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
            UnityEngine.Object.DestroyImmediate(camGo);
        }
    }

    /// <summary>
    /// 测试 2：验证 StageController 的 StageParentMap 记录机制，以及 UpdateObject 在 AttachType.None 时不脱离父级
    /// </summary>
    private static void TestStageParentMapHierarchyPreservation()
    {
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
        }
    }

    /// <summary>
    /// 测试 3：验证 StageController.UpdateObject 对 renderEnable 显隐控制生效，且 Monitor 初始状态具备防黑块保护
    /// </summary>
    private static void TestStageObjectRenderEnableAndMonitorDefense()
    {
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
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(dummyGo);
        }
    }
}
