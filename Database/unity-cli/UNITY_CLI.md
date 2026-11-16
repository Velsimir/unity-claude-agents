# Unity CLI — вторая точка входа к живому Editor'у

Официальный терминальный клиент Unity (`unity`), отдельно от Unity MCP (`mcp__UnityMCP__*`) и его не заменяет: MCP остаётся основным путём правки сцены, GameObject, префаба, ассета в открытом проекте (typed-схема, валидация), у CLI для этого только `unity command eval` с сырой C#-строкой и текстовым выводом. Выбирать CLI ради этого не нужно.

CLI закрывает то, чего у MCP нет, и служит диагностикой, когда MCP-мост не поднимается. Установка и полный справочник — скилл `unity:unity-cli`. Ниже: как выбрать MCP или CLI и особые случаи.

## Что CLI даёт сверх MCP

- **Установка и версии редактора** — `unity install`, `unity editors --installed`,
  `unity releases --lts`. MCP работает только с уже запущенным Editor'ом.
- **Проекты** — `unity projects create/info/clone`. Новый проект с нуля, git/UVCS в одном шаге.
- **Headless build и тесты** — `unity build`, `unity test --report-format junit`. Для CI,
  не для интерактивной сессии с открытым Editor'ом.
- **Лицензии** — `unity license activate/status`.
- **VCS с Unity-семантикой** — `unity vcs status/diff/conflicts/blame` понимают конфликты
  в `.unity`/`.prefab` не как текстовый diff, а как объекты сцены.
- **Диагностика связи с живым Editor'ом в обход MCP-моста** — `unity status`, `unity doctor`.
  `unity status` сам сообщает Safe Mode — см. ниже.

## Диагностика Safe Mode

`ReadMcpResourceTool` (`mcpforunity://instances`) с `instance_count: 0` **не доказывает**, что
Editor закрыт. Он может быть открыт, но застрять в **Safe Mode** из-за ошибки компиляции C# —
тогда пакет `com.unity.pipeline`, на котором держится и MCP-мост, и CLI, не грузится вовсе,
и оба канала одинаково молчат.

Прежде чем переходить в режим без MCP (шаг «Подключи Unity MCP» у `unity-foundation` и `playworks-playable-foundation`, шаг 1 у `unity-mechanic-integrator`), проверь:

```bash
unity status --json
```

`unity status` сам сообщает Safe Mode: выход с кодом 6 и состояние Editor'а в выводе. Отдельный `unity pipeline list` для этого не нужен.

Safe Mode — не повод уходить в fallback без Editor'а, а повод чинить ошибки компиляции и попросить пользователя перезапустить Unity. Принимать это за «Editor не открыт» и молча собирать чек-лист ручных шагов неверно.

## Sandboxed-агент — ложное «Editor не найден»

Шелл агента может исполняться в сэндбоксе, и `unity status`/`ReadMcpResourceTool` не видят реально запущенный Editor: сэндбокс скрывает процесс. Пустой список инстансов не доказывает, что Editor закрыт. Прежде чем писать «Unity недоступен» и уходить в режим без MCP/CLI, скажи, что сэндбокс мог скрыть открытый Editor, и спроси, открыт ли он.

## Базовые команды и флаги

| Команда | Для чего |
| --- | --- |
| `unity status --format json` | Список подключённых Editor'ов, состояние (`ready`/Safe Mode, код выхода 6) |
| `unity pipeline list` | Editor'ы и статус пакета Pipeline |
| `unity command --caller plugin --skill <имя-агента>` | Список команд, которые текущий Editor реально экспонирует — не гадать имя |
| `unity command eval --caller plugin --skill <имя-агента> '<C#>'` | Произвольный C# в живом Editor'е, когда нужной MCP-тулы нет |
| `unity command --query <слово>` | Поиск команды Editor'а по подстроке в имени, описании или теге; рядом `--tag`, `--limit`, `--offset` |
| `unity commands --grep <шаблон>` | Поиск по командам самого CLI (имена, описания, опции) и каталогу плагинов |
| `unity doctor --format json` | Auth, установленные редакторы, недавние ошибки — одной командой |
| `unity editors --installed --format json` | Какие версии Editor'а стоят локально |
| `unity projects info <path> --format json` | Версия Editor'а, которую требует проект |
| `unity build …` / `unity test …` | Headless, только CI и явная просьба пользователя — не для интерактивной правки |

