using R3;

namespace {{ROOT}}.Core.Common
{
    public interface IApplicationLifecycle
    {
        Observable<bool> Paused { get; }

        Observable<bool> FocusChanged { get; }

        Observable<Unit> Quitting { get; }
    }
}
