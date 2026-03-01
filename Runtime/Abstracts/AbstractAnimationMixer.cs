using System;
using System.Collections.Generic;
using System.Linq;
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
        
        [SerializeField] [Range(0.0f, 1.0f)] protected float weight;

        protected NativeArray<TransformStreamHandle> Handles;
        /// <summary>Per-bone overlay input index: 0 = base only, 1 = first overlay, 2 = second, etc.</summary>
        protected NativeArray<int> HandleInputIndex;
        protected NativeArray<float> HandleWeights;
        /// <summary>Per-bone once input index: 0 = from MaskMixer only, 1 = first once clip, etc.</summary>
        protected NativeArray<int> OnceHandleInputIndex;
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
            public PlayableClip PlayableClip;
            public CancellationTokenSource cts;
            public AvatarMask mask;
        }

        private sealed class OnceLayerState
        {
            public AnimationClipPlayable playable;
            public AnimationPlayOnceData data;
            public AvatarMask mask;
        }

        protected virtual void Awake()
        {
            Animator = GetComponent<Animator>();
            _baseAvatar = new AvatarMask();

            LoadAvatar();
            ReloadAvatar(GetOverlayMasks());
            ReloadOnceAvatar(GetOnceMasks());

            var job = new AnimationMixerJob()
            {
                Handles = Handles,
                HandleInputIndex = HandleInputIndex,
                HandleWeights = HandleWeights,
                Weight = weight,
                InputCount = 1,
            };

            var job2 = new AnimationMixerJob()
            {
                Handles = Handles,
                HandleInputIndex = OnceHandleInputIndex,
                HandleWeights = OnceHandleWeights,
                Weight = weight,
                InputCount = 1,
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
            if (HandleInputIndex.IsCreated) HandleInputIndex.Dispose();
            if (HandleWeights.IsCreated) HandleWeights.Dispose();
            if (OnceHandleInputIndex.IsCreated) OnceHandleInputIndex.Dispose();
            if (OnceHandleWeights.IsCreated) OnceHandleWeights.Dispose();
        }

        /// <summary>Plays an overlay clip on top of the base controller. Adds a new layer (supports multiple overlay inputs). Does not mutate the passed clip.</summary>
        /// <param name="playableClip">Clip config (clip, avatarMask, speed, blendTime). Not modified by the mixer.</param>
        /// <returns>Completes when the overlay is connected; cancel via <see cref="Cancel"/> with the layer index.</returns>
        public async Awaitable Play(PlayableClip playableClip, bool rewrite = true)
        {
            if (playableClip == null || playableClip.clip == null)
                return;
            
            var mask = playableClip.avatarMask != null ? playableClip.avatarMask : _baseAvatar;
            var state = new LayerState
            {
                cts = new CancellationTokenSource(),
                mask = mask,
                playable = AnimationClipPlayable.Create(Graph, playableClip.clip),
                PlayableClip = playableClip,
            };
            state.playable.SetTime(0);
            state.playable.SetSpeed(playableClip.speed);
            
            _layers.Add(state);
            
            ReconnectOverlayInputs();
            ReloadAvatar(GetOverlayMasks());
            UpdateMaskMixerJob();
            
            MaskMixer.SetInputWeight(_layers.Count, 0);

            float blendDuration = playableClip.blendTime > 0f ? playableClip.blendTime : 0.15f;
            await ChangeWeight(MaskMixer, _layers.Count, 1f, blendDuration, state.cts, useOnceWeights: false, rewrite);
            
            if (rewrite)
            {
                var last = _layers.Last();
                for (int i = 0; i < _layers.Count; i++)
                {
                    state = _layers[i];
                    if (state.Equals(last))
                    {
                        continue;
                    }
                    
                    state.cts?.Cancel();
                    state.cts?.Dispose();

                    if (state.playable.IsValid())
                        state.playable.Destroy();

                    _layers.RemoveAt(i);
                }
                
                ReconnectOverlayInputs();
                ReloadAvatar(GetOverlayMasks());
                UpdateMaskMixerJob();
            }
        }

        /// <summary>Plays a one-shot clip. Adds a new once layer (supports multiple simultaneous once clips). Uses only <see cref="AnimationPlayOnceData.CancelToken"/> for cancellation.</summary>
        /// <param name="animationPlayOnceData">Clip config, CancelToken, ProcessCallback, EndCallback. PlayableAnimationClip is not mutated.</param>
        /// <returns>Completes when the clip finishes or is cancelled.</returns>
        public async Awaitable PlayOnce(AnimationPlayOnceData animationPlayOnceData)
        {
            if (animationPlayOnceData?.PlayableClip?.clip == null)
                return;

            var mask = animationPlayOnceData.PlayableClip.avatarMask != null
                ? animationPlayOnceData.PlayableClip.avatarMask
                : _baseAvatar;
            var clip = animationPlayOnceData.PlayableClip.clip;
            var playableClip = AnimationClipPlayable.Create(Graph, clip);
            playableClip.SetDuration(clip.length);
            playableClip.SetTime(0);
            playableClip.SetSpeed(animationPlayOnceData.PlayableClip.speed);

            var state = new OnceLayerState { playable = playableClip, data = animationPlayOnceData, mask = mask };
            _onceLayers.Add(state);
            ReconnectOnceInputs();
            ReloadOnceAvatar(GetOnceMasks());
            UpdateOnceMixerJob();

            OnceMaskMixer.SetInputWeight(_onceLayers.Count, 0);
            
            var cts = animationPlayOnceData.CancelToken;
            float blendDuration = animationPlayOnceData.PlayableClip.blendTime > 0f ? animationPlayOnceData.PlayableClip.blendTime : 0.15f;
            await ChangeWeight(OnceMaskMixer, _onceLayers.Count, 1f, blendDuration, cts, useOnceWeights: true, false);

            if (!playableClip.IsValid() || (cts != null && cts.IsCancellationRequested))
                return;

            await WaitClipEnd(playableClip, animationPlayOnceData.ProcessCallback, cts);

            if (cts != null && cts.IsCancellationRequested)
                return;
            
            await CancelOnceClip(_onceLayers.IndexOf(state));
            
            animationPlayOnceData.EndCallback?.Invoke();
        }

        /// <summary>Cancels the once clip at the given layer index. Blends out, disconnects, and removes from the list.</summary>
        /// <param name="layer">Layer index (0-based). Ignored if out of range.</param>
        public async Awaitable CancelOnceClip(int layer)
        {
            if (layer < 0 || layer >= _onceLayers.Count)
                return;

            var state = _onceLayers[layer];
            var data = state.data;
            var cts = data?.CancelToken;
            
            float blendDuration = data.PlayableClip.outBlendTime > 0f ? data.PlayableClip.outBlendTime : 0.15f;
            await ChangeWeight(OnceMaskMixer, layer + 1, 0f, blendDuration, cts, useOnceWeights: true, rewrite: false);
            
            state.data?.CancelToken?.Cancel();

            if (state.playable.IsValid())
                state.playable.Destroy();

            var idx = _onceLayers.IndexOf(state);
            if (idx >= 0)
                _onceLayers.RemoveAt(idx);
            ReconnectOnceInputs();
            ReloadOnceAvatar(GetOnceMasks());
            UpdateOnceMixerJob();
        }

        /// <summary>Cancels the overlay at the given layer index. Blends out, disconnects, and removes from the layer list.</summary>
        /// <param name="layer">Layer index (0-based). Ignored if out of range.</param>
        public async Awaitable Cancel(int layer)
        {
            if (layer < 0 || layer >= _layers.Count)
                return;

            var state = _layers[layer];
            
            float blendDuration = state.PlayableClip.outBlendTime > 0f ? state.PlayableClip.outBlendTime : 0.15f;
            await ChangeWeight(MaskMixer, layer + 1, 0f, blendDuration, state.cts, useOnceWeights: false, rewrite: false);

            state.cts?.Cancel();
            state.cts?.Dispose();

            if (state.playable.IsValid())
                state.playable.Destroy();

            var idx = _layers.IndexOf(state);
            if (idx >= 0)
                _layers.RemoveAt(idx);
            ReconnectOverlayInputs();
            ReloadAvatar(GetOverlayMasks());
            UpdateMaskMixerJob();
        }

        private async Awaitable ChangeWeight(AnimationScriptPlayable mixer, int layer, float targetWeight, float duration,
            CancellationTokenSource token, bool useOnceWeights, bool rewrite)
        {
            var startWeight = mixer.GetInputWeight(layer);
            
            await Blend(duration, blendTime =>
            {
                if (token != null && token.IsCancellationRequested)
                    return;

                var inputCount = mixer.GetInputCount();
                if (inputCount == 1)
                {
                    return;
                }

                var job = mixer.GetJobData<AnimationMixerJob>();
                var w = Mathf.Lerp(startWeight, targetWeight, EaseOutQuad(blendTime));
                
                try
                {
                    mixer.SetInputWeight(layer, w);
                }
                catch
                {
                    layer = mixer.GetInputCount() - 1;

                    mixer.SetInputWeight(layer, w);
                }
                
                if (rewrite)
                {
                    for (int i = 1; i < inputCount; i++)
                    {
                        if (i != layer)
                        {
                            w = Mathf.Lerp(mixer.GetInputWeight(i), 0, EaseOutQuad(blendTime));
                            
                            mixer.SetInputWeight(i, w);
                        }
                    }
                }

                job.HandleWeights = useOnceWeights ? OnceHandleWeights : HandleWeights;
                job.HandleInputIndex = useOnceWeights ? OnceHandleInputIndex : HandleInputIndex;
                mixer.SetJobData(job);
            }, token);
        }
        
        private float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
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
        /// <param name="overlayMasks">Masks for each overlay layer (index 0 = input 1, etc.). Empty = all bones use base only.</param>
        protected abstract void ReloadAvatar(IReadOnlyList<AvatarMask> overlayMasks);
        /// <param name="onceMasks">Masks for each once layer (index 0 = input 1, etc.). Empty = all bones from MaskMixer only.</param>
        protected abstract void ReloadOnceAvatar(IReadOnlyList<AvatarMask> onceMasks);

        private List<AvatarMask> GetOverlayMasks()
        {
            var list = new List<AvatarMask>(_layers.Count);
            for (var i = 0; i < _layers.Count; i++)
                list.Add(_layers[i].mask ?? _baseAvatar);
            return list;
        }

        private List<AvatarMask> GetOnceMasks()
        {
            var list = new List<AvatarMask>(_onceLayers.Count);
            for (var i = 0; i < _onceLayers.Count; i++)
                list.Add(_onceLayers[i].mask ?? _baseAvatar);
            return list;
        }

        private void ReconnectOverlayInputs()
        {
            var count = 1 + _layers.Count;
            for (var i = 1; i < MaskMixer.GetInputCount(); i++)
                MaskMixer.DisconnectInput(i);
            MaskMixer.SetInputCount(count);
            for (var i = 0; i < _layers.Count; i++)
            {
                MaskMixer.ConnectInput(1 + i, _layers[i].playable, 0);
                MaskMixer.SetInputWeight(1 + i, 1f);
            }
        }

        private void ReconnectOnceInputs()
        {
            var count = 1 + _onceLayers.Count;
            for (var i = 1; i < OnceMaskMixer.GetInputCount(); i++)
                OnceMaskMixer.DisconnectInput(i);
            OnceMaskMixer.SetInputCount(count);
            for (var i = 0; i < _onceLayers.Count; i++)
            {
                OnceMaskMixer.ConnectInput(1 + i, _onceLayers[i].playable, 0);
                OnceMaskMixer.SetInputWeight(1 + i, 1f);
            }
        }

        private void UpdateMaskMixerJob()
        {
            var job = MaskMixer.GetJobData<AnimationMixerJob>();
            job.InputCount = MaskMixer.GetInputCount();
            job.HandleInputIndex = HandleInputIndex;
            MaskMixer.SetJobData(job);
        }

        private void UpdateOnceMixerJob()
        {
            var job = OnceMaskMixer.GetJobData<AnimationMixerJob>();
            job.InputCount = OnceMaskMixer.GetInputCount();
            job.HandleInputIndex = OnceHandleInputIndex;
            OnceMaskMixer.SetJobData(job);
        }

        private void RemoveOnceLayer(OnceLayerState state)
        {
            var idx = _onceLayers.IndexOf(state);
            if (idx < 0) return;
            if (state.playable.IsValid())
                state.playable.Destroy();
            _onceLayers.RemoveAt(idx);
            ReconnectOnceInputs();
            ReloadOnceAvatar(GetOnceMasks());
            UpdateOnceMixerJob();
        }

        private struct AnimationMixerJob : IAnimationJob
        {
            public NativeArray<TransformStreamHandle> Handles;
            /// <summary>Per-bone: 0 = base only, 1..InputCount-1 = blend with that input.</summary>
            public NativeArray<int> HandleInputIndex;
            public NativeArray<float> HandleWeights;
            public int InputCount;
            public float Weight;

            public void ProcessRootMotion(AnimationStream stream)
            {
                var velocity = stream.GetInputStream(0).velocity;
                var angularVelocity = stream.GetInputStream(0).angularVelocity;
                for (var inp = 1; inp < InputCount; inp++)
                {
                    var s = stream.GetInputStream(inp);
                    if (!s.isValid) continue;
                    velocity += s.velocity;
                    angularVelocity += s.angularVelocity;
                }
                
                stream.velocity = Vector3.Lerp(stream.velocity, velocity, Weight);
                stream.angularVelocity = Vector3.Lerp(stream.angularVelocity, angularVelocity, Weight);
            }

            public void ProcessAnimation(AnimationStream stream)
            {
                var streamA = stream.GetInputStream(0);

                for (var i = 0; i < Handles.Length; i++)
                {
                    var handle = Handles[i];
                    var inp = HandleInputIndex[i];
                    var boneWeight = HandleWeights[i];

                    if (inp > 0 && inp < InputCount)
                    {
                        var streamB = stream.GetInputStream(inp);
                        
                        if (streamB.isValid)
                        {
                            var w = stream.GetInputWeight(inp);
                            
                            var posA = handle.GetPosition(streamA);
                            var posB = handle.GetPosition(streamB);
                            handle.SetPosition(stream, Vector3.Lerp(posA, posB, boneWeight * w));

                            var rotA = handle.GetRotation(streamA);
                            var rotB = handle.GetRotation(streamB);
                            handle.SetRotation(stream, Quaternion.Slerp(rotA, rotB, boneWeight * w));
                            continue;
                        }
                    }

                    handle.SetPosition(stream, handle.GetPosition(streamA));
                    handle.SetRotation(stream, handle.GetRotation(streamA));
                }
            }
        }
    }
}