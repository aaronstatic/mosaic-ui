# Stores

MosaicUI's state management is built around a single base class: `Store<TSelf>`. The design is inspired by Zustand — stores are plain classes (not MonoBehaviours), state is mutated through property setters, and subscribers receive only the slice of state they care about.

---

## Creating a Store

A store is a class that extends `Store<TSelf>` where `TSelf` is the store's own type:

```csharp
using Unity.Properties;
using Mosaic.UI;

public class PlayerStore : Store<PlayerStore>
{
    private string _playerName;
    private int _credits;
    private float _health;

    [CreateProperty]
    public string PlayerName
    {
        get => _playerName;
        set => SetProperty(ref _playerName, value);
    }

    [CreateProperty]
    public int Credits
    {
        get => _credits;
        set => SetProperty(ref _credits, value);
    }

    [CreateProperty]
    public float Health
    {
        get => _health;
        set => SetProperty(ref _health, value);
    }
}
```

### Rules for store properties

- Backing fields are private
- The property getter returns the backing field
- The property setter calls `SetProperty(ref _field, value)`
- The `[CreateProperty]` attribute is required for UI Toolkit data binding; it is not required for selector subscriptions alone

`SetProperty` is generic and uses `EqualityComparer<T>.Default` to compare the incoming value against the current field value. If the values are equal, the assignment is skipped and no event is fired. If they differ, the field is assigned, the internal version counter is incremented, and `propertyChanged` is invoked with the property name.

---

## Registering Stores

Stores must be registered in `MosaicUI.Services` before any panel's `OnBind` method runs. The recommended pattern is a bootstrap MonoBehaviour that runs in `Awake`:

```csharp
using Mosaic.UI;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        MosaicUI.Initialize();

        MosaicUI.Services.CreateStore<PlayerStore>();
        MosaicUI.Services.CreateStore<InventoryStore>();
        MosaicUI.Services.CreateStore<MapStore>();
    }
}
```

`CreateStore<TStore>()` instantiates the store with `new TStore()`, calls `Register<TStore>(instance)`, and returns the instance. You can also instantiate a store manually and register it separately:

```csharp
var store = new PlayerStore();
store.PlayerName = "Commander";
MosaicUI.Services.Register(store);
```

Stores are retrieved by their concrete type. If you need to register a store against an interface, call `Register` directly:

```csharp
MosaicUI.Services.Register<IPlayerStore>(new PlayerStore());
```

---

## Selector Subscriptions

The primary way to react to state changes in a `PanelController` is with `Subscribe<TSlice>`:

```csharp
public override void OnBind()
{
    var store = GetStore<PlayerStore>();

    Subscriptions.Add(store.Subscribe(
        selector: s => s.Credits,
        callback: credits => _creditsLabel.text = credits.ToString("N0")
    ));
}
```

The selector is a `Func<TSelf, TSlice>` that extracts a value from the store. The callback fires only when the selected value changes — if `Credits` did not change, the callback does not run, even if another property on the store was modified.

### Subscribing to derived values

Selectors can compute derived values:

```csharp
Subscriptions.Add(store.Subscribe(
    s => s.Health / s.MaxHealth,
    ratio => _healthBar.value = ratio
));
```

The `EqualityComparer<TSlice>.Default` comparison is applied to the selector's return value. For value types (float, int, bool, Vector2, etc.) this is a value comparison. For reference types, it is a reference comparison unless the type overrides `Equals`.

### Subscribing to multiple properties

To react when any one of several properties changes, use a struct or tuple as the slice:

```csharp
Subscriptions.Add(store.Subscribe(
    s => (s.Credits, s.PlayerName),
    slice =>
    {
        _creditsLabel.text = slice.Credits.ToString();
        _nameLabel.text = slice.PlayerName;
    }
));
```

Anonymous tuples implement structural equality in C#, so the callback fires whenever either field changes.

### Subscription lifetime

