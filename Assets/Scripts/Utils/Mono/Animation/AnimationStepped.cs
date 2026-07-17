using UnityEngine;

namespace Hypocycloid.Utils
{
    /// <summary>
    /// Sampling animation (legacy or Animator) at discrete times without smooth interpolation.
    /// </summary>
    public class AnimationStepped : MonoBehaviour
    {
        public AnimationClip clip;

        [Range(0f, 2f), Tooltip("Time between samples (in seconds)")]
        public float interval = 0.25f;

        [Header("Animator")]
        public Animator animator;
        public string stateName;

        // Tracks time (legacy) or normalized time (Animator)
        float currentAnimTime = 0f;
        float timer = 0f;
        float stateLength = 1f;
        bool useAnimator;

        void OnEnable()
        {
            useAnimator = animator != null && !string.IsNullOrEmpty(stateName);
        }

        void Start()
        {
            if (useAnimator)
            {
                animator.speed = 0f; // Prevent automatic playback
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                stateLength = stateInfo.length;
            }
            else
            {
                if (clip == null)
                {
                    LogHelper.LogError("No AnimationClip assigned for legacy animation.", this);
                    enabled = false;
                    return;
                }

                if (TryGetComponent(out Animation legacyAnimation))
                {
                    // Add clip if not present and disable playback
                    if (!legacyAnimation.GetClip(clip.name))
                        legacyAnimation.AddClip(clip, clip.name);

                    legacyAnimation.playAutomatically = false;
                    legacyAnimation.enabled = true;
                }
            }
        }

        void Update()
        {
            AccumulateAndSample(Time.deltaTime);
        }

        // void FixedUpdate() { AccumulateAndSample(Time.fixedDeltaTime); }

        void AccumulateAndSample(float delta)
        {
            timer += delta;
            if (timer < interval)
                return;

            // Reset timer
            timer -= interval;

            if (useAnimator)
            {
                // Advance normalized time by interval fraction
                currentAnimTime += interval / stateLength;

                // Loop if exceeded 1.0
                if (currentAnimTime >= 1f)
                    currentAnimTime %= 1f;

                // Jump to new time and force evaluation
                animator.Play(stateName, -1, currentAnimTime);
                animator.Update(0f); // Immediately apply pose
            }
            else
            {
                // Advance legacy animation time by interval
                currentAnimTime += interval;

                // Loop if exceeded clip length
                if (currentAnimTime >= clip.length)
                    currentAnimTime %= clip.length;

                // Sample the pose (on this gameObject)
                clip.SampleAnimation(gameObject, currentAnimTime);
            }
        }
    }
}
