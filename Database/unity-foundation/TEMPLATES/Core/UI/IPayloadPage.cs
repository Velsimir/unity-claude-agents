using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Core.UI
{
    public interface IPayloadPage<in TPayload> : IPageBase
    {
        UniTask ShowAsync(TPayload payload, CancellationToken cancellationToken = default);
    }
}
