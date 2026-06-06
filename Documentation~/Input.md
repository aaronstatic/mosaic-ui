# Input

MosaicUI's input source service: `InputService`, exposed via the static facade as `MosaicUI.Input`. It wraps a consumer-supplied `InputActionAsset`, resolves named actions and maps, exposes service-level subscriptions to action phases, reads current action values, manages map enable/disable, and broadcasts a `ControlSchemeChanged` struct on `MosaicUI.Events` when the active control scheme switches.

> **Scope notice.** This document covers the *source service only*. Controller-level `BindAction` helpers, the action-to-command bridge, the UI-vs-world routing gate, and per-mode action-map activation are all provided by the sibling feature **`composition/input-binding`** and documented in [InputBinding.md](InputBinding.md).

> **Dependency notice.** Since this service is a first-class `MosaicUI` facade citizen, the `Mosaic.UI` runtime assembly now has a hard dependency on `com.unity.inputsystem` (1.19.0 or later). Every project that uses MosaicUI must have the Input System package installed.

---

## Accessing the service

```csharp
MosaicUI.Initialize(); // idempotent; called by MosaicUIManager.Start()
InputService input = MosaicUI.Input;
```

`MosaicUI.Input` is `null` before `Initialize()` and after `Shutdown()`.

---

## Assigning an asset

The service owns no hardcoded bindings. Assign a `InputActionAsset` once (typically from a serialized field on a manager) before using any resolution or subscription methods:

```csharp
[SerializeField] private InputActionAsset _inputActions;

private void Awake()
{
    MosaicUI.Initialize();
    MosaicUI.Input.SetAsset(_inputActions);
}
```

`SetAsset` performs a clean hot-swap if called with a second asset while the first is in use: every map the service enabled is disabled and every outstanding handler is detached before the new asset is stored. The new asset is stored *without enabling anything* — callers enable maps explicitly.

Passing `null` to `SetAsset` throws `ArgumentNullException`.

Calling any resolution or subscription method before `SetAsset` throws `InvalidOperationException("No InputActionAsset has been assigned. Call SetAsset(...) before resolving actions.")`.

---

## Resolving actions and maps by name

Actions are resolved by **qualified name** (`"Map/Action"`) and maps by **plain name** (`"Map"`). This is InputSystem's native `FindAction` syntax.

If the name is not found in the assigned asset, the service throws a house-style `InvalidOperationException` whose message contains the quoted name:

```
Action 'Player/Nope' is not found in the assigned InputActionAsset.
Action map 'Ghost' is not found in the assigned InputActionAsset.
```

These messages are consistent in form with `ServiceRegistry.Get<T>()` and `CommandRegistry.Invoke`: loud, named, single-quoted. There are no silent no-ops on a bad name — a mistyped name is a bug.

---

## Service-level subscriptions

Subscribe to a named action's `started`, `performed`, or `canceled` phase. Each call returns an `IDisposable` whose `Dispose()` detaches the callback. Double-disposing is a no-op.

```csharp
IDisposable token = MosaicUI.Input.SubscribePerformed("Player/Jump", ctx =>
{
    Debug.Log("Jump performed!");
});

// Later — detach the callback:
token.Dispose();
```

The three methods:

| Method | InputSystem event |
|---|---|
| `SubscribeStarted(name, callback)` | `action.started` |
| `SubscribePerformed(name, callback)` | `action.performed` |
| `SubscribeCanceled(name, callback)` | `action.canceled` |

Subscriptions track against the action map's enabled/disabled state: if the map is disabled (see [Enabling maps](#enabling-maps)), a subscribed action will not fire.

Passing a `null` callback throws `ArgumentNullException`. Resolving an unknown action name throws `InvalidOperationException` naming the action.

---

## Reading the current value

Read the current value of an action without subscribing to its phase events:

```csharp
Vector2 move = MosaicUI.Input.ReadValue<Vector2>("Player/Move");
float trigger = MosaicUI.Input.ReadValue<float>("Player/Fire");
```

