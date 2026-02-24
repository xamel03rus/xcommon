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
        private Dictionary<Transform, HumanBodyBones> _humanBoneMapping = new();
        private Dictionary<HumanBodyBones, float> _weightByBone;
        private HashSet<Transform> _maskedTransformsForMain = new();
        private HashSet<Transform> _maskedTransformsForOnce = new();

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
            var numTransforms = _collectedBones.Count;

            Handles = new NativeArray<TransformStreamHandle>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HandleMask = new NativeArray<bool>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HandleWeights = new NativeArray<float>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            OnceHandleMask = new NativeArray<bool>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            OnceHandleWeights = new NativeArray<float>(numTransforms, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            for (var i = 0; i < numTransforms; i++)
            {
                Handles[i] = Animator.BindStreamTransform(_collectedBones[i]);
                HandleMask[i] = false;
                OnceHandleMask[i] = false;

                if (!_humanBoneMapping.TryGetValue(_collectedBones[i], out var humanBone))
                    continue;

                var bodyPart = GetBodyPartForHumanBone(humanBone);
                var weightVal = GetWeightForBone(humanBone);

                if (ActiveAvatarMask.GetHumanoidBodyPartActive(bodyPart))
                {
                    HandleMask[i] = true;
                    HandleWeights[i] = weightVal;
                }

                if (ActiveAvatarOnceMask.GetHumanoidBodyPartActive(bodyPart))
                {
                    OnceHandleMask[i] = true;
                    OnceHandleWeights[i] = weightVal;
                }
            }

            BuildMaskedSet(HandleMask, _maskedTransformsForMain);
            BuildMaskedSet(OnceHandleMask, _maskedTransformsForOnce);

            for (var i = 0; i < numTransforms; i++)
            {
                if (!HandleMask[i] && IsChildOfAny(_collectedBones[i], _maskedTransformsForMain))
                {
                    HandleMask[i] = true;
                    HandleWeights[i] = 1f;
                }
                if (!OnceHandleMask[i] && IsChildOfAny(_collectedBones[i], _maskedTransformsForOnce))
                {
                    OnceHandleMask[i] = true;
                    OnceHandleWeights[i] = 1f;
                }
            }
        }

        private float GetWeightForBone(HumanBodyBones bone)
        {
            return _weightByBone != null && _weightByBone.TryGetValue(bone, out var w) ? w : 1f;
        }

        private void BuildMaskedSet(NativeArray<bool> mask, HashSet<Transform> set)
        {
            set.Clear();
            for (var i = 0; i < mask.Length; i++)
            {
                if (mask[i])
                    set.Add(_collectedBones[i]);
            }
        }

        private static bool IsChildOfAny(Transform t, HashSet<Transform> maskedSet)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (maskedSet.Contains(p))
                    return true;
            }
            return false;
        }

        protected override void ReloadAvatar()
        {
            var numTransforms = _collectedBones.Count;
            for (var i = 0; i < numTransforms; i++)
            {
                HandleMask[i] = false;
                if (!_humanBoneMapping.TryGetValue(_collectedBones[i], out _))
                    continue;
                var bodyPart = GetBodyPartForHumanBone(_humanBoneMapping[_collectedBones[i]]);
                if (!ActiveAvatarMask.GetHumanoidBodyPartActive(bodyPart))
                    continue;
                HandleMask[i] = true;
            }
            BuildMaskedSet(HandleMask, _maskedTransformsForMain);
            for (var i = 0; i < numTransforms; i++)
            {
                if (!HandleMask[i] && IsChildOfAny(_collectedBones[i], _maskedTransformsForMain))
                    HandleMask[i] = true;
            }
        }

        protected override void ReloadOnceAvatar()
        {
            var numTransforms = _collectedBones.Count;
            for (var i = 0; i < numTransforms; i++)
            {
                OnceHandleMask[i] = false;
                if (!_humanBoneMapping.TryGetValue(_collectedBones[i], out _))
                    continue;
                var bodyPart = GetBodyPartForHumanBone(_humanBoneMapping[_collectedBones[i]]);
                if (!ActiveAvatarOnceMask.GetHumanoidBodyPartActive(bodyPart))
                    continue;
                OnceHandleMask[i] = true;
            }
            BuildMaskedSet(OnceHandleMask, _maskedTransformsForOnce);
            for (var i = 0; i < numTransforms; i++)
            {
                if (!OnceHandleMask[i] && IsChildOfAny(_collectedBones[i], _maskedTransformsForOnce))
                    OnceHandleMask[i] = true;
            }
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