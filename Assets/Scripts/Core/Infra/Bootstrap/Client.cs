using Core.Infra.Messaging;
using Core.Infra.Module;
using Core.Infra.Utils;
using Core.Services.Asset;
using Core.Services.Asset.Resource;
using Core.Services.Data;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Core.Infra.Bootstrap
{
    public class Client : MonoBehaviour
    {
        protected IBroadcaster Broadcaster { get; private set; }

        protected ModuleScope Modules { get; private set; }
        protected IAssetService Assets { get; private set; }
        protected IRecordService Records { get; private set; }

        private void Awake()
        {
            Broadcaster = new Broadcaster();
            Modules = new ModuleScope(Broadcaster);

            CreateServices();
            Boot().FireAndForget();
        }

        private void OnDestroy()
        {
            try
            {
                Modules.Dispose();
            }
            finally
            {
                DisposeServices();
            }
        }

        private async UniTask Boot()
        {
            var cancellationToken = destroyCancellationToken;
            await InitializeServices();

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            CreateModules();

            Modules.Start();
            await StartGame();
        }

        protected virtual void CreateServices()
        {
            Assets = CreateAssetService();
            Records = new RecordService(Assets);
        }

        protected virtual IAssetService CreateAssetService() => new ResourcesAssetService();

        protected virtual void DisposeServices()
        {
        }

        protected virtual UniTask InitializeServices()
        {
            return Records.CreateAll();
        }

        protected virtual void CreateModules()
        {
        }

        protected T AddModule<T>(T module) where T : class, IModule
        {
            return Modules.Add(module);
        }

        protected virtual UniTask StartGame()
        {
            return UniTask.CompletedTask;
        }
    }
}
