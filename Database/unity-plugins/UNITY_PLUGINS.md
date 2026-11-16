# Плагины и пакеты, используемые в Unity-проектах

Справочник: что за плагин, как ставится, какой строкой. Источник истины для скилла `unity-plugin-install`.

Строки раздела 2 взяты из реальных `Packages/manifest.json` — вставлять как есть. Ссылки раздела 4 проверять: часть плагинов раздаётся только из личного кабинета или Asset Store.

---

## 1. Как этим пользоваться

1. Найди нужный пакет ниже. Нет в списке — сначала спроси владельца, не хочет ли он конкретный плагин; не тащи первый попавшийся с GitHub.
2. Покажи владельцу пакет, версию и точную строку, которую собираешься добавить.
3. После «да» правь `Packages/manifest.json`. Всё, что ставится не через manifest
   (`.unitypackage`, Asset Store, SDK из личного кабинета), оформляй ручным шагом.
4. Unity подтянет пакет при следующем открытии и сам обновит `Packages/packages-lock.json` (руками не править).

Общие правила — раздел 3.

---

## 2. Ставятся по git-URL (правка `Packages/manifest.json`)

### Асинхронность, реактивность, DI

| Пакет | Что делает | Строка |
|---|---|---|
| `com.cysharp.unitask` | async/await без аллокаций, замена корутинам | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11` |
| `com.cysharp.r3` | Rx нового поколения (R3), замена UniRx | `https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity#1.3.1` |
| `com.cysharp.messagepipe` | шина сообщений / event bus | `https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe#1.8.2` |
| `com.cysharp.messagepipe.zenject` | интеграция MessagePipe с Zenject | `https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe.Zenject#1.8.2` |
| `com.svermeulen.extenject` | Zenject/Extenject, DI-контейнер | `https://github.com/Mathijs-Bakker/Extenject.git?path=UnityProject/Assets/Plugins/Zenject/Source#9.3.1` |
| `com.leopotam.ecslite` | LeoEcs Lite, геймплей на ECS (профиль `unity-architectures/LEOECS-LITE.md`) | `https://github.com/Leopotam/ecslite.git#2026.4.25` |
| `com.leopotam.ecslite.unityeditor` | отладчик мира LeoEcs Lite, Editor-only; **2025.11.5 не компилируется на Unity 6.6** (CS0619 `EndNameEditAction`) | `https://github.com/Leopotam/ecslite-unityeditor.git#2025.11.5` |

**R3 ставится в два шага, иначе проект не компилируется.** Git-пакет `com.cysharp.r3` — только Unity-интеграция (`R3.Unity`); ядро (`R3.dll`, `TimeProvider`, `System.Runtime.CompilerServices.Unsafe`) лежит в NuGet и через manifest не тянется. Без второго шага — сотни CS0234/CS0246 в `Library/PackageCache/com.cysharp.r3@*` (не раскладка кода и не несовместимость версий).

#### Автоматическая установка ядра R3

NuGetForUnity восстанавливает всё из `Assets/packages.config`, кликать в `NuGet → Manage NuGet Packages` не нужно. Порядок для скрипта:

1. Узнать актуальную стабильную версию:
   `https://api.nuget.org/v3-flatcontainer/r3/index.json` — массив `versions`,
   берётся последняя без суффикса (`-preview`, `-rc`, `-beta`).
2. Написать `Assets/packages.config` с записью `R3` этой версии.
3. Запустить восстановление: пункт меню `NuGet → Restore Packages`.
   Зависимости разложит сам.
4. Проверить: появилась `Assets/Packages/R3.<версия>/` и ушли CS0234/CS0246
   из `com.cysharp.r3`.

**Запасной путь — если NuGetForUnity не импортировался** (EPERM, см. «Когда пакет не распаковался»): разложить ядро руками в формате NuGetForUnity —
`Assets/Packages/<Id>.<Версия>/lib/<tfm>/<файл>.dll` плюс `Assets/packages.config`.
Файлы берутся с `https://api.nuget.org/v3-flatcontainer/<id-в-нижнем-регистре>/<версия>/<id>.<версия>.nupkg`,
`tfm` — `netstandard2.1`, а если его нет в пакете, то `netstandard2.0`.
Качать `Invoke-WebRequest`, распаковывать `Expand-Archive` после переименования в `.zip`:
`.nupkg` — обычный zip. Зависимости сверять по `.nuspec` внутри скачанного пакета.

Проверенный состав под Unity 6:

