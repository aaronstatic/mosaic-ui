# Interaction

MosaicUI's *read* loop — `Store.SetProperty` → `propertyChanged` → selector `Subscribe` / UXML `dataSource` bindings → UI update — was established with the store system. The *write* loop closes it: a button click or field edit must flow back into state through a single, predictable path.

This document defines that path and the three opt-in layers that implement it.

---

## The Principle

> **Every user interaction — whether from a UI button, a form field, or a device action (keyboard, gamepad) — must have a non-UI invocation path identical to the real interaction.**

The click handler (and the device action callback) are thin shells over a store **action** (a method) or a named **command**. They never contain inline logic. This is the React/Zustand testing discipline applied to Unity: tests call the action or command directly and assert resulting state; they do not synthesize pointer events or device state.

**Device input is a non-UI invocation path.** A gamepad button mapped via `MapAction` and a UI button wired via `BindCommand` both call `CommandRegistry.Invoke(id)` — the same handler, identically, regardless of control scheme. The `CommandRegistry` is the single convergence point for both paths.

```
  WRITE                                    READ (already done)
  ─────                                    ──────────────────
  user click / field edit                  device action (keyboard, gamepad)
        │                                          │
        ▼                                          ▼
  Bind* helper (Layer 2)              MapAction* helper (Layer 2)
        │                                          │
        ├──────────────────── or ──────────────────┤
        ▼                                          ▼
  store action (Layer 1)      or      command (Layer 3)
        │                                   │  Commands.Invoke(id)
        │                                   ▼
        │                         registered handler ─── usually calls a store action
        │                                   │
        └───────────────────────────────────┘
                                          │
                                          ▼
                              Store.SetProperty(ref f, v)
                                          │
                                          ▼
                    propertyChanged ──► Subscribe selectors / UXML bindings ──► UI updates
```

The headless test path enters at the two boxes with no UI or device dependency: call a **store action** directly, or call **`MosaicUI.Commands.Invoke(id)`**, then assert via `store.Subscribe`.

---

## Layer 1 — Actions on Stores (Convention)

UI-triggered mutations are *store methods* with `private` backing setters. Nothing outside the store can poke a field directly.

```csharp
public class CounterStore : Store<CounterStore>
{
    private int _count;

    [CreateProperty]
    public int Count { get => _count; private set => SetProperty(ref _count, value); }

    // Actions — the only way external code mutates state.
    public void Increment() => Count++;
    public void Decrement() => Count--;
    public void Reset()     => Count = 0;
}
```

Layer 1 is pure convention and requires no new framework types. Its value is the discipline it enforces: every interaction is reachable without a pointer event, making behavior verifiable headlessly.

### Testing Layer 1

Test the action directly. No scene, no UI, no pointer events needed:

```csharp
var store = new CounterStore();
store.Increment();
Assert.That(store.Count, Is.EqualTo(1));
```

Because `SetProperty` uses `EqualityComparer<T>.Default`, calling `Reset()` when `Count` is already `0` fires no `propertyChanged` event. Tests can assert this no-op behavior explicitly.

---

## Layer 2 — Managed Interaction Helpers on `PanelController`

`PanelController` provides `protected` helper methods that wire UI callbacks and automatically drop an `IDisposable` into the controller's `Subscriptions` group. When the panel is disposed, all wired handlers are unhooked — no manual `OnDispose` cleanup required.

### `BindClick`

```csharp
// By element name — throws InvalidOperationException if no Button with that name is found.
BindClick("increment-btn", _store.Increment);

// By element reference.
BindClick(myButton, _store.Increment);
```

Wires `button.clicked += handler` and adds an auto-unhook disposable to `Subscriptions`.

### `BindValue<TValue>`

Two-way binds a `BaseField<TValue>` (TextField, Toggle, Slider, FloatField, …) to a store value:

```csharp
BindValue<string>("note-field", () => _store.Note, _store.SetNote);
```

- **Store → field:** applies the initial value with `SetValueWithoutNotify` (no `ChangeEvent` fires).
- **Field → store:** on `ChangeEvent<TValue>`, guards with `EqualityComparer<TValue>.Default` before calling the setter, preventing feedback loops and redundant store writes.

