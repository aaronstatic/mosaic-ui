# MosaicUI

A reusable Unity UI framework built on UI Toolkit with Zustand-inspired state management, declarative mode-driven panel composition, and a lightweight service locator.

Unity 6000.1+ | UPM Package | com.aaronstatic.mosaic-ui

---

## Overview

MosaicUI is a UI framework for Unity that treats the game's screen layout as a composition of named panels assigned to named slots in a layout template. Instead of manually managing which panels are visible, you declare what a "mode" looks like — a collection of panel-to-slot mappings — and MosaicUI handles transitions, diffing, and lifecycle automatically.

State is handled through stores inspired by Zustand. Each store is a plain C# class that extends `Store<TSelf>`, uses `SetProperty` to mutate properties, and notifies subscribers only when the selected slice of state actually changes. Stores integrate directly with UI Toolkit's data binding system via `INotifyBindablePropertyChanged`, so both manual subscriptions and UXML data bindings work from the same source.

The framework also includes managed interaction helpers on `PanelController` and a named command dispatch registry (`CommandRegistry`) for routing clicks and field edits back into state, a device-input layer (`MosaicUI.Input`) that wraps an Input System `InputActionAsset` and wires named actions into the same command/store path as a UI click, an optional floating window system, a `DataList` component wrapping `ListView` for data-bound lists, a lightweight service locator (`ServiceRegistry`), and a typed publish/subscribe event bus (`EventBus`). All of these components are optional — you can use the panel and mode system without windows, or use stores standalone without the rest of the framework.

> **Requires the Unity Input System package** (`com.unity.inputsystem`) — the `MosaicUI.Input` source service depends on it, so it is declared as a package dependency and resolved automatically by UPM.

For development, an in-editor **MosaicUI Debugger** (`Window > MosaicUI > Debugger`) makes the otherwise-invisible runtime observable live during play mode: registered stores and their values, fired events, registered commands, and the active composition. It is editor-only and adds zero cost to player builds.

### Key benefits

- Reduces per-panel boilerplate from three files (MonoBehaviour, UXML, USS) down to one or two (controller + UXML)
- Panels shared across modes are reused without teardown and re-instantiation
- Selector-based store subscriptions prevent unnecessary UI refreshes
- Framework-agnostic panel controllers: logic classes that happen to receive a `VisualElement` root, with no direct coupling to UIToolkit beyond that
- No static singletons per-panel — everything flows through `MosaicUI.Services`

---

## Installation

### Local package (recommended during development)

Add the following entry to the `dependencies` section of your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.aaronstatic.mosaic-ui": "file:../path/to/com.aaronstatic.mosaic-ui"
  }
}
```

### Git URL

```json
{
  "dependencies": {
    "com.aaronstatic.mosaic-ui": "https://github.com/aaronstatic/mosaic-ui.git"
  }
}
```

---

## Quick Start

### 1. Create a store

```csharp
using Unity.Properties;
using Mosaic.UI;

public class GameStore : Store<GameStore>
{
    private int _credits;

    [CreateProperty]
    public int Credits
    {
        get => _credits;
        set => SetProperty(ref _credits, value);
    }
}
```

### 2. Create a UXML panel template

Create `Assets/UI/HudPanel.uxml`:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Label name="credits-label" text="0" />
</ui:UXML>
```

### 3. Create a PanelController

```csharp
using Mosaic.UI;
using UnityEngine.UIElements;

public class HudController : PanelController
{
    private Label _creditsLabel;

    public override void OnBind()
    {
        _creditsLabel = Root.Q<Label>("credits-label");

        var store = GetStore<GameStore>();
        Subscriptions.Add(store.Subscribe(s => s.Credits, count =>
        {
            _creditsLabel.text = count.ToString();
        }));
    }
}
```

### 4. Create ScriptableObject assets in the Unity Editor

Use the Asset menu to create:

