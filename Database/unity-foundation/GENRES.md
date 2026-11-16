# Жанры и состав фундамента

Мобильные проекты: idle/clicker, match-3/puzzle, hyper-casual, runner, tower-defense, вплоть до
midcore. Ядро одинаковое, различаются опциональные механики и пакеты.

## 1. Ядро — ставится всегда, независимо от жанра

DI-каркас и инсталлеры · Boot с конвейером загрузки · Curtain · SceneLoader на enum ·
Save (участники, конверт секций, политика записи) · UI (страницы, стек, слои, MVP) ·
TickService · Factory + ObjectPool · IPlatformSdk с Editor-заглушкой · GameDebug ·
StaticStateResetter.

Редакторных валидаторов нет: агент проверяет свою работу сам по окончании сборки.

Пакеты ядра: Zenject, R3, MessagePipe (+Zenject), UniTask, DOTween, Newtonsoft.Json.
Git-URL'ы — в `../unity-plugins/UNITY_PLUGINS.md`.

## 2. Механики по жанрам

Берутся из `MECHANICS/`. Механики там нет — агент так и говорит, реализацию на месте не выдумывает.

| Жанр | Почти всегда нужно | Часто нужно |
|---|---|---|
| idle / clicker | Wallet, AFK-награды, Shop/IAP, Boosters, Daily | Quests, Prestige, Offers |
| match-3 / puzzle | Wallet, Lives/Energy, Shop/IAP, LevelMap | Daily, Quests, Leaderboard |
| hyper-casual | минимум: Wallet, Ads-обёртка | Skins, Daily |
| runner | Wallet, Shop, Skins, Boosters | Daily, Quests |
| tower-defense | Wallet, Inventory, Upgrades, LevelMap | Quests, Leaderboard |
| midcore | Wallet, Inventory, Equipment, Quests, Shop, Progression | Leaderboard, Guilds, Battle Pass |

## 3. Опциональные пакеты по потребности

| Потребность | Пакет |
|---|---|
| 2D-скелетная анимация | Spine |
| Много контента, загрузка по требованию | Addressables |
| Баланс из таблиц | BakingSheet |
| Локализация | Unity Localization или свой JSON-лоадер |
| Push-уведомления | `com.unity.mobile.notifications` |
| Реклама и IAP | не пакет, а адаптер `IPlatformSdk` под нужную площадку |
| Ускорение итераций | FastScriptReload |

## 4. Что агент спрашивает перед генерацией

1. Жанр из списка выше.
2. Имя проекта → корневой namespace.
3. Целевые платформы (Android / iOS / WebGL-площадка).
4. Какие опциональные механики подключить сразу.

Ответы определяют состав `manifest.json`, набор механик из `MECHANICS/`, содержимое
`MetaInstaller` и адаптер `IPlatformSdk` по умолчанию.