`ReadValue<TValue>` resolves the action and delegates to `InputAction.ReadValue<TValue>()`. A type or size mismatch surfaces InputSystem's own exception (already clear and specific — it is not wrapped here). `TValue` must be a `struct`.

---

## Enabling maps

Action maps are disabled by default on a freshly-assigned asset. Enable and disable maps by name:

```csharp
MosaicUI.Input.EnableMap("Player");
// ... input events flow ...
MosaicUI.Input.DisableMap("Player");
```

Re-enabling an already-enabled map and disabling a not-enabled map are both harmless no-ops. An unknown map name throws `InvalidOperationException` naming the map.

The service tracks which maps it has enabled so that `Dispose()` (see [Teardown](#teardown)) can disable them all, leaving no enabled InputSystem state behind.

> Per-mode map activation — enabling `Player` on mode enter and disabling it on mode exit — is provided by **`composition/input-binding`** via `MosaicUIManager.SetMode()` (see [InputBinding.md § Per-mode action maps](InputBinding.md)). This service exposes the `EnableMap`/`DisableMap` lever; the manager drives it.

---

## Active control scheme and `ControlSchemeChanged`

`MosaicUI.Input.ActiveControlScheme` returns the name of the currently active control scheme (e.g. `"Keyboard&Mouse"`, `"Gamepad"`), or `null` when no scheme is active.

When the active scheme changes, a `ControlSchemeChanged` struct is published on `MosaicUI.Events`:

```csharp
public struct ControlSchemeChanged
{
    public string Previous;  // name of the prior scheme, or null if none was active
    public string Current;   // name of the new scheme, or null if no scheme is now active
}
```

Subscribe to it the same way as any other event:

```csharp
MosaicUI.Events.Subscribe<ControlSchemeChanged>(e =>
{
    Debug.Log($"Scheme changed: {e.Previous} → {e.Current}");
    UpdatePromptIcons(e.Current);
});
```

Scheme detection is event-driven via `InputUser.onChange` filtered to `InputUserChange.ControlSchemeChanged`. There is no polling of `Keyboard.current`, `Gamepad.current`, or any `Device.current` — the service is notified only when the scheme actually changes. The equality guard means a redundant notification for the same scheme name publishes nothing.

---

## Teardown

`MosaicUI.Shutdown()` calls `InputService.Dispose()`, which:

- Disables every map the service enabled.
- Detaches every outstanding subscription callback.
- Removes the `InputUser.onChange` scheme-tracking hook.
- Drops the asset reference.

After `Dispose()`, driving any previously-subscribed action invokes nothing and `MosaicUI.Input` becomes `null`. A second `Dispose()` (or a second `Shutdown()`) is a no-op.

---

## Integration with `composition/input-binding`

The following capabilities are **provided by `composition/input-binding`** (see [InputBinding.md](InputBinding.md)):

- **`BindAction*` / `ReadAction` helpers** on `PanelController` / `WorldController` / `WorldFeature` — subscribe to a named action's phases and read its value, auto-disposed via `Subscriptions`.
- **`MapAction` / `MapAction<T>` bridge** — a device action's `performed` phase dispatches a `CommandRegistry` command, identically to `BindCommand`.
- **`UIRoutingGate`** — the authoritative "is the pointer/keyboard focus over a MosaicUI panel or window?" query, so world input can stand down when the UI owns the pointer.
- **Per-mode action-map activation** — `ModeDefinition` declares which maps to enable; `MosaicUIManager.SetMode()` calls `EnableMap`/`DisableMap` on this service as part of the mode diff.
- **`MosaicUIManager.inputActions`** — a serialized field that auto-wires the asset via `SetAsset` in `Start()`.

`composition/input-binding` consumes this service's public surface (`SetAsset`, `SubscribeStarted`/`Performed`/`Canceled`, `ReadValue`, `EnableMap`/`DisableMap`) unchanged. No further changes to `InputService` are required.
