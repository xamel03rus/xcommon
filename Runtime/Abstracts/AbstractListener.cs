using System;
using System.Collections.Generic;
#if MESSAGEPIPE_AVAILABLE
using MessagePipe;
#endif

namespace Xamel.Common.Abstracts
{
    public abstract class AbstractListener : IDisposable
    {
        protected List<IDisposable> Disposables = new List<IDisposable>();
        
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