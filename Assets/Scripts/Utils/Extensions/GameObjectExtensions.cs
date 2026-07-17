using UnityEngine;

namespace Hypocycloid.Utils
{
    public static class GameObjectExtensions
    {
        /// <summary>
        /// 在 GameObject 上查找指定组件，如果找不到则在其子对象中查找。找不到则抛出异常。
        /// </summary>
        public static T TryGetComponentOrChild<T>(this Component context)
            where T : Component
        {
            if (context.TryGetComponent(out T found))
                return found;

            found = context.GetComponentInChildren<T>(includeInactive: true);
            if (found != null)
                return found;

            throw new MissingComponentException(
                $"{context.GetType().Name} requires a {typeof(T).Name}, but none was found on self or children."
            );
        }
    }
}
