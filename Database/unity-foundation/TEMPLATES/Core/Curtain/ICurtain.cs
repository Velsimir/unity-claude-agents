using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Core.Curtain
{
    public interface ICurtain
    {
        bool IsShown { get; }

        UniTask ShowAsync(CancellationToken cancellationToken = default);
        UniTask HideAsync(CancellationToken cancellationToken = default);
    }
}
