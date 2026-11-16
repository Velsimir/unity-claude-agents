# Система UI

Стековая навигация поверх кэша инстансов. Страницы **не уничтожаются** при закрытии — для
мобильной казуалки постоянный спавн и Destroy окон дороже удержанной памяти.

## 1. Слои

Порядок отрисовки задаётся слоем, а не вызовами `SetAsLastSibling`:

```csharp
public enum UILayer
{
    Hud     = 0,    // всегда снизу, не перекрывает ничего
    Window  = 100,  // полноэкранные вкладки: магазин, инвентарь, статы
    Popup   = 200,  // модалки, подтверждения, награды
    Overlay = 300,  // тосты, всплывашки, подсказки туториала
    Loading = 400,  // экран загрузки между сценами
    System  = 500,  // «нет сети», «обновите игру»
}
```

Каждый слой — свой `Canvas` с фиксированным `sortingOrder` и своим root-трансформом. Страница
декларирует слой в инспекторе, провайдер инстанцирует её в root нужного слоя. Булевы флаги вида
`onTopOfNavigation` и спецметоды навигации не нужны: порядок вытекает из слоя.

## 2. Интерфейсы страниц

```csharp
IPageBase            PageId, UILayer, ReadOnlyReactiveProperty<PageState> State, UniTask HideAsync()
├ IPage              UniTask ShowAsync()
├ IPayloadPage<T>    UniTask ShowAsync(T payload)
└ IResultPage<T>     UniTask<T> ResultAsync()
```

Базовые классы: `PageBase` (`MonoBehaviour`) → `Page` и `PayloadPage<T>`.

`PageState`: `Hidden → Showing → Shown → Hiding → Hidden`. `ReactiveProperty`, наружу
`ReadOnlyReactiveProperty` — всё внешнее подписывается на него.

**Методы асинхронные, `async void` нет.** `ShowAsync` / `HideAsync` возвращают `UniTask`,
`PageService` их ждёт. Анимация показа — переопределяемый `OnShowAnimationAsync()`.

## 3. Лайфтайм страницы

Два примитива, привязанных к периоду показа:

```csharp
protected CancellationToken HiddenToken { get; }        // отменяется при уходе в Hidden
protected CompositeDisposable HiddenDisposables { get; } // очищается при уходе в Hidden
```

Подписки живут ровно между Show и Hide, отписываться вручную не нужно. Всё, что страница
запускает, получает `HiddenToken` — закрыли окно, задачи отменились.

**Контракт очистки.** Инстанс кэшируется, поэтому страница обязана сбросить своё состояние в
`protected virtual void OnHidden()` — иначе кэш становится утечкой состояния между открытиями.

## 4. IPageService

Семь методов, ничего проектно-специфичного:

```csharp
UniTask ShowAsync<TPage>()                            where TPage : class, IPage;
UniTask ShowAsync<TPage, TPayload>(TPayload payload)  where TPage : class, IPayloadPage<TPayload>;
UniTask<TResult> ShowForResultAsync<TPage, TResult>() where TPage : class, IPage, IResultPage<TResult>;
UniTask HideAsync<TPage>()                            where TPage : class, IPageBase;
UniTask HideTopAsync();
bool IsOpen<TPage>()                                  where TPage : class, IPageBase;
ReadOnlyReactiveProperty<Type> TopPage { get; }
```

Навигация по вкладкам HUD, открытие магазина на конкретной вкладке и прочая специфика живут
**в игровом слое поверх** этого API, а не внутри него.

Ожидание закрытия — мост R3 в UniTask: `await page.State.FirstAsync(s => s == PageState.Hidden)`.

## 5. Блокер ввода

На время анимаций Show и Hide поверх всего включается прозрачный `InputBlocker` с
`blocksRaycasts = true`. Закрывает класс багов: двойное нажатие открывает два окна, шлёт два
запроса, списывает две покупки.

## 6. PagesConfig и провайдер

`PagesConfig` — `ScriptableObject` со списком префабов страниц и root-трансформами слоёв.
`PagesProvider` инстанцирует страницу при первом показе, дальше отдаёт из кэша; `PageId`
прокидывается через sub-container:

```csharp
_factory.Instantiate<IPageBase>(prefab, layerRoot, container => container.BindInstance(new PageId(type)));
```

**Валидация при инициализации, не в рантайме.** Провайдер один раз проверяет весь конфиг: нет
дублей типов, нет null-префабов, на каждом префабе есть компонент страницы, у каждого слоя задан
root. Всё найденное — одним `LogError` со списком, в редакторе ещё `throw`. Молчаливый `return`
при отсутствии префаба запрещён.

## 7. Кнопка «Назад» на Android

Стек уже есть, обработчик — одна строка: `HideTopAsync()`. Стек пуст — диалог выхода. Заводится
сразу, чтобы не переделывать под релиз.

## 8. MVP на страницах

Страница — это View, логики не содержит: её Presenter биндится в инсталлере сцены, получает
страницу через `BindInstance` и подписывается на её `Observable`-события.

Исключение одно: у `PageBase` `[Inject]` конструктор для `PageId` и издателя событий состояния —
инфраструктура системы страниц, а не игровые сервисы.
