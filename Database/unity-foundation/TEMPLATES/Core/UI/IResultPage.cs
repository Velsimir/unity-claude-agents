using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Core.UI
{
    public interface IResultPage<TResult> : IPageBase
    {
        UniTask<TResult> ResultAsync(CancellationToken cancellationToken = default);
    }
}
