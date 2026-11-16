# Unity DOTS (Entities 1.x) — база знаний

Факты о стеке и каталог ловушек под Unity 6 и `com.unity.entities` 1.4. Собрано по учебным
материалам проекта на Entities 1.4.8 и официальной документации `com.unity.entities@1.4`.
Поведение, помеченное «проверить», сверять с документацией своей версии пакета.

**Накопительная база, общая для всех проектов.** Читают `dots-review` (каталог D*),
`architecture-review` и разведка через
профиль `unity-architectures/DOTS-ENTITIES.md`. Проектная специфика — в `CLAUDE.md` проекта.
Правила дописывания — раздел 10.

Не путать с LeoEcs Lite (`leoecs-lite/LEOECS_LITE.md`): другая модель хранения, кодогенерация,
джобы и Burst, другие ловушки.

---

## 0. Что это и что из этого следует

ECS на archetype/chunk-хранении с кодогенерацией (source generators), Job System и Burst.
Данные лежат плотно, обход линейный — выигрыш от layout, а не от «паттерна». Цена: строгие
правила доступа, structural changes, отдельный пайплайн контента (baking).

ECS оправдан, когда много однотипных сущностей с однотипной логикой. Уникальный протагонист,
UI, катсцены, диалоги — обычно остаются на GameObject (hybrid).

## 1. Стек

| Пакет | Роль | Ставится |
|---|---|---|
| `com.unity.entities` | ядро: World, entity, компоненты, системы, baking | вручную |
| `com.unity.entities.graphics` | рендер entity через BatchRendererGroup | вручную, если нужен рендер без GameObject |
| `com.unity.burst` | компилятор подмножества C# в нативный код | зависимостью |
| `com.unity.collections` | `NativeArray`, `NativeList`, … без GC | зависимостью |
| `com.unity.mathematics` | `float3`, `quaternion`, `math.*` | зависимостью |
| `com.unity.physics` | DOTS-физика | по необходимости |
| `com.unity.netcode` | мультиплеер на Entities | по необходимости |
| `com.unity.feature.ecs` | feature-набор, тянет основное | альтернатива ручной установке |
| C# Job System (`Unity.Jobs`) | многопоточность | **не пакет**, встроен в движок |

## 2. Модель данных

- **Entity** — пара `Index + Version`, без полей. Версия защищает от висячих ссылок: слот
  переиспользован → старый `Entity` невалиден (`SystemAPI.Exists` / `em.Exists`).
- **Archetype** — точный набор типов компонентов. **Chunk** — блок ~16 KB с entity одного
  archetype, компоненты одного типа лежат массивом.
- **Structural change** — create/destroy entity, add/remove компонента, смена shared-компонента:
  entity переезжает в chunk другого archetype. Запись поля — не structural.
- Structural change — **sync point**: завершает джобы, работающие с затронутыми данными, и
  инвалидирует ранее полученные `RefRW`/`RefRO`, `DynamicBuffer`, `NativeArray` из chunk.

### Виды компонентов

| Вид | Объявление | Где лежит | Jobs/Burst | Когда |
|---|---|---|---|---|
| unmanaged | `struct : IComponentData` | в chunk | да | по умолчанию |
| tag | пустой `struct : IComponentData` | 0 байт в chunk | да | фильтр в query |
| managed | `class : IComponentData` | отдельный массив мира, в chunk индекс | нет | мост к `UnityEngine.Object`, редкое исключение |
| buffer | `struct : IBufferElementData` | в chunk или в куче при переполнении | да | список на entity (точки патруля) |
| enableable | `+ IEnableableComponent` | в chunk + бит | да | вкл/выкл без structural change |
| shared | `struct : ISharedComponentData` | разбивает chunk'и по значению | частично | группировка (рендер-меш, команда) — смена значения structural |
| singleton | любой компонент, ожидаемо один в мире | — | да | конфиг, состояние игры, прокси игрока |

