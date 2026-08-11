using Gallop;
using LibMMD.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 将 CySpring 头发、耳朵、尾巴与裙摆骨骼转换为 PMX 2.0 原生刚体与 Joint。
/// </summary>
internal static class PMXPhysicsExporter
{
    // 首版使用独立组并关闭组内碰撞，避免没有身体碰撞体时发束互相挤压。
    private const int DynamicCollisionGroup = 3;
    private const int BodyCollisionGroup = 2;
    private const int SkirtCollisionGroup = 4;
    private const float DefaultRadius = 0.018f;
    private const float MinimumRadius = 0.006f;
    private const float MaximumRadius = 0.045f;
    private const float MinimumSegmentLength = 0.004f;
    private const float CapsuleThreshold = 2.5f;
    // 尾巴主要依靠弹簧回到导出时的静止姿态，宽角度范围仅用于防止关节翻转。
    private const float TailFreeBendDegrees = 75f;
    private const float TailFreeTwistDegrees = 30f;
    private const float TailRootXRestOffsetDegrees = 15f;
    private const float TailRootBendSpring = 12f;
    private const float TailTipBendSpring = 4f;
    private const float TailRootTwistSpring = 4f;
    private const float TailTipTwistSpring = 1.5f;
    private const float TailColliderClearanceMultiplier = 1.5f;
    private const float TailColliderMinimumRadiusMultiplier = 1.25f;
    private const float TailColliderMaximumRadius = 0.12f;
    private const float SkirtMinimumPanelHalfWidth = 0.012f;
    private const float SkirtMaximumPanelHalfWidth = 0.08f;
    private const float SkirtMinimumPanelHalfThickness = 0.004f;
    private const float SkirtMaximumPanelHalfThickness = 0.016f;
    private const float SkirtVerticalBendDegrees = 16f;
    private const float SkirtVerticalTwistDegrees = 6f;
    private const float SkirtHorizontalBendDegrees = 7f;
    private const float SkirtHorizontalTwistDegrees = 4f;
    private const float SkirtLegColliderRadiusScale = 0.8f;

    // 普通发束和尾巴减少摆幅，同时保留可见的延迟跟随。
    private static readonly PhysicsPreset DefaultPreset = new PhysicsPreset
    {
        RootMass = 0.8f,
        TipMass = 0.25f,
        RootTranslateDamp = 0.68f,
        TipTranslateDamp = 0.88f,
        RootRotateDamp = 0.76f,
        TipRotateDamp = 0.92f,
        BendLimitDegrees = 18f,
        TwistLimitDegrees = 8f,
        BendSpring = 12f,
        TwistSpring = 5f
    };

    // 耳朵应接近短而硬的附属骨，单独限制角度并加快回正。
    private static readonly PhysicsPreset EarPreset = new PhysicsPreset
    {
        RootMass = 0.65f,
        TipMass = 0.3f,
        RootTranslateDamp = 0.82f,
        TipTranslateDamp = 0.94f,
        RootRotateDamp = 0.88f,
        TipRotateDamp = 0.96f,
        BendLimitDegrees = 9f,
        TwistLimitDegrees = 4.5f,
        BendSpring = 20f,
        TwistSpring = 9f
    };

    // 尾根保留支撑，中后段降低阻尼与弹簧，避免整条尾巴像一根硬杆。
    private static readonly PhysicsPreset TailPreset = new PhysicsPreset
    {
        RootMass = 0.9f,
        TipMass = 0.28f,
        RootTranslateDamp = 0.72f,
        TipTranslateDamp = 0.86f,
        RootRotateDamp = 0.72f,
        TipRotateDamp = 0.88f,
        BendLimitDegrees = TailFreeBendDegrees,
        TwistLimitDegrees = TailFreeTwistDegrees,
        BendSpring = TailRootBendSpring,
        TwistSpring = TailRootTwistSpring
    };

    // 裙摆需要比发束更高的阻尼，并通过横向关节保持连续布片形状。
    private static readonly PhysicsPreset SkirtPreset = new PhysicsPreset
    {
        RootMass = 0.7f,
        TipMass = 0.3f,
        RootTranslateDamp = 0.82f,
        TipTranslateDamp = 0.92f,
        RootRotateDamp = 0.86f,
        TipRotateDamp = 0.95f,
        BendLimitDegrees = SkirtVerticalBendDegrees,
        TwistLimitDegrees = SkirtVerticalTwistDegrees,
        BendSpring = 22f,
        TwistSpring = 9f
    };

    private sealed class PhysicsPreset
    {
        internal float RootMass;
        internal float TipMass;
        internal float RootTranslateDamp;
        internal float TipTranslateDamp;
        internal float RootRotateDamp;
        internal float TipRotateDamp;
        internal float BendLimitDegrees;
        internal float TwistLimitDegrees;
        internal float BendSpring;
        internal float TwistSpring;
    }

    internal sealed class Context
    {
        internal readonly List<Chain> Chains = new List<Chain>();
        internal readonly List<SkirtColumn> SkirtColumns = new List<SkirtColumn>();
        internal readonly HashSet<Transform> DynamicBones = new HashSet<Transform>();
        internal SkirtController SkirtController;
    }

    internal sealed class Chain
    {
        internal Transform Root;
        internal bool IsEar;
        internal bool IsTail;
        internal readonly HashSet<Transform> Bones = new HashSet<Transform>();
        internal readonly Dictionary<Transform, float> Radii = new Dictionary<Transform, float>();
    }

    internal sealed class SkirtColumn
    {
        internal Chain Chain;
        internal bool IsCheckRightLeg;
        internal bool IsCheckLeftLeg;
    }

    private sealed class SkirtSegment
    {
        internal Transform Bone;
        internal Transform Child;
        internal int ColumnIndex;
        internal int RowIndex;
        internal int RigidIndex;
        internal Vector3 Start;
        internal Vector3 End;
        internal Vector3 Center;
        internal Vector3 Rotation;
        internal float Length;
        internal float HalfWidth;
        internal float HalfThickness;
    }

