using System;
using UnityEngine;

namespace Xamel.Runtime.Models
{
    [Serializable]
    public class HumanoidTransformWeight
    {
        public HumanBodyBones bone;

        [Range(0.0f, 1.0f)]
        public float weight;
    }
}