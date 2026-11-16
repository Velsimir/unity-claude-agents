# Фасад платформенных SDK

Универсальный «инжектор» SDK, как у GamePush: игровой код не знает ни одного вендора, площадка
меняется подменой одного ассета.

## 1. Состав

```csharp
public interface IPlatformSdk
{
    IAdsSdkService Ads { get; }
    IPurchaseSdkService Purchases { get; }
    IAnalyticsSdkService Analytics { get; }
    IAuthSdkService Auth { get; }
    ICloudSaveSdkService CloudSave { get; }
    ILeaderboardSdkService Leaderboards { get; }
    IGameplaySdkService Gameplay { get; }

    bool Supports(PlatformCapability capability);
    UniTask InitializeAsync(CancellationToken cancellationToken);
}
```

Игровой код зависит **только** от подинтерфейсов. `GamePush`, `Yandex`, `Poki`,
`LevelPlay`, `Galaxy` живут исключительно внутри своих адаптеров. Адаптеры пишут агенты
`poki-port`, `gamepush-port`, `rustore-port`, `galaxy-port`.

## 2. Заглушка — реализация по умолчанию

`EditorStubSdk` — нормальный рабочий режим, в редакторе стоит всегда:

- `ShowRewardedAsync` ждёт секунду и возвращает `true`;
- `Purchase` немедленно завершается успехом;
- аналитика пишет в `GameDebug`;
- облако пишет в тот же локальный файл.

Главное требование: игра полностью проходима без единого реального SDK — экономику проверяют, не
дожидаясь интеграции рекламы.

## 3. Capabilities

```csharp
public enum PlatformCapability
{
    RewardedAds, InterstitialAds, BannerAds,
    Purchases, CloudSave, Leaderboards, Auth, Share,
}
```

`Supports()` — чтобы UI сам прятал кнопку, которой на площадке нет: нет лидербордов — кнопка не
рисуется, а не падает при нажатии.

## 4. Выбор реализации

`PlatformSdkConfig : ScriptableObject` задаёт, какая реализация биндится; `PlatformInstaller`
читает его и биндит адаптеры. Смена площадки = смена одного ассета, ни строчки игрового кода.

## 5. Инициализация

`InitializeAsync` — первый шаг конвейера Boot, **с таймаутом и фолбэком на заглушку**. Зависший
SDK не блокирует запуск: по таймауту `LogError`, подмена на заглушку, дальше.

Ни один метод SDK не пробрасывает исключение в игровой код. Ошибка — `false` или пустой ответ
плюс запись в лог.

## 5a. Геймплей и пауза

`IGameplaySdkService` (`GameplayStarted` / `GameplayStopped`) — сигнал площадке, играет ли игрок
(Poki, Яндекс и подобные требуют его для рекламы и аналитики). Игровой код напрямую его не зовёт:
это делает `PauseService` на первом и последнем токене `PauseEffect.Gameplay`, см.
`ARCHITECTURE.md`, раздел 5a. Перекрывающиеся источники (реклама, попап, катсцена) складываются
счётчиком.

Адаптер, которому «старт без ввода игрока» не нужен или запрещён, решает это внутри себя.
Заглушка только пишет в лог.

Платформенный слой не знает о `Core`, поэтому паузу вокруг рекламы берёт вызывающий код:

```csharp
using (_pauseService.Acquire(PauseEffect.Ad))
    await _platformSdk.Ads.ShowRewardedAsync(placement, cancellationToken);
```

Добавление `Gameplay` в `IPlatformSdk` ломает сборку готовых адаптеров: им нужен
`IGameplaySdkService` (допустима пустая реализация).

## 6. Что не входит

Медиация, консент, ATT, возрастные ограничения — детали конкретного адаптера, фасад про них не знает.
