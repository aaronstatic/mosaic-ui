# Layouts and Modes

The mode system is the central organizing concept of MosaicUI. A mode declares which panels are on screen, where they appear, and which 3D world objects are active. Transitioning between modes triggers a diff — shared panels are reused, obsolete panels are disposed, and new panels are instantiated.

---

## Overview

Three ScriptableObject types work together:

| Asset | Purpose |
|---|---|
| `LayoutDefinition` | References a UXML file that defines the screen structure with named slots |
| `ModeDefinition` | Maps panels to slots and declares world features/controllers |
| `PanelDefinition` | References the UXML and controller for a single panel (see also: Panels) |

---

## Layout Definition

A `LayoutDefinition` is a ScriptableObject that holds a reference to a UXML file. The UXML file defines the screen shell — the structural containers that panels are inserted into.

Create via: **Assets / Create / MosaicUI / Layout Definition**

### Slot convention

Any `VisualElement` in the layout UXML that has:
- CSS class `mosaic-slot`
- A non-empty `name` attribute

becomes an addressable slot. Panels are inserted into slots by name.

`Assets/UI/GameLayout.uxml`:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement name="layout-root" style="flex-grow: 1; position: absolute;
                                                 top: 0; left: 0; right: 0; bottom: 0;">
        <ui:VisualElement name="top-bar"
                          class="mosaic-slot"
                          style="height: 60px;" />

        <ui:VisualElement name="main-area"
                          style="flex-grow: 1; flex-direction: row;">
            <ui:VisualElement name="sidebar"
                              class="mosaic-slot"
                              style="width: 280px;" />
            <ui:VisualElement name="content"
                              class="mosaic-slot"
                              style="flex-grow: 1;" />
        </ui:VisualElement>

        <ui:VisualElement name="bottom-bar"
                          class="mosaic-slot"
                          style="height: 48px;" />
    </ui:VisualElement>
</ui:UXML>
```

This layout exposes four slots: `top-bar`, `sidebar`, `content`, and `bottom-bar`.

### Layout UXML structure tips

- Set `position: absolute` and stretch the root to fill the UIDocument root for full-screen layouts
- Slots can be flex containers that stack multiple panels from the same slot vertically (controlled by sort order)
- Non-slot elements in the layout (backgrounds, separators, decorative elements) are part of the layout chrome and persist as long as the layout is active

---

## Mode Definition

A `ModeDefinition` declares everything that should be active in a given mode.

Create via: **Assets / Create / MosaicUI / Mode Definition**

Fields:

| Field | Type | Description |
|---|---|---|
| Mode Name | string | Identifier used by `SetMode(string)` |
| Layout | LayoutDefinition | The screen layout for this mode; if null, the manager's default layout is used |
| Panels | List\<PanelEntry\> | Panels to show and where to put them |
| World Features | List\<WorldFeatureEntry\> | 3D world GameObjects to instantiate |
| World Controllers | List\<WorldControllerEntry\> | Camera/input controller GameObjects to instantiate |

### PanelEntry fields

| Field | Description |
|---|---|
| Panel | The PanelDefinition asset |
| Target Slot | Name of the slot in the layout UXML |
| Sort Order | Controls insertion order within the slot (lower = earlier) |

### Multiple panels in one slot

When multiple panels target the same slot, they are stacked in sort order. Use this to layer related panels:

| Panel | Slot | Sort Order |
|---|---|---|
| MapBackground | content | 0 |
| MapOverlay | content | 10 |
| MapLabels | content | 20 |

### Sharing a layout across modes

If two modes use the same layout, no UXML re-clone occurs during the transition. The slot structure remains intact and the panel diff runs against the existing slots.

If a mode's Layout field is left empty, `MosaicUIManager` uses the `Default Layout` assigned on the manager component itself.

### ModeDefinition validation

The custom `ModeDefinitionEditor` inspector validates slot assignments at edit time. After assigning a layout, it clones the layout UXML and discovers all `mosaic-slot` elements. Any panel entry whose `Target Slot` does not match an available slot name produces an error help box. Available slot names are also displayed as an info box for reference.

---

## MosaicUIManager Setup

Add the `MosaicUIManager` MonoBehaviour to a persistent GameObject in your scene.

### Inspector fields

| Field | Description |
|---|---|
| UI Document | The UIDocument component to render into |
| Default Layout | Fallback layout used when a mode has no layout assigned |
| Starting Mode | Applied automatically on Start (after one yield frame) |
| Available Modes | List of all modes that can be activated by name via `SetMode(string)` |
| World Space Root | Transform under which world feature GameObjects are parented |
| Controller Root | Transform under which world controller GameObjects are parented |

### Initialization order

`MosaicUIManager.Start()` calls `MosaicUI.Initialize()` then yields one frame before calling `SetMode(startingMode)`. This gives the UIDocument time to fully build its visual tree. Stores should be registered in `Awake` or in a script with a higher script execution order to ensure they are available when panels bind.

---

## Transitioning Between Modes

### By reference

```csharp
[SerializeField] private MosaicUIManager _uiManager;
[SerializeField] private ModeDefinition _settingsMode;

