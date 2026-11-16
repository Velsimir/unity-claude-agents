# Архитектура фундамента Unity-проекта

База знаний агента `unity-foundation`: правила и границы. Эталонный код — `TEMPLATES/`, стиль — `CODE-STYLE.md`.

## 1. Слои

```
Platform   IPlatformSdk и адаптеры вендоров. Ничего не знает об игре.
Core       DI-каркас, Boot, Scenes, Save, UI-инфраструктура, Tick, Clock, Timers, Factory, Pool.
Meta       Кошелёк, инвентарь, магазин, квесты, прогресс. Знает Core, не знает Gameplay.
Gameplay   Правила конкретной игры. Знает всё ниже. Агент оставляет заготовку.
```

**Слой знает только то, что ниже него.** Компилятор это не форсит (asmdef не используем) —
проверяет агент поиском по коду при сборке фундамента и человек на ревью.

## 2. Три вида классов

| Вид | Базовый класс | Что это |
|---|---|---|
| Service | `Service` | Модель и логика. Не знает ни о View, ни о Presenter |
| Presenter | `Presenter` | Склейка сервиса и View. Обычный C#-класс, биндится в инсталлере |
| View | `View` | `MonoBehaviour`. Только `[SerializeField]`, сеттеры и `Observable` наружу |

**Во View не бывает `[Inject]`.** Ни одного. Данные — только через публичные методы, которые
дёргает Presenter. `[Inject]` в наследнике `View` — нарушение, ищется поиском по коду.
Причина: View переписывается чаще всего; без знания о сервисах её можно выкинуть, не трогая логику.

## 3. Состояние против событий

Без исключений:

- **Есть текущее значение → R3.** `ReactiveProperty<T>` приватно, наружу
  `ReadOnlyReactiveProperty<T>`. Баланс кошелька, текущий уровень, состояние страницы.
- **Нет текущего значения → MessagePipe.** `IPublisher<T>` / `ISubscriber<T>`.
  «Враг умер», «уровень пройден», «покупка совершена».

`Subject` из R3 требует владельца, раздающего ссылку; MessagePipe развязывает издателя и
подписчика полностью, связь собирает контейнер.

### Правила R3

- **`.AddTo(...)` обязателен всегда.** `Disposables` в сервисе и презентере,
  `this` в `MonoBehaviour`, `HiddenDisposables` на странице.
- **`ToReadOnlyReactiveProperty()` не вызывать в геттере** — каждый вызов создаёт неосвобождаемый
  объект (утечка). Создать один раз в поле.
- **`Observable.EveryUpdate()` не использовать напрямую.** Есть `ITickService` с
  `Update` / `FixedUpdate` / `SecondTick`. Таймерам — `SecondTick`, а не каждый кадр.

### Правила MessagePipe

Сообщения живут в статическом классе рядом с публикующей системой:

```csharp
public static class WalletMessages
{
    public sealed record BalanceChanged(CurrencyId Currency, long Delta, long Total);

    public static void Install(DiContainer container, MessagePipeOptions options)
        => container.BindMessageBroker<BalanceChanged>(options);
}
```

`CoreInstaller` находит все такие `Install(DiContainer, MessagePipeOptions)` рефлексией и
вызывает сам — новый файл сообщений не требует правки инсталлера.

## 4. Инсталлеры

```
ProjectContext (Assets/G/Resources/ProjectContext.prefab)
├── EventSystem        дочерний объект: EventSystem + InputSystemUIInputModule,
│                      единственный в проекте, живёт в DontDestroyOnLoad
├── ConfigsInstaller   ScriptableObjectInstaller, ассет. Биндит GameConfigs
└── ProjectInstaller   MonoInstaller. Создаёт ServicesRoot и Curtain, зовёт остальные
    ├── PlatformInstaller   IPlatformSdk и его подсервисы
    ├── CoreInstaller       SceneLoader, Pause, Clock, Save, Timers, Factory, Pool, MessagePipe
    ├── MetaInstaller       Wallet, Inventory, Shop… (по жанру, может быть пустым)
    └── GameplayInstaller   заготовка под игру

SceneContext сцены Boot
└── BootSceneInstaller  LoadingScreenView, ILoadingErrorHandler, LoadingService,
                        LoadingScreenPresenter, GameBootstrapper

SceneContext сцены Game
└── UIInstaller         UILayerRoots, стек страниц, InputBlocker
```

### Проектный контейнер не видит сценовый

Zenject резолвит только вверх: сценовый контейнер видит проектный, не наоборот. Правило:
**сервис живёт в том же контейнере, что и самая «низкая» его зависимость.** Нужен объект сцены —
биндится в сценовом инсталлере, даже если по смыслу глобальный.