| NuGet-пакет | Версия | tfm |
|---|---|---|
| `R3` | 1.3.1 | netstandard2.1 |
| `Microsoft.Bcl.TimeProvider` | 8.0.0 | netstandard2.0 |
| `Microsoft.Bcl.AsyncInterfaces` | 6.0.0 | netstandard2.1 |
| `System.ComponentModel.Annotations` | 5.0.0 | netstandard2.1 |
| `System.Runtime.CompilerServices.Unsafe` | 6.0.0 | netstandard2.0 |
| `System.Threading.Channels` | 8.0.0 | netstandard2.1 |

`Microsoft.Bcl.AsyncInterfaces` — транзитивная зависимость `Microsoft.Bcl.TimeProvider` под
netstandard2.0, её легко пропустить: признак отсутствия — `CS0012: 'IAsyncDisposable' is defined
in an assembly that is not referenced ... Microsoft.Bcl.AsyncInterfaces, Version=6.0.0.0`.
`ObservableCollections` отдельным пакетом не нужен: типы `R3.Collections` лежат внутри `R3.dll`.

MessagePipe.Zenject ставить только вместе с самим Zenject.

**Extenject требует Addressables.** `Runtime/Async/Providers/*` и `FactoryFromBinderBase` ссылаются на `AssetReference` и `AsyncOperationHandle`; без `com.unity.addressables` — сотни CS0246 в `Library/PackageCache/com.svermeulen.extenject@*`. Поэтому Addressables в ядре, даже без контента по требованию. Версия 4.x выбрана по минимуму Unity 6000.0 и changelog без удалённых API; 3.0.x имел ломающие изменения, откатанные в 3.1.1.

### Данные и контент

| Пакет | Что делает | Строка |
|---|---|---|
| `com.unity.addressables` | адресуемые ассеты; **обязателен из-за Extenject**, см. выше; **4.x ставится на Unity 6000.0** (минимум 6000.0 и у 2.10.3), совместимость с Extenject 9.3.1 по компиляции не проверена — при CS-ошибках в `com.svermeulen.extenject` откатить на `2.10.3` (ветка 2.x, Unity 6000.0–6.6) | `4.1.1` |
| `com.unity.localization` | локали, String и Asset Tables, работает поверх Addressables | `1.5.13` |
| `com.cathei.bakingsheet` | таблицы (Google Sheets / Excel) в ScriptableObject | `https://github.com/cathei/BakingSheet.git?path=UnityProject/Packages/com.cathei.bakingsheet#v4.1.3` |
| `com.esotericsoftware.spine.spine-csharp` | Spine, рантайм C# | `https://github.com/EsotericSoftware/spine-runtimes.git?path=spine-csharp/src#4.2` |
| `com.esotericsoftware.spine.spine-unity` | Spine, интеграция с Unity | `https://github.com/EsotericSoftware/spine-runtimes.git?path=spine-unity/Assets/Spine#4.2` |
| `com.esotericsoftware.spine.urp-shaders` | Spine, шейдеры под URP | `https://github.com/EsotericSoftware/spine-runtimes.git?path=spine-unity/Modules/com.esotericsoftware.spine.urp-shaders#4.2` |
| `com.github-glitchenzo.nugetforunity` | NuGet-пакеты в Unity | `https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity#v4.5.0` |
| `com.unity.nuget.newtonsoft-json` | JSON со словарями и полиморфизмом; ставится **по версии**, не по git-URL | `3.2.2` |

Все три пакета Spine ставятся одной версией (`#4.2` и т.п.) и обязаны совпадать с версией
редактора Spine, в котором экспортировали анимации, иначе рантайм не прочитает `.skel`/`.json`. Для новых проектов версию брать по редактору Spine (есть ветка `4.3`); `#4.2` — для существующих.

### UI и фидбек

| Пакет | Что делает | Строка |
|---|---|---|
| `com.coffee.ui-particle` | частицы внутри uGUI-канваса с правильным порядком отрисовки | `https://github.com/mob-sakai/ParticleEffectForUGUI.git#v4.14.1` |
| `com.benoitfreslon.vibration` | простая вибрация на iOS/Android | `https://github.com/BenoitFreslon/Vibration.git` |
| `com.unity.inputsystem` | New Input System; **входит в ядро** | `1.20.1` |

`com.unity.inputsystem` нужен не только для управления: `EventSystem` фундамента работает через `InputSystemUIInputModule`. В шаблонах Unity 2022.3 пакета может не быть — прописывать явно. После установки выставить `Active Input Handling = Input System Package (New)` (`PlayerSettings`); смена требует перезапуска редактора, до неё модуль падает в рантайме.

### Аналитика и атрибуция

