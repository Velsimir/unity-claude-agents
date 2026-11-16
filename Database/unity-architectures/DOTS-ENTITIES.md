# Профиль архитектуры: Unity DOTS (Entities 1.x) + hybrid

Геймплей или массовая симуляция на Unity Entities: данные в `IComponentData`, логика в
`ISystem`/`SystemBase`, контент через authoring + Baker в Sub Scene. Игрок, UI, мета и сейвы
обычно остаются на GameObject и DI проекта — для них `ZENJECT-MVP.md`, `SINGLETON-EVENTBUS.md`
или профиль проекта.

Факты о стеке и ловушки D1… — `~/.claude/Database/unity-dots/DOTS_ENTITIES.md`. Здесь только
признаки и сопоставление ролей.

Отдельного интегратора под DOTS нет: GameObject-часть встраивает `unity-mechanic-integrator`,
ECS-часть — основная сессия по этому профилю и базе. Ревью DOTS-кода — `dots-review`,
архитектурная критика — `architecture-review`.

## Признаки

| Признак | Где смотреть | Вес |
|---|---|---|
| пакет `com.unity.entities` (или `com.unity.feature.ecs`) | `Packages/manifest.json` | решающий |
| `ISystem` / `SystemBase`, `IComponentData`, `SystemAPI.Query` | код | решающий |
| `Baker<T>` и authoring-`MonoBehaviour` | код | поддерживающий |
| Sub Scene (`.unity` под объектом `SubScene`) | сцены | поддерживающий |
| `com.unity.entities.graphics`, `com.unity.physics` | `Packages/manifest.json` | поддерживающий |
| `[BurstCompile]`, `IJobEntity` | код | поддерживающий |

Уверенный вывод: пакет + системы на `ISystem`/`SystemBase`. Пакет стоит, а систем нет —
DOTS не используется (часто остаток эксперимента), профиль не применять.

Легко спутать: LeoEcs Lite (`com.leopotam.ecslite`, `EcsWorld`, `IEcsRunSystem`) — профиль
`LEOECS-LITE.md`; Morpeh, Arch и другие ECS — не этот профиль.

## Роли → реализация

| Роль | Реализация | Где подробно |
|---|---|---|
| `code-home` | папка фичи: `Components/`, `Systems/`, `Authoring/` рядом, своя asmdef со ссылками на `Unity.Entities`, `Unity.Burst`, `Unity.Mathematics`, `Unity.Collections`; точная конвенция — из `CLAUDE.md` проекта | `CLAUDE.md` проекта |
| `service-registration` | правило — набор систем; World создаёт их сам, место задают `[UpdateInGroup]` и `[UpdateBefore/After]`. Сервис остаётся только вне мира (сейв, мета, SDK) | `DOTS_ENTITIES.md` §3 |
| `config` | authoring-поля или `ScriptableObject`, прочитанный в Baker (`DependsOn`), → singleton-компонент или компонент на entity; большие неизменяемые таблицы — `BlobAsset` | §4 |
| `save-participant` | сериализации мира нет: участник сейва снаружи ECS держит DTO; при сохранении мост снимает данные query, при загрузке система или мост рождает entity из DTO | §7 |
| `ui-entry` | UI на GameObject; кнопка пишет компонент-запрос или singleton через мост | §7 |
| `presentation-binding` | системы `PresentationSystemGroup` или managed-компонент со ссылкой на `Transform`/`Animator`; рендер — Entities Graphics | §3, §7 |
| `state-stream` | текущее значение — компонент или singleton; наблюдатель снаружи читает его в мосте раз за кадр и публикует в R3/событие проекта | §7 |
| `event-bus` | компонент-событие на entity или entity-событие, снимаемое одной системой в конце группы; `IEnableableComponent` для частых флагов | §2, §5 |
| `tick` | `SimulationSystemGroup`, `SystemAPI.Time.DeltaTime`; фиксированный шаг — `FixedStepSimulationSystemGroup` | §3 |
| `input` | MonoBehaviour/Input System пишет singleton ввода через мост, системы читают | §7 |
| `external-provider` | singleton-компонент с данными или managed-мост в `SystemBase` | §3, §7 |
| `feedback-hook` | Presentation-система на компонентах-событиях → VFX, звук, анимация через companion GameObject или пул | §7 |
| `lifetime` | World живёт с приложением; entity — до `DestroyEntity` или выгрузки Sub Scene; cleanup-компоненты для освобождения внешних ресурсов | §2, §4 |
| `layer-boundary` | Simulation-системы без `UnityEngine.Object` и managed-кода; доступ к `EntityManager` из GameObject-мира — только через мост фичи | §6, §7 |

## Адаптация кода из библиотеки

`TEMPLATES/` написаны под Zenject и не копируются: состояние сервиса → компоненты, методы
правила → системы Simulation, события → компоненты-события, конфиг SO → Baker в singleton.
Процедура как в `leoecs-lite/MECHANIC-PORTING.md`, но с ловушками D*, а не L*.

## Проверки после встраивания

- ноль ошибок компиляции и кодогенерации (D1, D22);
- каждая система в нужной группе, порядок задан внутри одной группы (D15);
- structural changes только через ECB или вне итерации (D2);
- в Simulation нет managed-кода, Burst-системы проходят Burst Inspector (D9, D10);
- authoring-объекты в Sub Scene, в Runtime-инспекторе видны компоненты (D5, D6);
- в Entities Hierarchy видны entity, в Systems — система обновляется;
- мост GO ↔ ECS один на фичу (D16);
- после — ревью агентом `dots-review`.

## Грабли

Полный каталог — `DOTS_ENTITIES.md` §9. При встраивании чаще всего:

- MonoBehaviour-спавнер в `Awake` берёт мир до инициализации (D20);
- состояние переключают добавлением тега каждый кадр (D17);
- `GetSingleton` падает, потому что singleton создают два места (D12);
- Baker читает SO без `DependsOn` — правка баланса не доходит до entity (D7).

## Откуда

| Дата | Что | Из какого типа проекта |
|---|---|---|
| 2026-10-26 | первая версия по учебным материалам Entities 1.4 | hybrid: игрок и UI на GameObject, массовые агенты на Entities |
