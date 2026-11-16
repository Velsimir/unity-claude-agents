# Boot и сцены

## 1. Сцены

| Сцена | Роль |
|---|---|
| `Boot` | Первая в Build Settings. `ProjectContext`, `BootSceneInstaller`, экран загрузки |
| `Game` | Загружается Single. В ней `UIInstaller`, канвасы слоёв, `GameplayInstaller` |

Внутри `Game` уровни грузятся **Additive**, чтобы HUD не перезагружался.
Правило: **Single — между режимами, Additive — внутри режима.**

Исключение: **`Game` грузится из `Boot` аддитивно, а не Single.** Single уничтожил бы `Boot` вместе
с `GameBootstrapper` посреди его `await` — токен отменился бы, остаток метода не выполнился. Порядок:
грузим `Game` аддитивно, делаем активной, убираем шторку и только потом выгружаем `Boot` —
последней строкой, без `await`, потому что выгрузка уничтожает сам бутстрап.

## 1a. Эталонный состав сцен и префабов

База фундамента: агент собирает ровно её и ничего не добавляет от себя. Дальше состав меняется
под конкретную игру.

Все `Canvas`: `Screen Space - Overlay`, `CanvasScaler` в режиме `Scale With Screen Size`,
референс `1080x1920`, `Match` = `1` (подгонка по высоте — портретная мобильная игра).
`sortingOrder` не выдумывается: у слоёв UI он равен значению элемента `UILayer`.

### Boot

```
Main Camera            Camera, UniversalAdditionalCameraData
LoadingScreenCanvas    Canvas(order 0), CanvasScaler, GraphicRaycaster, LoadingScreenView
├── Background         Image
├── StepLabel          TextMeshProUGUI — текущий шаг конвейера
├── ProgressBar        Slider (interactable = false)
│   └── Fill Area/Fill Image
└── ErrorPanel         Image, объект выключен
    ├── ErrorLabel     TextMeshProUGUI
    ├── RetryButton    Button
    └── QuitButton     Button
SceneContext           SceneContext, BootSceneInstaller
```

Ни `EventSystem`, ни `AudioListener` в `Boot` нет: EventSystem приходит из `ProjectContext` и
обслуживает кнопки панели ошибки; звука в `Boot` нет. Все шесть полей `LoadingScreenView`
проставлены — пустая ссылка означает ошибку на старте. `BootSceneInstaller._loadingScreen`
ссылается на `LoadingScreenView`: без неё `LoadingScreenPresenter` падает в `OnInitialize` с
`ArgumentException`.

### Game

```
Main Camera            Camera, AudioListener — единственный слушатель в проекте
UI                     UILayerRoots
├── Canvas_Hud         order 0
├── Canvas_Window      order 100
├── Canvas_Popup       order 200
├── Canvas_Overlay     order 300
├── Canvas_Loading     order 400
├── Canvas_System      order 500
└── Canvas_InputBlocker order 1000
    └── InputBlocker   Image (alpha 0), CanvasGroup, InputBlocker
SceneContext           SceneContext, UIInstaller
```

`UILayerRoots` держит ссылки на все шесть корней слоёв и на блокер ввода, `UIInstaller._layerRoots` —
на `UILayerRoots`. Блокер на собственном канвасе выше всех слоёв: перекрывает любую страницу,
включая системную.

### ProjectContext (`Assets/G/Resources/ProjectContext.prefab`)

```
ProjectContext         ProjectContext, ProjectInstaller
└── EventSystem        EventSystem, InputSystemUIInputModule
```

`ProjectInstaller` держит ссылки на префаб шторки и `PlatformSdkConfig`,
в `ScriptableObjectInstallers` — `ConfigsInstaller`.

### Curtain (`Assets/G/Prefabs/Curtain.prefab`)

```
Curtain                Canvas(order 32000), CanvasScaler, GraphicRaycaster, CanvasGroup, CurtainView
└── Background         Image, чёрный
```

`order 32000` — выше всего, включая блокер ввода: шторка перекрывает любой UI.

### Стартовая страница (`Assets/G/Prefabs/UI/HudPage.prefab`)

```
HudPage                RectTransform (растянут), HudPage : Page, слой Hud
└── StubLabel          TextMeshProUGUI
```

Заглушка, доказывающая, что конвейер дошёл до конца. В `PagesConfig` прописана и как стартовая,
и первым элементом списка страниц.

## 2. ScenesName

Имена сцен — enum, не строки:

```csharp
public enum ScenesName
{
    Boot,
    Game,
}
```

`ScenesName.ToString()` **не использовать**: рефлексия и аллокация на каждый вызов, и ничто не
сверяет имя элемента с ассетом сцены. Вместо этого `SceneNameMap` — статический словарь. Обе
стороны сверяет агент при сборке фундамента:

- каждому элементу enum соответствует сцена в Build Settings;
- каждой сцене в Build Settings соответствует элемент enum.

Опечатка ловится в редакторе, а не в билде на устройстве.

## 3. ISceneLoader

```csharp
UniTask LoadAdditiveAsync(ScenesName scene, Action<DiContainer> bindings = null,
                          IProgress<float> progress = null, CancellationToken cancellationToken = default);
UniTask LoadSingleAsync(ScenesName scene, Action<DiContainer> bindings = null,
                        IProgress<float> progress = null, CancellationToken cancellationToken = default);
UniTask UnloadAsync(ScenesName scene, CancellationToken cancellationToken = default);
void SetActive(ScenesName scene);
```

Реализация — поверх `ZenjectSceneLoader`.

**`bindings`** — главный приём: параметры идут в `SceneContext` новой сцены через контейнер, а не
через статику или `PlayerPrefs`:

```csharp
await _sceneLoader.LoadAdditiveAsync(ScenesName.Game, container =>
    container.BindInstance(levelConfig));
```

**Нормализация прогресса.** `AsyncOperation.progress` при аддитивной загрузке доходит только до
`0.9` и стоит там до активации сцены:

```csharp
float normalized = Mathf.Clamp01(operation.progress / 0.9f);
```
и `1f` не репортим, пока не `isDone`, иначе полоска замирает на «100%».

**Выгрузка несуществующей сцены** — проверять `SceneManager.GetSceneByName(name).isLoaded`, а не
глушить исключение пустым `catch`.

## 4. Конвейер загрузки

Порядок инициализации — самое хрупкое место проекта, поэтому он задаётся **явным массивом в одном
месте**, а не числовыми приоритетами по файлам:

```csharp
ILoadingOperation[] operations =
{
    new InitializePlatformSdkOperation(_platformSdk),
    new LoadSaveOperation(_saveService),
    new RestoreProgressOperation(_saveRegistry),
    new LoadSceneOperation(_sceneLoader, ScenesName.Game),
};

await _loadingService.BeginLoadingAsync(operations, cancellationToken);
```

Контракт шага:

```csharp
public interface ILoadingOperation
{
    string Description { get; }   // "Инициализация SDK" — в лоадер и в лог
    float Weight { get; }         // вес шага в общем прогрессе, по умолчанию 1
    UniTask ExecuteAsync(CancellationToken cancellationToken);
}
```

`LoadingService` обязан:

- считать прогресс по накопленным весам, а не по магическим константам;
- накладывать **таймаут на каждый шаг** — зависший SDK не держит игру на чёрном экране навсегда;
- ловить исключение шага и показывать **экран ошибки с Retry**, а не молча вешаться;
- писать `Description` каждого шага в лог — по нему видно, где встало.

## 5. Curtain

Префаб с `CanvasGroup` и `Animator`, инстанцируется в `ProjectInstaller` с `DontDestroyOnLoad`.
Наружу — `ICurtain` с `ShowAsync()` / `HideAsync()`.

Ловушка: если `ShowAsync` ждёт анимацию через `UniTaskCompletionSource`, взводимый Animation Event,
потерянное при правке анимации событие вешает игру на чёрном экране **без единой ошибки в логе**.
Поэтому ожидание всегда с таймаутом и `LogError` по его истечении.

Затухание — через `Time.unscaledDeltaTime`: на паузе `Time.timeScale == 0`, обычный `deltaTime`
останавливает шторку.

## 6. Что настроить в проекте

- Build Settings: `Boot` под индексом 0, `Game` под 1.
- Player Settings → Scripting Define Symbols: **`UNITASK_DOTWEEN_SUPPORT`** — без него нет
  `tween.ToUniTask()`, а анимации окон надо ждать.
- Project Settings → Editor → Enter Play Mode Options включены, Reload Domain выключен
  (см. `ARCHITECTURE.md` §7).
- `Application.targetFrameRate` и `Screen.sleepTimeout` — в `ProjectInstaller`.
- `UniTaskScheduler.UnobservedTaskException` — в лог, иначе исключения внутри `.Forget()`
  пропадают бесследно.
