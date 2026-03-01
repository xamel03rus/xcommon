using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Animations;

namespace Xamel.Runtime.Models
{
    /// <summary>
    /// Configuration for an animation clip used by the mixer (clip, mask, speed, blend time).
    /// Runtime state (playable, cts) is owned by the mixer and not serialized.
    /// </summary>
    [Serializable]
    public class PlayableClip
    {
        public AnimationClip clip;
        [NonSerialized] public AnimationClipPlayable playable;
        public AvatarMask avatarMask;
        public float speed = 1f;
        public float blendTime = 0.15f;
        public float outBlendTime = 0.15f;
        [NonSerialized] public CancellationTokenSource cts;
    }
}