| Пакет | Что делает | Строка |
|---|---|---|
| `appsflyer-unity-plugin` | атрибуция AppsFlyer; **пин 6.17.91 оставлен**: v7.0.2 ломающая — минимум Unity 2023.1, переименованы `initSDK/startSDK/stopSDK/isSDKStopped` в `init/start/stop/isStopped`, удалены `AppsFlyerConsent.ForGDPRUser/ForNonGDPRUser`, `setSharingFilter*`, `waitForATTUserAuthorizationWithTimeoutInterval`, убран UI-сетап через `AppsFlyerObject.prefab`; до перехода на 7.x (последняя 7.0.3) править вызовы в проекте | `https://github.com/AppsFlyerSDK/appsflyer-unity-plugin.git#v6.17.91` |
| `io.appmetrica.analytics` | AppMetrica (аналитика Яндекса, нужна для RuStore-сборок) | `https://github.com/appmetrica/appmetrica-unity-plugin.git#v6.10.0` |

### Инструменты разработчика

| Пакет | Что делает | Строка |
|---|---|---|
| `com.coplaydev.unity-mcp` | MCP-мост: агент управляет открытым редактором Unity | `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v10.3.0` |
| `com.handzlikchris.fastscriptreload` | горячая перезагрузка C# без выхода из Play Mode | `https://github.com/handzlikchris/FastScriptReload.git?path=Assets` |
| `com.ligofff.simpletools-runtimecrashhandler` | показ рантайм-краша прямо в билде | `https://github.com/ligofff/SimpleTools-RuntimeCrashHandler.git` |

**MCP for Unity** — версия Unity-пакета обязана совпадать с версией сервера, прописанного
в конфиге клиента (сейчас `uvx --from mcpforunityserver==10.3.0 mcp-for-unity`). Пин `#main`
в проекте — источник расхождений, ставь тег. Требования: Unity 2021.3–6.x, Python 3.10+ через
`uv`. После установки в редакторе: `Window → MCP for Unity → Configure All Detected Clients`.
Мост работает только когда редактор открыт: при закрытом Unity инструменты `mcp__UnityMCP__*`
отвечают «No Unity Editor instances found». Работа с ним — скилл `unity-mcp-skill`.

**Unity в фоне не видит изменённые файлы.** Auto Refresh срабатывает по фокусу окна, а в фоне
Editor троттлит обновление: после записи `.cs`/`.jslib` агент может ждать компиляцию бесконечно,
пока владелец не переключится на Unity. Правило: после каждой записи — `refresh_unity` с
`compile: request`, `mode: force`, `wait_for_ready: true`; если `ready_for_tools` нет дольше ~60 с —
прочитать `mcpforunity://editor/state` и сообщить `blocking_reasons`, не ждать молча. Владельцу —
`Edit → Preferences → General → Interaction Mode = No Throttling`. Также: ошибка компиляции в
Assembly-CSharp на активной платформе (например, Android-only код при платформе Web) может не дать
мосту MCP подняться — смотреть `Editor.log` на `error CS`.

FastScriptReload — только для разработки, из релизных сборок исключать.

---

## 3. Общие правила установки

- **Пинуй версию.** `#main` тянет непредсказуемое обновление на любом `Resolve`. Исключение —
  когда владелец сознательно хочет свежую ветку.
- **Один резолвер зависимостей Android.** EDM4U (`External Dependency Manager for Unity`) идёт в комплекте почти каждого рекламного и Google/Firebase SDK; оставить одну, самую свежую копию. Дубли дают `Duplicate class`.
- **После добавления Android-SDK** прогнать `Assets → External Dependency Manager → Android
  Resolver → Force Resolve`. Ручные строки, дописанные в `mainTemplate.gradle`, при этом
  затираются — проверить их после резолва.
- **`Packages/packages-lock.json` руками не правится** — его пишет Unity.
- **Asset Store и `.unitypackage` агент не устанавливает.** Это ручной шаг: скачать, импортировать,
  проверить, что импортировались только нужные папки.
- **Плагин, который тянет свою версию общей зависимости** (Newtonsoft.Json, EDM4U,
  androidx-библиотеки), проверяй на конфликт с тем, что уже стоит, до установки.

---

### Когда пакет не распаковался (EPERM на Windows)

Пакет числится в `Packages/packages-lock.json`, но папки в `Library/PackageCache/` нет, зависящее от него сыплет сотнями CS0246. В `Editor.log`:

```
com.unity.scriptablebuildpipeline: EPERM: operation not permitted, rename
'...\Library\PackageCache\.tmp-21812-M58TvvGJeO5T\package' -> '...\com.unity.scriptablebuildpipeline@97537e3e823b'
```

