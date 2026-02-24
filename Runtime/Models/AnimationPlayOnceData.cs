using System;
using System.Threading;

namespace Xamel.Runtime.Models
{
    public class AnimationPlayOnceData
    {
        public AnimationPlayableClip PlayableAnimationClip;
        public CancellationTokenSource CancelToken;
        public bool WithBlending = false;
        public Action<float> ProcessCallback = null;
        public Action EndCallback = null;
    }
}