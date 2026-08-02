using Gallop;
using LibMMD.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 将头发、耳朵和尾巴的 CySpring 骨骼链转换为 PMX 2.0 原生刚体与 Joint。
/// </summary>
internal static class PMXPhysicsExporter
{
    // 首版使用独立组并关闭组内碰撞，避免没有身体碰撞体时发束互相挤压。
    private const int DynamicCollisionGroup = 3;
    private const int BodyCollisionGroup = 2;
    private const float DefaultRadius = 0.018f;
    private const float MinimumRadius = 0.006f;
    private const float MaximumRadius = 0.045f;
    private const float MinimumSegmentLength = 0.004f;
    private const float CapsuleThreshold = 2.5f;
    // 尾巴主要依靠弹簧回到导出时的静止姿态，宽角度范围仅用于防止关节翻转。
    private const float TailFreeBendDegrees = 75f;
    private const float TailFreeTwistDegrees = 30f;
    private const float TailRootBendSpring = 12f;
    private const float TailTipBendSpring = 4f;
    private const float TailRootTwistSpring = 4f;
    private const float TailTipTwistSpring = 1.5f;
    private const float TailColliderClearanceMultiplier = 1.5f;
    private const float TailColliderMinimumRadiusMultiplier = 1.25f;
    private const float TailColliderMaximumRadius = 0.12f;

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
        internal readonly HashSet<Transform> DynamicBones = new HashSet<Transform>();
    }

    internal sealed class Chain
    {
        internal Transform Root;
        internal bool IsEar;
        internal bool IsTail;
        internal readonly HashSet<Transform> Bones = new HashSet<Transform>();
        internal readonly Dictionary<Transform, float> Radii = new Dictionary<Transform, float>();
    }

    internal static Context Prepare(UmaContainerCharacter character, Transform skeletonRoot)
    {
        Context context = new Context();
        if (character == null || skeletonRoot == null || character.cySpringDataContainers == null)
            return context;

        Transform[] hierarchy = skeletonRoot.GetComponentsInChildren<Transform>(true);
        HashSet<Transform> claimedRoots = new HashSet<Transform>();

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
                jointBendLimit, jointTwistLimit, bendSpring, twistSpring));
        }
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
        float twistLimitDegrees, float bendSpring, float twistSpring)
    {
        float bend = bendLimitDegrees * Mathf.Deg2Rad;
        float twist = twistLimitDegrees * Mathf.Deg2Rad;
        return new MMDJoint
        {
            Name = bone.name + "_joint",
            NameEn = bone.name + "_joint",
            AssociatedRigidBodyIndex = new[] { parentRigidIndex, rigidIndex },
            Position = coordinateRoot.InverseTransformPoint(bone.position),
            Rotation = Vector3.zero,
            PositionLowLimit = Vector3.zero,
            PositionHiLimit = Vector3.zero,
            RotationLowLimit = new Vector3(-bend, -twist, -bend),
            RotationHiLimit = new Vector3(bend, twist, bend),
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
                throw new InvalidOperationException("第一阶段不允许导出 Type 2/3 刚体: " + body.Name);
            if (!IsFinite(body.Position) || !IsFinite(body.Rotation) || !IsFinite(body.Dimemsions))
                throw new InvalidOperationException("PMX 刚体包含 NaN 或 Infinity: " + body.Name);
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
            if (!IsFinite(joint.Position) || !IsFinite(joint.RotationLowLimit) ||
                !IsFinite(joint.RotationHiLimit) || !IsFinite(joint.SpringRotate))
                throw new InvalidOperationException("PMX Joint 包含 NaN 或 Infinity: " + joint.Name);
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
