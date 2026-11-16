using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Core.UI
{
    public interface IPage : IPageBase
    {
        UniTask ShowAsync(CancellationToken cancellationToken = default);
    }
}