- **MosaicUI / Panel Definition** — assign `HudPanel.uxml` and set Controller Type Name to `HudController, Assembly-CSharp`
- **MosaicUI / Layout Definition** — assign a layout UXML that contains an element with `class="mosaic-slot"` and `name="hud"`
- **MosaicUI / Mode Definition** — set Mode Name to `"Game"`, assign the layout, and add a panel entry pointing to your HudPanel with target slot `"hud"`

### 5. Set up MosaicUIManager in a scene

1. Create a GameObject in your scene and add the `MosaicUIManager` component
2. Assign the `UIDocument` component
3. Assign the starting `ModeDefinition`
4. Add the mode to the Available Modes list

### 6. Register stores on startup

```csharp
using Mosaic.UI;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        MosaicUI.Initialize();
        MosaicUI.Services.CreateStore<GameStore>();
    }
}
```

`MosaicUIManager.Start()` also calls `MosaicUI.Initialize()`, which is idempotent, so registration order is flexible as long as stores are registered before panels call `OnBind`.

---

## Core Concepts

### Stores

Stores are the single source of truth for UI state. A store extends `Store<TSelf>` and exposes properties via `SetProperty`, which performs an equality check and fires `propertyChanged` only when the value actually changes.

```csharp
public class ShipStore : Store<ShipStore>
{
    private string _shipName;
    private float _hullIntegrity;

    [CreateProperty]
    public string ShipName
    {
        get => _shipName;
        set => SetProperty(ref _shipName, value);
    }

    [CreateProperty]
    public float HullIntegrity
    {
        get => _hullIntegrity;
        set => SetProperty(ref _hullIntegrity, value);
    }
}
```

Stores implement `INotifyBindablePropertyChanged` and `IDataSourceViewHashProvider`, making them compatible with UI Toolkit's `dataSource` binding system.

Selector subscriptions let controllers subscribe to a derived slice of state. The callback is only invoked when the selected value changes:

```csharp
Subscriptions.Add(store.Subscribe(s => s.HullIntegrity, hp =>
{
    _hullBar.value = hp;
}));
```

Subscriptions are stored in the controller's `Subscriptions` group and disposed automatically when the panel is torn down.

See [Documentation~/Stores.md](Documentation~/Stores.md) for a complete guide.

### Interaction

If stores are the *read* loop, interaction is the *write* loop: button clicks and field edits flow back into state through a single, predictable path. `PanelController` provides managed `Bind*` helpers that wire a UI callback and automatically add an unhook `IDisposable` to the controller's `Subscriptions` group, so no manual cleanup is needed on dispose:

```csharp
public override void OnBind()
{
    var store = GetStore<CounterStore>();

    BindClick("increment-btn", store.Increment);                       // button → store action
    BindValue<string>("note-field", () => store.Note, store.SetNote);  // two-way field bind
    BindCommand("reset-btn", "counter/reset");                         // button → named command
}
```

For directed, decoupled dispatch, `MosaicUI.Commands` is a `CommandRegistry` — a one-handler-per-id command bus, distinct from the broadcast `EventBus`. Handlers register with an `IDisposable` token (add it to `Subscriptions` for auto-cleanup) and are invoked by id:

```csharp
Subscriptions.Add(MosaicUI.Commands.Register("counter/reset", store.Reset));
MosaicUI.Commands.Invoke("counter/reset");
```

Because every interaction is reachable without a pointer event, behavior is verifiable headlessly: tests call the store action or `Commands.Invoke(id)` directly and assert resulting state.

See [Documentation~/Interaction.md](Documentation~/Interaction.md) for a complete guide.

### Panels

A panel is a combination of:

- **PanelDefinition** — a ScriptableObject that names the panel, references its UXML and optional USS, and specifies the controller type as a fully-qualified string
- **PanelController** — a plain C# class (not a MonoBehaviour) that receives the instantiated `VisualElement` root and the `ServiceRegistry`
- **PanelInstance** — the runtime wrapper that MosaicUIManager manages; you do not create these directly