Пример — `LoadingService` в `BootSceneInstaller`, а не в `CoreInstaller`: его
`ILoadingErrorHandler` упирается в `LoadingScreenView`, который есть только в сцене `Boot`.
Биндинг в проектном контейнере падает на старте с
`ZenjectException: Unable to resolve 'ILoadingErrorHandler' while building object with type
'LoadingService'` — при `NonLazy`-создании, до появления какой-либо сцены. Это легко принять
за ошибку раскладки файлов или порядка инсталлеров — проверяй не порядок, а контейнеры: где
биндится сервис и где то, что он просит.

Сервисы биндятся **только** через расширение — иначе `IAsyncInitializable`/`IInitializable`
может не вызваться и сервис молча не стартует:

```csharp
Container.BindService<WalletService>();          // интерфейсы + сам тип
Container.BindServiceInterfacesOnly<SaveService>(); // только интерфейсы
```

## 5. Конфиги

Корневой `GameConfigs : ScriptableObject` держит ссылки на дочерние SO. Биндится
`ConfigsInstaller : ScriptableObjectInstaller` — ассетом, не компонентом сцены.

```csharp
public class MoveService : Service
{
    private readonly GameConfigs _configs;

    public MoveService(GameConfigs configs) => _configs = configs;

    private float Speed => _configs.Player.Speed;
}
```

Дочерние конфиги — **отдельные ассеты**, не вложенные `[Serializable]`-классы: правится один
файл, git мержит без конфликтов, ассет не пухнет.

`GameConfigs.OnValidate` обязан ругаться на каждую пустую ссылку — иначе
`NullReferenceException` всплывёт через час игры, а не при сборке.

## 5a. Пауза: токены, а не флаг

`IPauseService` (`Core/Pause/`) — единственное место, где трогаются `Time.timeScale`,
`AudioListener.pause`, блокировка ввода и сообщение площадке «геймплей остановлен».
Прямые записи в `Time.timeScale` из игрового кода запрещены.

```csharp
using (_pauseService.Acquire(PauseEffect.Time | PauseEffect.Audio))
{
    await ShowSettingsAsync(token);
}
```

- `Acquire(PauseEffect)` возвращает `IDisposable`-токен. Эффекты — флаги `Time`, `Audio`,
  `Input`, `Gameplay`, составной `Ad`. Счётчик у каждого эффекта свой: применяется на первом
  токене, снимается на последнем — перекрывающиеся источники (реклама поверх попапа) не снимают
  паузу друг у друга.
- `Gameplay` на первом/последнем токене зовёт `IPlatformSdk.Gameplay.GameplayStopped/Started`.
  Сам сервис про вендоров не знает.
- `ReleaseAll()` — аварийный клапан для утекшего токена (рекламный колбэк не вернулся).
  Поднимает generation: ранее выданные токены протухают, их поздний `Dispose` не уменьшает чужой
  счётчик. Из штатного потока не звать.
- `PauseScope` — компонент на корне попапа: токен в `OnEnable`, отдаёт в `OnDisable`.
  Инжектится контейнером, поэтому попап создаётся `GameFactory`/`PagesProvider`, а не
  `Object.Instantiate`; иначе громкий `LogError`. Это не `View`, правило «во View нет
  `[Inject]`» его не касается.
- Сервис сам глушит звук на сворачивании и потере фокуса (токен `Audio`).
- Состояние наружу — `ReadOnlyReactiveProperty<bool>`: `IsTimeFrozen`, `IsAudioMuted`,
  `IsInputBlocked`, `IsGameplayBlocked`.

`TickService` считает по `Time.deltaTime`: при `Time`-паузе `SecondTick` не идёт. Поэтому
`SaveService` флашит грязное состояние в момент заморозки времени.

## 5b. Время: часы, таймеры, смена суток

Три сервиса в `Core/Clock/` и `Core/Timers/`. Все считают по **реальному UTC** из `IGameClock`
(монотонный, откат часов не удлиняет таймер), а не по `Time.deltaTime`. `Time`-пауза замораживает
`Time.timeScale`, но не дедлайны: `GetRemaining` и `NextRolloverUtc` идут. `TickService` под
паузой не тикает, поэтому оба сервиса проверяют дедлайны ещё и на снятии заморозки
(`IsTimeFrozen` → `false`) и на возврате из фона (`IApplicationLifecycle.Paused` → `false`).
Пока время заморожено, завершения и смена суток **откладываются** (не прилетают посреди рекламы
или попапа) и приходят сразу после разморозки.

### `ITimerService` — именованные персистентные таймеры

```csharp
_timers.TryStart("lootbox.slot.2", TimeSpan.FromHours(4));
TimeSpan left = _timers.GetRemaining("lootbox.slot.2");
```

