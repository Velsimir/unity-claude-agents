using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Platform
{
    public interface IAuthSdkService
    {
        bool IsSignedIn { get; }
        string UserId { get; }

        UniTask<bool> SignInAsync(CancellationToken cancellationToken = default);
    }
}
