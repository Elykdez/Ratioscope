using UnityEngine;

namespace Hypocycloid.Utils
{
    // 对象池物体
    // 尽量继承或引用这个类，不要直接修改它。
    public class ObjectPoolItem : MonoBehaviour
    {
        // Assigned by code
        [field: SerializeField]
        public ObjectPool Pool { get; set; }

        protected void OnDisable()
        {
            if (Pool != null)
            {
                Pool.Return(gameObject);
            }
        }
    }
}
