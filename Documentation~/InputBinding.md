# Input Binding

This document covers the **integration half** of MosaicUI's input story: how device input flows into panels, world controllers, the command registry, and the mode lifecycle. The source service (`InputService`, `MosaicUI.Input`) is documented in [Input.md](Input.md); interaction write-loop patterns are in [Interaction.md](Interaction.md).

---

## Overview

`composition/input-binding` delivers four capabilities on top of the `InputService` source:

1. **`BindAction*` / `ReadAction` helpers** on `PanelController`, `WorldController`, and `WorldFeature` — react to a named action's phases and read its value, auto-disposed via the controller's `Subscriptions` group. Replaces per-frame `Device.current` / `Mouse.current` polling.
2. **`MapAction` / `MapAction<T>` bridge** — a device action dispatches a `CommandRegistry` command, identically to a `BindCommand` button click. One `CommandRegistry`; two front doors.
3. **`UIRoutingGate`** — an authoritative, queryable gate answering "is the pointer / keyboard focus captured by a MosaicUI panel or window?", so world-facing input can stand down when the UI already owns the pointer.
4. **Per-mode action maps** — `ModeDefinition` declares which maps are active for a mode; `MosaicUIManager.SetMode()` enables/disables them in lock-step with panel/world diffs.

---

## 1. `BindAction*` and `ReadAction`

### Concept

`BindAction*` is the device sibling of `BindClick`: it subscribes a handler to a named action's phase event and adds an auto-unhook disposable to the controller's `Subscriptions` group. When the panel/controller is disposed, the handler is detached automatically — no leaked InputSystem callbacks.

`ReadAction<TValue>` reads the current value of a named action without subscribing to a phase event. Use it in `Update()` loops to poll continuous input (analogue axes, pointer position).

### API — all three bases

The same `protected` methods are available on `PanelController`, `WorldController`, and `WorldFeature`:

```csharp
// Subscribe to a phase — returns an IDisposable already in Subscriptions.
protected IDisposable BindActionStarted (string actionName, Action<InputAction.CallbackContext> handler);
protected IDisposable BindActionPerformed(string actionName, Action<InputAction.CallbackContext> handler);
protected IDisposable BindActionCanceled (string actionName, Action<InputAction.CallbackContext> handler);

// Read the current value (struct TValue) without subscribing.
protected TValue ReadAction<TValue>(string actionName) where TValue : struct;
```

### Usage on a `WorldController`

```csharp
public class MapCameraController : WorldController
{
    private MapCameraStore _store;

    public override void OnActivated()
    {
        _store = GetStore<MapCameraStore>();

        // Discrete pick — fires once per click (performed phase).
        // Auto-disposed when this controller is deactivated/disposed.
        BindActionPerformed("Camera/Pick", ctx =>
        {
            var pos = ReadAction<Vector2>("Camera/PointerPos");
            // ... raycast and update store
        });
    }

    private void Update()
    {
        // Continuous orbit — polled every frame.
        var orbit = ReadAction<Vector2>("Camera/Orbit");
        if (orbit.sqrMagnitude > 0.001f)
            _store.Orbit(orbit * sensitivity);
    }
}
```

### Usage on a `PanelController`

```csharp
public override void OnBind()
{
    _store = GetStore<GameStore>();

    // Subscribe to a gamepad button while this panel is alive.
    BindActionPerformed("Player/Jump", _ => _store.Jump());
}
```

### Error behaviour

| Situation | Result |
|---|---|
| Unknown action name | `InvalidOperationException` naming the action (propagated from `InputService`) |
| No asset assigned yet | `InvalidOperationException` ("No `InputActionAsset` has been assigned") |
| Null handler | `ArgumentNullException` |

---

## 2. `MapAction` / `MapAction<T>` — the action→command bridge

### Concept

Where `BindCommand` routes a UI button click to a `CommandRegistry` command, `MapAction` routes a device action's `performed` phase to the **same** command — both call `CommandRegistry.Invoke(id)` identically. This is the "same front door" principle: a gamepad press and a UI button click are indistinguishable at the handler boundary.

See [Interaction.md](Interaction.md) for the full write-loop doctrine.

### API

```csharp
// Parameterless — performed phase invokes the command.
protected IDisposable MapAction(string actionName, string commandId);

// Typed payload — factory called with the CallbackContext at fire-time.
protected IDisposable MapAction<T>(string actionName, string commandId,
    Func<InputAction.CallbackContext, T> payload);
```

Available on `PanelController`, `WorldController`, and `WorldFeature`.

### Example

```csharp
// In OnBind / OnActivated — wires up before the mode is active.
Subscriptions.Add(Commands.Register("player/jump", _store.Jump));

// BindCommand: UI button click → command.
BindCommand("jump-btn", "player/jump");

// MapAction: device action's performed phase → the same command.
// A gamepad South button and the UI button both call Commands.Invoke("player/jump").
MapAction("Player/Jump", "player/jump");

// Typed payload — e.g. passing the scroll delta to a zoom command.
MapAction<float>("Camera/Zoom", "camera/zoom", ctx => ctx.ReadValue<float>());
```

