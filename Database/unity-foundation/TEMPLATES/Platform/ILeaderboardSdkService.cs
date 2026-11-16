using System.Threading;
using Cysharp.Threading.Tasks;

namespace {{ROOT}}.Platform
{
    public interface ILeaderboardSdkService
    {
        UniTask SubmitScoreAsync(string boardId, long score, CancellationToken cancellationToken = default);
        UniTask ShowAsync(string boardId, CancellationToken cancellationToken = default);
    }
}
