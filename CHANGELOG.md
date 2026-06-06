# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.3.0] - 2026-06-06

### Added

- **Input source service** (`MosaicUI.Input`) — new `Runtime/Input/` module, `Mosaic.UI` namespace; introduces the framework's first hard dependency on `com.unity.inputsystem`
  - `InputService` — wraps a consumer-supplied `InputActionAsset` assigned via `SetAsset` (hot-swappable). `SubscribeStarted`/`SubscribePerformed`/`SubscribeCanceled(name, handler)` return `IDisposable` phase subscriptions; `ReadValue<T>(name)` reads the current value; `EnableMap(name)`/`DisableMap(name)` toggle named maps; `ActiveControlScheme` tracks the active scheme. Unresolved action/map names and use-before-`SetAsset` throw `InvalidOperationException` in the house style; `Dispose()` reverses all enabled InputSystem state (no `Device.current` polling anywhere)
  - `ControlSchemeChanged` — public struct (`Previous` / `Current`) published on `MosaicUI.Events` when the active control scheme switches (driven by `InputUser.onChange`)
- **Static Facade**
  - `MosaicUI.Input` — global `InputService`, constructed by `Initialize()` and disposed/nulled by `Shutdown()` alongside `Services` / `Events` / `Commands`
- **Input binding layer** (extends the write loop to device input) — `Runtime/Input/InputBindingExtensions.cs` + `Runtime/Input/UIRoutingGate.cs`
  - `BindActionStarted` / `BindActionPerformed` / `BindActionCanceled(actionName, handler)` and `ReadAction<TValue>(actionName)` on `PanelController`, `WorldController`, and `WorldFeature` — mirror `BindClick`; subscriptions auto-add to the controller's `Subscriptions` group
  - `MapAction(actionName, commandId)` / `MapAction<T>(actionName, commandId, payload)` on the same three bases — the device sibling of `BindCommand`: a device action's `performed` phase invokes `MosaicUI.Commands`, so a gamepad/keyboard action and a UI button reach the **same** command identically across control schemes
  - `UIRoutingGate` — authoritative UI-vs-world routing gate over the UI Toolkit runtime panel (`RuntimePanelUtils.ScreenToPanel` + `IPanel.Pick` + `focusController`; no `EventSystem`): `IsPointerOverUI(screenPos)`, `IsKeyboardCaptured()`, `ShouldHandleWorldPointer(screenPos, takeRawInput = false)`. Registered in `MosaicUI.Services` and exposed as `MosaicUIManager.RoutingGate`
  - **Per-mode action maps** — `ModeDefinition.ActionMaps` (a `List<string>`) declares the maps active for a mode; `MosaicUIManager` auto-wires a serialized `InputActionAsset` (`SetAsset` on `Start()`) and diffs maps in lock-step with panels/world objects (`DiffActionMaps` in `SetMode()` / `Back()`); `internal ActiveActionMaps` introspection
- **World bases gain `Subscriptions`** — `WorldController` and `WorldFeature` now carry a `public SubscriptionGroup Subscriptions` (disposed first in `Dispose()`), so device subscriptions on world objects auto-clean on mode exit
- **Tests** — `InputServiceTests`, `InputBindingBridgeTests` (BindAction firing/dispose + the action→command bridge), `UIRoutingGateTests`, `ModeActionMapDiffTests` (Edit Mode)
- **Documentation** — `Documentation~/Input.md` (the source service) and `Documentation~/InputBinding.md` (the binding layer: `BindAction`/`MapAction`, the routing gate + its picking-mode requirement, per-mode maps)

### Changed

- `MosaicUI.Initialize()` now also constructs `MosaicUI.Input`; `MosaicUI.Shutdown()` disposes and nulls it. (No change to the existing `Services` / `Events` / `Commands` lifecycle.)
- `MosaicUIManager.ApplyLayout` now marks the layout **shell** (the `UIDocument` root and the cloned `TemplateContainer` wrapper) `PickingMode.Ignore`, so `UIRoutingGate` reports only actual slotted panels/windows as "over UI" and world clicks over empty UI areas pass through. Slotted content keeps `PickingMode.Position`, so buttons/fields are unaffected.
- `MosaicUIManager` gains a serialized `InputActionAsset` field (`Input` header) wired into `MosaicUI.Input.SetAsset` on `Start()`.
- `Documentation~/Interaction.md` — the "non-UI invocation path" doctrine now explicitly includes device input routed through the input layer; documents `MapAction` as `BindCommand`'s device sibling.
- **Dependency:** `Runtime/Mosaic.UI.asmdef` now references `Unity.InputSystem`, and `package.json` declares `com.unity.inputsystem` (1.19.0) — **MosaicUI now requires the Unity Input System package** (resolved automatically by UPM). The test assembly also references `Unity.InputSystem.TestFramework`.

