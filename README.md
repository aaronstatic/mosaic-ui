# MosaicUI

A reusable Unity UI framework built on UI Toolkit with Zustand-inspired state management, declarative mode-driven panel composition, and a lightweight service locator.

Unity 6000.1+ | UPM Package | com.aaronstatic.mosaic-ui

---

## Overview

MosaicUI is a UI framework for Unity that treats the game's screen layout as a composition of named panels assigned to named slots in a layout template. Instead of manually managing which panels are visible, you declare what a "mode" looks like — a collection of panel-to-slot mappings — and MosaicUI handles transitions, diffing, and lifecycle automatically.

State is handled through stores inspired by Zustand. Each store is a plain C# class that extends `Store<TSelf>`, uses `SetProperty` to mutate properties, and notifies subscribers only when the selected slice of state actually changes. Stores integrate directly with UI Toolkit's data binding system via `INotifyBindablePropertyChanged`, so both manual subscriptions and UXML data bindings work from the same source.

The framework also includes an optional floating window system, a `DataList` component wrapping `ListView` for data-bound lists, a lightweight service locator (`ServiceRegistry`), and a typed publish/subscribe event bus (`EventBus`). All of these components are optional — you can use the panel and mode system without windows, or use stores standalone without the rest of the framework.

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

Both are configured as prefab references in ModeDefinition entries and are instantiated/destroyed using the same diffing logic as panels.

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
| `MosaicUI.IsInitialized` | Whether the framework has been initialized |
| `MosaicUI.Initialize()` | Create Services and Events. Idempotent. |
| `MosaicUI.Shutdown()` | Dispose Services and Events. |

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
| `SetMode(ModeDefinition)` | Transition to a mode |
| `SetMode(string modeName)` | Transition to a mode by name |
| `Back()` | Restore the previous mode from history |

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
│   ├── Core/
│   │   ├── ServiceRegistry.cs
│   │   └── EventBus.cs              # Includes SubscriptionGroup
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
│   └── Inspectors/
│       ├── PanelDefinitionEditor.cs
│       └── ModeDefinitionEditor.cs
├── Tests/
│   └── EditMode/
│       ├── ServiceRegistryTests.cs
│       ├── EventBusTests.cs
│       ├── StoreTests.cs
│       ├── ModeHistoryTests.cs
│       └── WindowManagerTests.cs
├── Documentation~/
│   ├── GettingStarted.md
│   ├── Stores.md
│   ├── Modes.md
│   └── Windows.md
├── CHANGELOG.md
├── LICENSE.md
└── package.json
```

---

## License

MIT License. See [LICENSE.md](LICENSE.md) for details.
