using System;
using UnityEngine;

namespace Xamel.Runtime.Models
{
    [Serializable]
    public class AnimationPlayableClip
    {
        public AnimationClip animationClip;
        public AvatarMask avatarMask;
        public float speed = 1f;
        public float blendTime = 0.15f;
    }
}