Поля unmanaged-компонента: blittable-типы, `Entity`, `float3`, `FixedString*`, `BlobAssetReference`.
Не класть: `string`, `List<>`, `GameObject`, `MonoBehaviour`. `Vector3` работает, но в
DOTS-коде — `float3`.

Disabled enableable-компонент query по умолчанию **не видит**, как будто его нет.

## 3. Системы

| | `ISystem` | `SystemBase` |
|---|---|---|
| тип | `partial struct` | `partial class` |
| Burst | `[BurstCompile]` на struct и методах | `OnUpdate` не бёрстится, только джобы |
| managed-поля | нет | да |
| когда | новая логика по умолчанию | нужен managed-код, мост к GameObject |

Жизненный цикл: `OnCreate`, `OnUpdate`, `OnDestroy` с `ref SystemState state`. Система не
вешается на объект — World создаёт её сам (окно `Window → Entities → Systems`).

Основные вызовы:

```csharp
foreach (var (transform, speed, entity) in
         SystemAPI.Query<RefRW<LocalTransform>, RefRO<Speed>>()
             .WithAll<EnemyTag>().WithNone<DeadTag>().WithEntityAccess())
{
    transform.ValueRW.Position.z += speed.ValueRO.Value * SystemAPI.Time.DeltaTime;
}
```

- `RefRO<T>` / `ValueRO` — чтение, `RefRW<T>` / `ValueRW` — запись. Доступ на запись
  повышает change version chunk'а и запрещает параллельную запись этого типа другими джобами.
- `.WithAll/.WithNone/.WithAny/.WithDisabled/.WithPresent`, `SystemAPI.QueryBuilder()` для
  переиспользуемого `EntityQuery`.
- `state.RequireForUpdate<T>()` — система не обновляется, пока нет ни одной entity с `T`.
- `SystemAPI.GetSingleton/GetSingletonRW/SetSingleton/HasSingleton` — исключение, если таких 0
  или больше одного.
- `SystemAPI.*` работает только внутри типов систем (кодогенерация), не в статических хелперах.

### Группы и порядок

`InitializationSystemGroup` → `SimulationSystemGroup` (геймплей по умолчанию, внутри —
`TransformSystemGroup`, ECB-системы Begin/End) → `PresentationSystemGroup`.
`[UpdateInGroup]`, `[UpdateBefore/After]` действуют **только внутри одной группы**.
`LocalToWorld` пересчитывается в `TransformSystemGroup` из `LocalTransform` и `Parent`.

## 4. Создание entity

| Путь | Как | Когда |
|---|---|---|
| Baking | authoring-`MonoBehaviour` + `Baker<T>` в **Sub Scene** | контент уровня, префабы, всё, что настраивают в инспекторе |
| Рантайм из кода | `EntityManager.CreateEntity` + `AddComponentData` | спавн из MonoBehaviour, прототип, данные из сейва |
| Рантайм в системе | ECB: `CreateEntity` / `Instantiate(prefabEntity)` | массовый спавн, пули, FX |
| entity-префаб | Baker: `GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic)` в компонент-ссылку, потом `Instantiate` | враги, снаряды |

Baker:
- `GetEntity(TransformUsageFlags.X)`: `None` — без трансформа, `Renderable` — статичный
  рендер, `Dynamic` — двигается в рантайме, `WorldSpace`, `NonUniformScale`, `ManualOverride`.
- `AddComponent`, `AddBuffer`, `AddComponentObject` (managed).
- Читать другие объекты и ассеты — **только через API бейкера** (`GetComponent`,
  `DependsOn`), иначе инкрементальный bake не увидит изменения.
- Baker без состояния и побочных эффектов: может вызываться многократно и в любом порядке.
- Bake идёт в редакторе (live baking) и при сборке, не каждый кадр. В рантайме authoring-
  `MonoBehaviour` не существует.
- Префаб-entity несёт тег `Prefab` и query его не видит.

`World.DefaultGameObjectInjectionWorld` — мир по умолчанию. До инициализации и после выхода
из Play — `null` или `!IsCreated`.