The panel lifecycle is:

1. `OnBind()` — root and services are set; query elements and set up subscriptions here
2. `OnShow()` — panel becomes visible
3. `OnHide()` — panel is hidden but not destroyed (happens on mode transitions where the panel is not in the new mode)
4. `OnModeChanged(modeName)` — called when the mode changes but this panel remains active (shared panels)
5. `OnDispose()` / `Dispose()` — panel is torn down; `Subscriptions` group is disposed automatically

For panels that need contextual data (e.g., "inspect this specific ship"), use `PanelController<TContext>`:

```csharp
public class ShipInspectorController : PanelController<ShipData>
{
    public override void OnBind()
    {
        Root.Q<Label>("ship-name").text = Context.Name;
    }
}
```

### Layouts and Modes

A **LayoutDefinition** references a UXML file that defines the screen structure. Any element in that UXML with the CSS class `mosaic-slot` and a non-empty `name` attribute becomes a named slot that panels can be assigned to:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement name="top-bar" class="mosaic-slot" />
    <ui:VisualElement name="sidebar" class="mosaic-slot" />
    <ui:VisualElement name="hud" class="mosaic-slot" />
</ui:UXML>
```

A **ModeDefinition** lists which panels go into which slots, in what sort order, along with optional world-space features and controllers. When `MosaicUIManager.SetMode()` is called, it diffs the incoming panel list against the currently active panels: panels not in the new mode are disposed, panels shared between modes are reused and optionally moved to a new slot, and new panels are instantiated.

Mode transitions push the previous mode onto a `ModeHistory` stack, enabling `Back()` navigation:

```csharp
_manager.SetMode("Settings");
// later...
_manager.Back(); // returns to previous mode
```

See [Documentation~/Modes.md](Documentation~/Modes.md) for a complete guide.

### World Features and Controllers

For 3D world-space objects that exist per-mode (camera rigs, map layers, planet visuals), MosaicUI provides two MonoBehaviour bases:

**WorldFeature** — for visual/interactive 3D objects tied to a mode:

```csharp
public class StarMapFeature : WorldFeature
{
    public override void OnShow()
    {
        // activate visual elements
    }

    public override void OnHide()
    {
        // deactivate visual elements
    }
}
```

**WorldController** — for camera and input controllers active during a mode:

```csharp
public class StarMapCameraController : WorldController
{
    public override void OnActivated()
    {
        // enable camera
    }

    public override void OnDeactivated()
    {
        // disable camera
    }
}
```

Both are configured as prefab references in ModeDefinition entries and are instantiated/destroyed using the same diffing logic as panels. Like `PanelController`, both world bases carry a `Subscriptions` group and the `BindAction*`/`ReadAction`/`MapAction` input helpers (below), so device-driven world controllers auto-clean their subscriptions on mode exit.

### Input

`MosaicUI.Input` is the device-input **source**: an `InputService` that wraps a consumer-supplied `InputActionAsset`, resolves named actions/maps, exposes phase subscriptions and `ReadValue`, enables/disables maps, and tracks the active control scheme (broadcasting `ControlSchemeChanged` on `MosaicUI.Events`). On top of it, controllers get the same auto-disposed ergonomics as `BindClick`:

```csharp
public class MapCameraController : WorldController
{
    public override void OnActivated()
    {
        // device action → named command (the SAME CommandRegistry path a UI button hits)
        MapAction("Camera/Pick", "map/pick");
        // react to a phase directly (auto-unhooked via Subscriptions)
        BindActionPerformed("Camera/Recenter", _ => GetStore<MapCameraStore>().Recenter());
    }

