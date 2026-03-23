# Windows

The window system is an optional module (`Mosaic.UI.Windows` namespace) that provides floating, draggable, resizable panels layered on top of the main UI. Windows are not part of the mode/slot system — they exist in a separate overlay layer and are opened and closed programmatically.

---

## Overview

Three classes form the window system:

| Class | Description |
|---|---|
| `WindowDefinition` | ScriptableObject configuring the window chrome and panel |
| `WindowManager` | Service for opening, closing, and toggling windows |
| `WindowChrome` | The `VisualElement` shell with title bar, drag, resize, and close |

Windows wrap a `PanelDefinition`, so the panel inside a window follows the same lifecycle as mode-based panels: `OnBind`, `OnShow`, `OnHide`, `Dispose`.

---

## WindowDefinition

Create via: **Assets / Create / MosaicUI / Window Definition**

| Field | Type | Default | Description |
|---|---|---|---|
| Window Name | string | | Unique identifier; used as key for open/close/toggle |
| Panel | PanelDefinition | | The panel displayed inside the window |
| Default Size | Vector2 | 400 x 300 | Initial width and height in pixels |
| Min Size | Vector2 | 200 x 150 | Minimum width and height when resizing |
| Max Size | Vector2 | 1920 x 1080 | Maximum width and height when resizing |
| Draggable | bool | true | Whether the title bar can be dragged |
| Resizable | bool | true | Whether the bottom-right handle resizes the window |
| Closable | bool | true | Whether the close (x) button appears |
| Title | string | | Display title; falls back to Window Name if empty |

---

## Setting Up WindowManager

`WindowManager` is a service — you construct it and register it manually. It requires a `VisualElement` to use as the window layer (typically positioned to cover the full screen and sit above other panels) and the `ServiceRegistry` reference.

```csharp
using Mosaic.UI;
using Mosaic.UI.Windows;
using UnityEngine;
using UnityEngine.UIElements;

public class UIBootstrap : MonoBehaviour
{
    [SerializeField] private UIDocument _uiDocument;

    private void Awake()
    {
        MosaicUI.Initialize();

        // Register stores
        MosaicUI.Services.CreateStore<PlayerStore>();

        // Create a full-screen window layer
        var windowLayer = new VisualElement();
        windowLayer.style.position = Position.Absolute;
        windowLayer.style.top = 0;
        windowLayer.style.left = 0;
        windowLayer.style.right = 0;
        windowLayer.style.bottom = 0;
        windowLayer.style.flexGrow = 1;
        windowLayer.pickingMode = PickingMode.Ignore;
        _uiDocument.rootVisualElement.Add(windowLayer);

        // Create WindowManager with optional persistence
        var persistence = new PlayerPrefsWindowPersistence();
        var windowManager = new WindowManager(windowLayer, MosaicUI.Services, persistence);
        MosaicUI.Services.Register(windowManager);
    }
}
```

The window layer should be added to the root after the MosaicUI layout root so it renders on top. Setting `pickingMode = PickingMode.Ignore` on the layer itself ensures clicks pass through to the mode panels when no window is under the cursor.

---

## Opening Windows

### Basic open

```csharp
[SerializeField] private WindowDefinition _inventoryWindow;

public void OpenInventory()
{
    var windows = MosaicUI.Services.Get<WindowManager>();
    windows.Open(_inventoryWindow);
}
```

If the window is already open, `Open` returns the existing `PanelInstance` without creating a duplicate.

### Context windows

Use `Open<TContext>` when the window panel needs data about a specific subject:

```csharp
[SerializeField] private WindowDefinition _shipInspectorWindow;

public void InspectShip(ShipData ship)
{
    var windows = MosaicUI.Services.Get<WindowManager>();
    windows.Open<ShipData>(_shipInspectorWindow, ship);
}
```

For this to work, the panel's controller must extend `PanelController<TContext>`:

```csharp
using Mosaic.UI;
using UnityEngine.UIElements;

public class ShipInspectorController : PanelController<ShipData>
{
    public override void OnBind()
    {
        Root.Q<Label>("ship-name").text = Context.Name;
        Root.Q<Label>("hull").text = $"{Context.HullIntegrity:P0}";
    }
}
```

Context windows are keyed by `"{windowName}:{context.ToString()}"`. Two calls with different context values will open two separate windows. Two calls with the same context value return the existing instance.

---

## Closing Windows

```csharp
// Close by window name key
windows.Close("InventoryWindow");
```

The `Close(key)` method saves the window's current position and size (if persistence is configured), disposes the panel, and removes the chrome from the hierarchy.

### Toggle

```csharp
windows.Toggle(_inventoryWindow);
```

If the window is open, it closes. If it is closed, it opens.

### Close all

```csharp
windows.CloseAll();
```

Closes and disposes all currently open windows.

---

## Checking Window State

```csharp
bool isOpen = windows.IsOpen("InventoryWindow");
```

`IsOpen` checks the window by its window name string, not by a `WindowDefinition` reference.

---

## Window Persistence