public void OpenSettings()
{
    _uiManager.SetMode(_settingsMode);
}
```

### By name

```csharp
_uiManager.SetMode("Settings");
```

The name must match the `Mode Name` field of a `ModeDefinition` in the manager's Available Modes list. If not found, an error is logged and no transition occurs.

### What happens during a transition

1. The current mode is pushed onto `ModeHistory`
2. If the new mode has a different layout (or the first mode is being set), `ApplyLayout` is called:
   - All active panels are disposed
   - The UIDocument root is cleared
   - The layout UXML is cloned and added to the root
   - All `mosaic-slot` elements are discovered and registered by name
3. Panel diffing runs:
   - Panels in the current mode but not in the new mode are disposed (controller `Dispose()`, root removed from hierarchy)
   - Panels in both modes are reused; if their slot or sort order changed, they are moved
   - Panels in the new mode but not the current mode are instantiated, bound, and shown
4. World feature and world controller diffing runs with the same add/remove logic
5. All remaining active panels receive `OnModeChanged(modeName)`

---

## ModeHistory and Back Navigation

Every call to `SetMode` pushes the previous mode onto a stack. `Back()` pops and restores it:

```csharp
// Navigate forward
_uiManager.SetMode("Settings");
_uiManager.SetMode("AudioSettings");

// Navigate back to Settings
_uiManager.Back();

// Navigate back to the mode before Settings
_uiManager.Back();
```

If the history stack is empty, `Back()` logs a warning and does nothing.

The history is accessible for custom UI (e.g., disabling a Back button when there is no history):

```csharp
bool canGoBack = _uiManager.History.Count > 0;
```

`ModeHistory` methods:

| Method | Description |
|---|---|
| `Push(mode)` | Push a mode onto the stack |
| `Pop()` | Remove and return the top mode; null if empty |
| `Peek()` | Return the top mode without removing; null if empty |
| `Clear()` | Empty the stack |
| `Count` | Number of entries in the stack |

Note: `Back()` on `MosaicUIManager` does not push the current mode before restoring the previous one. The intent is pure "go back" — not circular navigation.

---

## World Features

`WorldFeature` is an abstract MonoBehaviour for 3D scene objects that are active during a specific mode. Typical uses: star map visuals, planet markers, fleet icons in a strategy view.

Create a prefab with a component that extends `WorldFeature`:

```csharp
using Mosaic.UI;
using UnityEngine;

public class StarMapFeature : WorldFeature
{
    [SerializeField] private GameObject _visualsRoot;

    public override void Initialize(ServiceRegistry services)
    {
        base.Initialize(services);
        // Access stores: var store = GetStore<MapStore>();
    }

    public override void OnShow()
    {
        _visualsRoot.SetActive(true);
    }

    public override void OnHide()
    {
        _visualsRoot.SetActive(false);
    }

    public override void OnModeChanged(string modeName)
    {
        // Called when the mode changes but this feature is still active
    }

    protected override void OnDispose()
    {
        // Custom cleanup before GameObject is destroyed
    }
}
```

Register the prefab in ModeDefinition under **World Features** with a `Render Order` value for sorting.

The `Dispose()` method (not virtual) calls `OnHide()`, then `OnDispose()`, then `Destroy(gameObject)`.

---

## World Controllers

`WorldController` is an abstract MonoBehaviour for camera rigs and input handlers that are active during a specific mode.

```csharp
using Mosaic.UI;
using UnityEngine;

public class StrategyMapCamera : WorldController
{
    [SerializeField] private Camera _camera;

    public override void OnActivated()
    {
        _camera.enabled = true;
    }

    public override void OnDeactivated()
    {
        _camera.enabled = false;
    }

    public override void OnModeChanged(string modeName)
    {
        // Called when the mode changes but this controller is still active
    }

    protected override void OnDispose()
    {
        // Custom cleanup before GameObject is destroyed
    }
}
```

Register the prefab in ModeDefinition under **World Controllers** with a `Priority` value.

The `Priority` field is a serialized int on the component (set in the prefab's Inspector, not in ModeDefinition). It is exposed as a public property for ordering when you manage controllers manually, but `MosaicUIManager` instantiates and disposes them in the order they appear in the ModeDefinition list.

The `Dispose()` method calls `OnDeactivated()`, then `OnDispose()`, then `Destroy(gameObject)`.

---

## Example: Multi-Mode Application

### Modes

| Mode Name | Layout | Panels | World Features |
|---|---|---|---|
| StarMap | MapLayout | TopBar(top-bar), MinimapPanel(minimap) | StarMapFeature |
| SystemView | MapLayout | TopBar(top-bar), SystemInfoPanel(sidebar) | SystemViewFeature |
| Battle | BattleLayout | BattleHUD(hud), ShipStatus(sidebar) | BattleArenaFeature |

`TopBar` is shared between `StarMap` and `SystemView` — it is instantiated once and reused. When transitioning from `StarMap` to `SystemView`, `TopBar.OnModeChanged("SystemView")` is called, letting it adjust its content without full reinitialisation.

Transitioning from `SystemView` to `Battle` uses a different layout, so all panels are disposed and the layout is rebuilt.

### Transition code

```csharp
public void StartBattle()
{
    _uiManager.SetMode("Battle");
}

public void EndBattle()
{
    // Back returns to SystemView (which was active before Battle)
    _uiManager.Back();
}
```
