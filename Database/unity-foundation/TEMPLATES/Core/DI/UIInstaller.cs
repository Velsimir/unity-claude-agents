using UnityEngine;
using Zenject;
using {{ROOT}}.Core.UI;

namespace {{ROOT}}.Core.DI
{
    public class UIInstaller : MonoInstaller
    {
        [SerializeField] private UILayerRoots _layerRoots;

        public override void InstallBindings()
        {
            Container.Bind<UILayerRoots>().FromInstance(_layerRoots).AsSingle();

            Container.BindServiceInterfacesOnly<PagesProvider>();
            Container.BindService<PageService>();
        }
    }
}
