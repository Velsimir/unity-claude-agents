# Профиль архитектуры: Zenject + MVP (фундамент `unity-foundation`)

Архитектура агента `unity-foundation`. Факты — в `~/.claude/Database/unity-foundation/`, здесь
только сопоставление ролей из `ROLES.md` с ними и ссылки. Файл трогается, только если сдвинулась
сама точка встраивания.

Код в `MECHANICS/*/TEMPLATES/` написан под эту архитектуру: механика копируется как есть,
адаптация — замена `{{ROOT}}`.

## Признаки

| Признак | Где смотреть | Вес |
|---|---|---|
| `com.svermeulen.extenject` или папка `Plugins/Zenject` | `Packages/manifest.json`, `Assets/` | решающий |
| `MetaInstaller`, `CoreInstaller`, `ProjectInstaller` | код, `Assets/<ROOT>/Scripts/Core/DI/` | решающий |
| `GameConfigs : ScriptableObject` + `ConfigsInstaller` | код | решающий |
| `ISaveParticipant`, `ISaveService` | код, `Core/Save/` | решающий |
| `PageBase`, `IPageService`, `PagesConfig` | код, `Core/UI/` | поддерживающий |
| пакеты R3, MessagePipe, UniTask | `Packages/manifest.json` | поддерживающий |
| `ProjectContext.prefab` в `Resources/` | ассеты | поддерживающий |

Уверенный вывод: Zenject + `MetaInstaller` + `GameConfigs` + `ISaveParticipant`. Корень кода —
`Assets/<ROOT>/Scripts/`, `<ROOT>` обычно `G`, но берётся из фактического пути, а не предполагается.

Легко спутать: просто Zenject без фундамента (свои инсталлеры, нет `GameConfigs` и
`ISaveParticipant`) — **не** этот профиль, роли другие. Zenject + LeoEcs в геймплее — этот профиль
для мета-слоя и `LEOECS-LITE.md` для геймплея, разведка отмечает оба.

## Роли → реализация

| Роль | Реализация | Где подробно |
|---|---|---|
| `code-home` | мета — `Assets/<ROOT>/Scripts/Meta/<Mechanic>/`, геймплей — `Assets/<ROOT>/Scripts/Gameplay/<Mechanic>/`; неймспейс `<ROOT>.Meta.<Mechanic>` / `<ROOT>.Gameplay.<Mechanic>`; asmdef нет | `unity-foundation/ARCHITECTURE.md` §1, §7 |
| `service-registration` | `Container.BindService<XxxService>()` в `MetaInstaller` (мета) или `GameplayInstaller` (геймплей); сервис со ссылкой на объект сцены — в сценовый инсталлер | `ARCHITECTURE.md` §4 |
| `config` | `XxxConfig : ScriptableObject` отдельным ассетом, поле и свойство в `GameConfigs`, проверка на null в `GameConfigs.OnValidate`; сервис получает `GameConfigs` в конструктор | `ARCHITECTURE.md` §5 |
| `save-participant` | сервис реализует `ISaveParticipant` (`Key`, `Order`, `Capture`, `Restore`); регистрируется сам через `BindService`; при отсутствии секции — дефолт, без исключения | `SAVE-SYSTEM.md` §1, §4 |
| `ui-entry` | страница `Page`/`PayloadPage<T>`, префаб в `PagesConfig` с `UILayer`; открытие — `IPageService.ShowAsync<TPage>()` | `UI-SYSTEM.md` §1, §4, §6 |
| `presentation-binding` | Presenter биндится в `UIInstaller` сцены, получает страницу через `BindInstance`, подписывается на её `Observable`; во View нет `[Inject]`, кроме `PageBase` | `ARCHITECTURE.md` §2, `UI-SYSTEM.md` §8 |
| `state-stream` | приватный `ReactiveProperty<T>`, наружу `ReadOnlyReactiveProperty<T>`, созданный один раз в поле | `ARCHITECTURE.md` §3 |
| `event-bus` | MessagePipe: `static class XxxMessages` с `record`-сообщениями и `Install(DiContainer, MessagePipeOptions)`; инсталлер не правится — `CoreInstaller` находит `Install` рефлексией | `ARCHITECTURE.md` §3 |
| `tick` | `ITickService.Update` / `FixedUpdate` / `SecondTick`; таймеры — `SecondTick`; `Observable.EveryUpdate()` запрещён | `ARCHITECTURE.md` §3 |
| `input` | UI-ввод — события View через Presenter; геймплейный ввод читает проект (Input System) и передаёт в правило вызовом метода — механика ввод не читает | `MECHANICS/Gameplay/_TEMPLATE` «Тик и ввод» |
| `external-provider` | интерфейс `IXxxProvider` в папке механики, реализация — в проекте, биндится в том же инсталлере, что и механика | — |
| `feedback-hook` | проект подписывается на сообщения механики (MessagePipe) или на её `ReadOnlyReactiveProperty` и запускает VFX, звук, анимацию у себя | `gamecraft/FEEDBACK.md` |
| `lifetime` | проектный контейнер — глобальные сервисы; сценовый — то, что держит объекты сцены; подписки `.AddTo(Disposables)` в сервисе и презентере, `HiddenDisposables` на странице | `ARCHITECTURE.md` §3, §4 |
| `layer-boundary` | под `Meta/` нет `using <ROOT>.Gameplay`; под `Gameplay/` нет `using <ROOT>.UI` и ссылок на страницы; во View нет `[Inject]` | `ARCHITECTURE.md` §1, §2 |

## Старые записи механик

Записи в `MECHANICS/`, заведённые до `ROLES.md`, в «Точках интеграции» называют классы
фундамента. Перевод:

| В записи | Роль |
|---|---|
| строка биндинга в `MetaInstaller` / инсталлере | `service-registration` |
| поле и свойство в `GameConfigs` | `config` |
| `ISaveParticipant`, ключ секции, `Order` | `save-participant` |
| префаб в `PagesConfig`, `UILayer` | `ui-entry` |
| сообщения MessagePipe | `event-bus` |
| `ITickService` | `tick` |
| реализация провайдеров | `external-provider` |

## Адаптация кода из библиотеки

Нет: `TEMPLATES/` копируются, `{{ROOT}}` заменяется на фактический корень.

## Проверки после встраивания

- ни одного `{{ROOT}}` в добавленном коде;
- во View нет `[Inject]`, кроме `PageBase`;
- границы слоёв (`layer-boundary`) — поиском по `using`;
- после выхода из Play Mode в `save.json` есть секция механики под своим ключом;
- страница встаёт в свой `UILayer`, значения из конфига применяются;
- `AudioListener` и `EventSystem` в проекте по одному.

## Грабли

- Сервис, которому нужен объект сцены, забинжен в проектном контейнере →
  `ZenjectException: Unable to resolve ...` при старте. `ARCHITECTURE.md` §4.
- Сервис забинжен в обход `BindService` → `IInitializable` не вызван, сервис молча не стартует.
- `SaveService` получает `List<ISaveParticipant>` в конструктор вместо ленивого → цикл
  зависимостей. `SAVE-SYSTEM.md` §4.
- `ToReadOnlyReactiveProperty()` в геттере → утечка на каждый вызов.

## Откуда

| Дата | Что | Из какого типа проекта |
|---|---|---|
| 2026-09-28 | первая версия, сопоставление с базой `unity-foundation` | фундамент `unity-foundation` |
