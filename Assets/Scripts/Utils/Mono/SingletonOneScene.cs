using UnityEngine;

namespace Hypocycloid.Utils
{
    public abstract class SingletonOneScene<T> : MonoBehaviour
        where T : SingletonOneScene<T>
    {
        static T ins;
        static bool _isInitialized;

        public static T Ins
        {
            get
            {
                if (ins == null && !_isInitialized)
                {
                    ins = FindFirstObjectByType<T>();
                    _isInitialized = true;

                    if (ins == null)
                    {
                        LogHelper.LogWarning($"Problem during the finding of {typeof(T)}.");
                    }
                }
                return ins;
            }
        }

        void Awake()
        {
            if (ins != null && ins != this)
            {
                LogHelper.Log(
                    "Another instance of " + GetType() + " is already exist! Destroying self..."
                );
                DestroyImmediate(gameObject);
                return;
            }

            ins = this as T;

            if (!_isInitialized)
            {
                _isInitialized = true;
                ins.Init();
            }
        }

        public virtual void Init() { }

        protected virtual void OnDestroy()
        {
            if (ins == this)
                ins = null;
        }

        protected virtual void OnApplicationQuit()
        {
            // And then destroy the game object if it's not needed anymore
            if (gameObject != null)
                Destroy(gameObject);
        }
    }
}
