using System;
using System.Collections.Generic;
using UnityEngine;
#if MESSAGEPIPE_AVAILABLE
using MessagePipe;
#endif

namespace Xamel.Common.Abstracts
{
    public class AbstractMonoListener : MonoBehaviour, IDisposable
    {
        protected List<IDisposable> Disposables = new List<IDisposable>();

        protected virtual void OnDestroy()
        {
            Dispose();
        }

#if MESSAGEPIPE_AVAILABLE
        protected void CreateSubscriber<TMessage>(ISubscriber<TMessage> subscriber, Action<TMessage> handler)
        {
            var d = DisposableBag.CreateBuilder();

            subscriber.Subscribe(handler).AddTo(d);

            Disposables.Add(d.Build());
        }
#endif

        public virtual void Dispose()
        {
            Disposables.ForEach(d => d.Dispose());
            Disposables.Clear();
        }
    }
}