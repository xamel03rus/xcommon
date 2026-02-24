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
    [DisallowMultipleComponent]
    public abstract class AbstractAnimationMixer : MonoBehaviour
    {
        protected string BoneTag = "Bone";

        private AvatarMask _baseAvatar;

        protected List<AnimationPlayableClip> Layers = new();
        
        protected List<AnimationPlayableClip> OnceLayers = new();

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

        public Animator Animator;
        
        public AnimatorControllerPlayable Controller;
        
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

            Graph = PlayableGraph.Create($"{gameObject.name} - Graph");
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
            {
                Graph.Destroy();
            }

            foreach (var l in Layers)
            {
                if (l.playable.IsValid())
                {
                    l.playable.Destroy();
                }
            }

            foreach (var l in OnceLayers)
            {
                if (l.playable.IsValid())
                {
                    l.playable.Destroy();
                }
            }

            if (MaskMixer.IsValid())
            {
                MaskMixer.Destroy();
            }

            if (OnceMaskMixer.IsValid())
            {
                OnceMaskMixer.Destroy();
            }

            Handles.Dispose();
            HandleMask.Dispose();
            HandleWeights.Dispose();
        }
        
        public async void Play(AnimationPlayableClip playableClip)
        {
            if (MaskMixer.GetInputCount() == 2)
            {
                await Cancel(1);
            }

            if (playableClip.avatarMask == null)
            {
                playableClip.avatarMask = _baseAvatar;
            }

            ActiveAvatarMask = playableClip.avatarMask;
            
            ReloadAvatar();

            await ChangeWeight(MaskMixer, weight, 0.15f, playableClip.cts);
            
            playableClip.playable = AnimationClipPlayable.Create(Graph, playableClip.clip);
            playableClip.playable.SetTime(0);
            playableClip.playable.SetSpeed(playableClip.speed);

            Layers.Add(playableClip);
            
            MaskMixer.SetInputCount(2);
            MaskMixer.DisconnectInput(1);
            MaskMixer.ConnectInput(1, playableClip.playable, 0);
            MaskMixer.SetInputWeight(1, 1);
        }

        public async Awaitable PlayOnce(AnimationPlayOnceData animationPlayOnceData)
        {
            if (OnceMaskMixer.GetInputCount() == 2)
            {
                await CancelOnceClip(1);
            }
            
            var mask = animationPlayOnceData.PlayableAnimationClip.avatarMask;
            if (mask == null)
            {
                mask = _baseAvatar;
            }

            ActiveAvatarOnceMask = mask;

            ReloadOnceAvatar();
            
            var clip = animationPlayOnceData.PlayableAnimationClip.clip;
            var playableClip = AnimationClipPlayable.Create(Graph, clip);
            playableClip.SetDuration(clip.length);
            playableClip.SetTime(0);
            playableClip.SetSpeed(0);

            animationPlayOnceData.PlayableAnimationClip.playable = playableClip;

            OnceLayers.Add(animationPlayOnceData.PlayableAnimationClip);

            OnceMaskMixer.SetInputCount(2);
            OnceMaskMixer.ConnectInput(1, playableClip, 0);
            OnceMaskMixer.SetInputWeight(1, 1);
            
            await ChangeWeight(OnceMaskMixer, weight, 0.15f, animationPlayOnceData.PlayableAnimationClip.cts);

            if (!playableClip.IsValid() || animationPlayOnceData.PlayableAnimationClip.cts.IsCancellationRequested)
            {
                return;
            }

            playableClip.SetSpeed(animationPlayOnceData.PlayableAnimationClip.speed);

            await WaitClipEnd(playableClip, animationPlayOnceData.ProcessCallback, animationPlayOnceData.PlayableAnimationClip.cts);

            if (animationPlayOnceData.PlayableAnimationClip.cts.IsCancellationRequested)
            {
                return;
            }
            
            animationPlayOnceData.EndCallback?.Invoke();
            
            await CancelOnceClip(1);
        }

        public async Awaitable CancelOnceClip(int layer)
        {
            await ChangeWeight(OnceMaskMixer, 0f, 0.15f, OnceLayers[layer].cts);

            OnceLayers[layer].cts.Cancel();

            if (OnceMaskMixer.GetInputCount() == 2)
            {
                OnceMaskMixer.DisconnectInput(1);
                OnceMaskMixer.SetInputCount(1);
            }
            
            if (OnceLayers[layer].playable.IsValid())
            {
                OnceLayers[layer].playable.Destroy();
            }
        }
        
        public async Awaitable Cancel(int layer)
        {
            await ChangeWeight(MaskMixer, 0f, 0.1f, Layers[layer].cts);
            
            if (MaskMixer.GetInputCount() == 2)
            {
                MaskMixer.DisconnectInput(1);
                MaskMixer.SetInputCount(1);
            }

            if (Layers[layer].playable.IsValid())
            {
                Layers[layer].playable.Destroy();
            }
        }

        private async Awaitable ChangeWeight(AnimationScriptPlayable mixer, float targetWeight, float duration, CancellationTokenSource token)
        {
            await Blend(duration, blendTime =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                
                var job = mixer.GetJobData<AnimationMixerJob>();
                var w = Mathf.Lerp(job.Weight, targetWeight, blendTime);

                job.HandleWeights = HandleWeights;
                job.Weight = w;

                mixer.SetJobData(job);
            });
        }
        
        private static async Awaitable Blend(float duration, Action<float> blendCallback)
        {
            var blendTime = 0f;
            while (blendTime < 1f)
            {
                blendTime += Time.deltaTime / duration;
                blendCallback(blendTime);
                await Awaitable.EndOfFrameAsync();
            }

            blendCallback(1f);
        }

        private static async Awaitable WaitClipEnd(AnimationClipPlayable clip, Action<float> callback,
            CancellationTokenSource playOnceCts)
        {
            while (clip.IsValid() && !clip.IsDone() && !playOnceCts.IsCancellationRequested)
            {
                callback?.Invoke((float) (clip.GetTime() / clip.GetDuration()));

                await Awaitable.EndOfFrameAsync();
            }
        }

        private static async Awaitable WaitClipStart(AnimationClipPlayable clip, Action<float> callback,
            CancellationTokenSource playOnceCts)
        {
            while (clip.IsValid() && clip.GetTime() > 0 && !playOnceCts.IsCancellationRequested)
            {
                callback?.Invoke((float) (clip.GetTime() / clip.GetDuration()));

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

                var velocity = Vector3.Lerp(streamA.velocity, streamB.velocity, Weight);
                var angularVelocity = Vector3.Lerp(streamA.angularVelocity, streamB.angularVelocity, Weight);
                stream.velocity = velocity;
                stream.angularVelocity = angularVelocity;
            }

            public void ProcessAnimation(AnimationStream stream)
            {
                var streamA = stream.GetInputStream(0);
                var streamB = stream.GetInputStream(1);
                var streamC = stream.GetInputStream(2);

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

                        if (streamC.isValid && HandleMask[i])
                        {
                            var posC = handle.GetPosition(streamC);
                            var rotC = handle.GetRotation(streamC);

                            handle.SetPosition(stream, Vector3.Lerp(posB, posC, Weight * boneWeight));
                            handle.SetRotation(stream, Quaternion.Slerp(rotB, rotC, Weight * boneWeight));
                        }

                        continue;
                    }

                    handle.SetPosition(stream, handle.GetPosition(streamA));
                    handle.SetRotation(stream, handle.GetRotation(streamA));
                }
            }
        }
    }
}