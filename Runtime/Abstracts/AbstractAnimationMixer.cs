using System;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using X.Runtime.Models;

namespace Xamel.Common.Abstracts
{
    [DisallowMultipleComponent]
    public abstract class AbstractAnimationMixer : MonoBehaviour
    {
        protected Animator Animator;

        protected string BoneTag = "Bone";

        private AvatarMask _baseAvatar;

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

        private AnimationClipPlayable _playableClip;

        private AnimationClipPlayable _oncePlayableClip;
        
        private CancellationTokenSource _playOnceCts;

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

            if (_playableClip.IsValid())
            {
                _playableClip.Destroy();
            }

            if (_oncePlayableClip.IsValid())
            {
                _oncePlayableClip.Destroy();
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
        
        public async void Play(AnimationClip clip, AvatarMask mask = null, float speed = 1f)
        {
            if (MaskMixer.GetInputCount() == 2)
            {
                await Stop();
            }

            if (mask == null)
            {
                mask = _baseAvatar;
            }

            ActiveAvatarMask = mask;
            
            ReloadAvatar();

            await Blend(0.15f, blendTime =>
            {
                var w = Mathf.Lerp(0f, weight, blendTime);
                var job = MaskMixer.GetJobData<AnimationMixerJob>();

                job.HandleWeights = HandleWeights;
                job.Weight = w;

                MaskMixer.SetJobData(job);
            });
            
            _playableClip = AnimationClipPlayable.Create(Graph, clip);
            _playableClip.SetTime(0);
            _playableClip.SetSpeed(speed);

            MaskMixer.SetInputCount(2);
            MaskMixer.DisconnectInput(1);
            MaskMixer.ConnectInput(1, _playableClip, 0);
            MaskMixer.SetInputWeight(1, 1);
        }

        public async Awaitable PlayOnce(AnimationPlayOnceData animationPlayOnceData)
        {
            if (OnceMaskMixer.GetInputCount() == 2)
            {
                await CancelOnceClip();
            }
            
            var mask = animationPlayOnceData.PlayableAnimationClip.avatarMask;
            if (mask == null)
            {
                mask = _baseAvatar;
            }

            ActiveAvatarOnceMask = mask;

            ReloadOnceAvatar();
            
            var clip = animationPlayOnceData.PlayableAnimationClip.animationClip;
            var playableClip = AnimationClipPlayable.Create(Graph, clip);
            playableClip.SetDuration(clip.length);
            playableClip.SetTime(0);
            playableClip.SetSpeed(0);

            _oncePlayableClip = playableClip;

            OnceMaskMixer.SetInputCount(2);
            OnceMaskMixer.ConnectInput(1, playableClip, 0);
            OnceMaskMixer.SetInputWeight(1, 1);
            
            _playOnceCts = animationPlayOnceData.CancelToken;
            
            await Blend(0.15f, bTime =>
            {
                if (_playOnceCts.IsCancellationRequested)
                {
                    return;
                }

                var w = Mathf.Lerp(0f, weight, bTime);
                var job = OnceMaskMixer.GetJobData<AnimationMixerJob>();

                job.HandleWeights = OnceHandleWeights;
                job.Weight = w;

                OnceMaskMixer.SetJobData(job);
            });
            
            if (!playableClip.IsValid() || _playOnceCts.IsCancellationRequested)
            {
                return;
            }

            playableClip.SetSpeed(animationPlayOnceData.PlayableAnimationClip.speed);

            await WaitClipEnd(playableClip, animationPlayOnceData.ProcessCallback, _playOnceCts);

            if (_playOnceCts.IsCancellationRequested)
            {
                return;
            }
            
            animationPlayOnceData.EndCallback?.Invoke();
            
            await CancelOnceClip();
        }

        public async Awaitable CancelOnceClip()
        {
            await Blend(0.15f, bTime =>
            {
                if (_playOnceCts.IsCancellationRequested)
                {
                    return;
                }

                var w = Mathf.Lerp(weight, 0f, bTime);
                var job = OnceMaskMixer.GetJobData<AnimationMixerJob>();

                job.HandleWeights = OnceHandleWeights;
                job.Weight = w;

                OnceMaskMixer.SetJobData(job);
            });
            
            _playOnceCts.Cancel();

            if (OnceMaskMixer.GetInputCount() == 2)
            {
                OnceMaskMixer.DisconnectInput(1);
                OnceMaskMixer.SetInputCount(1);
            }
            
            if (_oncePlayableClip.IsValid())
            {
                _oncePlayableClip.Destroy();
            }
        }
        
        public async Awaitable Stop()
        {
            await Blend(0.1f, blendTime =>
            {
                var w = Mathf.Lerp(weight, 0f, blendTime);
                var job = MaskMixer.GetJobData<AnimationMixerJob>();

                job.HandleWeights = HandleWeights;
                job.Weight = w;

                MaskMixer.SetJobData(job);
            });

            if (MaskMixer.GetInputCount() == 2)
            {
                MaskMixer.DisconnectInput(1);
                MaskMixer.SetInputCount(1);
            }

            if (_playableClip.IsValid())
            {
                _playableClip.Destroy();
            }
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