    private void Update()
    {
        var orbit = ReadAction<Vector2>("Camera/Orbit");   // continuous read
        // ...
    }
}
```

- **`BindActionStarted/Performed/Canceled(actionName, handler)`** and **`ReadAction<T>(actionName)`** — on `PanelController`, `WorldController`, and `WorldFeature`; subscriptions auto-dispose via `Subscriptions`.
- **`MapAction(actionName, commandId)` / `MapAction<T>(...)`** — the device sibling of `BindCommand`: a gamepad/keyboard action and a UI button reach the **same** `MosaicUI.Commands` command identically across control schemes.
- **`UIRoutingGate`** — an authoritative, queryable gate ("is the pointer / keyboard focus over a MosaicUI panel or window?") built on the UI Toolkit runtime panel (no `EventSystem`). Registered in `MosaicUI.Services` by `MosaicUIManager`; world input calls `gate.ShouldHandleWorldPointer(screenPos)` to stand down when the UI owns the pointer.
- **Per-mode action maps** — a `ModeDefinition` lists the action map(s) active in that mode; `MosaicUIManager` auto-wires its serialized `InputActionAsset` and enables/disables maps via the same diff that swaps panels and world objects.

See [Documentation~/Input.md](Documentation~/Input.md) (the source service) and [Documentation~/InputBinding.md](Documentation~/InputBinding.md) (the binding layer) for complete guides.

### Windows

The optional window system provides floating, draggable, resizable panels layered on top of the main UI. A **WindowDefinition** ScriptableObject configures the window chrome (title, size constraints, drag, resize, close button). The **WindowManager** service handles open/close/toggle:

```csharp
var windows = new WindowManager(windowLayerElement, MosaicUI.Services);
MosaicUI.Services.Register(windows);

// Open a window
windows.Open(myWindowDefinition);

// Open a window with typed context
windows.Open<ShipData>(inspectorWindowDef, selectedShip);

// Toggle
windows.Toggle(myWindowDefinition);
```

Windows enforce single-instance behavior by window name. Position and size can be persisted across sessions using `PlayerPrefsWindowPersistence` or a custom `IWindowPersistence` implementation.

See [Documentation~/Windows.md](Documentation~/Windows.md) for a complete guide.

### Services and Events

**ServiceRegistry** is a lightweight dictionary-backed service locator. Register any class instance and retrieve it by type:

```csharp
MosaicUI.Services.Register<IMyService>(new MyServiceImpl());
var svc = MosaicUI.Services.Get<IMyService>();

// Convenience: create a store and register it in one call
var store = MosaicUI.Services.CreateStore<GameStore>();
```

**EventBus** is a typed publish/subscribe bus. Messages are plain structs or classes:

```csharp
// Define a message
public struct ShipDestroyedEvent { public int ShipId; }

// Subscribe
var sub = MosaicUI.Events.Subscribe<ShipDestroyedEvent>(evt =>
{
    Debug.Log($"Ship {evt.ShipId} destroyed");
});

// Publish
MosaicUI.Events.Publish(new ShipDestroyedEvent { ShipId = 42 });

// Unsubscribe
sub.Dispose();
```

Use `SubscriptionGroup` to batch-dispose multiple subscriptions:

```csharp
var group = new SubscriptionGroup();
group.Add(MosaicUI.Events.Subscribe<ShipDestroyedEvent>(OnShipDestroyed));
group.Add(MosaicUI.Events.Subscribe<BattleEndedEvent>(OnBattleEnded));

// Dispose all at once
group.Dispose();
```

### Debugger (Editor)

The **MosaicUI Debugger** is an editor-only window (`Window > MosaicUI > Debugger`) that surfaces the framework's runtime state during play mode. It attaches automatically when `MosaicUI` initializes and shows a "MosaicUI not initialized" message otherwise. Four tabs:

- **State** — every entry registered in `MosaicUI.Services`; stores additionally show their `[CreateProperty]` values, updating live as `propertyChanged` fires
- **Events** — a running, frame- and timestamped log of everything published through `MosaicUI.Events` (capped ring buffer, type filter, Clear)
- **Commands** — the ids currently registered in `MosaicUI.Commands`
- **Composition** — the active `MosaicUIManager`: current mode, the `ModeHistory` back-stack, active panels and their slots, world features/controllers, and open windows

The debugger reads runtime state through read-only `internal` introspection members exposed to the editor assembly via `[assembly: InternalsVisibleTo("Mosaic.UI.Editor")]`, plus an `#if UNITY_EDITOR`-gated `EventBus.Published` hook — so it carries **zero cost in player builds**.

