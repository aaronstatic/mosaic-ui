# Getting Started with MosaicUI

This tutorial walks through building a minimal counter panel from scratch. By the end you will have a working panel that displays a count, increments it on button click, and is managed by the MosaicUI mode system.

## Prerequisites

- Unity 6000.1 or later
- MosaicUI package installed (see [README](../README.md) for installation)
- A scene with a `UIDocument` component

---

## Step 1: Create a Store

Stores hold the state that your UI reacts to. Create a new C# script in your project:

`Assets/Scripts/CounterStore.cs`

```csharp
using Unity.Properties;
using Mosaic.UI;

public class CounterStore : Store<CounterStore>
{
    private int _count;

    [CreateProperty]
    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    public void Increment()
    {
        Count++;
    }

    public void Decrement()
    {
        Count--;
    }
}
```

Key points:
- The class extends `Store<CounterStore>` (the self-referential generic is required by the selector subscription system)
- `[CreateProperty]` marks the property for UI Toolkit data binding
- `SetProperty` performs an equality check; `propertyChanged` is only fired when the value actually changes
- Business logic methods like `Increment` live on the store, not in the controller

---

## Step 2: Create the Panel UXML

Create a UXML file for the panel's visual structure.

`Assets/UI/CounterPanel.uxml`

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement class="counter-panel">
        <ui:Label name="count-label" text="0" />
        <ui:VisualElement class="counter-panel__buttons">
            <ui:Button name="decrement-button" text="-" />
            <ui:Button name="increment-button" text="+" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

---

## Step 3: Create the PanelController

The controller wires the store to the visual elements. Create a new C# script:

`Assets/Scripts/CounterController.cs`

```csharp
using Mosaic.UI;
using UnityEngine.UIElements;

public class CounterController : PanelController
{
    private Label _countLabel;
    private Button _incrementButton;
    private Button _decrementButton;

    public override void OnBind()
    {
        // Query elements from the cloned UXML root
        _countLabel = Root.Q<Label>("count-label");
        _incrementButton = Root.Q<Button>("increment-button");
        _decrementButton = Root.Q<Button>("decrement-button");

        var store = GetStore<CounterStore>();

        // Subscribe to the Count slice — callback fires only when Count changes
        Subscriptions.Add(store.Subscribe(s => s.Count, count =>
        {
            _countLabel.text = count.ToString();
        }));

        // Set the initial display value
        _countLabel.text = store.Count.ToString();

        // Wire up button callbacks
        _incrementButton.clicked += store.Increment;
        _decrementButton.clicked += store.Decrement;
    }

    protected override void OnDispose()
    {
        // Unregister button callbacks to avoid leaks
        if (_incrementButton != null)
        {
            var store = GetStore<CounterStore>();
            _incrementButton.clicked -= store.Increment;
            _decrementButton.clicked -= store.Decrement;
        }
    }
}
```

Key points:
- `OnBind()` is the right place to query elements and set up subscriptions; `Root` and `Services` are both available by this point
- `GetStore<T>()` is shorthand for `Services.Get<T>()`
- Subscriptions added to `Subscriptions` are automatically disposed when the panel is torn down
- Button callbacks that capture a reference to the store should be unregistered in `OnDispose()` to prevent leaks

---

## Step 4: Create a Layout UXML

The layout defines the slot structure. Any element with `class="mosaic-slot"` and a `name` attribute becomes an addressable slot.

`Assets/UI/MainLayout.uxml`

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement name="root" style="flex-grow: 1; flex-direction: column;">
        <ui:VisualElement name="hud" class="mosaic-slot" style="flex-grow: 1;" />
    </ui:VisualElement>
</ui:UXML>
```

This layout has a single slot named `hud`. Panels assigned to `hud` in a ModeDefinition will be inserted here.

---

## Step 5: Create the ScriptableObject Assets

In the Unity Project window, right-click and use the Create menu for each asset.

### PanelDefinition

Create via: **Assets / Create / MosaicUI / Panel Definition**

Configure in the Inspector:
- **Panel Name**: `CounterPanel`
- **UXML**: drag in `CounterPanel.uxml`
- **USS**: leave empty for now
- **Controller Type Name**: `CounterController, Assembly-CSharp`

The Controller Type Name format is `FullyQualifiedClassName, AssemblyName`. For scripts in `Assets/`, the assembly is `Assembly-CSharp`. The custom inspector validates this field and shows a green info box if the type resolves correctly.

### LayoutDefinition

Create via: **Assets / Create / MosaicUI / Layout Definition**

Configure in the Inspector:
- **Layout UXML**: drag in `MainLayout.uxml`

### ModeDefinition

Create via: **Assets / Create / MosaicUI / Mode Definition**

Configure in the Inspector:
- **Mode Name**: `Main`
- **Layout**: drag in the LayoutDefinition asset
- **Panels**: click the + button and set:
  - **Panel**: drag in the PanelDefinition asset
  - **Target Slot**: `hud`
  - **Sort Order**: `0`

The ModeDefinitionEditor custom inspector validates that the target slot name exists in the layout UXML and shows an error if it does not.

---

## Step 6: Set Up the Scene

### Bootstrap MonoBehaviour

Create a new script that registers the store before the framework initializes panels:

`Assets/Scripts/GameBootstrap.cs`

```csharp
using Mosaic.UI;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        MosaicUI.Initialize();
        MosaicUI.Services.CreateStore<CounterStore>();
    }
}
```

`CreateStore<T>()` instantiates the store, registers it, and returns it. Because `MosaicUIManager.Start()` yields one frame before calling `SetMode`, registering in `Awake` ensures the store is available when `OnBind` is called.

Add this MonoBehaviour to a GameObject in the scene. Make sure it runs in `Awake` before `MosaicUIManager.Start()`.

### MosaicUIManager

1. Create a new empty GameObject named `MosaicUIManager`
2. Add the `MosaicUIManager` component
3. In the Inspector, configure:
   - **UI Document**: drag in the scene's `UIDocument` component
   - **Starting Mode**: drag in the `Main` ModeDefinition asset
   - **Available Modes**: click + and drag in the `Main` ModeDefinition asset

The **Available Modes** list is used by `SetMode(string modeName)`. The **Starting Mode** is applied on Start.

---

## Step 7: Run It

Press Play. You should see:

1. The layout UXML is cloned and added to the UIDocument's root
2. `CounterPanel.uxml` is cloned and inserted into the `hud` slot
3. `CounterController.OnBind()` is called — the count label shows `0`
4. Clicking `+` calls `store.Increment()`, which sets `Count` to `1` via `SetProperty`, which fires `propertyChanged`, which triggers the subscription callback, which updates the label to `1`
5. Clicking `-` decrements

---

## Next Steps

- Read [Stores.md](Stores.md) for the full store system guide including UI Toolkit data binding
- Read [Modes.md](Modes.md) for multi-mode applications, Back navigation, and world-space features
- Read [Windows.md](Windows.md) for floating window support