## [0.2.0] - 2026-06-06

### Added

- **Developer Tooling — in-editor MosaicUI Debugger** (new `Editor/Debugger/` module, `Mosaic.UI.Editor`; editor-only, **zero player-build cost**)
  - `MosaicDebuggerWindow` — an `EditorWindow` at **`Window > MosaicUI > Debugger`**: a single tabbed UI Toolkit window with four live panes that attaches when play mode initializes `MosaicUI` and detaches on exit, showing a clear "MosaicUI not initialized" empty state otherwise
  - **State** pane — lists every entry in `MosaicUI.Services`; for stores, reflects `[CreateProperty]` values and live-updates via `INotifyBindablePropertyChanged.propertyChanged` (no manual refresh), and re-scans for services registered after attach
  - **Events** pane — a ring-buffered (cap 500), newest-first, frame- and timestamped log of every `MosaicUI.Events` publish, with a type filter and Clear; in-memory only (reset on play-mode exit)
  - **Commands** pane — lists `MosaicUI.Commands.RegisteredIds` (sorted), polled
  - **Composition** pane — for the active `MosaicUIManager`: current mode, the `ModeHistory` back-stack, active panels → slots (+ sort order), world features/controllers, and (best-effort) open windows from a `WindowManager` discovered in `MosaicUI.Services`
  - `DebuggerPane` base contract (`Attach` / `Detach` / `Refresh`, build-once `Root`) shared by the four panes

- **Editor introspection seams** (read-only `internal` API, exposed to `Mosaic.UI.Editor` via `InternalsVisibleTo`; consumed by the debugger, no public surface added)
  - `ServiceRegistry.Entries` — live read-only `IReadOnlyDictionary<Type, object>` view of registrations
  - `ModeHistory.Items` — read-only `IReadOnlyCollection<ModeDefinition>` (top-to-bottom = back-stack order)
  - `MosaicUIManager.ActivePanels` / `Slots` / `ActiveWorldFeatures` / `ActiveWorldControllers` — read-only views of the live diff state
  - `WindowManager.OpenWindows` — `IEnumerable<OpenWindowInfo>` projecting each open window to a `key + WindowDefinition + PanelInstance` readonly struct (private `WindowState`/`WindowChrome`/persistence stay hidden)
  - `EventBus.SubscriberCount(Type)` — current handler count for a type (`#if UNITY_EDITOR`)
  - `Runtime/AssemblyInfo.cs` now also grants `InternalsVisibleTo("Mosaic.UI.Editor")`

- **Tests**
  - `IntrospectionSeamTests` (Edit Mode) — covers each new seam plus the `EventBus.Published` hook (fires exactly once, after dispatch, even with zero subscribers; detaches cleanly)

### Changed

- `EventBus.Publish<T>` restructured so an editor observer can monitor **every** publish: it previously early-returned when a type had no subscribers; it now guards the dispatch loop instead. **Real-subscriber dispatch semantics are unchanged** (still snapshot-on-publish; still no delivery when there are no subscribers). Adds an editor-only `Published` event (`internal event Action<Type, object>`, `#if UNITY_EDITOR`) fired **once, after** dispatch — compiled out entirely in player builds, and ordered so it can never affect subscriber delivery.

## [0.1.1] - 2026-06-06

### Added

