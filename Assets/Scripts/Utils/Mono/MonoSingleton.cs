using System;
using UnityEngine;

namespace Hypocycloid.Utils
{
    public abstract class MonoSingleton<T> : MonoBehaviour
        where T : MonoSingleton<T>
    {
        static bool _isInitialized;

        static T ins = null;
        public static T Ins
        {
            get
            {
                // Instance requiered for the first time, we look for it
                if (ins == null && !_isInitialized)
                {
                    ins = FindFirstObjectByType(typeof(T)) as T;

                    // Object not found, we create a temporary one
                    if (ins == null)
                    {
                        LogHelper.Log($"No instance of {typeof(T)}, a temporary one is created.");
                        ins = new GameObject(
                            "_" + typeof(T).ToString(),
                            typeof(T)
                        ).GetComponent<T>();

                        // Problem during the creation, this should not happen
                        _isInitialized = true;
                        ins.Init();
                        if (ins == null && Application.isPlaying)
                        {
                            LogHelper.LogWarning($"Problem during the creation of {typeof(T)}.");
                        }
                    }
                }
                return ins;
            }
        }

        // If no other monobehaviour request the instance in an awake function
        // executing before this one, no need to search the object.
        void Awake()
        {
            if (ins == null)
            {
                ins = this as T;
            }
            else if (ins != this)
            {
                LogHelper.Log(
                    "Another instance of " + GetType() + " is already exist! Destroying self..."
                );
                DestroyImmediate(gameObject);
                return;
            }
            if (!_isInitialized)
            {
                DontDestroyOnLoad(gameObject);
                _isInitialized = true;
                ins.Init();
            }
        }

        /// <summary>
        /// This function is called when the instance is used the first time
        /// Put all the initializations you need here, as you would do in Awake
        /// </summary>
        public virtual void Init() { }

        protected virtual void OnDestroy()
        {
            if (ins == this)
                ins = null;
        }

        /// Make sure the instance isn't referenced anymore when the user quit, just in case.
        protected virtual void OnApplicationQuit()
        {
            // And then destroy the game object if it's not needed anymore
            if (gameObject != null)
                Destroy(gameObject);
        }
    }
}