**v1 note:** `BindValue` does not push future external store changes back to the field. If you need that, add a separate subscription alongside:

```csharp
BindValue<string>("note-field", () => _store.Note, _store.SetNote);
Subscriptions.Add(_store.Subscribe(s => s.Note, v => noteField.SetValueWithoutNotify(v)));
```

### `BindCommand`

Wires a button click to `MosaicUI.Commands.Invoke`:

```csharp
// Parameterless command.
BindCommand("reset-btn", "counter/reset");

// Command with a typed payload (factory called at click-time).
BindCommand<int>("fire-btn", "weapon/fire", () => _selectedSlot);
```

### `MapAction` / `MapAction<T>`

The device sibling of `BindCommand`. Where `BindCommand` routes a UI button click to a `CommandRegistry` command, `MapAction` routes a device action (keyboard key, gamepad button, analogue axis) to the **same** command — both call `CommandRegistry.Invoke(id)` identically, regardless of control scheme.

```csharp
// Parameterless — device action's 'performed' phase invokes the command.
// Identical to: BindCommand("jump-btn", "player/jump")
MapAction("Player/Jump", "player/jump");

// Typed payload — factory called with the CallbackContext at fire-time.
MapAction<int>("Player/WeaponSlot", "weapon/select", ctx => (int)ctx.ReadValue<float>());
```

`MapAction` is available on `PanelController`, `WorldController`, and `WorldFeature`. It subscribes the action's `performed` phase (the discrete "it happened" moment — the device analogue of a click) and adds an auto-unhook `IDisposable` to `Subscriptions`, exactly as `BindCommand` adds an auto-unhook for the click handler. The command need not be registered at `MapAction`-time; an unregistered id throws `InvalidOperationException` from `CommandRegistry.Invoke` when the action fires.

> **The "same front door" principle:** a keyboard press and a UI button click that both invoke `"player/jump"` are indistinguishable to the `CommandRegistry` handler and to any test that calls `MosaicUI.Commands.Invoke("player/jump")` directly. Control scheme is irrelevant at the handler boundary.

See [InputBinding.md](InputBinding.md) for the full device-input integration guide (`BindAction*`, `ReadAction`, `MapAction`, the UI routing gate, and per-mode action maps).

### The `Commands` Accessor

Inside a `PanelController`, `Commands` is a shorthand for `MosaicUI.Commands`:

```csharp
Subscriptions.Add(Commands.Register("counter/reset", _store.Reset));
```

### Full `OnBind` Example

```csharp
public override void OnBind()
{
    _store = GetStore<CounterStore>();

    // Layer 3: register a panel-scoped command (freed on panel dispose).
    Subscriptions.Add(Commands.Register("counter/reset", _store.Reset));

    // Layer 2: managed interaction helpers.
    BindClick("increment-btn", _store.Increment);
    BindClick("decrement-btn", _store.Decrement);
    BindCommand("reset-btn", "counter/reset");       // click → Commands.Invoke
    BindValue<string>("note-field", () => _store.Note, _store.SetNote);

    // Reactive label updated in code (focus-independent).
    var label = Root.Q<Label>("count-label");
    label.text = $"Count: {_store.Count}";
    Subscriptions.Add(_store.Subscribe(s => s.Count, c => label.text = $"Count: {c}"));
}
```

---

## Layer 3 — Named Command Dispatch (`CommandRegistry`)