- **Interaction System** (new `Runtime/Interaction/` module, `Mosaic.UI` namespace)
  - `CommandRegistry` — directed, one-handler-per-id command dispatch. `Register(id, Action)` and `Register<T>(id, Action<T>)` return `IDisposable` unregistration tokens; `Invoke(id)` / `Invoke<T>(id, payload)` call the registered handler; `Has(id)`, `RegisteredIds` (a snapshot, not a live reference), and `Clear()` for introspection/lifecycle
  - Strict error contract: `Invoke` on an unregistered id, a duplicate `Register` of a live id, and arity/type mismatches all throw `InvalidOperationException` with messages naming the id and the expected vs supplied type; null/empty ids throw `ArgumentNullException`/`ArgumentException`
  - `CallbackDisposable` — internal, idempotent unhook `IDisposable` shared by the `Bind*` helpers

- **Static Facade**
  - `MosaicUI.Commands` — global `CommandRegistry`, constructed by `Initialize()` and cleared/nulled by `Shutdown()` alongside `Services` and `Events`

- **Panel Interaction Helpers** (on `PanelController`)
  - `BindClick(string, Action)` / `BindClick(Button, Action)` — wires a button's `clicked` event and auto-adds an unhook disposable to `Subscriptions`; the by-name overload throws `InvalidOperationException` if no `Button` with that name exists in the panel root
  - `BindValue<TValue>(string, Func<TValue>, Action<TValue>)` / `BindValue<TValue>(BaseField<TValue>, Func<TValue>, Action<TValue>)` — two-way binds a `BaseField<TValue>` to a store value: pushes the initial value with `SetValueWithoutNotify` and writes back on `ChangeEvent<TValue>`, guarded by `EqualityComparer<TValue>.Default` to prevent feedback loops (v1: does not push future external store changes back to the field)
  - `BindCommand(string, string)` / `BindCommand<T>(string, string, Func<T>)` — wires a button click to `MosaicUI.Commands.Invoke`, with an optional click-time payload factory
  - `Commands` protected accessor — shorthand for `MosaicUI.Commands` from within a controller

- **Tests**
  - Edit Mode suites: `CommandRegistryTests`, `PanelControllerBindTests`, `StoreActionTests`
  - `Runtime/AssemblyInfo.cs` exposes `Mosaic.UI` internals to `Mosaic.UI.Tests` via `InternalsVisibleTo` (enables direct `CallbackDisposable` and internal-setter coverage)

- **Documentation**
  - `Documentation~/Interaction.md` — the write-loop layers (store actions, managed `Bind*` helpers, named commands), commands-vs-events guidance, command-id namespacing convention, and the headless testing discipline

### Changed

- `Documentation~/Stores.md` — documents the `Bind*` interaction helpers for click/field handlers and notes the three static facade members (`Services` / `Events` / `Commands`)

## [0.1.0] - 2026-03-23

### Added

- **Core Infrastructure**
  - `MosaicUI` static entry point with `Initialize()` / `Shutdown()` lifecycle; idempotent, safe to call from multiple bootstraps
  - `ServiceRegistry` lightweight service locator with `Register<T>`, `Get<T>`, `TryGet<T>`, `CreateStore<T>`, and `Clear()`
  - `EventBus` publish/subscribe system with typed `Subscribe<T>` returning `IDisposable` tokens; iterates over a snapshot to allow modifications during publish
  - `SubscriptionGroup` helper for batch disposal of multiple `IDisposable` subscriptions

- **State Management**
  - `Store<TSelf>` abstract base class implementing `INotifyBindablePropertyChanged` for UI Toolkit data binding and `IDataSourceViewHashProvider` for binding efficiency
  - `SetProperty<T>(ref field, value, [CallerMemberName])` with equality check and automatic `propertyChanged` notification
  - Internal `StoreSubscription<TStore, TSlice>` — selector-based subscription that only invokes the callback when the selected slice value actually changes
  - `Subscribe<TSlice>(selector, callback)` public API on `Store<TSelf>`

- **Panel System**
  - `PanelDefinition` ScriptableObject (`Assets/Create/MosaicUI/Panel Definition`) with Panel Name, UXML, optional USS, and Controller Type Name fields
  - `PanelController` abstract base class with injected `Root`, `Services`, and `Subscriptions`; lifecycle hooks: `OnBind`, `OnShow`, `OnHide`, `OnModeChanged`, `OnDispose`
  - `PanelController<TContext>` generic variant for typed context injection via `SetContext`; `Context` property available in `OnBind` and beyond
  - `PanelInstance` runtime wrapper handling UXML cloning, USS application, controller instantiation via reflection, slot tracking, sort order, and full teardown