> The Composition tab's **windows** list is best-effort: `MosaicUIManager` does not own a `WindowManager` (you construct it yourself). To see open windows there, register your manager as a service — `MosaicUI.Services.Register(windows)` — and the debugger will discover it.

---

## Architecture

### Data flow

```
Game Code
    |
    v
Store.SetProperty(ref field, value)
    |
    +-- propertyChanged (INotifyBindablePropertyChanged)
    |       |
    |       +-- UI Toolkit data binding (UXML dataSource)
    |
    +-- StoreSubscription selector evaluation
            |
            +-- callback only fires if selected value changed
                    |
                    v
            PanelController updates VisualElement
```

### Mode transition flow

```
MosaicUIManager.SetMode(newMode)
    |
    +-- Push currentMode to ModeHistory
    +-- ApplyLayout if layout changed
    |       |
    |       +-- DisposeAllPanels
    |       +-- Clone layout UXML
    |       +-- Discover mosaic-slot elements
    |
    +-- DiffPanels
    |       |
    |       +-- Remove panels not in newMode (Dispose)
    |       +-- Reuse shared panels (move slot if needed)
    |       +-- Instantiate new panels (OnBind → Show → OnShow)
    |
    +-- DiffWorldFeatures (same pattern)
    +-- DiffWorldControllers (same pattern)
    +-- NotifyModeChanged to all active panels
```

---

## API Reference

### MosaicUI (static)

| Member | Description |
|---|---|
| `MosaicUI.Services` | The global `ServiceRegistry` instance |
| `MosaicUI.Events` | The global `EventBus` instance |
| `MosaicUI.Commands` | The global `CommandRegistry` instance |
| `MosaicUI.Input` | The global `InputService` (device-input source) instance |
| `MosaicUI.IsInitialized` | Whether the framework has been initialized |
| `MosaicUI.Initialize()` | Create Services, Events, Commands, and Input. Idempotent. |
| `MosaicUI.Shutdown()` | Dispose/clear and null Services, Events, Commands, and Input. |

### ServiceRegistry

| Member | Description |
|---|---|
| `Register<T>(service)` | Register a service by its type |
| `Get<T>()` | Retrieve a service; throws if not registered |
| `TryGet<T>(out service)` | Retrieve a service without throwing |
| `CreateStore<TStore>()` | Instantiate, register, and return a store |
| `Clear()` | Remove all registrations |

### EventBus

| Member | Description |
|---|---|
| `Subscribe<T>(handler)` | Subscribe to message type T; returns `IDisposable` |
| `Publish<T>(message)` | Publish a message to all subscribers of type T |
| `Clear()` | Remove all subscriptions |

### CommandRegistry

| Member | Description |
|---|---|
| `Register(id, Action)` | Register a parameterless command; returns an `IDisposable` unregister token |
| `Register<T>(id, Action<T>)` | Register a command with a typed payload; returns an `IDisposable` token |
| `Invoke(id)` | Invoke a parameterless command; throws if unregistered or arity-mismatched |
| `Invoke<T>(id, payload)` | Invoke a typed command; throws if unregistered or type-mismatched |
| `Has(id)` | Whether a command id is currently registered |
| `RegisteredIds` | Snapshot `IReadOnlyCollection<string>` of registered ids |
| `Clear()` | Remove all registrations |

### InputService (`MosaicUI.Input`)

