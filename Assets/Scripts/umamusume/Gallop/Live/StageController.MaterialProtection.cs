using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// StageController 材质防护分部类：
    /// 负责舞台部件实例化后的材质损坏检测、空材质回退及备用材质生成，杜绝白色无光照死模。
    /// </summary>
    public partial class StageController
    {
        /// <summary>
        /// 针对舞台部件实例下的所有渲染器进行空材质防护检测与修复，防止因缺少材质而呈现纯白死模遮蔽舞台。
        /// </summary>
        /// <param name="instance">已实例化的舞台部件对象</param>
        /// <param name="partName">部件资源预制体名称</param>
        private void ProtectRendererMaterials(GameObject instance, string partName)
        {
            if (instance == null)
                return;

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return;

            Material[] loadedMaterialsCache = null;

            foreach (var r in renderers)
            {
                if (r == null)
                    continue;

                // 检测 sharedMaterial == null、sharedMaterials 包含 null，或者材质的 Shader 损坏
                var sharedMats = r.sharedMaterials;
                bool hasInvalid = false;

                if (sharedMats == null || sharedMats.Length == 0)
                {
                    hasInvalid = true;
                }
                else
                {
                    for (int i = 0; i < sharedMats.Length; i++)
                    {
                        var mat = sharedMats[i];
                        if (mat == null || mat.shader == null || !mat.shader.isSupported || mat.shader.name == "Hidden/InternalErrorShader")
                        {
                            hasInvalid = true;
                            break;
                        }
                    }
                }

                // 若材质正常存在且着色器完整，绝不强行覆盖修改其原有材质或底色
                if (!hasInvalid)
                    continue;

                bool isSky = (r.name ?? "").IndexOf("sky", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (partName ?? "").IndexOf("sky", StringComparison.OrdinalIgnoreCase) >= 0;

                Debug.LogWarning($"[StageController] 舞台部件 '{partName}' 下的渲染器 '{r.name}' (isSky={isSky}) 存在空材质或着色器损坏，正在尝试安全修复...");

                var fixedMats = (sharedMats != null && sharedMats.Length > 0)
                    ? (Material[])sharedMats.Clone()
                    : new Material[1];

                bool disableRenderer = false;

                for (int i = 0; i < fixedMats.Length; i++)
                {
                    var mat = fixedMats[i];
                    bool isCurrentInvalid = mat == null || mat.shader == null || !mat.shader.isSupported || mat.shader.name == "Hidden/InternalErrorShader";
                    if (!isCurrentInvalid)
                        continue;

                    // 1. 尝试从当前内存中已载入的材质中查找相匹配的材质
                    if (loadedMaterialsCache == null)
                    {
                        loadedMaterialsCache = Resources.FindObjectsOfTypeAll<Material>();
                    }

                    Material fallbackMat = FindMatchingLoadedMaterial(loadedMaterialsCache, r.name, partName);

                    // 2. 若未找到相匹配的已载入材质：
                    // 注意：天空网格严格禁止从同部件中借用草地或普通物体材质，防止借错导致全屏错乱！
                    if (fallbackMat == null && !isSky)
                    {
                        fallbackMat = FindSiblingFallbackMaterial(renderers);
                    }

                    // 3. 若仍未找到：
                    // 如果是天空网格且没有任何可用天空材质，安全禁用该 Renderer，防止纯白无光照白模遮蔽整个舞台背景！
                    if (fallbackMat == null)
                    {
                        if (isSky)
                        {
                            Debug.LogWarning($"[StageController] 天空网格 '{r.name}' 缺少天空专用材质，为防止白模遮蔽全屏，安全禁用该渲染器。");
                            disableRenderer = true;
                            break;
                        }

                        fallbackMat = CreateSafeFallbackMaterial(r.name);
                    }

                    fixedMats[i] = fallbackMat;
                }

                if (disableRenderer)
                {
                    r.enabled = false;
                }
                else
                {
                    r.sharedMaterials = fixedMats;
                    Debug.Log($"[StageController] 渲染器 '{r.name}' 材质已成功修复为安全材质: {string.Join(", ", fixedMats.Select(m => m != null ? m.name : "null"))}");
                }
            }
        }

        /// <summary>
        /// 从已载入的材质列表中按渲染器名、部件名检索最匹配的备用材质
        /// </summary>
        private Material FindMatchingLoadedMaterial(Material[] loadedMaterials, string rendererName, string partName)
        {
            if (loadedMaterials == null || loadedMaterials.Length == 0)
                return null;

            string rName = (rendererName ?? "").ToLowerInvariant();
            string pName = (partName ?? "").ToLowerInvariant();

            // 1. 精确/包含渲染器名称匹配（例如 sky000、sky001 等）
            if (!string.IsNullOrEmpty(rName))
            {
                for (int i = 0; i < loadedMaterials.Length; i++)
                {
                    var mat = loadedMaterials[i];
                    if (mat == null || string.IsNullOrEmpty(mat.name))
                        continue;

                    string mName = mat.name.ToLowerInvariant();
                    if (mName.Contains(rName))
                        return mat;
                }
            }

            // 2. 天空网格专属匹配：若当前为天空网格，优先匹配包含 sky 与 env 的材质
            bool isSky = rName.Contains("sky") || pName.Contains("sky");
            if (isSky)
            {
                for (int i = 0; i < loadedMaterials.Length; i++)
                {
                    var mat = loadedMaterials[i];
                    if (mat == null || string.IsNullOrEmpty(mat.name))
                        continue;

                    string mName = mat.name.ToLowerInvariant();
                    if (mName.Contains("sky") && mName.Contains("env"))
                        return mat;
                }
            }

            return null;
        }

        /// <summary>
        /// 从同一部件中的其他有效渲染器中获取可用的备用材质
        /// </summary>
        private Material FindSiblingFallbackMaterial(Renderer[] siblings)
        {
            if (siblings == null)
                return null;

            for (int i = 0; i < siblings.Length; i++)
            {
                var sib = siblings[i];
                if (sib == null)
                    continue;

                var mats = sib.sharedMaterials;
                if (mats == null)
                    continue;

                for (int j = 0; j < mats.Length; j++)
                {
                    if (mats[j] != null)
                        return mats[j];
                }
            }

            return null;
        }

        /// <summary>
        /// 创建温和的基础无光照材质兜底。移除将天空网格强行刷为深死黑的逻辑，采用自然柔和的浅白/浅灰，
        /// 确保材质缺失或着色器损坏时能够接受时间轴 BgColor 染色与光模糊（Bloom）后处理的晕染。
        /// </summary>
        private Material CreateSafeFallbackMaterial(string rendererName)
        {
            Shader safeShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (safeShader == null) safeShader = Shader.Find("Unlit/Color");
            if (safeShader == null) safeShader = Shader.Find("Unlit/Texture");
            if (safeShader == null) safeShader = Shader.Find("Sprites/Default");
            if (safeShader == null) safeShader = Shader.Find("Hidden/InternalErrorShader");

            Material mat = (safeShader != null) ? new Material(safeShader) : new Material(Shader.Find("Standard"));
            mat.name = $"Fallback_SafeUnlit_{rendererName ?? "unknown"}";

            // 采用自然柔和的浅白/浅灰色作为底色，允许时间轴的 BgColor 颜色乘法与 Bloom 辉光正常呈现
            Color fallbackColor = new Color(0.9f, 0.9f, 0.9f, 1f);

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", fallbackColor);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", fallbackColor);
            }

            return mat;
        }
    }
}
