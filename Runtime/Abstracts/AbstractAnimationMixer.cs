using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xamel.Runtime.Models;

namespace Xamel.Common.Abstracts
{
    /// <summary>
    /// Base class for blending an Animator Controller with overlay and one-shot animation clips
    /// using AvatarMask and per-bone weights. Use <see cref="Play"/> for overlay and <see cref="PlayOnce"/> for one-shot.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class AbstractAnimationMixer : MonoBehaviour
    {
        protected string BoneTag = "Bone";

        private AvatarMask _baseAvatar;
        private static int _graphId;

        private readonly List<LayerState> _layers = new();
        private readonly List<OnceLayerState> _onceLayers = new();

        protected AvatarMask ActiveAvatarMask;
        protected AvatarMask ActiveAvatarOnceMask;

        [SerializeField] [Range(0.0f, 1.0f)] protected float weight;

        protected NativeArray<TransformStreamHandle> Handles;
        protected NativeArray<bool> HandleMask;
        protected NativeArray<float> HandleWeights;
        protected NativeArray<bool> OnceHandleMask;
        protected NativeArray<float> OnceHandleWeights;

        protected PlayableGraph Graph;
        protected AnimationScriptPlayable MaskMixer;
        protected AnimationScriptPlayable OnceMaskMixer;

        /// <summary>Attached Animator. Assigned in Awake.</summary>
        public Animator Animator { get; protected set; }

        /// <summary>Playable for the base runtime animator controller. Assigned in Awake.</summary>
        public AnimatorControllerPlayable Controller;

        private sealed class LayerState
        {
            public AnimationClipPlayable playable;
            public CancellationTokenSource cts;
        }

        private sealed class OnceLayerState
        {
            public AnimationClipPlayable playable;
            public AnimationPlayOnceData data;
        }

        protected virtual void Awake()
        {
            Animator = GetComponent<Animator>();
            _baseAvatar = new AvatarMask();
            ActiveAvatarMask = _baseAvatar;
            ActiveAvatarOnceMask = _baseAvatar;

            LoadAvatar();

            var job = new AnimationMixerJob()
            {
                Handles = Handles,
                HandleMask = HandleMask,
                HandleWeights = HandleWeights,
                Weight = weight,
            };

            var job2 = new AnimationMixerJob()
            {
                Handles = Handles,
                HandleMask = OnceHandleMask,
                HandleWeights = OnceHandleWeights,
                Weight = weight,
            };

            var graphName = $"{gameObject.name} - Graph #{_graphId}";
            Graph = PlayableGraph.Create(graphName);
            _graphId++;
            Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            Controller = AnimatorControllerPlayable.Create(Graph, Animator.runtimeAnimatorController);
            Animator.runtimeAnimatorController = null;

            MaskMixer = AnimationScriptPlayable.Create(Graph, job);
            MaskMixer.SetProcessInputs(false);
            MaskMixer.AddInput(Controller, 0, 1.0f);

            OnceMaskMixer = AnimationScriptPlayable.Create(Graph, job2);
            OnceMaskMixer.SetProcessInputs(false);
            OnceMaskMixer.AddInput(MaskMixer, 0, 1.0f);

            var output = AnimationPlayableOutput.Create(Graph, "output", Animator);
            output.SetSourcePlayable(OnceMaskMixer);

            Graph.Play();
        }

        private void OnDisable()
        {
            if (Graph.IsValid())
                Graph.Destroy();

            foreach (var l in _layers)
            {
                if (l.playable.IsValid())
                    l.playable.Destroy();
                l.cts?.Cancel();
                l.cts?.Dispose();
            }
            _layers.Clear();

            foreach (var l in _onceLayers)
            {
                if (l.playable.IsValid())
                    l.playable.Destroy();
            }
            _onceLayers.Clear();

            if (MaskMixer.IsValid())
                MaskMixer.Destroy();
            if (OnceMaskMixer.IsValid())
                OnceMaskMixer.Destroy();

            if (Handles.IsCreated) Handles.Dispose();
            if (HandleMask.IsCreated) HandleMask.Dispose();
            if (HandleWeights.IsCreated) HandleWeights.Dispose();
            if (OnceHandleMask.IsCreated) OnceHandleMask.Dispose();
            if (OnceHandleWeights.IsCreated) OnceHandleWeights.Dispose();
        }

        /// <summary>Plays an overlay clip on top of the base controller. Does not mutate the passed clip; blend time is read from playableClip.blendTime.</summary>
        /// <param name="playableClip">Clip config (clip, avatarMask, speed, blendTime). Not modified by the mixer.</param>
        /// <returns>Completes when the overlay is connected; cancellation is possible via the mixer's Cancel(0).</returns>
        public async Awaitable Play(AnimationPlayableClip playableClip)
        {
            if (playableClip == null || playableClip.clip == null)
                return;

            if (MaskMixer.GetInputCount() == 2 && _layers.Count > 0)
                await Cancel(0);

            var mask = playableClip.avatarMask != null ? playableClip.avatarMask : _baseAvatar;
            ActiveAvatarMask = mask;
            ReloadAvatar();

            var state = new LayerState
            {
                cts = new CancellationTokenSource(),
                playable = AnimationClipPlayable.Create(Graph, playableClip.clip)
            };
            state.playable.SetTime(0);
            state.playable.SetSpeed(playableClip.speed);

            float blendDuration = playableClip.blendTime > 0f ? playableClip.blendTime : 0.15f;
            await ChangeWeight(MaskMixer, weight, blendDuration, state.cts, useOnceWeights: false);

            if (state.cts.IsCancellationRequested)
            {
                if (state.playable.IsValid()) state.playable.Destroy();
                state.cts.Dispose();
                return;
            }

            _layers.Add(state);
            MaskMixer.SetInputCount(2);
            MaskMixer.DisconnectInput(1);
            MaskMixer.ConnectInput(1, state.playable, 0);
            MaskMixer.SetInputWeight(1, 1f);
        }

        /// <summary>Plays a one-shot clip. Uses only <see cref="AnimationPlayOnceData.CancelToken"/> for cancellation; callbacks from the same data are invoked during and at end of playback.</summary>
        /// <param name="animationPlayOnceData">Clip config, CancelToken, ProcessCallback, EndCallback. PlayableAnimationClip is not mutated.</param>
        /// <returns>Completes when the clip finishes or is cancelled.</returns>
        public async Awaitable PlayOnce(AnimationPlayOnceData animationPlayOnceData)
        {
            if (animationPlayOnceData?.PlayableAnimationClip?.clip == null)
                return;

            if (OnceMaskMixer.GetInputCount() == 2 && _onceLayers.Count > 0)
                await CancelOnceClip(0);

            var mask = animationPlayOnceData.PlayableAnimationClip.avatarMask != null
                ? animationPlayOnceData.PlayableAnimationClip.avatarMask
                : _baseAvatar;
            ActiveAvatarOnceMask = mask;
            ReloadOnceAvatar();

            var clip = animationPlayOnceData.PlayableAnimationClip.clip;
            var playableClip = AnimationClipPlayable.Create(Graph, clip);
            playableClip.SetDuration(clip.length);
            playableClip.SetTime(0);
            playableClip.SetSpeed(0);

            var state = new OnceLayerState { playable = playableClip, data = animationPlayOnceData };
            _onceLayers.Add(state);

            OnceMaskMixer.SetInputCount(2);
            OnceMaskMixer.ConnectInput(1, playableClip, 0);
            OnceMaskMixer.SetInputWeight(1, 1f);

            var cts = animationPlayOnceData.CancelToken;
            await ChangeWeight(OnceMaskMixer, weight, 0.15f, cts, useOnceWeights: true);

            if (!playableClip.IsValid() || (cts != null && cts.IsCancellationRequested))
                return;

            playableClip.SetSpeed(animationPlayOnceData.PlayableAnimationClip.speed);

            await WaitClipEnd(playableClip, animationPlayOnceData.ProcessCallback, cts);

            if (cts != null && cts.IsCancellationRequested)
                return;

            animationPlayOnceData.EndCallback?.Invoke();
            await CancelOnceClip(0);
        }

        /// <summary>Cancels the current once overlay at the given layer index (use 0 for single overlay). Blends out then disconnects and removes from the list.</summary>
        /// <param name="layer">Layer index (0 for the only once overlay). Ignored if out of range.</param>
        public async Awaitable CancelOnceClip(int layer)
        {
            if (layer < 0 || layer >= _onceLayers.Count)
                return;

            var state = _onceLayers[layer];
            var cts = state.data?.CancelToken;
            await ChangeWeight(OnceMaskMixer, 0f, 0.15f, cts, useOnceWeights: true);

            state.data?.CancelToken?.Cancel();

            if (OnceMaskMixer.GetInputCount() == 2)
            {
                OnceMaskMixer.DisconnectInput(1);
                OnceMaskMixer.SetInputCount(1);
            }

            if (state.playable.IsValid())
                state.playable.Destroy();

            _onceLayers.RemoveAt(layer);
        }

        /// <summary>Cancels the overlay at the given layer index (use 0 for single overlay). Blends out then disconnects and removes from the layer list.</summary>
        /// <param name="layer">Layer index (0 for the only overlay). Ignored if out of range.</param>
        public async Awaitable Cancel(int layer)
        {
            if (layer < 0 || layer >= _layers.Count)
                return;

            var state = _layers[layer];
            await ChangeWeight(MaskMixer, 0f, 0.1f, state.cts, useOnceWeights: false);

            state.cts?.Cancel();
            state.cts?.Dispose();

            if (MaskMixer.GetInputCount() == 2)
            {
                MaskMixer.DisconnectInput(1);
                MaskMixer.SetInputCount(1);
            }

            if (state.playable.IsValid())
                state.playable.Destroy();

            _layers.RemoveAt(layer);
        }

        private async Awaitable ChangeWeight(AnimationScriptPlayable mixer, float targetWeight, float duration,
            CancellationTokenSource token, bool useOnceWeights)
        {
            await Blend(duration, blendTime =>
            {
                if (token != null && token.IsCancellationRequested)
                    return;

                var job = mixer.GetJobData<AnimationMixerJob>();
                var w = Mathf.Lerp(job.Weight, targetWeight, blendTime);
                job.Weight = w;
                job.HandleWeights = useOnceWeights ? OnceHandleWeights : HandleWeights;
                mixer.SetJobData(job);
            }, token);
        }

        private static async Awaitable Blend(float duration, Action<float> blendCallback, CancellationTokenSource token = null)
        {
            var blendTime = 0f;
            while (blendTime < 1f)
            {
                if (token != null && token.IsCancellationRequested)
                    return;
                blendTime += Time.deltaTime / duration;
                blendCallback(blendTime);
                await Awaitable.EndOfFrameAsync();
            }
            blendCallback(1f);
        }

        private static async Awaitable WaitClipEnd(AnimationClipPlayable clip, Action<float> callback,
            CancellationTokenSource playOnceCts)
        {
            while (clip.IsValid() && !clip.IsDone() && (playOnceCts == null || !playOnceCts.IsCancellationRequested))
            {
                callback?.Invoke((float)(clip.GetTime() / clip.GetDuration()));
                await Awaitable.EndOfFrameAsync();
            }
        }

        protected abstract void LoadAvatar();
        protected abstract void ReloadAvatar();
        protected abstract void ReloadOnceAvatar();

        private struct AnimationMixerJob : IAnimationJob
        {
            public NativeArray<TransformStreamHandle> Handles;
            public NativeArray<bool> HandleMask;
            public NativeArray<float> HandleWeights;
            public float Weight;

            public void ProcessRootMotion(AnimationStream stream)
            {
                var streamA = stream.GetInputStream(0);
                var streamB = stream.GetInputStream(1);

                if (!streamB.isValid)
                {
                    stream.velocity = streamA.velocity;
                    stream.angularVelocity = streamA.angularVelocity;
                    return;
                }

                stream.velocity = Vector3.Lerp(streamA.velocity, streamB.velocity, Weight);
                stream.angularVelocity = Vector3.Lerp(streamA.angularVelocity, streamB.angularVelocity, Weight);
            }

            public void ProcessAnimation(AnimationStream stream)
            {
                var streamA = stream.GetInputStream(0);
                var streamB = stream.GetInputStream(1);

                for (var i = 0; i < Handles.Length; i++)
                {
                    var handle = Handles[i];
                    var boneWeight = HandleWeights[i];

                    if (streamB.isValid && HandleMask[i])
                    {
                        var posA = handle.GetPosition(streamA);
                        var posB = handle.GetPosition(streamB);
                        handle.SetPosition(stream, Vector3.Lerp(posA, posB, Weight * boneWeight));

                        var rotA = handle.GetRotation(streamA);
                        var rotB = handle.GetRotation(streamB);
                        handle.SetRotation(stream, Quaternion.Slerp(rotA, rotB, Weight * boneWeight));
                    }
                    else
                    {
                        handle.SetPosition(stream, handle.GetPosition(streamA));
                        handle.SetRotation(stream, handle.GetRotation(streamA));
                    }
                }
            }
        }
    }
}