    internal static Context Prepare(UmaContainerCharacter character, Transform skeletonRoot)
    {
        Context context = new Context();
        if (character == null || skeletonRoot == null || character.cySpringDataContainers == null)
            return context;

        Transform[] hierarchy = skeletonRoot.GetComponentsInChildren<Transform>(true);
        HashSet<Transform> claimedRoots = new HashSet<Transform>();
        context.SkirtController = character.GetComponent<SkirtController>() ??
                                  character.GetComponentInChildren<SkirtController>(true);
        CollectSkirtColumns(character, skeletonRoot, context, claimedRoots);

        foreach (CySpringDataContainer container in character.cySpringDataContainers)
        {
            if (container == null || container.springParam == null) continue;

            foreach (CySpringParamDataElement element in container.springParam)
            {
                if (!IsLinearPhysicsElement(container, element)) continue;
                Chain chain = BuildChain(element, hierarchy);
                if (chain == null || !claimedRoots.Add(chain.Root)) continue;

                context.Chains.Add(chain);
                context.DynamicBones.UnionWith(chain.Bones);
            }
        }

        return context;
    }

    internal static void Build(Context context, Transform coordinateRoot, PMXBoneExporter.Result boneResult,
        RawMMDModel model)
    {
        List<MMDRigidBody> rigidBodies = new List<MMDRigidBody>();
        List<MMDJoint> joints = new List<MMDJoint>();
        Dictionary<Transform, int> dynamicRigidIndexes = new Dictionary<Transform, int>();

        foreach (Chain chain in context.Chains)
        {
            BuildChainPhysics(chain, coordinateRoot, boneResult, rigidBodies, joints, dynamicRigidIndexes);
        }
        BuildSkirtPhysics(context, coordinateRoot, boneResult, rigidBodies, joints, dynamicRigidIndexes);

        MarkPostPhysicsBones(dynamicRigidIndexes.Keys, boneResult);
        Validate(rigidBodies, joints, boneResult.Bones.Length);
        model.Rigidbodies = rigidBodies.ToArray();
        model.Joints = joints.ToArray();
    }

    private static Chain BuildChain(CySpringParamDataElement element, Transform[] hierarchy)
    {
        if (element == null || string.IsNullOrEmpty(element._boneName)) return null;

        Dictionary<string, float> configuredBones = new Dictionary<string, float>(StringComparer.Ordinal);
        configuredBones[element._boneName] = SanitizeRadius(element._collisionRadius);
        if (element._childElements != null)
        {
            foreach (CySpringParamDataChildElement child in element._childElements)
            {
                if (child != null && !string.IsNullOrEmpty(child._boneName))
                    configuredBones[child._boneName] = SanitizeRadius(child._collisionRadius);
            }
        }

        // 同名节点存在时，选择能覆盖最多配置子骨的候选根，避免绑定到附件或辅助节点。
        Transform root = hierarchy
            .Where(t => string.Equals(t.name, element._boneName, StringComparison.Ordinal))
            .OrderByDescending(t => CountConfiguredDescendants(t, configuredBones))
            .FirstOrDefault();
        if (root == null) return null;

        Chain chain = new Chain
        {
            Root = root,
            // 使用整条配置链判断，兼容根骨名称不含部位、子骨才带 ear/mimi 的模型。
            IsEar = configuredBones.Keys.Any(IsEarBoneName),
            IsTail = configuredBones.Keys.Any(IsTailBoneName)
        };
        CollectContinuousBones(root, configuredBones, chain);
        return chain.Bones.Count > 0 ? chain : null;
    }

    private static void CollectSkirtColumns(UmaContainerCharacter character, Transform skeletonRoot,
        Context context, HashSet<Transform> claimedRoots)
    {
        SkirtController controller = context.SkirtController;
        if (controller == null || controller.SkirtDataArray == null) return;

        foreach (SkirtController.SkirtData skirtData in controller.SkirtDataArray)
        {
            if (skirtData == null || skirtData.SkirtRoot == null || skirtData.SkirtChild == null) continue;
            if (skirtData.SkirtRoot != skeletonRoot && !skirtData.SkirtRoot.IsChildOf(skeletonRoot)) continue;
            if (claimedRoots.Contains(skirtData.SkirtRoot)) continue;

            Chain bestChain = null;
            foreach (CySpringDataContainer container in character.cySpringDataContainers)
            {
                if (container == null || container.springParam == null) continue;
                foreach (CySpringParamDataElement element in container.springParam)
                {
                    if (element == null || !string.Equals(
                            element._boneName, skirtData.SkirtRoot.name, StringComparison.Ordinal)) continue;

                    Chain candidate = BuildChainFromRoot(element, skirtData.SkirtRoot);
                    if (candidate == null || !candidate.Bones.Contains(skirtData.SkirtChild) ||
                        !IsStrictLinearChain(candidate, skirtData.SkirtChild)) continue;
                    if (bestChain == null || candidate.Bones.Count > bestChain.Bones.Count) bestChain = candidate;
                }
            }

            if (bestChain == null) continue;
            claimedRoots.Add(bestChain.Root);
            context.SkirtColumns.Add(new SkirtColumn
            {
                Chain = bestChain,
                IsCheckRightLeg = skirtData.IsCheckRightLeg,
                IsCheckLeftLeg = skirtData.IsCheckLeftLeg
            });

            foreach (Transform bone in bestChain.Bones)
            {
                // 裙摆末端即使没有蒙皮权重，也决定上一节骨的末端指向和整条物理链的完整性。
                // 此处的骨骼已经通过 SkirtController、CySpring 与严格线性链三重校验，可以安全保留。
                context.DynamicBones.Add(bone);
            }
        }
    }

    private static Chain BuildChainFromRoot(CySpringParamDataElement element, Transform root)
    {
        if (element == null || root == null ||
            !string.Equals(element._boneName, root.name, StringComparison.Ordinal)) return null;

        Dictionary<string, float> configuredBones = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [element._boneName] = SanitizeRadius(element._collisionRadius)
        };
        if (element._childElements != null)
        {
            foreach (CySpringParamDataChildElement child in element._childElements)
            {
                if (child != null && !string.IsNullOrEmpty(child._boneName))
                    configuredBones[child._boneName] = SanitizeRadius(child._collisionRadius);
            }
        }