- **Layout and Mode System**
  - `LayoutDefinition` ScriptableObject (`Assets/Create/MosaicUI/Layout Definition`) referencing a UXML layout shell
  - `ModeDefinition` ScriptableObject (`Assets/Create/MosaicUI/Mode Definition`) with mode name, layout override, panel entries (panel + slot + sort order), world feature entries, and world controller entries
  - `MosaicUIManager` MonoBehaviour orchestrator: initializes framework on Start, applies layouts by cloning UXML and discovering `mosaic-slot`-classed elements, diffs panels/world features/world controllers on mode transitions, notifies shared panels via `OnModeChanged`, shuts down on destroy
  - `ModeHistory` stack-based navigation with `Push`, `Pop`, `Peek`, `Clear`, and `Count`; exposed on `MosaicUIManager.History`
  - `MosaicUIManager.Back()` restores the previous mode from history
  - `SlotContainer` manages sorted panel insertion within a layout slot using a `SortedList<int, List<PanelInstance>>`; rebuilds visual order on change
  - Panel diffing: shared panels across modes are reused without disposal; panels removed from a mode are disposed; layout changes dispose all panels before rebuilding

- **World-Space System**
  - `WorldFeature` abstract MonoBehaviour base for 3D world objects active per mode; lifecycle: `Initialize(services)`, `OnShow`, `OnHide`, `OnModeChanged`, `OnDispose`, `Dispose` (destroys GameObject)
  - `WorldController` abstract MonoBehaviour base for camera / input controllers per mode; lifecycle: `Initialize(services)`, `OnActivated`, `OnDeactivated`, `OnModeChanged`, `OnDispose`, `Dispose`; `Priority` field for ordering
  - Both are instantiated under configurable root transforms (`worldSpaceRoot`, `controllerRoot`) on `MosaicUIManager`

- **Window System** (optional module in `Mosaic.UI.Windows` namespace)
  - `WindowDefinition` ScriptableObject (`Assets/Create/MosaicUI/Window Definition`) with window name, panel reference, default/min/max size, draggable/resizable/closable flags, and title
  - `WindowChrome` custom `VisualElement` with title bar, drag via pointer capture, bottom-right resize handle, close button, bring-to-front on click; CSS classes: `mosaic-window`, `mosaic-window__title-bar`, `mosaic-window__title`, `mosaic-window__close-button`, `mosaic-window__content`, `mosaic-window__resize-handle`
  - `WindowManager` service: `Open(definition)` with single-instance enforcement, `Open<TContext>(definition, context)` for typed context injection, `Close(key)`, `Toggle(definition)`, `CloseAll()`, `IsOpen(windowName)`
  - `IWindowPersistence` interface with `Save`, `Load`, `Delete`, `Clear`; `WindowSavedState` struct (position + size)
  - `PlayerPrefsWindowPersistence` default implementation using `JsonUtility` serialization under a `MosaicUI_Window_` prefix
  - Position and size restored from persistence on window open; saved on close

- **Components**
  - `DataList` `[UxmlElement]` custom VisualElement wrapping `ListView` with `DynamicHeight` virtualization; `[UxmlAttribute]` properties: `ItemTemplate` (VisualTreeAsset), `SelectionType`; code-only properties: `ItemsSource`, `SelectedIndex`; events: `OnSelectionChanged`, `OnItemClicked`; per-item `dataSource` set for nested UI Toolkit data binding; `RefreshItems()` and `Rebuild()` methods

- **Editor Tools**
  - `PanelDefinitionEditor` custom inspector: renders standard fields plus controller type validation with error/info help boxes; resolves type at edit time and checks `PanelController` inheritance
  - `ModeDefinitionEditor` custom inspector: draws default inspector then validates panel slot assignments against available slot names discovered by cloning the layout UXML at edit time

- **Styles**
  - `MosaicDefaults.uss` base stylesheet for `mosaic-data-list` and `mosaic-data-list__item`
  - `WindowChrome.uss` stylesheet for all `mosaic-window__*` elements

- **Tests**
  - Edit Mode test suite: `ServiceRegistryTests`, `EventBusTests`, `StoreTests`, `ModeHistoryTests`, `WindowManagerTests`
