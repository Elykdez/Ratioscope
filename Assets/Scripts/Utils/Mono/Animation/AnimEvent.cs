using UnityEngine;
using UnityEngine.Events;

namespace Hypocycloid.Utils
{
    public class AnimEvent : MonoBehaviour
    {
        [SerializeField]
        UnityEvent<AnimationEvent, GameObject> onEvent;
        public UnityEvent<AnimationEvent, GameObject> OnEvent
        {
            get
            {
                onEvent ??= new UnityEvent<AnimationEvent, GameObject>();
                return onEvent;
            }
        }

        [SerializeField]
        UnityEvent<GameObject> onEndEvent;
        public UnityEvent<GameObject> OnEndEvent
        {
            get
            {
                onEndEvent ??= new UnityEvent<GameObject>();
                return onEndEvent;
            }
        }

        public void Event(AnimationEvent animEvent)
        {
            OnEvent.Invoke(animEvent, gameObject);
        }

        public void EndEvent()
        {
            OnEndEvent.Invoke(gameObject);
        }
    }
}
