using UnityEngine;

namespace Gallop.Live
{
    /// <summary>
    /// 主摄像机多机位最终画面屏幕后处理合成组件 (MultiCameraFinalComposite)
    /// 挂载在主摄像机 (Camera.main) 节点上，处于摄像机渲染管线末端。
    /// 核心职责：
    /// 1. 当 Live 处于多机位激活合成状态时，将主相机已渲染的屏幕画面 (src) 与激活的次机位离屏 RenderTexture
    ///    按时间轴下发的分屏参数通过 MultiCameraComposite 执行 SDF 分割线/羽化 Blit 合成到目标帧缓冲 (dest)；
    /// 2. 当 Live 处于单镜头阶段（无多机位激活或 FadeValue 归零休眠）时，直接执行 Graphics.Blit(src, dest)，产生零额外性能损耗；
    /// 3. 支持多路分屏链式合成与单路分屏零分配快路径；
    /// 4. 支持 Inspector 手动指定合成器或运行时自动对接 Director.instance。
    /// </summary>
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class MultiCameraFinalComposite : MonoBehaviour
    {
        [Header("多机位合成器（可选，留空则自动从 Director.instance 获取）")]
        [SerializeField] private MultiCameraComposite _composite;

        /// <summary>
        /// 显式关联的多机位分屏合成器组件引用
        /// </summary>
        public MultiCameraComposite TargetComposite
        {
            get => _composite;
            set => _composite = value;
        }

        /// <summary>
        /// Unity 原生屏幕后处理回调：当挂载的 Camera 完成本帧几何与光影渲染后自动触发
        /// </summary>
        /// <param name="src">主相机本帧已完成渲染的屏幕画面</param>
        /// <param name="dest">输出目标帧缓冲或渲染纹理</param>
        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            Director director = Director.instance;
            if (director == null)
            {
                // 无 Director 时若显式配置了单个合成器则尝试合成
                if (_composite != null && _composite.IsCompositeActive && _composite.FadeValue > 0.001f)
                {
                    _composite.CompositeTextures(src, _composite.SubTexture, dest);
                }
                else
                {
                    Graphics.Blit(src, dest);
                }
                return;
            }

            MultiCameraComposite[] composites = director.MultiCameraComposites;
            if (composites == null || composites.Length == 0)
            {
                // 兼容单一合成器兜底检查
                MultiCameraComposite singleComp = director.MultiCamComposite != null ? director.MultiCamComposite : _composite;
                if (singleComp != null && singleComp.IsCompositeActive && singleComp.FadeValue > 0.001f)
                {
                    singleComp.CompositeTextures(src, singleComp.SubTexture, dest);
                }
                else
                {
                    Graphics.Blit(src, dest);
                }
                return;
            }

            // 统计处于激活合成状态且拥有有效次机位纹理的合成器
            int activeCount = 0;
            MultiCameraComposite firstActive = null;

            for (int i = 0; i < composites.Length; i++)
            {
                MultiCameraComposite c = composites[i];
                if (c != null && c.IsCompositeActive && c.FadeValue > 0.001f && c.SubTexture != null)
                {
                    activeCount++;
                    if (firstActive == null)
                    {
                        firstActive = c;
                    }
                }
            }

            // 当没有多机位激活时（单镜头阶段），直接执行 Graphics.Blit(src, dest)，产生零额外性能损耗
            if (activeCount == 0)
            {
                Graphics.Blit(src, dest);
                return;
            }

            // 优化快路径：仅有单一路次机位处于激活状态（最常见场景），无需分配临时 RenderTexture
            if (activeCount == 1)
            {
                firstActive.CompositeTextures(src, firstActive.SubTexture, dest);
                return;
            }

            // 多路机位同时激活时：利用临时双缓冲管线依次链式合成
            RenderTexture currentSource = src;
            RenderTexture tempRT1 = null;
            RenderTexture tempRT2 = null;
            int processed = 0;

            for (int i = 0; i < composites.Length; i++)
            {
                MultiCameraComposite c = composites[i];
                if (c != null && c.IsCompositeActive && c.FadeValue > 0.001f && c.SubTexture != null)
                {
                    processed++;
                    bool isLast = (processed == activeCount);

                    if (isLast)
                    {
                        // 最后一层直接写入最终输出 dest
                        c.CompositeTextures(currentSource, c.SubTexture, dest);
                    }
                    else
                    {
                        // 中间层通过临时双缓冲交替传递
                        RenderTexture nextTarget = (processed % 2 == 1)
                            ? (tempRT1 ?? (tempRT1 = RenderTexture.GetTemporary(src.width, src.height, 0, src.format)))
                            : (tempRT2 ?? (tempRT2 = RenderTexture.GetTemporary(src.width, src.height, 0, src.format)));

                        c.CompositeTextures(currentSource, c.SubTexture, nextTarget);
                        currentSource = nextTarget;
                    }
                }
            }

            if (tempRT1 != null)
            {
                RenderTexture.ReleaseTemporary(tempRT1);
            }
            if (tempRT2 != null)
            {
                RenderTexture.ReleaseTemporary(tempRT2);
            }
        }
    }
}
