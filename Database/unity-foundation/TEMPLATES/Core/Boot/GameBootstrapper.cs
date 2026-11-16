using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Zenject;
using {{ROOT}}.Core.Boot.Operations;
using {{ROOT}}.Core.Common;
using {{ROOT}}.Core.Curtain;
using {{ROOT}}.Core.Save;
using {{ROOT}}.Core.Scenes;
using {{ROOT}}.Platform;

namespace {{ROOT}}.Core.Boot
{
    public class GameBootstrapper : IInitializable, IDisposable
    {
        private readonly ILoadingService _loadingService;
        private readonly ISceneLoader _sceneLoader;
        private readonly ISaveService _saveService;
        private readonly IPlatformSdk _platformSdk;
        private readonly ICurtain _curtain;

        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public GameBootstrapper(
            ILoadingService loadingService,
            ISceneLoader sceneLoader,
            ISaveService saveService,
            IPlatformSdk platformSdk,
            ICurtain curtain)
        {
            _loadingService = loadingService;
            _sceneLoader = sceneLoader;
            _saveService = saveService;
            _platformSdk = platformSdk;
            _curtain = curtain;
        }

        public void Initialize()
        {
            RunAsync(_cancellationTokenSource.Token).Forget();
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }

        private async UniTaskVoid RunAsync(CancellationToken cancellationToken)
        {
            await _curtain.ShowAsync(cancellationToken);

            ILoadingOperation[] operations =
            {
                new InitializePlatformSdkOperation(_platformSdk),
                new LoadSaveOperation(_saveService),
                new RestoreProgressOperation(_saveService),
                new LoadSceneOperation(_sceneLoader, ScenesName.Game),
            };

            bool succeeded = await _loadingService.BeginLoadingAsync(operations, cancellationToken);

            if (succeeded == false)
            {
                GameDebug.LogError("[Bootstrap] Загрузка не завершена, шторку не убираем.");
                return;
            }

            _sceneLoader.SetActive(ScenesName.Game);

            await _curtain.HideAsync(cancellationToken);

            _sceneLoader.UnloadAsync(ScenesName.Boot).Forget();
        }
    }
}
