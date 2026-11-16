using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Core.Boot
{
    public interface ILoadingErrorHandler
    {
        UniTask<bool> AskRetryAsync(string stepDescription, Exception exception, CancellationToken cancellationToken);
    }
}
