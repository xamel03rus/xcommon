using UnityEditor;
using UnityEngine;

namespace XCommon.Abstracts
{
    public abstract class AbstractResource : ScriptableObject
    {
#if UNITY_EDITOR
        protected void OnValidate()
        {
            EditorUtility.SetDirty(this);
            
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.SaveAssets();
            };
        }
#endif
    }
}