Версии тут ни при чём, подбором версий не лечится. Порядок:

1. Удалить остатки `Library/PackageCache/.tmp-*`.
2. Удалить битую папку пакета, если она всё же создалась.
3. Удалить пакет из кэша загрузок: `%LOCALAPPDATA%\Unity\cache\npm\packages.unity.com\<пакет>`.
4. Тронуть `Packages/manifest.json` и переключиться в редактор — UPM переразрешит зависимости.

Диагностика: сравнить `packages-lock.json` с `Library/PackageCache/`. Повторяется — папку проекта в исключения антивируса и проверить облачную синхронизацию (EPERM на rename даёт сторонний процесс, держащий файл).

## 4. Ставятся не через manifest

### Монетизация и медиация

| Плагин | Как ставится | Заметки |
|---|---|---|
| AppLovin MAX | `.unitypackage`, https://github.com/AppLovin/AppLovin-MAX-Unity-Plugin | адаптеры сетей ставятся отдельно через MAX Integration Manager; версии SDK и адаптеров должны быть согласованы |
| LevelPlay (ironSource) | `.unitypackage` с developers.is.com | согласие: `IronSource.Agent.setConsent(bool)` |
| Google Mobile Ads (AdMob) | `.unitypackage` из репозитория googleads | UMP-форма не работает в сборках для не-Google сторов |
| Amazon Publisher Services (APS) | `.unitypackage` из личного кабинета APS | ставится как источник bid'ов поверх MAX |
| Samsung IAP | `.unitypackage`, https://developer.samsung.com/iap | детали — `Database/galaxy/GALAXY_PORTING.md` |
| Unity IAP (`com.unity.purchasing`) | UPM, реестр Unity | при порте в сторонний стор либо вырезается, либо гарантированно не вызывается |

### Аналитика, атрибуция, бэкенд

| Плагин | Как ставится | Заметки |
|---|---|---|
| Firebase Unity SDK | `.unitypackage`, https://firebase.google.com/download/unity | ставить только нужные модули; `google-services.json` в `Assets/StreamingAssets` или через плагин |
| Facebook SDK for Unity | `.unitypackage`, https://github.com/facebook/facebook-sdk-for-unity | тянет свои activity в манифест |
| External Dependency Manager (EDM4U) | https://github.com/googlesamples/unity-jar-resolver или OpenUPM `com.google.external-dependency-manager` | см. правило про дубли в разделе 3 |
| Google Play Plugins for Unity | https://github.com/google/play-unity-plugins, OpenUPM-скоупы `com.google.play.common`, `com.google.play.core`, `com.google.play.review` | In-App Review работает только в сборках из Google Play; для других сторов нужен свой механизм |

### Платформенные слои

| Плагин | Как ставится | Заметки |
|---|---|---|
| PokiPlugin | приходит папкой `Assets/PokiPlugin*` | см. `Database/poki/POKI_PORTING.md` |
| GamePush | `.unitypackage` из GitHub-релизов `https://github.com/GamePushService/GamePush-Unity-plugin/releases` (для обновления — `_PluginOnly`), ложится в `Assets/Plugins/GamePush` | версию здесь не пинить: актуальная — последний стабильный релиз, в проекте — `PluginData.SDK_VERSION`. Установка, обновление, осиротевшие файлы — `Database/gamepush/GAMEPUSH_PORTING.md`, раздел 1; ставит агент `gamepush-port` |
| SimpleGDPRConsent | папкой `Assets/Plugins/SimpleGDPRConsent` (плагин yasirkula) | `IsGDPRApplicable` делает синхронный http-запрос и блокирует главный поток — вызывать заранее и из фонового потока |

### Ассеты из Asset Store (ставятся руками, ищутся по имени)

| Плагин | Что делает |
|---|---|
| Feel / Nice Vibrations (More Mountains) | джус, тряска камеры, хаптика |
| Dreamteck Splines | сплайны и движение по ним |
| DOTween | твины |
| Epic Toon FX, Layer Lab, JMO Assets | арт-паки эффектов и UI |

Для них агент только напоминает, что плагин нужен, и говорит, что импорт — ручной.

---

## 4a. Базовый набор и наборы по жанрам

Раздел для агента `unity-foundation`: что класть в новый мобильный проект.

### Ядро — ставится всегда