| Member | Description |
|---|---|
| `SetAsset(InputActionAsset)` | Assign (hot-swappable) the asset whose actions/maps are resolved |
| `SubscribeStarted/Performed/Canceled(name, handler)` | Subscribe to a named action's phase; returns an `IDisposable` |
| `ReadValue<T>(name)` | Read a named action's current value (`T : struct`) |
| `EnableMap(name)` / `DisableMap(name)` | Enable/disable a named action map |
| `ActiveControlScheme` | The active control scheme name (broadcasts `ControlSchemeChanged` on `MosaicUI.Events` when it changes) |

### UIRoutingGate

| Member | Description |
|---|---|
| `IsPointerOverUI(screenPos)` | Whether the pointer is over a MosaicUI panel/window (runtime-panel `Pick`) |
| `IsKeyboardCaptured()` | Whether a UI element currently holds keyboard focus |
| `ShouldHandleWorldPointer(screenPos, takeRawInput = false)` | `takeRawInput \|\| !IsPointerOverUI(...)` — world input's stand-down check |

### Store\<TSelf\>

| Member | Description |
|---|---|
| `propertyChanged` | Event fired on any property change |
| `SetProperty<T>(ref field, value)` | Set field, fire change notification if value differs |
| `Subscribe<TSlice>(selector, callback)` | Subscribe to a derived slice; callback fires only on slice change |
| `GetViewHashCode()` | Version counter for UI Toolkit binding efficiency |

### PanelController

| Member | Description |
|---|---|
| `Root` | The instantiated `VisualElement` root of the panel |
| `Services` | The `ServiceRegistry` injected at bind time |
| `Subscriptions` | `SubscriptionGroup` — add store/event subs here |
| `GetStore<T>()` | Shorthand for `Services.Get<T>()` |
| `Commands` | Shorthand for `MosaicUI.Commands` (the global `CommandRegistry`) |
| `BindClick(name/button, handler)` | Wire a button's `clicked` event; auto-unhooked on dispose |
| `BindValue<T>(name/field, getter, setter)` | Two-way bind a `BaseField<T>` to a store value |
| `BindCommand(name, commandId)` | Wire a button click to `Commands.Invoke(commandId)` |
| `BindActionStarted/Performed/Canceled(action, handler)` | Subscribe to a `MosaicUI.Input` action phase; auto-unhooked on dispose |
| `ReadAction<T>(action)` | Read a named action's current value via `MosaicUI.Input` |
| `MapAction(action, commandId)` / `MapAction<T>(...)` | Wire a device action's `performed` phase to `Commands.Invoke` (device sibling of `BindCommand`) |
| `OnBind()` | Override: query elements, set up subscriptions |
| `OnShow()` | Override: panel is becoming visible |
| `OnHide()` | Override: panel is being hidden |
| `OnModeChanged(modeName)` | Override: mode changed while panel is shared |
| `OnDispose()` | Override: custom cleanup before disposal |

### MosaicUIManager

