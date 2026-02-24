using System;
using System.Threading;

namespace Xamel.Runtime.Models
{
    /// <summary>
    /// Data for a one-shot animation: clip config, single cancellation token, and callbacks.
    /// Use CancelToken to cancel playback; do not use PlayableAnimationClip.cts.
    /// </summary>
    public class AnimationPlayOnceData
    {
        public AnimationPlayableClip PlayableAnimationClip;
        /// <summary>Single source for cancellation. Cancel this to stop the once clip.</summary>
        public CancellationTokenSource CancelToken;
        public bool WithBlending = false;
        public Action<float> ProcessCallback = null;
        public Action EndCallback = null;
    }
}