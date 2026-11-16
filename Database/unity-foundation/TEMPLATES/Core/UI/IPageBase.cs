using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace {{ROOT}}.Core.UI
{
    public interface IPageBase
    {
        PageId PageId { get; }
        UILayer Layer { get; }
        ReadOnlyReactiveProperty<PageState> State { get; }

        UniTask HideAsync(CancellationToken cancellationToken = default);
    }
}