| Member | Description |
|---|---|
| `CurrentMode` | The currently active `ModeDefinition` |
| `History` | The `ModeHistory` stack |
| `RoutingGate` | The `UIRoutingGate` (also registered in `MosaicUI.Services`) |
| `SetMode(ModeDefinition)` | Transition to a mode (diffs panels, world objects, **and the mode's declared action maps**) |
| `SetMode(string modeName)` | Transition to a mode by name |
| `Back()` | Restore the previous mode from history |

> `MosaicUIManager` has a serialized `InputActionAsset` field (`Input` header); on `Start()` it calls `MosaicUI.Input.SetAsset(...)`, and each `ModeDefinition` lists the action maps active for that mode.

### WindowManager

| Member | Description |
|---|---|
| `Open(definition)` | Open a window; returns existing instance if already open |
| `Open<TContext>(definition, context)` | Open a context-bound window |
| `Close(key)` | Close a window by its internal key |
| `Toggle(definition)` | Open if closed, close if open |
| `CloseAll()` | Close all open windows |
| `IsOpen(windowName)` | Check whether a window is currently open |

---

## Project Structure

```
com.aaronstatic.mosaic-ui/
├── Runtime/
│   ├── MosaicUI.cs                  # Static entry point
│   ├── AssemblyInfo.cs              # InternalsVisibleTo Mosaic.UI.Tests + Mosaic.UI.Editor
│   ├── Core/
│   │   ├── ServiceRegistry.cs
│   │   └── EventBus.cs              # Includes SubscriptionGroup
│   ├── Interaction/
│   │   ├── CommandRegistry.cs       # Named command dispatch
│   │   └── CallbackDisposable.cs    # Internal unhook IDisposable
│   ├── Input/
│   │   ├── InputService.cs          # MosaicUI.Input — InputActionAsset source service
│   │   ├── ControlSchemeChanged.cs  # EventBus message (scheme switches)
│   │   ├── InputBindingExtensions.cs # Shared BindAction*/MapAction* impl
│   │   └── UIRoutingGate.cs         # UI-vs-world routing gate
│   ├── State/
│   │   ├── Store.cs
│   │   └── StoreSubscription.cs
│   ├── Panels/
│   │   ├── PanelDefinition.cs       # ScriptableObject
│   │   ├── PanelController.cs
│   │   ├── PanelControllerT.cs      # Generic context variant
│   │   └── PanelInstance.cs
│   ├── Modes/
│   │   ├── LayoutDefinition.cs      # ScriptableObject
│   │   ├── ModeDefinition.cs        # ScriptableObject
│   │   └── ModeHistory.cs
│   ├── Layout/
│   │   ├── MosaicUIManager.cs       # MonoBehaviour orchestrator
│   │   └── SlotContainer.cs
│   ├── WorldSpace/
│   │   ├── WorldFeature.cs
│   │   └── WorldController.cs
│   ├── Windows/
│   │   ├── WindowDefinition.cs      # ScriptableObject
│   │   ├── WindowManager.cs
│   │   ├── WindowChrome.cs          # VisualElement
│   │   └── WindowPersistence.cs     # Interface + PlayerPrefs impl
│   ├── Components/
│   │   └── DataList.cs              # UxmlElement wrapping ListView
│   └── Styles/
│       ├── MosaicDefaults.uss
│       └── WindowChrome.uss
├── Editor/
│   ├── Inspectors/
│   │   ├── PanelDefinitionEditor.cs
│   │   └── ModeDefinitionEditor.cs
│   └── Debugger/                    # In-editor MosaicUI Debugger (Window > MosaicUI > Debugger)
│       ├── MosaicDebuggerWindow.cs  # EditorWindow + tab strip + play-mode attach/detach
│       └── Panes/
│           ├── DebuggerPane.cs      # Attach/Detach/Refresh base contract
│           ├── StateInspectorPane.cs
│           ├── EventMonitorPane.cs
│           ├── CommandsInspectorPane.cs
│           └── CompositionInspectorPane.cs
├── Tests/
│   └── EditMode/
│       ├── ServiceRegistryTests.cs
│       ├── EventBusTests.cs
│       ├── StoreTests.cs
│       ├── StoreActionTests.cs
│       ├── CommandRegistryTests.cs
│       ├── PanelControllerBindTests.cs
│       ├── ModeHistoryTests.cs
│       ├── WindowManagerTests.cs
│       ├── IntrospectionSeamTests.cs
│       ├── InputServiceTests.cs
│       ├── InputBindingBridgeTests.cs   # BindAction + MapAction bridge
│       ├── UIRoutingGateTests.cs
│       └── ModeActionMapDiffTests.cs
├── Documentation~/
│   ├── GettingStarted.md
│   ├── Stores.md
│   ├── Interaction.md
│   ├── Modes.md
│   ├── Windows.md
│   ├── Input.md            # MosaicUI.Input source service
│   └── InputBinding.md     # BindAction/MapAction, UIRoutingGate, per-mode maps
├── CHANGELOG.md
├── LICENSE.md
└── package.json
```

---

## License

MIT License. See [LICENSE.md](LICENSE.md) for details.