`--project-path <path>` — обязателен, если на машине может быть открыто больше одного
Editor'а: без него CLI целится по текущему рабочему каталогу шелла.
`--format json --non-interactive` — на каждый вызов, который парсится, а не читается
человеком.

`--timeout <секунды>` — только целое число секунд (`--timeout 60`), без суффиксов вроде `60s`; по умолчанию 30.

## Выбор Editor'а — до первого действия с ним

Обязательно для всех агентов, трогающих живой Editor через MCP или CLI. Открытых проектов может быть несколько, MCP по умолчанию шлёт вызовы в **любой** из них; импорт пакета, правка сцены или `execute_code` не в тот инстанс молча меняют чужой проект.

Порядок, до первого вызова, который что-то читает из Editor'а или меняет в нём:

1. **Список открытых Editor'ов.** MCP — `ReadMcpResourceTool`, server `UnityMCP`, uri
   `mcpforunity://instances` (имена вида `Name@hash`). CLI — `unity status --format json`; сузить выдачу: `--port <n>`, `--project-path <путь>`, с 1.0.0-beta.14 ещё `--pid` и `--instance` (в beta.13 их нет — сверять по `unity status --help`).
2. **`Application.dataPath` каждого кандидата.** MCP — `execute_code` с параметром
   `unity_instance="<Name@hash>"` на каждый инстанс и телом `return Application.dataPath;`.
   CLI — поле `projectPath` из `unity status --json` (версия Editor'а — `editorVersion`; старые `project`/`version` deprecated); если его там нет — `unity command eval
   --project-path <путь>` с тем же выражением. Имя инстанса — не доказательство: у двух
   проектов может совпадать имя папки.
3. **Сравнение.** Подходит инстанс, у которого `dataPath` равен `<корень проекта>/Assets`
   (регистр и слэши нормализовать).
4. **Ровно один подходит** — закрепить: MCP — `set_active_instance` с его `Name@hash`, CLI —
   `--project-path <корень проекта>` на **каждом** вызове. Дальше работать только с ним.
5. **Ни одного или больше одного** — остановиться и спросить владельца, перечислив найденные
   инстансы с их `dataPath`. Не выбирать самому, не действовать по умолчанию.
   `AskUserQuestion` недоступен — вернуть вопрос в отчёте.

Повторить проверку после переподключения моста (перезапуск Editor'а, таймаут, смена `instance_count`): активный инстанс сбрасывается.

Если по ошибке что-то ушло не в тот проект — остановиться, сказать владельцу, что именно и куда
попало, и ничего не откатывать в чужом проекте без его явного «да».

## Порт-агентам (poki-port, gamepush-port, rustore-port, galaxy-port)

У этих агентов есть свои «Жёсткие границы», и CLI их не отменяет:

- **Batchmode запрещён** («Unity в batchmode не запускаешь никогда») — значит `unity build`,
  `unity test`, `unity run` им недоступны так же, как ручной batchmode-вызов.
- **Правка живого Editor'а** (`unity command`/`unity command eval`: сцены, ассеты, GameObject):
  - `poki-port`, `gamepush-port` — можно, когда нужной MCP-тулы нет; MCP остаётся первым выбором.
    `ProjectSettings` — только после «да» владельца на конкретное изменение;
  - `rustore-port`, `galaxy-port` — нельзя: у них ассеты и настройки проекта под запретом,
    а это та же операция, что и прямая правка `.unity`/`.prefab`, просто через другой канал.
- **Git — только на чтение** — значит из `unity vcs` можно `status`, `diff`, `blame`,
  `conflicts`, `summarize`, `affected`; нельзя `sync`, `switch`, `merge-setup`, `resolve`,
  `hooks` и всё, что меняет рабочее дерево или конфиг репозитория.

Пригождается — диагностика перед вопросом пользователю:

- `unity status` / `unity doctor` — есть ли у пользователя живой Editor, если это влияет
  на формулировку ручного шага («открой Unity и сделай X» vs «Unity уже открыт»);
- `unity editors --installed` + `unity projects info <path>` — сверить версию Editor'а
  у проекта с тем, что реально стоит на машине, до того как списывать баг на несовместимость;
- `unity vcs status/diff/blame/summarize` — как `git log/diff/status`, но с пониманием конфликтов в сценах и префабах.

Соблазн решить задачу через `unity build` (у `rustore-port`/`galaxy-port` ещё и через `unity command`) — сигнал остановиться и спросить пользователя, а не обходить границу через CLI.
