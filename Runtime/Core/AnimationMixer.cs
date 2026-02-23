using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using XCommon.Abstracts;

namespace Xamel.Common.Core
{
    public class AnimationMixer : AbstractAnimationMixer
    {
        [Serializable]
        public class BoneTransformWeight
        {
            public Transform bone;

            [Range(0.0f, 1.0f)]
            public float weight;
        }

        [SerializeField]
        private BoneTransformWeight[] weightRewrites;

        protected override void LoadAvatar()
        {
            var allTransforms = Animator.transform.GetComponentsInChildren<Transform>();
            var transforms = new List<Transform>();
            foreach (var t in allTransforms)
            {
                if (Animator.transform.Equals(t))
                {
                    continue;
                }
                
                if (t.gameObject.CompareTag(BoneTag))
                {
                    transforms.Add(t);
                }
            }

            var numTransforms = transforms.Count;
            
            Handles = new NativeArray<TransformStreamHandle>(numTransforms, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            HandleMask = new NativeArray<bool>(numTransforms, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            HandleWeights = new NativeArray<float>(numTransforms, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            
            for (var i = 0; i < numTransforms; i++)
            {
                Handles[i] = Animator.BindStreamTransform(transforms[i]);
                HandleMask[i] = false;

                var index = Array.IndexOf(allTransforms, transforms[i]);
                
                if (!ActiveAvatarMask.GetTransformActive(index))
                {
                    continue;
                }

                HandleMask[i] = true;

                var rewriteWeight = 1f;
                var rewrite = weightRewrites.ToList().Find(w => w.bone.Equals(transforms[i]));
                if (rewrite != null)
                {
                    rewriteWeight = rewrite.weight;
                }

                HandleWeights[i] = rewriteWeight;
            }
        }

        protected override void ReloadAvatar()
        {
            var allTransforms = Animator.transform.GetComponentsInChildren<Transform>();
            var transforms = new List<Transform>();
            foreach (var t in allTransforms)
            {
                if (Animator.transform.Equals(t))
                {
                    continue;
                }

                if (t.gameObject.CompareTag(BoneTag))
                {
                    transforms.Add(t);
                }
            }

            var numTransforms = transforms.Count;
            
            for (var i = 0; i < numTransforms; i++)
            {
                HandleMask[i] = false;

                var index = Array.IndexOf(allTransforms, transforms[i]);

                if (!ActiveAvatarMask.GetTransformActive(index))
                {
                    continue;
                }

                HandleMask[i] = true;
            }
        }

        protected override void ReloadOnceAvatar()
        {
            throw new NotImplementedException();
        }
    }
}