using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Animations;

namespace Xamel.Runtime.Models
{
    [Serializable]
    public class AnimationPlayableClip
    {
        public AnimationClip clip;
        public AnimationClipPlayable playable;
        public AvatarMask avatarMask;
        public float speed = 1f;
        public float blendTime = 0.15f;
        public CancellationTokenSource cts;
    }
}