When a `WindowManager` is constructed with an `IWindowPersistence` implementation, window positions and sizes are:
- **Restored** when the window is opened
- **Saved** when the window is closed

### Built-in: PlayerPrefsWindowPersistence

`PlayerPrefsWindowPersistence` stores window state as JSON in `PlayerPrefs` under the key `MosaicUI_Window_{windowKey}`.

```csharp
var persistence = new PlayerPrefsWindowPersistence();
var windows = new WindowManager(windowLayer, MosaicUI.Services, persistence);
```

To delete a saved state for a specific window:

```csharp
persistence.Delete("InventoryWindow");
```

Note: `PlayerPrefsWindowPersistence.Clear()` is a no-op because `PlayerPrefs` does not support prefix-based deletion. Delete individual keys with `Delete(key)` instead.

### Custom persistence backend

Implement `IWindowPersistence` to use a different storage backend (a save file, a database, etc.):

```csharp
using Mosaic.UI.Windows;
using UnityEngine;

public class FileWindowPersistence : IWindowPersistence
{
    private readonly string _filePath;

    public FileWindowPersistence(string filePath)
    {
        _filePath = filePath;
    }

    public void Save(string key, WindowSavedState state)
    {
        // Write to file...
    }

    public WindowSavedState? Load(string key)
    {
        // Read from file; return null if not found
        return null;
    }

    public void Delete(string key)
    {
        // Remove key from file...
    }

    public void Clear()
    {
        // Delete all saved states...
    }
}
```

The `WindowSavedState` struct:

```csharp
public struct WindowSavedState
{
    public Vector2 position; // top-left corner in pixels
    public Vector2 size;     // width and height in pixels
}
```

---

## WindowChrome

`WindowChrome` is a `VisualElement` subclass that provides the window's visual shell. You do not typically interact with it directly, but understanding its structure is useful for styling.

### Structure

```
WindowChrome (.mosaic-window)
├── VisualElement (.mosaic-window__title-bar)
│   ├── Label (.mosaic-window__title)
│   └── Button (.mosaic-window__close-button)   [only if Closable]
├── VisualElement (.mosaic-window__content)
│   └── [panel root inserted here]
└── VisualElement (.mosaic-window__resize-handle) [only if Resizable]
```

### CSS classes

| Class | Element | Description |
|---|---|---|
| `mosaic-window` | Root | Positioned absolute; has default size |
| `mosaic-window__title-bar` | Title bar | Drag target; contains title and close button |
| `mosaic-window__title` | Label | Displays the window title |
| `mosaic-window__close-button` | Button | Fires `CloseRequested` event when clicked |
| `mosaic-window__content` | Content area | Flex container holding the panel root |
| `mosaic-window__resize-handle` | Resize handle | Bottom-right corner; drag to resize |

### Accessible properties

```csharp
string title = chrome.Title;     // get or set the title label text
VisualElement content = chrome.Content; // the content container
```

### Styling

The package ships `WindowChrome.uss` with default styles. Override in your project's USS:

```css
/* Custom window styling */
.mosaic-window {
    background-color: rgba(20, 25, 35, 0.95);
    border-radius: 6px;
    border-width: 1px;
    border-color: rgba(80, 100, 140, 0.6);
}

.mosaic-window__title-bar {
    background-color: rgba(30, 40, 60, 1.0);
    height: 32px;
    border-top-left-radius: 6px;
    border-top-right-radius: 6px;
    flex-direction: row;
    align-items: center;
    padding: 0 8px;
}

.mosaic-window__title {
    flex-grow: 1;
    color: rgba(200, 210, 230, 1.0);
    font-size: 13px;
}

.mosaic-window__close-button {
    width: 20px;
    height: 20px;
    background-color: transparent;
    border-width: 0;
    color: rgba(180, 190, 210, 0.8);
}

.mosaic-window__resize-handle {
    position: absolute;
    bottom: 0;
    right: 0;
    width: 16px;
    height: 16px;
    cursor: resize-bottom-right;
}
```

Add your USS file to the UIDocument's style sheet list or reference it from a panel's USS to override the defaults.

---

## Behavior Notes

### Single instance per key

`Open(definition)` and `Toggle(definition)` use `definition.WindowName` as the key. Only one instance of a given window name can be open at a time. `Open<TContext>` uses `"{windowName}:{context}"` as the key, allowing multiple instances with different contexts.

### Bring to front

Clicking anywhere on a window calls `BringToFront()` on the chrome `VisualElement`, which moves it to the end of its parent's child list (top of the z-order within the window layer).

### Window layer and mode transitions

The window layer is separate from the mode layout. Mode transitions do not affect open windows. If you need windows to close on mode transitions, call `windows.CloseAll()` in your mode change logic or in a panel's `OnHide` callback.

### No WindowManager in ModeDefinition

`WindowManager` is not wired into `MosaicUIManager`. You set it up manually, register it as a service, and retrieve it from panels or MonoBehaviours as needed. This is intentional — window management is orthogonal to the mode system.
