using System;
using System.Collections.Generic;
#if MESSAGEPIPE_AVAILABLE
using MessagePipe;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace Xamel.Common.Abstracts
{
    public abstract class AbstractView : MonoBehaviour, IDisposable
    {
        protected UIDocument Document;
        
        protected event Action OnShow;
        
        protected event Action OnHide;

        private List<IDisposable> _disposables = new List<IDisposable>();
        
        protected virtual void Awake()
        {
            Document = GetComponent<UIDocument>();
            Document.rootVisualElement.style.display = DisplayStyle.None;
        }

#if MESSAGEPIPE_AVAILABLE
        protected void CreateSubscriber<TMessage>(ISubscriber<TMessage> subscriber, Action<TMessage> handler)
        {
            var d = DisposableBag.CreateBuilder();

            subscriber.Subscribe(handler).AddTo(d);

            _disposables.Add(d.Build());
        }
#endif
        
        public void SetVisibility(bool visibility)
        {
            Document.rootVisualElement.style.display = visibility ? DisplayStyle.Flex : DisplayStyle.None;

            if (visibility)
            {
                OnShow?.Invoke();
                return;
            }
            
            OnHide?.Invoke();
        }
        
        public void Toggle()
        {
            SetVisibility(Document.rootVisualElement.resolvedStyle.display != DisplayStyle.Flex);
        }

        public void Dispose()
        {
            _disposables?.ForEach(d => d.Dispose());
            _disposables = null;
        }
    }
}