`MosaicUI.Commands` is a `CommandRegistry` — a directed, one-handler-per-id dispatch. It is distinct from `EventBus` (see [Commands vs Events](#commands-vs-events)).

### Registration and Invocation

```csharp
// Register — returns IDisposable; add to Subscriptions for auto-cleanup.
Subscriptions.Add(Commands.Register("counter/increment", _store.Increment));

// Register with typed payload.
Subscriptions.Add(Commands.Register<int>("weapon/fire", slot => _weapons.Fire(slot)));

// Invoke — calls the registered handler.
MosaicUI.Commands.Invoke("counter/increment");
MosaicUI.Commands.Invoke<int>("weapon/fire", 2);
```

### Introspection

```csharp
bool exists = MosaicUI.Commands.Has("counter/increment");
IReadOnlyCollection<string> all = MosaicUI.Commands.RegisteredIds;
```

### Error Behavior

| Situation | Result |
|---|---|
| `Invoke` on an unregistered id | `InvalidOperationException` naming the id |
| Duplicate `Register` of a live id | `InvalidOperationException` naming the id |
| `Invoke(id)` when handler is `Action<T>` | `InvalidOperationException` (arity mismatch; states expected vs supplied type) |
| `Invoke<T>(id, v)` when handler is `Action` | `InvalidOperationException` (arity mismatch) |
| `Invoke<T>(id, v)` when handler's `T` ≠ supplied `T` | `InvalidOperationException` (type mismatch; states expected vs supplied type) |

### Lifecycle

`Commands` is a static facade member, constructed by `MosaicUI.Initialize()` and cleared by `MosaicUI.Shutdown()`. `Initialize()` is idempotent — a second call is a no-op; the same instance is preserved.

```csharp
MosaicUI.Commands   // CommandRegistry; non-null after Initialize(), null after Shutdown()
MosaicUI.Services   // ServiceRegistry
MosaicUI.Events     // EventBus
```

---

## Commands vs Events

| | `CommandRegistry` (`MosaicUI.Commands`) | `EventBus` (`MosaicUI.Events`) |
|---|---|---|
| **Intent** | Directed / imperative: "do this" | Broadcast / notification: "this happened" |
| **Handlers** | Exactly one per id | 0..N per message type |
| **No handler** | Throws `InvalidOperationException` | Silent (no subscribers = no-op) |
| **Use case** | Button → action, automation test → command | Cross-panel events, game → UI notifications |
| **Introspection** | `Has(id)`, `RegisteredIds` | None (by design) |

**Rule of thumb:** if the calling code *requires* something to handle the message, use a command. If it is just announcing that something happened, use the event bus.

---

## Command-Id Namespacing Convention

Command ids are strings in `"feature/action"` format:

```
"counter/increment"
"counter/reset"
"inventory/equip"
"weapon/fire"
```

The prefix is the feature or domain area; the suffix is the imperative verb or operation. This makes ids self-describing in logs and automation scripts, and keeps panel-scoped ids from colliding with global ones.

Because each panel disposes its `Subscriptions` on teardown, a panel-scoped `Register` automatically frees the id. A remount re-registers cleanly without a duplicate-id collision.

---

## Testing Discipline

### Test actions directly, not DOM clicks

The `Bind*` helpers are the only UI-coupled part of the interaction system. The behavior they invoke — a store action or a command handler — is fully exercisable without a scene:

```csharp
// Good: test the unit of behavior.
store.Increment();
Assert.That(store.Count, Is.EqualTo(1));

MosaicUI.Commands.Invoke("counter/reset");
Assert.That(store.Count, Is.EqualTo(0));

// Avoid: synthesizing pointer events in EditMode to trigger BindClick.
// The click handler is a thin shell; test what it calls, not the shell.
```

### Use Subscribe for assertions, not direct field reads

Store subscriptions fire synchronously on the game thread, so you can assert immediately after an action without waiting a frame:

```csharp
int received = -1;
using var sub = store.Subscribe(s => s.Count, v => received = v);

store.Increment();

Assert.That(received, Is.EqualTo(1));
```

### Headless scene verification

When verifying the integration scene without pointer events, use `MosaicUI.Commands.Invoke` and store actions, then assert state via `Subscribe` or direct property reads. Update labels in `Subscribe` callbacks (not via UXML `dataSource` bindings that require editor focus to repaint) and verify visuals with `ScreenCapture.CaptureScreenshot`.

---

## See Also

- [Stores.md](Stores.md) — store system, `SetProperty`, selector subscriptions, `BindClick`/`BindValue`/`BindCommand` for interaction handlers
- [GettingStarted.md](GettingStarted.md) — end-to-end minimal counter panel walkthrough
- [Modes.md](Modes.md) — panel lifecycle (`OnBind`/`OnShow`/`OnHide`/`OnModeChanged`/`OnDispose`) and mode transitions
- [InputBinding.md](InputBinding.md) — device-input integration: `BindAction*`, `ReadAction`, `MapAction`, the UI routing gate, and per-mode action maps
