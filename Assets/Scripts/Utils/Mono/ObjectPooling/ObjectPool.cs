using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hypocycloid.Utils
{
    public interface IResetable
    {
        void OnReset();
    }

    /// <summary>
    /// ObjectPool.Ins.[public method name]
    /// </summary>
    public class ObjectPool : MonoBehaviour
    {
        [SerializeField]
        int poolingThreashold = 100;
        readonly Dictionary<string, Stack<GameObject>> cache = new();

        public GameObject Create(GameObject prototype, Transform trans = null)
        {
            string poolKey = prototype.name;

            if (!cache.TryGetValue(poolKey, out var typeStack))
            {
                typeStack = new Stack<GameObject>();
                cache.Add(poolKey, typeStack);
            }

            GameObject go;
            if (typeStack.Count > 0)
            {
                go = typeStack.Pop();
                go.SetActive(true);

                // Reset position and parent if needed
                if (trans != null)
                {
                    go.transform.SetParent(trans);
                    go.transform.localPosition = Vector3.zero;
                }

                // Call reset on IResetable components
                var resetables = go.GetComponents<IResetable>();
                foreach (var resetable in resetables)
                {
                    resetable.OnReset();
                }
            }
            else
            {
                go = Instantiate(prototype, trans);
                go.name = poolKey; // Ensure the pooled object has the same name as prototype
            }

            return go;
        }

        public void Return(GameObject obj)
        {
            if (obj == null)
                return;
            // Reset transform
            if (obj.activeInHierarchy)
                obj.SetActive(false);

            // Add to pool using the object's name
            if (!cache.TryGetValue(obj.name, out var typeStack))
            {
                typeStack = new Stack<GameObject>();
                cache.Add(obj.name, typeStack);
            }

            if (typeStack.Count < poolingThreashold)
            {
                typeStack.Push(obj);
                // Delay pooling to the next frame if called from OnDisable
                StartCoroutine(DelayedCollect(obj));
            }
            else
            {
                Destroy(obj);
            }
        }

        public void Release(GameObject prototype)
        {
            if (prototype == null)
                return;

            if (cache.TryGetValue(prototype.name, out var typeStack))
            {
                while (typeStack.Count > 0)
                {
                    var obj = typeStack.Pop();
                    if (obj != null)
                    {
                        Destroy(obj);
                    }
                }
                cache.Remove(prototype.name);
            }
        }

        IEnumerator DelayedCollect(GameObject obj)
        {
            yield return null;
            obj.transform.SetParent(gameObject.transform);
        }
    }
}
