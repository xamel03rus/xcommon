using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using Xamel.Common.Abstracts;
using Xamel.Runtime.Models;

namespace Xamel.Common.Core
{
    /// <summary>
    /// Animation mixer for humanoid rigs. Uses <see cref="HumanBodyBones"/> and AvatarMask body parts; bones are collected by tag. Supports overlay and once layers with per-bone weight overrides.
    /// </summary>
    public class HumanoidAnimationMixer : AbstractAnimationMixer
    {
        private List<Transform> _collectedBones = new();
        private Dictionary<Transform, int> _boneToIndex = new();
        private Dictionary<Transform, HumanBodyBones> _humanBoneMapping = new();
        private Dictionary<HumanBodyBones, float> _weightByBone;

        [SerializeField] private HumanoidTransformWeight[] weightRewrites;

        private static AvatarMaskBodyPart GetBodyPartForHumanBone(HumanBodyBones bone)
        {
            switch (bone)
            {
                case HumanBodyBones.Hips:
                    return AvatarMaskBodyPart.Root;
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                case HumanBodyBones.Neck:
                    return AvatarMaskBodyPart.Body;
                
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.LeftThumbProximal:
                case HumanBodyBones.LeftThumbIntermediate:
                case HumanBodyBones.LeftThumbDistal:
                case HumanBodyBones.LeftIndexProximal:
                case HumanBodyBones.LeftIndexIntermediate:
                case HumanBodyBones.LeftIndexDistal:
                case HumanBodyBones.LeftMiddleProximal:
                case HumanBodyBones.LeftMiddleIntermediate:
                case HumanBodyBones.LeftMiddleDistal:
                case HumanBodyBones.LeftRingProximal:
                case HumanBodyBones.LeftRingIntermediate:
                case HumanBodyBones.LeftRingDistal:
                case HumanBodyBones.LeftLittleProximal:
                case HumanBodyBones.LeftLittleIntermediate:
                case HumanBodyBones.LeftLittleDistal:
                    return AvatarMaskBodyPart.LeftArm;
                
                case HumanBodyBones.RightShoulder:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.RightLowerArm:
                case HumanBodyBones.RightHand:
                case HumanBodyBones.RightThumbProximal:
                case HumanBodyBones.RightThumbIntermediate:
                case HumanBodyBones.RightThumbDistal:
                case HumanBodyBones.RightIndexProximal:
                case HumanBodyBones.RightIndexIntermediate:
                case HumanBodyBones.RightIndexDistal:
                case HumanBodyBones.RightMiddleProximal:
                case HumanBodyBones.RightMiddleIntermediate:
                case HumanBodyBones.RightMiddleDistal:
                case HumanBodyBones.RightRingProximal:
                case HumanBodyBones.RightRingIntermediate:
                case HumanBodyBones.RightRingDistal:
                case HumanBodyBones.RightLittleProximal:
                case HumanBodyBones.RightLittleIntermediate:
                case HumanBodyBones.RightLittleDistal:
                    return AvatarMaskBodyPart.RightArm;
                
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.LeftToes:
                    return AvatarMaskBodyPart.LeftLeg;
                
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.RightLowerLeg:
                case HumanBodyBones.RightFoot:
                case HumanBodyBones.RightToes:
                    return AvatarMaskBodyPart.RightLeg;

                case HumanBodyBones.Head:
                case HumanBodyBones.Jaw:
                case HumanBodyBones.LeftEye:
                case HumanBodyBones.RightEye:
                    return AvatarMaskBodyPart.Head;

                default:
                    return AvatarMaskBodyPart.Body;
            }
        }

        protected override void LoadAvatar()
        {
            _weightByBone = new Dictionary<HumanBodyBones, float>();
            if (weightRewrites != null)
            {
                foreach (var w in weightRewrites)
                    _weightByBone[w.bone] = w.weight;
            }

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var boneTransform = Animator.GetBoneTransform(bone);
                if (boneTransform == null) continue;
                _humanBoneMapping.Add(boneTransform, bone);
            }

