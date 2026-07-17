using System;
using System.Collections;
using UnityEngine;

// using Object = UnityEngine.Object;

namespace Hypocycloid.Utils
{
    public static class CoroutineHelper
    {
        public class TokenSource
        {
            public bool IsSuccess = false;
        }

        public static WaitForSeconds waitHalf = new(.5f);
        public static WaitForSeconds wait1s = new(1f);
        public static WaitForSeconds wait2s = new(2f);
        public static WaitForSeconds wait3s = new(3f);
        public static WaitForSeconds wait4s = new(4f);
        public static WaitForSeconds wait5s = new(5f);

        public static Coroutine Run(IEnumerator enumerator, MonoBehaviour hook)
        {
            Coroutine coroutine = null;
            Dispatcher.Invoke(() =>
            {
                coroutine = hook.StartCoroutine(enumerator);
            });

            return coroutine;
        }

        public static void RunAfterOneFrame(MonoBehaviour hook, Action action) =>
            RunAfterFrameDelay(hook, 1, action);

        public static void RunAfterFrameDelay(MonoBehaviour hook, int framesToWait, Action action)
        {
            Run(routine(), hook);
            IEnumerator routine()
            {
                for (int i = 0; i < framesToWait; i++)
                    yield return new WaitForEndOfFrame();

                action.Invoke();
            }
        }

        public static IEnumerator WaitForEndFrame(Action cb = null)
        {
            yield return new WaitForEndOfFrame();
            cb?.Invoke();
        }

        /// <summary>
        /// 在下一帧执行
        /// </summary>
        public static Coroutine NextFrame(MonoBehaviour mono, Action action)
        {
            return mono.StartCoroutine(ExecuteNextFrame(action));
        }

        public static IEnumerator ExecuteNextFrame(Action action)
        {
            yield return null;
            action?.Invoke();
        }

        /// <summary>
        /// 在当前帧的最后执行（等待所有渲染和 UI 更新完成）
        /// </summary>
        public static Coroutine EndOfFrame(MonoBehaviour mono, Action action)
        {
            return mono.StartCoroutine(ExecuteAtEndOfFrame(action));
        }

        public static IEnumerator ExecuteAtEndOfFrame(Action action)
        {
            yield return new WaitForEndOfFrame(); // 确保在当前帧最后执行
            action?.Invoke();
        }

        /// <summary>
        /// 在指定秒数后执行
        /// </summary>
        public static Coroutine AfterSeconds(MonoBehaviour mono, float seconds, Action action)
        {
            return mono.StartCoroutine(ExecuteAfterSeconds(seconds, action));
        }

        static IEnumerator ExecuteAfterSeconds(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action?.Invoke();
        }

        /// <summary>
        /// 等待直到指定条件满足后执行
        /// </summary>
        public static Coroutine WaitUntil(MonoBehaviour mono, Func<bool> condition, Action action)
        {
            return mono.StartCoroutine(ExecuteWaitUntil(condition, action));
        }

        static IEnumerator ExecuteWaitUntil(Func<bool> condition, Action action)
        {
            yield return new WaitUntil(condition);
            action?.Invoke();
        }

        /// <summary>
        /// 等待直到指定条件不满足后执行
        /// </summary>
        public static Coroutine WaitWhile(MonoBehaviour mono, Func<bool> condition, Action action)
        {
            return mono.StartCoroutine(ExecuteWaitWhile(condition, action));
        }

        static IEnumerator ExecuteWaitWhile(Func<bool> condition, Action action)
        {
            yield return new WaitWhile(condition);
            action?.Invoke();
        }

        public static IEnumerator TryUntilSuccess(
            Func<TokenSource, IEnumerator> coroutineToTry,
            float baseInterval,
            float timeout,
            float expIncrement = 1.0f,
            TokenSource cancelToken = null
        )
        {
            float currentInterval = baseInterval;
            float maxInterval = Mathf.Min(baseInterval * 4, timeout);
            float elapsed = 0;
            cancelToken ??= new TokenSource();

            yield return null;

            while (elapsed < timeout)
            {
                // Wrap in another coroutine to capture success/failure
                yield return RunSafe(coroutineToTry(cancelToken));

                if (cancelToken.IsSuccess)
                    yield break;

                yield return new WaitForSeconds(currentInterval);

                if (cancelToken.IsSuccess)
                    yield break;

                currentInterval = Mathf.Min(currentInterval * expIncrement, maxInterval);
            }
        }

        public static IEnumerator RunSafe(IEnumerator coroutine, Action<Exception> onFail = null)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!coroutine.MoveNext())
                        break;

                    current = coroutine.Current;
                }
                catch (Exception e)
                {
                    LogHelper.LogWarning($"Coroutine execution failed: {e.Message}");
                    onFail.Invoke(e);
                    break;
                }

                yield return current;
            }
        }

        /// <summary>
        /// 批量执行
        /// </summary>
        public static IEnumerator RunChunked(
            int totalItems,
            int itemsPerFrame,
            Action<int, int> perChunkAction,
            Action onCompleteAction
        )
        {
            int processed = 0;
            while (processed < totalItems)
            {
                int start = processed;
                int end = Mathf.Min(start + itemsPerFrame, totalItems);
                processed = end;

                perChunkAction?.Invoke(start, end);
                yield return null; // Yield every chunk
            }

            onCompleteAction?.Invoke();
        }

        public class Debounce
        {
            Action callback = null;

            Coroutine corountine = null;

            public void Run(Action callback, float interval, MonoBehaviour mono)
            {
                this.callback = callback;

                ResetTime(mono);

                if (mono.isActiveAndEnabled)
                    corountine = mono.StartCoroutine(DebounceCorountine(interval));
                else
                    callback?.Invoke();
            }

            public void ResetTime(MonoBehaviour mono)
            {
                if (corountine != null)
                {
                    mono.StopCoroutine(corountine);
                }
            }

            IEnumerator DebounceCorountine(float time)
            {
                yield return new WaitForSeconds(time);
                callback?.Invoke();
            }
        }

        public class Throttle
        {
            float targetTime = 0;

            public bool IsThrottling() => targetTime > 0;

            public void Run(Action callback, float interval)
            {
                if (targetTime <= Time.time)
                {
                    targetTime = Time.time + interval;
                    callback?.Invoke();
                }
            }

            public void ResetTimer()
            {
                targetTime = 0;
            }
        }
    }

    public class MonoBehaviourHook : MonoBehaviour
    {
        public Action OnUpdate;

        void Update()
        {
            OnUpdate?.Invoke();
        }
    }
}
