using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using Xamel.Common.Abstracts;

namespace Xamel.Common.Core
{
    /// <summary>
    /// Animation mixer that selects bones by tag (<see cref="AbstractAnimationMixer.BoneTag"/>). Supports overlay and once layers with AvatarMask and per-bone weight overrides.
    /// </summary>
    public class AnimationMixer : AbstractAnimationMixer
    {
        /// <summary>Per-bone weight override (0–1). Bones not listed use weight 1.</summary>
        [Serializable]
        public class BoneTransformWeight
        {
            public Transform bone;
            [Range(0.0f, 1.0f)] public float weight;
        }

        [SerializeField] private BoneTransformWeight[] weightRewrites;

        private List<Transform> _transforms;
        private Dictionary<Transform, int> _transformToIndex;
        private Dictionary<Transform, float> _weightByTransform;

        protected override void LoadAvatar()
        {
            _transforms = CollectBones(Animator.transform, BoneTag);
            var allTransforms = Animator.transform.GetComponentsInChildren<Transform>();
            _transformToIndex = new Dictionary<Transform, int>();
            for (var i = 0; i < allTransforms.Length; i++)
                _transformToIndex[allTransforms[i]] = i;

            _weightByTransform = new Dictionary<Transform, float>();
            if (weightRewrites != null)
            {
                foreach (var w in weightRewrites)
                {
                    if (w.bone != null)
                        _weightByTransform[w.bone] = w.weight;
                }
            }

            var n = _transforms.Count;
            Handles = new NativeArray<TransformStreamHandle>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HandleMask = new NativeArray<bool>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HandleWeights = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            OnceHandleMask = new NativeArray<bool>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            OnceHandleWeights = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            for (var i = 0; i < n; i++)
            {
                var t = _transforms[i];
                Handles[i] = Animator.BindStreamTransform(t);
                HandleMask[i] = false;
                OnceHandleMask[i] = false;

                if (!_transformToIndex.TryGetValue(t, out var idx))
                    continue;
                if (!ActiveAvatarMask.GetTransformActive(idx))
                    continue;

                HandleMask[i] = true;
                HandleWeights[i] = GetWeightForTransform(t);

                if (!ActiveAvatarOnceMask.GetTransformActive(idx))
                    continue;
                OnceHandleMask[i] = true;
                OnceHandleWeights[i] = GetWeightForTransform(t);
            }
        }

        protected override void ReloadAvatar()
        {
            var n = _transforms.Count;
            for (var i = 0; i < n; i++)
            {
                HandleMask[i] = false;
                if (!_transformToIndex.TryGetValue(_transforms[i], out var idx))
                    continue;
                if (!ActiveAvatarMask.GetTransformActive(idx))
                    continue;
                HandleMask[i] = true;
            }
        }

        protected override void ReloadOnceAvatar()
        {
            var n = _transforms.Count;
            for (var i = 0; i < n; i++)
            {
                OnceHandleMask[i] = false;
                if (!_transformToIndex.TryGetValue(_transforms[i], out var idx))
                    continue;
                if (!ActiveAvatarOnceMask.GetTransformActive(idx))
                    continue;
                OnceHandleMask[i] = true;
                OnceHandleWeights[i] = GetWeightForTransform(_transforms[i]);
            }
        }

        private float GetWeightForTransform(Transform t)
        {
            return _weightByTransform.TryGetValue(t, out var w) ? w : 1f;
        }

        private static List<Transform> CollectBones(Transform root, string boneTag)
        {
            var list = new List<Transform>();
            var all = root.GetComponentsInChildren<Transform>();
            foreach (var t in all)
            {
                if (root.Equals(t)) continue;
                if (t.gameObject.CompareTag(boneTag))
                    list.Add(t);
            }
            return list;
        }
    }
}
