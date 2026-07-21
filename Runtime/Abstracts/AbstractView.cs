using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Xamel.Common.Abstracts
{
    public abstract class AbstractView : MonoBehaviour
    {
        protected UIDocument Document;
        
        protected event Action OnShow;
        
        protected event Action OnHide;

        protected virtual void Awake()
        {
            Document = GetComponent<UIDocument>();
            Document.rootVisualElement.style.display = DisplayStyle.None;
        }

        /// <summary>Видна ли вьюха (по style.display её корня).</summary>
        public bool IsVisible => Document != null && Document.rootVisualElement.style.display == DisplayStyle.Flex;

        public void SetVisibility(bool visibility)
        {
            if (visibility == IsVisible) return; // состояние не меняется — не дёргаем OnShow/OnHide повторно

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
    }
}