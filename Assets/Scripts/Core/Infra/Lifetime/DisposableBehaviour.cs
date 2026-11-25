using System;
using Cysharp.Threading.Tasks;
using Core.Resources;
using UnityEngine;

namespace Core.Infra.Lifetime
{
    public class DisposableBehaviour : MonoBehaviour
    {
        protected ResourceScope Disposables { get; } = new();

        protected virtual void Awake()
        {
            OnInit().Forget();
        }

        protected virtual UniTask OnInit() => UniTask.CompletedTask;

        protected virtual void OnDestroy()
        {
            Disposables.Dispose();
        }
    }

    public class DisposableBehaviour<T> : MonoBehaviour
    {
        private AsyncInitialization _initialization = new();
        protected ResourceScope Disposables => _initialization.Resources;
        protected T ActiveModel { get; private set; }
        protected System.Threading.CancellationToken InitializationToken => _initialization.Token;
        public bool IsInitialized => _initialization.IsReady;

        public async UniTask<IDisposable> Init(T model, System.Threading.CancellationToken token = default)
        {
            using var linked =
                System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token, destroyCancellationToken);
            linked.Token.ThrowIfCancellationRequested();
            if (ShouldRenewInitialization())
            {
                _initialization = new AsyncInitialization();
            }

            return await _initialization.Run(async () =>
            {
                ActiveModel = model;
                await OnInit();
                InitializationToken.ThrowIfCancellationRequested();
            }, linked.Token, OnDeinit);
        }

        protected virtual UniTask OnInit() => UniTask.CompletedTask;

        protected virtual void OnDeinit()
        {
        }

        protected virtual void OnDestroy() => _initialization.Dispose();

        private bool ShouldRenewInitialization()
        {
            return _initialization.IsReleased && !_initialization.IsRunning;
        }
    }
}
