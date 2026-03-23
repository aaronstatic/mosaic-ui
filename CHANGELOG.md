# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