### Key properties

- **Default phase is `performed`** — the discrete "it happened" moment, the device analogue of a click.
- **The command need not be registered at `MapAction`-time** (mirrors `BindCommand`). An unregistered id throws `InvalidOperationException` from `CommandRegistry.Invoke` when the action fires.
- **Auto-disposed via `Subscriptions`** — the token is added to the controller's group and detached on teardown.

---

## 3. `UIRoutingGate` — the UI-vs-world routing gate

### Concept

When the user clicks, should the world act on it, or did the UI panel/window already consume it? `UIRoutingGate` answers this authoritatively using the UI Toolkit runtime panel — no `EventSystem` dependency.

Because `WindowManager` parents floating windows into the **same** `UIDocument` tree as panels, a single `IPanel.Pick` covers both panels and windows.

### How to reach the gate

The manager owns and registers the gate in `MosaicUI.Services`. World controllers reach it the same way they reach stores:

```csharp
public override void OnActivated()
{
    var gate = Services.Get<UIRoutingGate>();
    // ... use it below
}
```

### API

```csharp
// True when the screen position lands on a real UI element (not the pick-ignored root).
bool IsPointerOverUI(Vector2 screenPos);

// True when a UI element (e.g. a TextField) holds keyboard focus.
bool IsKeyboardCaptured();

// The recommended world-input guard — returns true = proceed, false = suppress.
// takeRawInput = true bypasses the gate (explicit opt-out for intentional raw input).
bool ShouldHandleWorldPointer(Vector2 screenPos, bool takeRawInput = false);
```

### Usage pattern in a world controller

```csharp
private UIRoutingGate _gate;

public override void OnActivated()
{
    _gate = Services.Get<UIRoutingGate>();

    // Discrete pick: check the gate before raycasting.
    BindActionPerformed("Camera/Pick", ctx =>
    {
        var pos = ReadAction<Vector2>("Camera/PointerPos");
        if (!_gate.ShouldHandleWorldPointer(pos))
            return;   // pointer is over a UI panel — do not pick into the world

        PerformRaycast(pos);
    });
}

private void Update()
{
    var pos = ReadAction<Vector2>("Camera/PointerPos");

    // Continuous orbit: only orbit when the pointer is NOT over UI.
    var orbitDelta = ReadAction<Vector2>("Camera/Orbit");
    if (orbitDelta.sqrMagnitude > 0.001f && _gate.ShouldHandleWorldPointer(pos))
        _store.Orbit(orbitDelta * sensitivity);
}
```

### Pointer coordinate convention

The pointer position must come from the input layer, not from `Mouse.current`:

```csharp
// Correct — read from the asset action:
var pos = ReadAction<Vector2>("Camera/PointerPos");
// Bind "Camera/PointerPos" to <Pointer>/position (or <Mouse>/position) in the asset.

// Wrong — direct device polling:
// var pos = Mouse.current.position.value;  // ← not allowed in the archetype
```

The gate passes `screenPos` to `RuntimePanelUtils.ScreenToPanel` internally, which applies the `PanelSettings` scale. The exact Y-flip convention is validated in the reference scene (the T4.0 spike finding: Input System `Pointer.current.position` is bottom-left origin; `ScreenToPanel` maps it correctly).

### Safe defaults

- When no UIDocument is built yet, `IsPointerOverUI` and `IsKeyboardCaptured` both return `false` (no `NullReferenceException`).
- Elements with `picking-mode: ignore` are skipped by `IPanel.Pick` — clicking on empty space between panels correctly reports "not over UI".

### ⚠️ Picking-mode requirement for the layout shell

For the gate to report empty (non-panel) areas as **not** over UI, the full-screen **layout shell must be picking-transparent** — otherwise `IPanel.Pick` returns the shell everywhere and the gate suppresses *all* world input. Two parts, one you get for free:

1. **Framework (automatic):** `MosaicUIManager.ApplyLayout` now sets the `UIDocument` root **and** the cloned layout `TemplateContainer` wrapper to `PickingMode.Ignore`. You don't do anything for these.
2. **Your layout UXML (you do this):** mark your layout's own full-screen root element `picking-mode="Ignore"` — as a **UXML attribute**, not a USS style:

   ```xml
   <!-- correct: picking-mode is a UXML attribute / C# property -->
   <ui:VisualElement name="layout-root" picking-mode="Ignore" style="flex-grow: 1;" />

   <!-- WRONG: picking-mode is NOT a USS property; this is silently ignored -->
   <ui:VisualElement name="layout-root" style="flex-grow: 1; picking-mode: ignore;" />
   ```

Slotted panels/windows keep their default `PickingMode.Position`, so buttons, fields, and panel backgrounds remain pickable — only the empty shell is click-through. (This requirement was surfaced by the map-camera reference scene; see `MapLayout.uxml`.)

---

## 4. Per-mode action maps

### Concept

A `ModeDefinition` declares which input maps are active while that mode is on screen. `MosaicUIManager.SetMode()` enables/disables maps in lock-step with the panel and world diff — after a transition, exactly the declared maps are enabled.

### Wiring the asset