        Chain chain = new Chain { Root = root };
        CollectContinuousBones(root, configuredBones, chain);
        return chain.Bones.Count > 0 ? chain : null;
    }

    private static bool IsStrictLinearChain(Chain chain, Transform expectedFirstChild)
    {
        if (chain == null || chain.Root == null || expectedFirstChild == null) return false;

        Transform current = chain.Root;
        int visited = 0;
        bool foundExpectedChild = false;
        while (current != null && chain.Bones.Contains(current))
        {
            visited++;
            Transform next = GetSingleChainChild(current, chain.Bones);
            if (current == chain.Root) foundExpectedChild = next == expectedFirstChild;
            if (next == null) break;
            current = next;
        }
        return foundExpectedChild && visited == chain.Bones.Count && visited >= 2;
    }

    private static void CollectContinuousBones(Transform bone, Dictionary<string, float> configuredBones, Chain chain)
    {
        if (!configuredBones.TryGetValue(bone.name, out float radius)) return;

        chain.Bones.Add(bone);
        chain.Radii[bone] = radius;
        for (int i = 0; i < bone.childCount; i++)
        {
            Transform child = bone.GetChild(i);
            // 只沿真实父子关系前进，配置中间缺节点时绝不跨越连接。
            if (configuredBones.ContainsKey(child.name)) CollectContinuousBones(child, configuredBones, chain);
        }
    }

    private static int CountConfiguredDescendants(Transform root, Dictionary<string, float> configuredBones)
    {
        int count = configuredBones.ContainsKey(root.name) ? 1 : 0;
        for (int i = 0; i < root.childCount; i++)
            count += CountConfiguredDescendants(root.GetChild(i), configuredBones);
        return count;
    }

    private static void BuildChainPhysics(Chain chain, Transform coordinateRoot, PMXBoneExporter.Result boneResult,
        List<MMDRigidBody> rigidBodies, List<MMDJoint> joints,
        Dictionary<Transform, int> dynamicRigidIndexes)
    {
        if (!boneResult.BoneIndexes.ContainsKey(chain.Root)) return;

        PhysicsPreset preset = chain.IsEar ? EarPreset : chain.IsTail ? TailPreset : DefaultPreset;
        Transform anchorBone = FindNearestExportedParentTransform(chain.Root.parent, boneResult.BoneIndexes);
        int anchorBoneIndex = anchorBone != null ? boneResult.BoneIndexes[anchorBone] : 0;
        int anchorIndex = rigidBodies.Count;
        rigidBodies.Add(CreateAnchorBody(chain, coordinateRoot, anchorBoneIndex));

        if (chain.IsTail)
        {
            MMDRigidBody bodyCollider = CreateTailBodyCollider(chain, coordinateRoot, anchorBone, anchorBoneIndex);
            if (bodyCollider != null) rigidBodies.Add(bodyCollider);
        }

        List<Transform> orderedBones = chain.Bones.OrderBy(GetDepth).ToList();
        int maximumDepth = orderedBones.Count > 0 ? orderedBones.Max(GetDepth) - GetDepth(chain.Root) : 0;
        float jointBendLimit = preset.BendLimitDegrees;
        float jointTwistLimit = preset.TwistLimitDegrees;

        foreach (Transform bone in orderedBones)
        {
            if (!boneResult.BoneIndexes.TryGetValue(bone, out int boneIndex) || dynamicRigidIndexes.ContainsKey(bone))
                continue;

            Transform child = GetSingleChainChild(bone, chain.Bones);
            float depthRatio = maximumDepth > 0
                ? Mathf.Clamp01((GetDepth(bone) - GetDepth(chain.Root)) / (float)maximumDepth)
                : 1f;
            int rigidIndex = rigidBodies.Count;
            rigidBodies.Add(CreateDynamicBody(
                bone, child, chain.Radii[bone], depthRatio, coordinateRoot, boneIndex, preset));
            dynamicRigidIndexes[bone] = rigidIndex;

            int parentRigidIndex = bone == chain.Root
                ? anchorIndex
                : FindParentRigidIndex(bone.parent, chain.Bones, dynamicRigidIndexes, anchorIndex);
            float bendSpring = chain.IsTail
                ? Mathf.Lerp(TailRootBendSpring, TailTipBendSpring, depthRatio)
                : preset.BendSpring;
            float twistSpring = chain.IsTail
                ? Mathf.Lerp(TailRootTwistSpring, TailTipTwistSpring, depthRatio)
                : preset.TwistSpring;
            joints.Add(CreateJoint(
                bone, coordinateRoot, parentRigidIndex, rigidIndex,
                jointBendLimit, jointTwistLimit, bendSpring, twistSpring,
                chain.IsTail && bone == chain.Root));
        }
    }

    private static void BuildSkirtPhysics(Context context, Transform coordinateRoot,
        PMXBoneExporter.Result boneResult, List<MMDRigidBody> rigidBodies, List<MMDJoint> joints,
        Dictionary<Transform, int> dynamicRigidIndexes)
    {
        if (context.SkirtController == null || context.SkirtColumns.Count < 3) return;

        List<SkirtColumn> columns = OrderSkirtColumns(context.SkirtColumns, context.SkirtController, coordinateRoot);
        List<List<SkirtSegment>> segmentsByColumn = new List<List<SkirtSegment>>();
        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            List<Transform> bones = GetOrderedLinearBones(columns[columnIndex].Chain);
            List<SkirtSegment> segments = new List<SkirtSegment>();
            for (int rowIndex = 0; rowIndex + 1 < bones.Count; rowIndex++)
            {
                Transform bone = bones[rowIndex];
                Transform child = bones[rowIndex + 1];
                if (!boneResult.BoneIndexes.ContainsKey(bone)) break;

                Vector3 start = coordinateRoot.InverseTransformPoint(bone.position);
                Vector3 end = coordinateRoot.InverseTransformPoint(child.position);
                float length = Vector3.Distance(start, end);
                if (!IsFinite(length) || length <= MinimumSegmentLength) break;

                segments.Add(new SkirtSegment
                {
                    Bone = bone,
                    Child = child,
                    ColumnIndex = columnIndex,
                    RowIndex = rowIndex,
                    Start = start,
                    End = end,
                    Center = (start + end) * 0.5f,
                    Length = length,
                    RigidIndex = -1
                });
            }
            segmentsByColumn.Add(segments);
        }

        // 少于三列或任意列没有有效首段时无法建立稳定的裙摆网格。
        if (segmentsByColumn.Count < 3 || segmentsByColumn.Any(segments => segments.Count == 0)) return;

        bool closeRing = IsClosedSkirtRing(columns, context.SkirtController, coordinateRoot);
        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            List<SkirtSegment> segments = segmentsByColumn[columnIndex];
            for (int rowIndex = 0; rowIndex < segments.Count; rowIndex++)
            {
                SkirtSegment segment = segments[rowIndex];
                Vector3 tangent = CalculateSkirtTangent(segmentsByColumn, columnIndex, rowIndex, closeRing);
                segment.HalfWidth = EstimateSkirtPanelHalfWidth(
                    segmentsByColumn, columnIndex, rowIndex, closeRing);
                float configuredRadius = columns[columnIndex].Chain.Radii[segment.Bone];
                segment.HalfThickness = Mathf.Clamp(
                    configuredRadius * 0.55f,
                    SkirtMinimumPanelHalfThickness,
                    SkirtMaximumPanelHalfThickness);
                segment.Rotation = CalculatePanelRotation(segment.End - segment.Start, tangent);
            }
        }

        for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            SkirtColumn column = columns[columnIndex];
            List<SkirtSegment> segments = segmentsByColumn[columnIndex];
            Transform anchorBone = FindNearestExportedParentTransform(column.Chain.Root.parent, boneResult.BoneIndexes);
            int anchorBoneIndex = anchorBone != null ? boneResult.BoneIndexes[anchorBone] : 0;
            int parentRigidIndex = rigidBodies.Count;
            rigidBodies.Add(CreateSkirtAnchorBody(column, coordinateRoot, anchorBoneIndex));

            for (int rowIndex = 0; rowIndex < segments.Count; rowIndex++)
            {
                SkirtSegment segment = segments[rowIndex];
                float depthRatio = segments.Count > 1 ? rowIndex / (float)(segments.Count - 1) : 0f;
                int boneIndex = boneResult.BoneIndexes[segment.Bone];
                segment.RigidIndex = rigidBodies.Count;
                rigidBodies.Add(CreateSkirtPanelBody(segment, boneIndex, depthRatio));
                dynamicRigidIndexes[segment.Bone] = segment.RigidIndex;
                joints.Add(CreateSkirtVerticalJoint(segment, parentRigidIndex, depthRatio));
                parentRigidIndex = segment.RigidIndex;
            }
        }

        int commonRowCount = segmentsByColumn.Min(segments => segments.Count);
        int horizontalConnectionCount = closeRing ? columns.Count : columns.Count - 1;
        for (int rowIndex = 0; rowIndex < commonRowCount; rowIndex++)
        {
            for (int columnIndex = 0; columnIndex < horizontalConnectionCount; columnIndex++)
            {
                SkirtSegment a = segmentsByColumn[columnIndex][rowIndex];
                SkirtSegment b = segmentsByColumn[(columnIndex + 1) % columns.Count][rowIndex];
                joints.Add(CreateSkirtHorizontalJoint(a, b));
            }
        }

        AddSkirtLegColliders(
            context.SkirtController, columns, coordinateRoot, boneResult.BoneIndexes, rigidBodies);
    }

    private static List<Transform> GetOrderedLinearBones(Chain chain)
    {
        List<Transform> result = new List<Transform>();
        Transform current = chain.Root;
        while (current != null && chain.Bones.Contains(current))
        {
            result.Add(current);
            current = GetSingleChainChild(current, chain.Bones);
        }
        return result;
    }

    private static List<SkirtColumn> OrderSkirtColumns(IEnumerable<SkirtColumn> columns,
        SkirtController controller, Transform coordinateRoot)
    {
        List<SkirtColumn> result = columns.ToList();
        Vector3 center = controller.CenterBone != null
            ? coordinateRoot.InverseTransformPoint(controller.CenterBone.position)
            : result.Aggregate(Vector3.zero, (sum, column) =>
                sum + coordinateRoot.InverseTransformPoint(column.Chain.Root.position)) / result.Count;

        return result.OrderBy(column =>
        {
            Vector3 position = coordinateRoot.InverseTransformPoint(column.Chain.Root.position) - center;
            return Mathf.Atan2(position.z, position.x);
        }).ToList();
    }

    private static bool IsClosedSkirtRing(IList<SkirtColumn> columns, SkirtController controller,
        Transform coordinateRoot)
    {
        if (columns.Count < 3) return false;
        Vector3 center = controller.CenterBone != null
            ? coordinateRoot.InverseTransformPoint(controller.CenterBone.position)
            : columns.Aggregate(Vector3.zero, (sum, column) =>
                sum + coordinateRoot.InverseTransformPoint(column.Chain.Root.position)) / columns.Count;
        List<float> angles = columns.Select(column =>
        {
            Vector3 position = coordinateRoot.InverseTransformPoint(column.Chain.Root.position) - center;
            return Mathf.Atan2(position.z, position.x);
        }).OrderBy(angle => angle).ToList();

        float maximumGap = 0f;
        for (int i = 0; i < angles.Count; i++)
        {
            float next = i + 1 < angles.Count ? angles[i + 1] : angles[0] + Mathf.PI * 2f;
            maximumGap = Mathf.Max(maximumGap, next - angles[i]);
        }
        float averageGap = Mathf.PI * 2f / angles.Count;
        return maximumGap <= Mathf.Min(averageGap * 2.25f, 150f * Mathf.Deg2Rad);
    }

    private static Vector3 CalculateSkirtTangent(IList<List<SkirtSegment>> columns,
        int columnIndex, int rowIndex, bool closeRing)
    {
        SkirtSegment current = columns[columnIndex][rowIndex];
        SkirtSegment previous = GetNeighborSkirtSegment(columns, columnIndex - 1, rowIndex, closeRing);
        SkirtSegment next = GetNeighborSkirtSegment(columns, columnIndex + 1, rowIndex, closeRing);
        if (previous != null && next != null) return next.Center - previous.Center;
        if (next != null) return next.Center - current.Center;
        if (previous != null) return current.Center - previous.Center;
        return Vector3.right;
    }

    private static float EstimateSkirtPanelHalfWidth(IList<List<SkirtSegment>> columns,
        int columnIndex, int rowIndex, bool closeRing)
    {
        SkirtSegment current = columns[columnIndex][rowIndex];
        SkirtSegment previous = GetNeighborSkirtSegment(columns, columnIndex - 1, rowIndex, closeRing);
        SkirtSegment next = GetNeighborSkirtSegment(columns, columnIndex + 1, rowIndex, closeRing);
        float spacing = 0f;
        int sampleCount = 0;
        if (previous != null)
        {
            spacing += Vector3.Distance(current.Center, previous.Center);
            sampleCount++;
        }
        if (next != null)
        {
            spacing += Vector3.Distance(current.Center, next.Center);
            sampleCount++;
        }
        if (sampleCount == 0) return SkirtMinimumPanelHalfWidth;
        return Mathf.Clamp(spacing / sampleCount * 0.48f,
            SkirtMinimumPanelHalfWidth, SkirtMaximumPanelHalfWidth);
    }

    private static SkirtSegment GetNeighborSkirtSegment(IList<List<SkirtSegment>> columns,
        int columnIndex, int rowIndex, bool closeRing)
    {
        if (closeRing)
        {
            columnIndex %= columns.Count;
            if (columnIndex < 0) columnIndex += columns.Count;
        }
        else if (columnIndex < 0 || columnIndex >= columns.Count)
        {
            return null;
        }

        List<SkirtSegment> segments = columns[columnIndex];
        return rowIndex >= 0 && rowIndex < segments.Count ? segments[rowIndex] : null;
    }

    private static Vector3 CalculatePanelRotation(Vector3 segmentDirection, Vector3 tangentDirection)
    {
        Vector3 up = segmentDirection.normalized;
        Vector3 right = Vector3.ProjectOnPlane(tangentDirection, up);
        if (right.sqrMagnitude < MinimumSegmentLength * MinimumSegmentLength)
            right = Vector3.ProjectOnPlane(Vector3.right, up);
        if (right.sqrMagnitude < MinimumSegmentLength * MinimumSegmentLength)
            right = Vector3.ProjectOnPlane(Vector3.forward, up);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, up).normalized;
        right = Vector3.Cross(up, forward).normalized;

        // Unity 的 Quaternion.eulerAngles 与 MMD/Bullet 的 Z-Y-X 欧拉顺序不同。
        // 先转换完整旋转基，再提取 PMX 欧拉角，避免正面和背面裙片被横向旋转。
        Quaternion unityRotation = Quaternion.LookRotation(forward, up);
        Quaternion pmxRotation = new Quaternion(
            -unityRotation.x, unityRotation.y, -unityRotation.z, unityRotation.w);
        Vector3 pmxEuler = ExtractMmdEulerDegrees(pmxRotation);

        // PMXWriter 会对 X/Z 再做坐标转换，这里转换回其内存坐标约定。
        return new Vector3(
            NormalizeDegrees(-pmxEuler.x),
            NormalizeDegrees(pmxEuler.y),
            NormalizeDegrees(-pmxEuler.z));
    }

    private static Vector3 ExtractMmdEulerDegrees(Quaternion rotation)
    {
        Matrix4x4 matrix = Matrix4x4.Rotate(rotation);
        float sinY = Mathf.Clamp(-matrix.m20, -1f, 1f);
        float y = Mathf.Asin(sinY);
        float cosY = Mathf.Cos(y);
        float x;
        float z;

        // MMD/Bullet 按 Rz * Ry * Rx 解释 PMX 的刚体欧拉角。
        if (Mathf.Abs(cosY) > 0.00001f)
        {
            x = Mathf.Atan2(matrix.m21, matrix.m22);
            z = Mathf.Atan2(matrix.m10, matrix.m00);
        }
        else
        {
            // 万向节锁处固定 Z，保留可表示的 X/Y 合成旋转。
            x = Mathf.Atan2(-matrix.m12, matrix.m11);
            z = 0f;
        }

        return new Vector3(x, y, z) * Mathf.Rad2Deg;
    }

    private static MMDRigidBody CreateSkirtAnchorBody(SkirtColumn column,
        Transform coordinateRoot, int boneIndex)
    {
        float radius = Mathf.Max(MinimumRadius, column.Chain.Radii[column.Chain.Root] * 0.5f);
        return new MMDRigidBody
        {
            Name = column.Chain.Root.name + "_skirt_anchor",
            NameEn = column.Chain.Root.name + "_skirt_anchor",
            AssociatedBoneIndex = boneIndex,
            CollisionGroup = SkirtCollisionGroup,
            CollisionMask = (ushort)(1 << SkirtCollisionGroup),
            Shape = MMDRigidBody.RigidBodyShape.RigidShapeSphere,
            Dimemsions = new Vector3(radius, 0, 0),
            Position = coordinateRoot.InverseTransformPoint(column.Chain.Root.position),
            Rotation = Vector3.zero,
            Mass = 0,
            TranslateDamp = 1,
            RotateDamp = 1,
            Restitution = 0,
            Friction = 0,
            Type = MMDRigidBody.RigidBodyType.RigidTypeKinematic
        };
    }

    private static MMDRigidBody CreateSkirtPanelBody(SkirtSegment segment, int boneIndex,
        float depthRatio)
    {
        return new MMDRigidBody
        {
            Name = segment.Bone.name + "_skirt_physics",
            NameEn = segment.Bone.name + "_skirt_physics",
            AssociatedBoneIndex = boneIndex,
            CollisionGroup = SkirtCollisionGroup,
            CollisionMask = (ushort)(1 << SkirtCollisionGroup),
            Shape = MMDRigidBody.RigidBodyShape.RigidShapeBox,
            // PMX 盒体尺寸按半轴构造，段间留出小间隙避免初始姿势互相挤压。
            Dimemsions = new Vector3(
                segment.HalfWidth, segment.Length * 0.46f, segment.HalfThickness),
            Position = segment.Center,
            Rotation = segment.Rotation,
            Mass = Mathf.Lerp(SkirtPreset.RootMass, SkirtPreset.TipMass, depthRatio),
            TranslateDamp = Mathf.Lerp(
                SkirtPreset.RootTranslateDamp, SkirtPreset.TipTranslateDamp, depthRatio),
            RotateDamp = Mathf.Lerp(
                SkirtPreset.RootRotateDamp, SkirtPreset.TipRotateDamp, depthRatio),
            Restitution = 0,
            Friction = 0,
            Type = MMDRigidBody.RigidBodyType.RigidTypePhysics
        };
    }

    private static MMDJoint CreateSkirtVerticalJoint(SkirtSegment segment,
        int parentRigidIndex, float depthRatio)
    {
        float bend = SkirtVerticalBendDegrees * Mathf.Deg2Rad;
        float twist = SkirtVerticalTwistDegrees * Mathf.Deg2Rad;
        float bendSpring = Mathf.Lerp(26f, 14f, depthRatio);
        float twistSpring = Mathf.Lerp(10f, 6f, depthRatio);
        return new MMDJoint
        {
            Name = segment.Bone.name + "_skirt_vertical_joint",
            NameEn = segment.Bone.name + "_skirt_vertical_joint",
            AssociatedRigidBodyIndex = new[] { parentRigidIndex, segment.RigidIndex },
            Position = segment.Start,
            Rotation = segment.Rotation,
            PositionLowLimit = Vector3.zero,
            PositionHiLimit = Vector3.zero,
            RotationLowLimit = new Vector3(-bend, -twist, -bend * 0.75f),
            RotationHiLimit = new Vector3(bend, twist, bend * 0.75f),
            SpringTranslate = Vector3.zero,
            SpringRotate = new Vector3(bendSpring, twistSpring, bendSpring * 0.8f)
        };
    }

    private static MMDJoint CreateSkirtHorizontalJoint(SkirtSegment a, SkirtSegment b)
    {
        Vector3 tangent = b.Center - a.Center;
        Vector3 direction = (a.End - a.Start + b.End - b.Start) * 0.5f;
        float spacing = tangent.magnitude;
        float averageLength = (a.Length + b.Length) * 0.5f;
        Vector3 positionAllowance = new Vector3(
            Mathf.Clamp(spacing * 0.04f, 0.0005f, 0.006f),
            Mathf.Clamp(averageLength * 0.02f, 0.0005f, 0.004f),
            Mathf.Clamp((a.HalfThickness + b.HalfThickness) * 0.2f, 0.0005f, 0.003f));
        float bend = SkirtHorizontalBendDegrees * Mathf.Deg2Rad;
        float twist = SkirtHorizontalTwistDegrees * Mathf.Deg2Rad;
        return new MMDJoint
        {
            Name = a.Bone.name + "_to_" + b.Bone.name + "_skirt_horizontal_joint",
            NameEn = a.Bone.name + "_to_" + b.Bone.name + "_skirt_horizontal_joint",
            AssociatedRigidBodyIndex = new[] { a.RigidIndex, b.RigidIndex },
            Position = (a.Center + b.Center) * 0.5f,
            Rotation = CalculatePanelRotation(direction, tangent),
            PositionLowLimit = -positionAllowance,
            PositionHiLimit = positionAllowance,
            RotationLowLimit = new Vector3(-bend, -twist, -bend),
            RotationHiLimit = new Vector3(bend, twist, bend),
            SpringTranslate = new Vector3(24f, 18f, 24f),
            SpringRotate = new Vector3(16f, 8f, 16f)
        };
    }

    private static void AddSkirtLegColliders(SkirtController controller,
        IEnumerable<SkirtColumn> columns, Transform coordinateRoot,
        Dictionary<Transform, int> boneIndexes, List<MMDRigidBody> rigidBodies)
    {
        bool checkLeft = columns.Any(column => column.IsCheckLeftLeg);
        bool checkRight = columns.Any(column => column.IsCheckRightLeg);
        // CySpring 的腿围参数偏向排斥计算，直接作为 PMX 胶囊半径会明显粗一圈。
        float kneeRadius = SanitizeSkirtColliderRadius(
            controller.KneeColliderRadius * SkirtLegColliderRadiusScale, 0.044f);
        float ankleRadius = SanitizeSkirtColliderRadius(
            controller.AnkleColliderRadius * SkirtLegColliderRadiusScale, 0.036f);
        if (checkLeft) AddLegColliderChain(
            "left", controller.KneeLBone, controller.AnkleLBone, kneeRadius, ankleRadius,
            coordinateRoot, boneIndexes, rigidBodies);
        if (checkRight) AddLegColliderChain(
            "right", controller.KneeRBone, controller.AnkleRBone, kneeRadius, ankleRadius,
            coordinateRoot, boneIndexes, rigidBodies);
    }

    private static void AddLegColliderChain(string side, Transform knee, Transform ankle,
        float kneeRadius, float ankleRadius, Transform coordinateRoot,
        Dictionary<Transform, int> boneIndexes, List<MMDRigidBody> rigidBodies)
    {
        if (knee == null || !boneIndexes.ContainsKey(knee)) return;
        Transform thigh = FindNearestExportedParentTransform(knee.parent, boneIndexes);
        if (thigh != null)
            AddKinematicCapsule(side + "_thigh_skirt_collider", thigh, knee, kneeRadius,
                coordinateRoot, boneIndexes, rigidBodies);
        if (ankle != null && boneIndexes.ContainsKey(ankle))
            AddKinematicCapsule(side + "_shin_skirt_collider", knee, ankle,
                (kneeRadius + ankleRadius) * 0.5f, coordinateRoot, boneIndexes, rigidBodies);
    }

    private static void AddKinematicCapsule(string name, Transform startBone, Transform endBone,
        float radius, Transform coordinateRoot, Dictionary<Transform, int> boneIndexes,
        List<MMDRigidBody> rigidBodies)
    {
        if (startBone == null || endBone == null || !boneIndexes.TryGetValue(startBone, out int boneIndex)) return;
        Vector3 start = coordinateRoot.InverseTransformPoint(startBone.position);
        Vector3 end = coordinateRoot.InverseTransformPoint(endBone.position);
        float length = Vector3.Distance(start, end);
        if (length <= MinimumSegmentLength) return;
        rigidBodies.Add(new MMDRigidBody
        {
            Name = name,
            NameEn = name,
            AssociatedBoneIndex = boneIndex,
            CollisionGroup = BodyCollisionGroup,
            CollisionMask = (ushort)(1 << BodyCollisionGroup),
            Shape = MMDRigidBody.RigidBodyShape.RigidShapeCapsule,
            Dimemsions = new Vector3(radius, length, 0),
            Position = (start + end) * 0.5f,
            Rotation = CalculateCapsuleRotation(end - start),
            Mass = 0,
            TranslateDamp = 1,
            RotateDamp = 1,
            Restitution = 0,
            Friction = 0,
            Type = MMDRigidBody.RigidBodyType.RigidTypeKinematic
        });
    }

    private static float SanitizeSkirtColliderRadius(float radius, float fallback)
    {
        return IsFinite(radius) && radius > 0f ? Mathf.Clamp(radius, 0.02f, 0.09f) : fallback;
    }

    private static MMDRigidBody CreateAnchorBody(Chain chain, Transform coordinateRoot, int boneIndex)
    {
        float radius = Mathf.Max(MinimumRadius, chain.Radii[chain.Root] * 0.75f);
        return new MMDRigidBody
        {
            Name = chain.Root.name + "_anchor",
            NameEn = chain.Root.name + "_anchor",
            AssociatedBoneIndex = boneIndex,
            CollisionGroup = DynamicCollisionGroup,
            CollisionMask = (ushort)(1 << DynamicCollisionGroup),
            Shape = MMDRigidBody.RigidBodyShape.RigidShapeSphere,
            Dimemsions = new Vector3(radius, 0, 0),
            Position = coordinateRoot.InverseTransformPoint(chain.Root.position),
            Rotation = Vector3.zero,
            Mass = 0,
            TranslateDamp = 1,
            RotateDamp = 1,
            Restitution = 0,
            Friction = 0,
            Type = MMDRigidBody.RigidBodyType.RigidTypeKinematic
        };
    }

    private static MMDRigidBody CreateDynamicBody(Transform bone, Transform child, float configuredRadius,
        float depthRatio, Transform coordinateRoot, int boneIndex, PhysicsPreset preset)
    {
        Vector3 start = coordinateRoot.InverseTransformPoint(bone.position);
        Vector3 end = child != null ? coordinateRoot.InverseTransformPoint(child.position) : start;
        float length = Vector3.Distance(start, end);
        float radius = Mathf.Clamp(configuredRadius, MinimumRadius, MaximumRadius);
        if (length > MinimumSegmentLength)
            radius = Mathf.Min(radius, Mathf.Max(MinimumRadius, length * 0.28f));

        bool useCapsule = length >= Mathf.Max(MinimumSegmentLength, radius * CapsuleThreshold);
        Vector3 position = useCapsule ? (start + end) * 0.5f : start;
        Vector3 rotation = useCapsule ? CalculateCapsuleRotation(end - start) : Vector3.zero;

        return new MMDRigidBody
        {
            Name = bone.name + "_physics",
            NameEn = bone.name + "_physics",
            AssociatedBoneIndex = boneIndex,
            CollisionGroup = DynamicCollisionGroup,
            CollisionMask = (ushort)(1 << DynamicCollisionGroup),
            Shape = useCapsule
                ? MMDRigidBody.RigidBodyShape.RigidShapeCapsule
                : MMDRigidBody.RigidBodyShape.RigidShapeSphere,
            // PMX 胶囊尺寸使用 X=半径、Y=轴向长度；球体只读取 X。
            Dimemsions = useCapsule ? new Vector3(radius, length, 0) : new Vector3(radius, 0, 0),
            Position = position,
            Rotation = rotation,
            Mass = Mathf.Lerp(preset.RootMass, preset.TipMass, depthRatio),
            TranslateDamp = Mathf.Lerp(preset.RootTranslateDamp, preset.TipTranslateDamp, depthRatio),
            RotateDamp = Mathf.Lerp(preset.RootRotateDamp, preset.TipRotateDamp, depthRatio),
            Restitution = 0,
            Friction = 0,
            Type = MMDRigidBody.RigidBodyType.RigidTypePhysics
        };
    }

    private static MMDRigidBody CreateTailBodyCollider(Chain chain, Transform coordinateRoot,
        Transform anchorBone, int anchorBoneIndex)
    {
        if (anchorBone == null) return null;

        Vector3 rootPosition = coordinateRoot.InverseTransformPoint(chain.Root.position);
        Vector3 anchorPosition = coordinateRoot.InverseTransformPoint(anchorBone.position);
        float rootDistance = Vector3.Distance(rootPosition, anchorPosition);
        float tailRadius = chain.Radii[chain.Root];
        if (rootDistance <= MinimumSegmentLength) return null;

        // 球面停在尾根内侧并保留约一个半尾巴半径的间隙，避免初始帧互相穿插。
        float radius = Mathf.Clamp(
            rootDistance - tailRadius * TailColliderClearanceMultiplier,
            tailRadius * TailColliderMinimumRadiusMultiplier,
            TailColliderMaximumRadius);

        return new MMDRigidBody
        {
            Name = chain.Root.name + "_body_blocker",
            NameEn = chain.Root.name + "_body_blocker",
            AssociatedBoneIndex = anchorBoneIndex,
            CollisionGroup = BodyCollisionGroup,
            CollisionMask = (ushort)(1 << BodyCollisionGroup),
            Shape = MMDRigidBody.RigidBodyShape.RigidShapeSphere,
            Dimemsions = new Vector3(radius, 0, 0),
            Position = anchorPosition,
            Rotation = Vector3.zero,
            Mass = 0,
            TranslateDamp = 1,
            RotateDamp = 1,
            Restitution = 0,
            Friction = 0.5f,
            Type = MMDRigidBody.RigidBodyType.RigidTypeKinematic
        };
    }

    private static MMDJoint CreateJoint(Transform bone, Transform coordinateRoot,
        int parentRigidIndex, int rigidIndex, float bendLimitDegrees,
        float twistLimitDegrees, float bendSpring, float twistSpring, bool offsetTailRoot)
    {
        float bend = bendLimitDegrees * Mathf.Deg2Rad;
        float twist = twistLimitDegrees * Mathf.Deg2Rad;
        Vector3 rotationLowLimit = new Vector3(-bend, -twist, -bend);
        Vector3 rotationHiLimit = new Vector3(bend, twist, bend);
        if (offsetTailRoot)
        {
            // PMX 角限制按文件原始轴写入；仅让尾根相对模型 X 轴静态偏转 +15 度。
            rotationLowLimit.x = TailRootXRestOffsetDegrees * Mathf.Deg2Rad;
        }

        return new MMDJoint
        {
            Name = bone.name + "_joint",
            NameEn = bone.name + "_joint",
            AssociatedRigidBodyIndex = new[] { parentRigidIndex, rigidIndex },
            Position = coordinateRoot.InverseTransformPoint(bone.position),
            Rotation = Vector3.zero,
            PositionLowLimit = Vector3.zero,
            PositionHiLimit = Vector3.zero,
            RotationLowLimit = rotationLowLimit,
            RotationHiLimit = rotationHiLimit,
            SpringTranslate = Vector3.zero,
            // PMX 旋转弹簧以初始刚体关系为零点，根部较强、末端逐节减弱。
            SpringRotate = new Vector3(bendSpring, twistSpring, bendSpring)
        };
    }

    private static bool IsEarBoneName(string boneName)
    {
        if (string.IsNullOrEmpty(boneName)) return false;
        string name = boneName.ToLowerInvariant();
        return name.Contains("ear") || name.Contains("mimi") || name.Contains("耳");
    }

    private static bool IsTailBoneName(string boneName)
    {
        if (string.IsNullOrEmpty(boneName)) return false;
        string name = boneName.ToLowerInvariant();
        return name.Contains("tail") || name.Contains("shippo") || name.Contains("尻尾") || name.Contains("尾");
    }

    private static Vector3 CalculateCapsuleRotation(Vector3 direction)
    {
        if (direction.sqrMagnitude < MinimumSegmentLength * MinimumSegmentLength) return Vector3.zero;
        Vector3 euler = Quaternion.FromToRotation(Vector3.up, direction.normalized).eulerAngles;
        return new Vector3(NormalizeDegrees(euler.x), NormalizeDegrees(euler.y), NormalizeDegrees(euler.z));
    }

    private static float NormalizeDegrees(float value)
    {
        return value > 180f ? value - 360f : value;
    }


    private static Transform GetSingleChainChild(Transform bone, HashSet<Transform> chainBones)
    {
        Transform result = null;
        for (int i = 0; i < bone.childCount; i++)
        {
            Transform child = bone.GetChild(i);
            if (!chainBones.Contains(child)) continue;
            if (result != null) return null; // 分叉节点使用球体，避免胶囊偏向任意一个分支。
            result = child;
        }
        return result;
    }

    private static int FindParentRigidIndex(Transform parent, HashSet<Transform> chainBones,
        Dictionary<Transform, int> indexes, int fallback)
    {
        Transform current = parent;
        while (current != null && chainBones.Contains(current))
        {
            if (indexes.TryGetValue(current, out int index)) return index;
            current = current.parent;
        }
        return fallback;
    }

    private static Transform FindNearestExportedParentTransform(Transform parent,
        Dictionary<Transform, int> boneIndexes)
    {
        for (Transform current = parent; current != null; current = current.parent)
            if (boneIndexes.ContainsKey(current)) return current;
        return null;
    }

    private static int GetDepth(Transform transform)
    {
        int depth = 0;
        for (Transform current = transform; current != null; current = current.parent) depth++;
        return depth;
    }

    private static float SanitizeRadius(float radius)
    {
        return IsFinite(radius) && radius > 0 ? Mathf.Clamp(radius, MinimumRadius, MaximumRadius) : DefaultRadius;
    }

    private static bool IsLinearPhysicsElement(CySpringDataContainer container, CySpringParamDataElement element)
    {
        if (container == null || element == null) return false;

        string boneText = (element._boneName ?? string.Empty).ToLowerInvariant();
        if (element._childElements != null)
        {
            foreach (CySpringParamDataChildElement child in element._childElements)
                if (child != null) boneText += " " + (child._boneName ?? string.Empty).ToLowerInvariant();
        }

        if (ContainsAny(boneText, "skirt", "cloth", "dress", "bust", "breast", "mune")) return false;
        if (ContainsAny(boneText, "head", "hair", "ear", "tail")) return true;

        // 骨名不含部位信息时才使用容器路径兜底，避免混合容器中的裙摆污染头发判断。
        string path = GetTransformPath(container.transform).ToLowerInvariant();
        if (ContainsAny(path, "skirt", "cloth", "dress", "bust", "breast", "mune")) return false;
        return ContainsAny(path, "head", "hair", "ear", "tail");
    }

    private static string GetTransformPath(Transform transform)
    {
        string path = string.Empty;
        for (Transform current = transform; current != null; current = current.parent)
            path = current.name + "/" + path;
        return path;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void MarkPostPhysicsBones(IEnumerable<Transform> transforms, PMXBoneExporter.Result boneResult)
    {
        foreach (Transform transform in transforms)
        {
            if (!boneResult.BoneIndexes.TryGetValue(transform, out int index)) continue;
            boneResult.Bones[index].PostPhysics = true;
            boneResult.Bones[index].TransformLevel = System.Math.Max(1, boneResult.Bones[index].TransformLevel);
        }
    }

    private static void Validate(List<MMDRigidBody> rigidBodies, List<MMDJoint> joints, int boneCount)
    {
        for (int i = 0; i < rigidBodies.Count; i++)
        {
            MMDRigidBody body = rigidBodies[i];
            if (body.AssociatedBoneIndex < 0 || body.AssociatedBoneIndex >= boneCount)
                throw new InvalidOperationException("PMX 刚体骨骼索引无效: " + body.Name);
            if (body.CollisionGroup < 0 || body.CollisionGroup > 15)
                throw new InvalidOperationException("PMX 刚体碰撞组无效: " + body.Name);
            if (body.Type != MMDRigidBody.RigidBodyType.RigidTypeKinematic &&
                body.Type != MMDRigidBody.RigidBodyType.RigidTypePhysics)
                throw new InvalidOperationException("PMX 导出不允许 Type 2/3 刚体: " + body.Name);
            if (!IsFinite(body.Position) || !IsFinite(body.Rotation) || !IsFinite(body.Dimemsions))
                throw new InvalidOperationException("PMX 刚体包含 NaN 或 Infinity: " + body.Name);
            if (body.Dimemsions.x <= 0f ||
                (body.Shape != MMDRigidBody.RigidBodyShape.RigidShapeSphere && body.Dimemsions.y <= 0f) ||
                (body.Shape == MMDRigidBody.RigidBodyShape.RigidShapeBox && body.Dimemsions.z <= 0f))
                throw new InvalidOperationException("PMX 刚体尺寸无效: " + body.Name);
        }

        foreach (MMDJoint joint in joints)
        {
            int a = joint.AssociatedRigidBodyIndex[0];
            int b = joint.AssociatedRigidBodyIndex[1];
            if (a < 0 || a >= rigidBodies.Count || b < 0 || b >= rigidBodies.Count)
                throw new InvalidOperationException("PMX Joint 刚体索引无效: " + joint.Name);
            if (rigidBodies[a].Type == MMDRigidBody.RigidBodyType.RigidTypeKinematic &&
                rigidBodies[b].Type == MMDRigidBody.RigidBodyType.RigidTypeKinematic)
                throw new InvalidOperationException("PMX Joint 不能连接两个 Type 0 刚体: " + joint.Name);
            if (!IsFinite(joint.Position) || !IsFinite(joint.Rotation) ||
                !IsFinite(joint.PositionLowLimit) || !IsFinite(joint.PositionHiLimit) ||
                !IsFinite(joint.RotationLowLimit) || !IsFinite(joint.RotationHiLimit) ||
                !IsFinite(joint.SpringTranslate) || !IsFinite(joint.SpringRotate))
                throw new InvalidOperationException("PMX Joint 包含 NaN 或 Infinity: " + joint.Name);
            if (joint.PositionLowLimit.x > joint.PositionHiLimit.x ||
                joint.PositionLowLimit.y > joint.PositionHiLimit.y ||
                joint.PositionLowLimit.z > joint.PositionHiLimit.z)
                throw new InvalidOperationException("PMX Joint 平移上下限倒置: " + joint.Name);
            if (joint.RotationLowLimit.x > joint.RotationHiLimit.x ||
                joint.RotationLowLimit.y > joint.RotationHiLimit.y ||
                joint.RotationLowLimit.z > joint.RotationHiLimit.z)
                throw new InvalidOperationException("PMX Joint 旋转上下限倒置: " + joint.Name);
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