All subscriptions added to a controller's `Subscriptions` group are disposed automatically when the panel is disposed. You do not need to manually unsubscribe store selectors.

For button clicks and field changes, use the `BindClick`, `BindValue`, and `BindCommand` helpers on `PanelController` — they register the callback *and* add an auto-unhook disposable to `Subscriptions`, so no manual cleanup in `OnDispose` is required. Wire interactions to store actions (methods like `Increment()`) rather than inline lambdas. See [Interaction.md](Interaction.md) for the full interaction system including `MosaicUI.Commands` for named command dispatch.

---

## Setting Initial Values

The subscription callback fires when the selected slice changes — it does not fire immediately on subscribe. To set the initial UI state, read from the store directly in `OnBind`:

```csharp
public override void OnBind()
{
    var store = GetStore<PlayerStore>();

    _creditsLabel.text = store.Credits.ToString("N0");

    Subscriptions.Add(store.Subscribe(
        s => s.Credits,
        credits => _creditsLabel.text = credits.ToString("N0")
    ));
}
```

---

## UI Toolkit Data Binding

Because `Store<TSelf>` implements `INotifyBindablePropertyChanged` and `IDataSourceViewHashProvider`, stores can be used directly as a `dataSource` for UI Toolkit's binding system.

### UXML binding

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Label name="credits-label"
              binding-path="Credits" />
</ui:UXML>
```

In the controller:

```csharp
public override void OnBind()
{
    var store = GetStore<PlayerStore>();
    Root.dataSource = store;
}
```

When `store.Credits` changes, `propertyChanged` fires with the property name `"Credits"`. UI Toolkit's binding system picks this up and re-evaluates the `Credits` binding path.

### When to use data binding vs. selector subscriptions

| Scenario | Recommendation |
|---|---|
| Simple label / progress bar mirroring a single property | Data binding (less code) |
| Computed or derived values (e.g., `Health / MaxHealth`) | Selector subscription |
| Conditional logic on change (e.g., play a sound when health drops below 20%) | Selector subscription |
| Lists bound to a `DataList` or `ListView` | Data binding via `dataSource` |
| Cross-panel communication | `EventBus` (not stores) |

---

## Using Stores Outside Controllers

The `MosaicUI` static facade exposes three global members: `MosaicUI.Services` (service/store registry), `MosaicUI.Events` (broadcast event bus), and `MosaicUI.Commands` (directed command dispatch). Stores are accessed through `Services`.

Stores are plain C# objects. Any code that has access to `MosaicUI.Services` can read or write to them:

```csharp
// From a MonoBehaviour
private void OnPlayerEarnedCredits(int amount)
{
    var store = MosaicUI.Services.Get<PlayerStore>();
    store.Credits += amount;
}
```

```csharp
// From an ECS system (non-Burst)
var store = MosaicUI.Services.Get<PlayerStore>();
store.Health = playerHealth;
```

Stores are not thread-safe. All reads and writes should happen on the main thread.

---

## Best Practices

**Keep stores focused.** One store per domain area (player stats, inventory, map state) is easier to reason about than a single monolithic store. Panels can access multiple stores.

**Do not put game logic in stores.** Stores should hold state and optionally expose simple mutation helpers (like `Increment()`). Complex game logic belongs in systems, managers, or controllers.

**Prefer selectors for granular subscriptions.** Subscribing to a narrow slice (a single int) is cheaper than subscribing to the entire store and diffing manually.

**Avoid storing Unity objects in stores.** `GameObject`, `Transform`, `Texture2D`, and similar managed Unity objects in stores can cause issues with the equality comparer and create unintended references across scenes. Store IDs or value types instead, and resolve the actual object at use time.

**Initialize stores before the first mode transition.** `MosaicUIManager.Start()` defers one frame before calling `SetMode`. Registering stores in `Awake` or at `Start` script execution order before `MosaicUIManager` ensures they are available when panels call `OnBind`.