- Дедлайн — `EndUtcTicks` (`long`) в секции `timers`, без `DateTimeKind`.
- `TryStart` не перезапускает существующий, `Reschedule` заменяет (`true`, если таймера не было),
  `Cancel` снимает, `Skip` завершает немедленно (ускорение за рекламу/валюту).
- Окончание приходит двумя путями. **`ITimerOwner`** — владелец с `TimerPrefix` получает
  `OnTimerCompleted` для всех id с этим префиксом; доставка гарантированная, владельцы резолвятся
  лениво (`LazyInject<List<ITimerOwner>>`), как участники сейва. **`TimerMessages.Completed`** в
  MessagePipe — для остальных подписчиков.
- `Completed.WasExpiredOffline` — восстановлен из сейва и к первой проверке уже истёк;
  `Overdue` — на сколько. Срабатывают на первой проверке после `RestoreAll`, не теряются.
- Сообщения не буферизуются: подписчик ещё не созданной сцены `Game` офлайн-завершение не увидит.
  Кому нужно гарантированно — `ITimerOwner` (сервис в проектном контейнере) или опрос `IsRunning` /
  состояния своего сервиса при создании.
- Запись: на `Start`/`Reschedule`/`Cancel`/завершение — `MarkDirty(Important)`. Серию вызовов в
  одном кадре `SaveService` схлопывает в одну запись (`NextFrame`), отдельный debounce не нужен.
  Завершение обязано уходить на диск вместе с состоянием владельца, иначе после краша таймер
  сработает второй раз.
- Callback владельца может сам вызвать `TryStart`/`Reschedule` (повторяющийся таймер); завершения
  не рекурсивны, новый дедлайн проверится на следующем тике.

Id — `<механика>.<объект>`: `lootbox.slot.2`, `energy.refill`. Префикс владельца уникален в проекте.

### `IDayRolloverService` — смена суток

- Номер суток — `int` от нуля `DateTime` со смещением `DayRolloverSettings.UtcOffsetMinutes`
  (по умолчанию `0` — полночь UTC; константа в `CoreInstaller`).
- `CurrentDay` — `ReadOnlyReactiveProperty<int>`, `NextRolloverUtc`, `TimeUntilNextRollover`,
  `GetDay(utc)` — для UI-таймеров «до обновления».
- `DayRolloverMessages.DayChanged(Day, DaysPassed, WasOffline, DayStartUtc)` — на первой проверке
  после полуночи (секундная точность). `DaysPassed > 1` — полночь прошла офлайн несколько раз.
  `WasOffline` — приложение было закрыто или свёрнуто с прошлой проверки.
- Последние виденные сутки — в секции `dayRollover`. На новой установке события нет: дата
  просто фиксируется.
- Поздний подписчик (механика сцены) не полагается на сообщение: хранит свои последние сутки и
  сверяет с `CurrentDay.CurrentValue` при создании.

### Что это даёт механикам библиотеки

`daily-sign-in`, `energy-regen`, `afk-reward`, `timed-lootbox-slots` могут опираться на эти
сервисы вместо своих `SecondTick`-счётчиков: дедлайны — `ITimerService`, «новый день» —
`DayChanged` / `CurrentDay`. Сами механики **не правятся**; перевод выполняет
`unity-mechanic-integrator` при подключении. Регенерацию по `lastRegenUtcTicks` из `energy-regen`
таймеры не заменяют (это пересчёт накопленного, а не дедлайн).

## 6. Ошибки

- **Ошибка конфигурации — громкая.** Нет префаба страницы, пустая ссылка в конфиге, нет сцены под
  элементом enum: `GameDebug.LogError` со списком проблем, в редакторе ещё и `throw`. Молчаливый
  `return` запрещён.
- **Ошибка данных — тихая, с фолбэком.** Битая секция сейва: лог, дефолт, игра идёт дальше.
- **Пустой `catch { }` запрещён.** Либо конкретный тип исключения с логом, либо не ловим.

## 7. Без asmdef

Asmdef не используем. Компенсация двух потерь:

- **Время компиляции** → Project Settings → Editor → Enter Play Mode Options **включены**,
  Reload Domain и Reload Scene **выключены**: вход в Play Mode мгновенный. Цена: статика и
  подписки не сбрасываются между запусками, поэтому обязателен `StaticStateResetter` с
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`.
- **Запрет ссылок между слоями** → поиск по коду при сборке фундамента и на ревью. Редакторных
  валидаторов нет: шумят в консоли и путаются со счётчиком ошибок компиляции.

## 8. Чего в фундаменте нет намеренно

- Корутин и `ICoroutineRunner` — всё на UniTask.
- Групп сцен вида «выгрузить все сцены типа Level» — это игровая логика.
- Синглтонов и статических точек доступа к сервисам.
- `async void` — см. `CODE-STYLE.md`.
