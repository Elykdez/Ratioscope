using UnityEngine;
using UnityEngine.Events;

namespace Hypocycloid.UI
{
    public class UITimeCounter : MonoBehaviour
    {
        public float timeCounter;
        public bool isCD;

        [SerializeField]
        bool isPaused;

        public float Timer { get; private set; }

        [SerializeField]
        UnityEvent<string> onTimeUpdate;

        public UnityEvent<string> OnTimeUpdate
        {
            get
            {
                onTimeUpdate ??= new();
                return onTimeUpdate;
            }
        }

        void Start()
        {
            Timer = timeCounter;
        }

        void Update()
        {
            if (!isPaused)
            {
                if (isCD)
                    Timer -= Time.deltaTime;
                else
                    Timer += Time.deltaTime;

                OnTimeUpdate.Invoke(FormatTime(Timer));
            }
        }

        public void Pause(bool isPaused)
        {
            this.isPaused = isPaused;
        }

        public void ResetState()
        {
            Timer = timeCounter = 0;
            OnTimeUpdate.Invoke($"{0:00}:{0:00}");
            Pause(false);
        }

        public static string FormatTime(float timer)
        {
            int minutes = Mathf.FloorToInt(timer / 60f);
            int seconds = Mathf.FloorToInt(timer - minutes * 60);
            return $"{minutes:00}:{seconds:00}";
        }
    }
}