## 5. Отложенные команды (ECB)

```csharp
var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
    .CreateCommandBuffer(state.WorldUnmanaged);
ecb.DestroyEntity(entity);
```

- В джобе параллельно — `ecb.AsParallelWriter()` с `sortKey` = `[ChunkIndexInQuery]`, иначе
  порядок команд недетерминирован.
- Свой `new EntityCommandBuffer(Allocator.Temp)` — сам `Playback` и `Dispose`.
- Entity, созданная через ECB, до playback — временная: её нельзя хранить вне этого буфера.

## 6. Jobs и Burst

- `IJobEntity` (`Execute` по компонентам), `IJobChunk`; `Schedule` / `ScheduleParallel`;
  зависимости — через `state.Dependency`. Safety-система запрещает параллельную запись одного
  типа без явных правил.
- Burst — opt-in: `[BurstCompile]` на типе и методе. Компилирует подмножество C#: без
  managed-объектов, `class`, `string` (кроме `FixedString` и литералов), boxing, большинства
  `UnityEngine.*`, исключений как управления потоком.
- `Debug.Log` в Burst — только со строковыми литералами и простым форматированием; с
  managed-строками и объектами — ошибка компиляции Burst.
- Порядок работы: логика без Burst → убрать managed из горячего пути → `[BurstCompile]` →
  Profiler и Burst Inspector.
- **WebGL/Web**: потоков нет или мало — джобы фактически на главном потоке; поддержку Burst и
  Entities Graphics под Web для своей версии — проверить. Главный выигрыш там — layout данных.

## 7. Hybrid: мосты GameObject ↔ ECS

- **GO → ECS**: MonoBehaviour пишет singleton-компонент (`PlayerPosition`) или компонент-запрос
  на entity; системы читают.
- **ECS → GO**: Presentation-система (`SystemBase`) пишет в managed-компонент со ссылкой на
  `Transform`/`Animator`, или GO читает singleton/query в `LateUpdate`.
- Каждый доступ к `EntityManager` с главного потока может стать sync point — мост держать
  в одном месте и вызывать раз за кадр, не из сотни `Update`.
- Companion GameObject для аниматора, звука, частиц у entity — допустим, но это managed-путь.

## 8. Отладка

`Window → Entities`: Hierarchy, Systems, Components, Archetypes. Inspector entity в режиме
Runtime. В Sub Scene **Scene view показывает authoring-объект**, Game view — runtime-entity:
«в Scene стоит, в Game едет» — норма. Profiler: Timeline, отметки систем, sync points.

## 9. Каталог ловушек для ревью

Формат: что написано → что происходит → как правильно. Номера стабильны.

