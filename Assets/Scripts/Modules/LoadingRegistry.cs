using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hypocycloid.Ratioscope
{
    public interface ILoadingEffectReceiver
    {
        int BeginLoading();
        void EndLoading(int token);
    }

    [DisallowMultipleComponent]
    public sealed class LoadingRegistry : MonoBehaviour
    {
        static readonly List<ILoadingEffectReceiver> Receivers = new();
        static readonly HashSet<int> ActiveRequests = new();
        static int NextRequestToken { get; set; } = 1;
        static ILoadingEffectReceiver ActiveReceiver { get; set; }
        static int ActiveReceiverToken { get; set; }

        int eventRequestDepth;
        int eventRequestToken;

        public void BeginLoadingEffect()
        {
            eventRequestDepth++;
            if (eventRequestToken == 0)
                eventRequestToken = BeginLoadingRequest();
        }

        public void EndLoadingEffect()
        {
            if (eventRequestDepth <= 0)
                return;

            eventRequestDepth--;
            if (eventRequestDepth == 0)
                EndEventRequest();
        }

        void OnDisable()
        {
            ClearEventRequests();
        }

        void OnDestroy()
        {
            ClearEventRequests();
        }

        public static void Register(ILoadingEffectReceiver receiver)
        {
            if (!IsAlive(receiver) || Receivers.Contains(receiver))
                return;

            Receivers.Add(receiver);
            EnsureEffectToken();
        }

        public static void Unregister(ILoadingEffectReceiver receiver)
        {
            for (int i = Receivers.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(Receivers[i], receiver))
                    Receivers.RemoveAt(i);
            }

            if (ReferenceEquals(ActiveReceiver, receiver))
                EndEffectToken();

            EnsureEffectToken();
        }

        public static int BeginLoadingRequest()
        {
            int token = AllocateRequestToken();
            ActiveRequests.Add(token);
            EnsureEffectToken();
            return token;
        }

        public static void EndLoadingRequest(int token)
        {
            if (token <= 0 || !ActiveRequests.Remove(token))
                return;

            if (ActiveRequests.Count == 0)
                EndEffectToken();
        }

        void ClearEventRequests()
        {
            eventRequestDepth = 0;
            EndEventRequest();
        }

        void EndEventRequest()
        {
            if (eventRequestToken == 0)
                return;

            EndLoadingRequest(eventRequestToken);
            eventRequestToken = 0;
        }

        static int AllocateRequestToken()
        {
            if (NextRequestToken == int.MaxValue)
                NextRequestToken = 1;

            while (ActiveRequests.Contains(NextRequestToken))
            {
                NextRequestToken++;
                if (NextRequestToken == int.MaxValue)
                    NextRequestToken = 1;
            }

            return NextRequestToken++;
        }

        static void EnsureEffectToken()
        {
            if (ActiveRequests.Count == 0)
                return;

            if (ActiveReceiverToken > 0 && IsAlive(ActiveReceiver))
                return;

            EndEffectToken();
            ActiveReceiver = ResolveReceiver();
            if (ActiveReceiver != null)
                ActiveReceiverToken = ActiveReceiver.BeginLoading();
        }

        static void EndEffectToken()
        {
            if (ActiveReceiverToken > 0 && IsAlive(ActiveReceiver))
                ActiveReceiver.EndLoading(ActiveReceiverToken);

            ActiveReceiverToken = 0;
            ActiveReceiver = null;
        }

        static ILoadingEffectReceiver ResolveReceiver()
        {
            for (int i = Receivers.Count - 1; i >= 0; i--)
            {
                ILoadingEffectReceiver receiver = Receivers[i];
                if (!IsAlive(receiver))
                {
                    Receivers.RemoveAt(i);
                    continue;
                }

                return receiver;
            }

            return null;
        }

        static bool IsAlive(ILoadingEffectReceiver receiver)
        {
            if (receiver == null)
                return false;

            if (receiver is Object unityObject)
                return unityObject != null;

            return true;
        }
    }
}
