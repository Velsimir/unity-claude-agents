using System.Threading;
using Cysharp.Threading.Tasks;
using {{ROOT}}.Platform;

namespace {{ROOT}}.Core.Boot.Operations
{
    public class InitializePlatformSdkOperation : ILoadingOperation
    {
        private readonly IPlatformSdk _platformSdk;

        public InitializePlatformSdkOperation(IPlatformSdk platformSdk)
        {
            _platformSdk = platformSdk;
        }

        public string Description => "Инициализация платформы";
        public float Weight => 3f;

        public UniTask ExecuteAsync(CancellationToken cancellationToken)
        {
            return _platformSdk.InitializeAsync(cancellationToken);
        }
    }
}
