using System;
using UnityEngine;

namespace Xamel.Runtime.Models
{
    /// <summary>Per-bone weight override for humanoid mixers (0–1). Bones not listed use weight 1.</summary>
    [Serializable]
    public class HumanoidTransformWeight
    {
        public HumanBodyBones bone;
        [Range(0.0f, 1.0f)] public float weight;
    }
}