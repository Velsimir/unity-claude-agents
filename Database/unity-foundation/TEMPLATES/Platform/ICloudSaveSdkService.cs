using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Platform
{
    public interface ICloudSaveSdkService
    {
        bool IsAvailable { get; }

        UniTask<string> ReadAsync(CancellationToken cancellationToken = default);
        UniTask WriteAsync(string payload, CancellationToken cancellationToken = default);
    }
}
