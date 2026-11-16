using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Platform
{
    public interface IPlatformSdk
    {
        IAdsSdkService Ads { get; }
        IPurchaseSdkService Purchases { get; }
        IAnalyticsSdkService Analytics { get; }
        IAuthSdkService Auth { get; }
        ICloudSaveSdkService CloudSave { get; }
        ILeaderboardSdkService Leaderboards { get; }
        IGameplaySdkService Gameplay { get; }

        bool IsInitialized { get; }

        bool Supports(PlatformCapability capability);

        UniTask InitializeAsync(CancellationToken cancellationToken = default);
    }
}
