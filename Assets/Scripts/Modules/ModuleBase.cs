using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hypocycloid.Ratioscope
{
    public abstract class ModuleBase : MonoBehaviour, IModule
    {
        readonly Dictionary<int, int> loadingRequests = new();
        readonly HashSet<int> occupiedRequests = new();
        int nextStateToken = 1;

        public ModuleState State { get; private set; } = ModuleState.Ready;
        public event Action<ModuleState> StateChanged;

        protected int BeginModuleLoading()
        {
            int token = AllocateStateToken();
            loadingRequests.Add(token, LoadingRegistry.BeginLoadingRequest());
            RefreshState();
            return token;
        }

        protected void EndModuleLoading(int token)
        {
            if (!loadingRequests.Remove(token, out int loadingToken))
                return;

            LoadingRegistry.EndLoadingRequest(loadingToken);
            RefreshState();
        }

        protected int BeginModuleWork()
        {
            int token = AllocateStateToken();
            occupiedRequests.Add(token);
            RefreshState();
            return token;
        }

        protected void EndModuleWork(int token)
        {
            if (occupiedRequests.Remove(token))
                RefreshState();
        }

        protected virtual void OnDisable()
        {
            ClearModuleRequests();
        }

        protected virtual void OnDestroy()
        {
            ClearModuleRequests();
        }

        void RefreshState()
        {
            ModuleState next =
                loadingRequests.Count > 0 ? ModuleState.Loading
                : occupiedRequests.Count > 0 ? ModuleState.Occupied
                : ModuleState.Ready;
            if (State == next)
                return;

            State = next;
            StateChanged?.Invoke(State);
        }

        void ClearModuleRequests()
        {
            foreach (int loadingToken in loadingRequests.Values)
                LoadingRegistry.EndLoadingRequest(loadingToken);

            loadingRequests.Clear();
            occupiedRequests.Clear();
            RefreshState();
        }

        int AllocateStateToken()
        {
            if (nextStateToken == int.MaxValue)
                nextStateToken = 1;

            while (
                loadingRequests.ContainsKey(nextStateToken)
                || occupiedRequests.Contains(nextStateToken)
            )
            {
                nextStateToken++;
                if (nextStateToken == int.MaxValue)
                    nextStateToken = 1;
            }

            return nextStateToken++;
        }
    }
}