In the Inspector on the `MosaicUIManager` GameObject, under the **"Input"** header, assign the `InputActionAsset` to the **`inputActions`** field. The manager calls `MosaicUI.Input.SetAsset(asset)` in `Start()` (null-guarded: leaving the field empty makes per-mode maps inert, no throw).

### Declaring maps on a `ModeDefinition`

In the Inspector on a `ModeDefinition` ScriptableObject, the **"Action Maps"** list (a `List<string>`) declares which map names to enable. Each name must match an `InputActionMap` name in the assigned `InputActionAsset`.

```
ModeDefinition "MapCameraMode"
  Panels:         [MapHudPanel → hud]
  World Features: [MapStars prefab]
  World Controllers: [MapCameraController prefab]
  Action Maps:    ["Camera"]    ← enables the "Camera" map while this mode is active
```

### Behaviour

- **`SetMode(modeA)`**: maps leaving A's set are disabled; maps entering are enabled. `MosaicUI.Input.EnabledMaps` equals exactly `modeA.ActionMaps` after the call.
- **`Back()`**: the prior mode's maps are restored (same diff, in reverse).
- **A mode with no declared maps**: all previously-active maps are disabled.
- **No asset wired**: the diff is skipped — no throw.

### Verification

```csharp
// After SetMode(cameraMode) where cameraMode.ActionMaps = ["Camera"]:
Assert.That(MosaicUI.Input.EnabledMaps, Contains.Item("Camera"));

// After SetMode(maplessMode) where maplessMode.ActionMaps is empty:
Assert.That(MosaicUI.Input.EnabledMaps.Count, Is.EqualTo(0));
```

---

## 5. Reference archetype — map-camera scene

The harness scene `Assets/Scenes/composition/input-binding/` is the reference implementation of the orbit/pan/zoom + star pick/hover archetype, built **entirely on the input layer** — zero `Device.current` / `Mouse.current` / `Camera.main` polling.

### Architecture

| File | Role |
|---|---|
| `MapCameraStore` | `Store<MapCameraStore>` — holds yaw/pitch/distance/pan + selection/hover state; private setters + public action methods (Layer-1 doctrine). |
| `MapCameraController` | `WorldController` — wires orbit/pan/zoom/pick via `BindAction*` + `ReadAction` in `Update`; gates pointer-drag actions with `ShouldHandleWorldPointer`; performs `Physics.Raycast` star-picks. |
| `MapStarsFeature` | `WorldFeature` — owns the star sphere GameObjects; tints them by subscribing to `SelectedStar`/`HoveredStar`. |
| `MapHudController` | `PanelController` — corner HUD showing `SelectionLabel`; a `Button` (gate pick surface) + a `TextField` (keyboard-capture proof). |
| `MapBootstrap` | `MonoBehaviour` — registers `MapCameraStore` in `Awake()` before the manager's `Start()`. |
| `StarMarker` | `MonoBehaviour` — lightweight identifier on each star sphere (holds `StarIndex`). |
| `MapHudPanel.uxml` | HUD panel UXML. |
| `MapLayout.uxml` | Layout with a `"hud"` mosaic-slot and a `picking-mode: ignore` root. |

### What the scene proves

1. **Zero device polling** — `grep` for `Device.current`, `Mouse.current`, `Keyboard.current`, `Gamepad.current`, `Camera.main` in the scene scripts yields no results.
2. **Gate correctness** — pointer over the HUD panel's button → `gate.IsPointerOverUI(pos) = true` → world pick suppressed; pointer over empty space → `false` → pick proceeds.
3. **Per-mode maps live** — `SetMode(cameraMode)` → `MosaicUI.Input.EnabledMaps` contains `"Camera"`; `SetMode(maplessMode)` → `"Camera"` disabled.
4. **Live device path** — driving the `Camera/Orbit` action (code-first via `MosaicUI.Input`) advances `MapCameraStore.Yaw`/`Pitch`; the camera transform updates in the same frame's `Update`.

---

## 6. `MosaicUIManager` serialized fields added by this feature

| Field | Header | Type | Purpose |
|---|---|---|---|
| `inputActions` | Input | `InputActionAsset` | The asset whose maps `DiffActionMaps` enables/disables. Null-guarded. |

World controllers also have two new observable members:

| Member | Access | Purpose |
|---|---|---|
| `RoutingGate` | `public UIRoutingGate` | Direct access to the gate (fallback; prefer `Services.Get<UIRoutingGate>()`). |
| `ActiveActionMaps` | `internal IReadOnlyCollection<string>` | Introspection view of currently-enabled map names. |

---

## See Also

- [Input.md](Input.md) — the `InputService` source service (`SetAsset`, `Subscribe*`, `ReadValue`, `EnableMap`/`DisableMap`)
- [Interaction.md](Interaction.md) — write-loop doctrine: `BindClick`, `BindCommand`, `MapAction`, the "same front door" principle, and the `CommandRegistry`
- [Modes.md](Modes.md) — mode transitions, `ModeDefinition`, `SetMode`, `Back`
- [GettingStarted.md](GettingStarted.md) — end-to-end minimal counter panel walkthrough
