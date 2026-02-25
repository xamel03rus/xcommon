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
            HandleInputIndex = new NativeArray<int>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HandleWeights = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            OnceHandleInputIndex = new NativeArray<int>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            OnceHandleWeights = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            for (var i = 0; i < n; i++)
            {
                Handles[i] = Animator.BindStreamTransform(_transforms[i]);
                HandleInputIndex[i] = 0;
                OnceHandleInputIndex[i] = 0;
                HandleWeights[i] = 1f;
                OnceHandleWeights[i] = 1f;
            }
        }

        protected override void ReloadAvatar(IReadOnlyList<AvatarMask> overlayMasks)
        {
            var n = _transforms.Count;
            for (var i = 0; i < n; i++)
                HandleInputIndex[i] = 0;

            if (overlayMasks == null) return;
            for (var l = 0; l < overlayMasks.Count; l++)
            {
                var mask = overlayMasks[l];
                if (mask == null) continue;
                for (var i = 0; i < n; i++)
                {
                    if (!_transformToIndex.TryGetValue(_transforms[i], out var idx))
                        continue;
                    if (mask.GetTransformActive(idx))
                    {
                        HandleInputIndex[i] = l + 1;
                        HandleWeights[i] = GetWeightForTransform(_transforms[i]);
                    }
                }
            }
        }

        protected override void ReloadOnceAvatar(IReadOnlyList<AvatarMask> onceMasks)
        {
            var n = _transforms.Count;
            for (var i = 0; i < n; i++)
                OnceHandleInputIndex[i] = 0;

            if (onceMasks == null) return;
            for (var l = 0; l < onceMasks.Count; l++)
            {
                var mask = onceMasks[l];
                if (mask == null) continue;
                for (var i = 0; i < n; i++)
                {
                    if (!_transformToIndex.TryGetValue(_transforms[i], out var idx))
                        continue;
                    if (mask.GetTransformActive(idx))
                    {
                        OnceHandleInputIndex[i] = l + 1;
                        OnceHandleWeights[i] = GetWeightForTransform(_transforms[i]);
                    }
                }
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
