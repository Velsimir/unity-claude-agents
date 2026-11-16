using MessagePipe;
using Zenject;
using {{ROOT}}.Core.Clock;
using {{ROOT}}.Core.Factory;
using {{ROOT}}.Core.Pause;
using {{ROOT}}.Core.Pool;
using {{ROOT}}.Core.Save;
using {{ROOT}}.Core.Scenes;
using {{ROOT}}.Core.Timers;

namespace {{ROOT}}.Core.DI
{
    public class CoreInstaller : Installer<CoreInstaller>
    {
        private const int DAY_ROLLOVER_UTC_OFFSET_MINUTES = 0;

        public override void InstallBindings()
        {
            InstallMessagePipe();
            InstallFactory();
            InstallScenes();
            InstallPause();
            InstallClock();
            InstallSave();
            InstallTimers();
        }

        private void InstallClock()
        {
            Container.BindInstance(new DayRolloverSettings(DAY_ROLLOVER_UTC_OFFSET_MINUTES));
            Container.BindServiceInterfacesOnly<GameClock>();
            Container.BindServiceInterfacesOnly<DayRolloverService>();
        }

        private void InstallTimers()
        {
            Container.BindServiceInterfacesOnly<TimerService>();
        }

        private void InstallPause()
        {
            Container.BindServiceInterfacesOnly<PauseService>();
        }

        private void InstallMessagePipe()
        {
            MessagePipeOptions options = Container.BindMessagePipe();
            Container.InstallAllMessageBrokers(options);
        }

        private void InstallFactory()
        {
            Container.Bind<GameFactory>().AsSingle();
            Container.BindInterfacesAndSelfTo<ObjectPoolService>().AsSingle();
        }

        private void InstallScenes()
        {
            Container.BindServiceInterfacesOnly<SceneLoaderService>();
        }

        private void InstallSave()
        {
            InstallSaveCodec();

            Container.Bind<ISaveStorage>()
                .WithId(SaveStorageId.LOCAL)
                .To<LocalFileSaveStorage>()
                .AsCached();

            Container.Bind<ISaveStorage>()
                .WithId(SaveStorageId.CLOUD)
                .To<CloudSaveStorage>()
                .AsCached();

            Container.BindService<SaveService>();
        }

        private void InstallSaveCodec()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            const bool isEncrypting = false;
#else
            const bool isEncrypting = true;
#endif
            Container.Bind<ISaveCodec>().To<SaveCodec>().AsSingle().WithArguments(isEncrypting);
        }
    }
}