| № | Ловушка | Что происходит | Как правильно |
|---|---|---|---|
| D1 | система/джоба без `partial` | ошибки кодогенерации | `partial struct ... : ISystem` |
| D2 | structural change (`em.AddComponent`, `DestroyEntity`) внутри `foreach` по `SystemAPI.Query` | исключение о structural change во время итерации | ECB, playback после цикла |
| D3 | `RefRW`/`DynamicBuffer`, полученные до structural change, используются после | данные из старого chunk, исключение safety или порча | перечитать после изменения |
| D4 | `RefRW` там, где хватает `RefRO` | лишний change version (срабатывают change-фильтры), запрет параллелизма | `RefRO` для чтения |
| D5 | authoring-объект вне Sub Scene | Baker не вызывается, компонентов в Runtime нет | класть в Sub Scene или создавать в рантайме |
| D6 | `GetEntity(TransformUsageFlags.None/Renderable)` для движущегося объекта | нет `LocalTransform` или движение не применяется, query пуст | `Dynamic` |
| D7 | Baker читает чужой объект или ассет напрямую (`authoring.other.GetComponent`, поле SO) | правка источника не перепекает entity — старые данные | `GetComponent<T>(obj)`, `DependsOn(asset)` бейкера |
| D8 | Baker с логикой, статическим состоянием, рандомом без сида | недетерминированный bake, разные данные между запусками | Baker только переносит данные; логика в системах |
| D9 | managed-компонент или managed-поле в горячем пути | нет Burst, нет джобов, GC | unmanaged-компонент; managed только на мосту |
| D10 | `[BurstCompile]` на методе, но код Burst-несовместим | ошибка Burst или исполнение без Burst — потеря скорости без явного сигнала в логике | Burst Inspector, убрать managed |
| D11 | `Entity`, сохранённая в компоненте/сервисе, используется после уничтожения | исключение или чужая entity при переиспользованном слоте | `Exists` перед доступом; подписка на уничтожение |
| D12 | `GetSingleton<T>` без гарантии ровно одного | исключение при 0 или >1 | `RequireForUpdate<T>()` / `HasSingleton`; один владелец создания |
| D13 | `RequireForUpdate` у системы, которая должна отработать «уход» последней entity | система выключается до того, как увидит пустой мир — финальная логика не выполняется | убрать require или отдельная cleanup-система, cleanup-компоненты |
| D14 | чтение `LocalToWorld` до `TransformSystemGroup` после записи `LocalTransform` | значение прошлого кадра, рывки | читать `LocalTransform` или ставить систему после трансформов |
| D15 | `[UpdateBefore/After]` на систему из другой группы | атрибут игнорируется (предупреждение), порядок случайный | `[UpdateInGroup]` в общую группу или порядок групп |
| D16 | доступ к `EntityManager` из многих MonoBehaviour каждый кадр | sync points, главный поток ждёт джобы | один мост, раз за кадр |
| D17 | частый add/remove тега для переключения состояния | structural change каждый кадр, переезды chunk'ов | `IEnableableComponent` |
| D18 | `AsParallelWriter()` с постоянным или случайным `sortKey` | недетерминированный порядок команд | `[ChunkIndexInQuery] int sortKey` |
| D19 | `NativeArray`/ECB с `Allocator.TempJob`/`Persistent` без `Dispose` | утечка, предупреждения safety | `Dispose` / `using`, Temp — в пределах кадра |
| D20 | `World.DefaultGameObjectInjectionWorld` без проверки | NRE в `Awake` до инициализации, при выходе из Play | `world != null && world.IsCreated` |
| D21 | гайды эпохи Entities 0.x: `ConvertToEntity`, `GameObjectConversionSystem`, `IConvertGameObjectToEntity`, `Entities.ForEach` в новом коде | API удалён или устарел | Baker + `ISystem` + `SystemAPI.Query` / `IJobEntity` |
| D22 | `SystemAPI.*` в статическом хелпере или MonoBehaviour | ошибка кодогенерации | передать `ref SystemState` / данные в хелпер; снаружи — `EntityManager` |
| D23 | ожидание, что Scene view покажет движение entity из Sub Scene | «система не работает», хотя работает | проверять Game view и Entities Hierarchy |

## 10. Правила дописывания этой базы

- Сюда — **факты**: API, семантика, ловушки, паттерны. Процедуры ревью — в агентах.
- Новая ловушка — следующий свободный номер (D24, …). Номера не переиспользуются и не
  перенумеровываются.
- Факт сверяется с документацией `com.unity.entities@<версия>` или исходниками в
  `Library/PackageCache/com.unity.entities@*`; неподтверждённое помечается «проверить».
- Смена мажорной версии Entities — пройти разделы 3–6 и каталог, отметить изменившееся.
- Проектные имена — только как пример.

## Ссылки

- Entities 1.4: `https://docs.unity3d.com/Packages/com.unity.entities@1.4/manual/index.html`
- Baking: `.../com.unity.entities@1.4/manual/baking.html`
- Structural changes: `.../com.unity.entities@1.4/manual/components-structural-changes.html`
- Burst: `https://docs.unity3d.com/Packages/com.unity.burst@latest/`
- Job System: `https://docs.unity3d.com/Manual/JobSystem.html`
- Сэмплы: `https://github.com/Unity-Technologies/EntityComponentSystemSamples`