            CollectBones();
            for (var i = 0; i < _collectedBones.Count; i++)
                _boneToIndex[_collectedBones[i]] = i;

            var numTransforms = _collectedBones.Count;

            Handles = new NativeArray<TransformStreamHandle>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HandleInputIndex = new NativeArray<int>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HandleWeights = new NativeArray<float>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            OnceHandleInputIndex = new NativeArray<int>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            OnceHandleWeights = new NativeArray<float>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            for (var i = 0; i < numTransforms; i++)
            {
                Handles[i] = Animator.BindStreamTransform(_collectedBones[i]);
                HandleInputIndex[i] = 0;
                OnceHandleInputIndex[i] = 0;
                HandleWeights[i] = 1f;
                OnceHandleWeights[i] = 1f;
            }
        }

        private float GetWeightForBone(HumanBodyBones bone)
        {
            return _weightByBone != null && _weightByBone.TryGetValue(bone, out var w) ? w : 1f;
        }

        /// <summary>Set HandleInputIndex for bones in mask; then propagate to children (child gets same input as first masked ancestor).</summary>
        protected override void ReloadAvatar(IReadOnlyList<AvatarMask> overlayMasks)
        {
            var n = _collectedBones.Count;
            for (var i = 0; i < n; i++)
                HandleInputIndex[i] = 0;

            if (overlayMasks == null) return;
            for (var l = 0; l < overlayMasks.Count; l++)
            {
                var mask = overlayMasks[l];
                if (mask == null) continue;
                for (var i = 0; i < n; i++)
                {
                    if (!_humanBoneMapping.TryGetValue(_collectedBones[i], out var humanBone))
                        continue;
                    var bodyPart = GetBodyPartForHumanBone(humanBone);
                    if (!mask.GetHumanoidBodyPartActive(bodyPart))
                        continue;
                    HandleInputIndex[i] = l + 1;
                    HandleWeights[i] = GetWeightForBone(humanBone);
                }
            }
            PropagateInputToChildren(HandleInputIndex, HandleWeights);
        }

        protected override void ReloadOnceAvatar(IReadOnlyList<AvatarMask> onceMasks)
        {
            var n = _collectedBones.Count;
            for (var i = 0; i < n; i++)
                OnceHandleInputIndex[i] = 0;

            if (onceMasks == null) return;
            for (var l = 0; l < onceMasks.Count; l++)
            {
                var mask = onceMasks[l];
                if (mask == null) continue;
                for (var i = 0; i < n; i++)
                {
                    if (!_humanBoneMapping.TryGetValue(_collectedBones[i], out var humanBone))
                        continue;
                    var bodyPart = GetBodyPartForHumanBone(humanBone);
                    if (!mask.GetHumanoidBodyPartActive(bodyPart))
                        continue;
                    OnceHandleInputIndex[i] = l + 1;
                    OnceHandleWeights[i] = GetWeightForBone(humanBone);
                }
            }
            PropagateInputToChildren(OnceHandleInputIndex, OnceHandleWeights);
        }

        private void PropagateInputToChildren(NativeArray<int> inputIndex, NativeArray<float> weights)
        {
            var n = _collectedBones.Count;
            for (var i = 0; i < n; i++)
            {
                if (inputIndex[i] != 0) continue;
                var ancestorInput = GetFirstAncestorInput(_collectedBones[i], inputIndex);
                if (ancestorInput != 0)
                {
                    inputIndex[i] = ancestorInput;
                    weights[i] = 1f;
                }
            }
        }

        private int GetFirstAncestorInput(Transform t, NativeArray<int> inputIndex)
        {
            for (var p = t.parent; p != null; p = p.parent)
            {
                if (_boneToIndex.TryGetValue(p, out var idx) && inputIndex[idx] != 0)
                    return inputIndex[idx];
            }
            return 0;
        }

        private void CollectBones()
        {
            foreach (var t in Animator.transform.GetComponentsInChildren<Transform>())
            {
                if (Animator.transform.Equals(t))
                {
                    continue;
                }
                
                if (t.gameObject.CompareTag(BoneTag))
                {
                    _collectedBones.Add(t);
                }
            }
        }
    }
}