`com.svermeulen.extenject`, `com.cysharp.r3`, `com.cysharp.unitask`,
`com.cysharp.messagepipe`, `com.cysharp.messagepipe.zenject`,
`com.unity.nuget.newtonsoft-json`, `com.unity.addressables`, `com.unity.inputsystem`,
`com.github-glitchenzo.nugetforunity`, `com.coplaydev.unity-mcp`,
DOTween (из Asset Store, не из manifest).

`com.github-glitchenzo.nugetforunity` входит в ядро безусловно: без него не ставится ядро R3, на R3 завязаны `PageBase`, `Presenter` и `Service` фундамента. Порядок R3 — раздел «Асинхронность, реактивность, DI» выше.

`com.coplaydev.unity-mcp` входит в ядро безусловно — в любой новый проект, даже если агент его сейчас не использует.

Обязательные настройки после установки ядра:

- Ядро R3 через NuGetForUnity — см. «Автоматическая установка ядра R3» выше.
  До этого шага Assembly-CSharp не компилируется вообще.
- `Active Input Handling = Input System Package (New)`; смена требует перезапуска редактора.
- Scripting Define Symbols: `UNITASK_DOTWEEN_SUPPORT` — без него нет `tween.ToUniTask()`.
- DOTween: `Tools → Demigiant → DOTween Utility Panel → Setup DOTween`, иначе код с твинами не компилируется.

Машиночитаемая копия ядра — `../project-templates/common/packages.json` и `nuget.json`, по ней
создаёт проекты `new_project.py`. Меняешь состав ядра здесь — правь и там.

### Добавляется по жанру

| Жанр | Что обычно добавляется сверх ядра |
|---|---|
| idle / clicker | нотификации, Spine (если персонажи анимируются скелетно) |
| match-3 / puzzle | нотификации; BakingSheet, если уровни и баланс ведутся в таблицах |
| hyper-casual | ничего сверх ядра — вес билда важнее удобства |
| runner | Spine или обычная анимация, нотификации |
| tower-defense | BakingSheet для баланса волн, Addressables при большом объёме контента |
| midcore | Addressables, BakingSheet, локализация, нотификации |

### Добавляется по потребности, а не по жанру

| Потребность | Пакет |
|---|---|
| Контент грузится по требованию, билд упирается в лимит стора | Addressables |
| Баланс ведётся в Google Sheets или Excel | BakingSheet |
| Несколько языков | Unity Localization или свой JSON-лоадер |
| Push-уведомления | `com.unity.mobile.notifications` |
| Скелетная 2D-анимация | Spine (все три пакета одной версией) |
| Ускорение итераций | FastScriptReload |

Реклама, IAP и аналитика пакетом не выбираются: игровой код работает с фасадом `IPlatformSdk`,
а конкретный SDK живёт в адаптере под площадку. См. `../unity-foundation/PLATFORM-SDK.md`.

### Правило

Лишний пакет — вес билда, время компиляции и зависимость, которая сломается при обновлении Unity. Не «на всякий случай»: пакет добавляется под конкретную задачу.

---

## 5. Как дополнять этот файл

- Новый пакет добавляй **строкой из реального manifest.json**, а не по памяти.
- Пиши правило, не историю: «Spine ставится тремя пакетами одной версии».
- Не выдумывай ссылки: не уверен в URL — название и издатель без ссылки.
- Внутренние ссылки, ключи, токены сюда не попадают.
- Плагин из одного проекта в общий список не тащи — ему место в `CLAUDE.md` проекта.

## 6. Журнал актуализации

- 2026-09-07 — файл заведён. Список собран из `Packages/manifest.json` нескольких мобильных проектов и папок `Assets`. MCP for Unity: пакет `#v10.3.0`, сервер `mcpforunityserver==10.3.0` (релиз 2026-10-04).
- 2026-09-14 — добавлены Newtonsoft.Json и раздел 4a (базовый набор и наборы по жанрам)
  для агента `unity-foundation`.
- 2026-09-14 — `com.unity.inputsystem` добавлен в ядро; установка ядра R3 переписана
  на автоматическую через `packages.config` и `NuGet → Restore Packages`.
- 2026-10-10 — добавлены `com.leopotam.ecslite` и `com.leopotam.ecslite.unityeditor`; ядро продублировано
  в `project-templates/common/` для `new_project.py`.
- 2026-10-25 — версии сверены с первоисточниками: inputsystem 1.20.1, newtonsoft-json 3.2.2, Addressables 4.1.1, AppMetrica v6.10.0; git-строкам ядра добавлены теги; AppsFlyer остался на 6.17.91 (7.x ломающая).
- 2026-10-26 — MCP for Unity: добавлен блок «Unity в фоне не видит изменённые файлы».
