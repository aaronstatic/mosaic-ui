using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Mosaic.UI
{
    public class MosaicUIManager : MonoBehaviour
    {
        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;

        [Header("Layout")]
        [SerializeField] private LayoutDefinition defaultLayout;

        [Header("Modes")]
        [SerializeField] private ModeDefinition startingMode;
        [SerializeField] private List<ModeDefinition> availableModes = new();

        [Header("World Space")]
        [SerializeField] private Transform worldSpaceRoot;
        [SerializeField] private Transform controllerRoot;

        [Header("Input")]
        // The InputActionAsset whose maps the per-mode action-map diff (DiffActionMaps) enables and
        // disables. Wired into MosaicUI.Input via SetAsset(...) in Start() — null-guarded, so leaving
        // this unset never throws: the input source simply has no asset and the per-mode maps are inert
        // (DiffActionMaps is skipped when no asset is assigned — see DiffActionMaps).
        [SerializeField] private InputActionAsset inputActions;

        public ModeDefinition CurrentMode { get; private set; }
        public ModeHistory History { get; } = new();

        /// <summary>
        /// The authoritative UI-vs-world routing gate, owned by this manager (which owns the only
        /// <see cref="UIDocument"/>). World input helpers should reach it via
        /// <c>MosaicUI.Services.Get&lt;UIRoutingGate&gt;()</c> (the same path controllers use for every
        /// other collaborator — see <see cref="Start"/>); this property is the direct-access fallback.
        /// The gate reads the live runtime panel from <see cref="uiDocument"/> on each query.
        /// </summary>
        public UIRoutingGate RoutingGate { get; private set; }

        private LayoutDefinition _currentLayout;
        private VisualElement _layoutRoot;
        private readonly Dictionary<string, SlotContainer> _slots = new();
        private readonly Dictionary<PanelDefinition, PanelInstance> _activePanels = new();
        private readonly Dictionary<GameObject, GameObject> _activeWorldFeatures = new();
        private readonly Dictionary<GameObject, GameObject> _activeWorldControllers = new();

        // The action-map names currently enabled by the per-mode diff (DiffActionMaps). The diff is
        // the sole driver of map enablement on this manager, so this set equals MosaicUI.Input.EnabledMaps
        // after any transition (when an asset is assigned). Mirrors the _active* diff-tracking collections.
        private readonly HashSet<string> _activeActionMaps = new();

        // Read-only introspection views for the editor debugger (composition pane).
        // CurrentMode and History are already public. These views reflect live diff state; mutating
        // through them is impossible (read-only interface).
        internal IReadOnlyDictionary<PanelDefinition, PanelInstance> ActivePanels => _activePanels;
        internal IReadOnlyDictionary<string, SlotContainer> Slots => _slots;
        internal IReadOnlyDictionary<GameObject, GameObject> ActiveWorldFeatures => _activeWorldFeatures;
        internal IReadOnlyDictionary<GameObject, GameObject> ActiveWorldControllers => _activeWorldControllers;
        internal IReadOnlyCollection<string> ActiveActionMaps => _activeActionMaps;

        private IEnumerator Start()
        {
            MosaicUI.Initialize();

            // Own the UI-vs-world routing gate. The Func defers panel resolution to query time,
            // so constructing here (before the visual tree is fully built) is safe — the panel is
            // read live on each IsPointerOverUI/IsKeyboardCaptured call. Register the instance in
            // MosaicUI.Services so world controllers reach it via Services.Get<UIRoutingGate>(),
            // consistent with how controllers reach every other collaborator (Decision 3). This
            // runs before the first SetMode (below), which is where world controllers are first
            // instantiated, so the gate is always registered before any world controller queries it.
            RoutingGate = new UIRoutingGate(() => uiDocument != null ? uiDocument.rootVisualElement?.panel : null);
            MosaicUI.Services.Register(RoutingGate);

            // Wire the per-mode input asset into the input source. Null-guarded (R7): an unset field
            // never throws — the input source simply has no asset and the per-mode action-map diff is
            // inert (DiffActionMaps skips when no asset was assigned). Done before the first SetMode so
            // the starting mode's maps can be enabled by the diff below.
            if (inputActions != null)
                MosaicUI.Input.SetAsset(inputActions);

            // Wait one frame for UIDocument to fully initialize its visual tree
            yield return null;

            if (startingMode != null)
                SetMode(startingMode);
        }

        private void OnDestroy()
        {
            DisposeAllPanels();
            DisposeAllWorldFeatures();
            DisposeAllWorldControllers();
            MosaicUI.Shutdown();
        }

        // --- Mode Management ---

        public void SetMode(string modeName)
        {
            var mode = availableModes.Find(m => m.ModeName == modeName);
            if (mode == null)
            {
                Debug.LogError($"[MosaicUI] Mode '{modeName}' not found in available modes.");
                return;
            }
            SetMode(mode);
        }

        public void SetMode(ModeDefinition mode)
        {
            if (mode == null)
            {
                Debug.LogError("[MosaicUI] Cannot set null mode.");
                return;
            }

            // Push current mode to history (if there was one)
            if (CurrentMode != null)
                History.Push(CurrentMode);

            // Handle layout change
            var newLayout = mode.Layout != null ? mode.Layout : defaultLayout;
            if (newLayout != _currentLayout)
            {
                ApplyLayout(newLayout);
            }

            // Diff panels
            DiffPanels(mode);

            // Diff world features
            DiffWorldFeatures(mode);

            // Diff world controllers
            DiffWorldControllers(mode);

            // Diff per-mode action maps (enable maps entering, disable maps leaving)
            DiffActionMaps(mode);

            CurrentMode = mode;

            // Notify all active panels of mode change
            foreach (var kvp in _activePanels)
            {
                kvp.Value.NotifyModeChanged(mode.ModeName);
            }
        }

        public void Back()
        {
            var previousMode = History.Pop();
            if (previousMode == null)
            {
                Debug.LogWarning("[MosaicUI] No previous mode in history.");
                return;
            }

            // Handle layout change
            var newLayout = previousMode.Layout != null ? previousMode.Layout : defaultLayout;
            if (newLayout != _currentLayout)
            {
                ApplyLayout(newLayout);
            }

            DiffPanels(previousMode);
            DiffWorldFeatures(previousMode);
            DiffWorldControllers(previousMode);
            DiffActionMaps(previousMode);
            CurrentMode = previousMode;

            foreach (var kvp in _activePanels)
            {
                kvp.Value.NotifyModeChanged(previousMode.ModeName);
            }
        }

        // --- Layout ---

        private void ApplyLayout(LayoutDefinition layout)
        {
            if (layout == null || layout.LayoutUxml == null)
            {
                Debug.LogError("[MosaicUI] Layout or its UXML is null.");
                return;
            }

            // Dispose all current panels before changing layout
            DisposeAllPanels();

            // Clear existing layout
            var root = uiDocument.rootVisualElement;
            root.Clear();
            _slots.Clear();

            // The layout SHELL must not catch pointer picks: only slotted panels/windows are
            // interactive UI. Without this, the full-screen UIDocument root + the cloned
            // TemplateContainer wrapper are pickable, so UIRoutingGate.IsPointerOverUI() would
            // report the whole screen as "over UI" and suppress all world input. Marking the shell
            // picking-ignore lets clicks over empty (non-panel) areas pass through to the world.
            // (Slot content keeps its own default PickingMode.Position, so buttons etc. still work.)
            root.pickingMode = PickingMode.Ignore;

            // Clone layout UXML
            _layoutRoot = layout.LayoutUxml.CloneTree();
            _layoutRoot.style.flexGrow = 1;
            _layoutRoot.pickingMode = PickingMode.Ignore;   // the CloneTree() TemplateContainer wrapper
            root.Add(_layoutRoot);

            // Find all slot containers (elements with class "mosaic-slot")
            var slotElements = _layoutRoot.Query(className: "mosaic-slot").ToList();
            foreach (var slotElement in slotElements)
            {
                if (!string.IsNullOrEmpty(slotElement.name))
                {
                    _slots[slotElement.name] = new SlotContainer(slotElement.name, slotElement);
                }
            }

            _currentLayout = layout;
        }

        // --- Panel Diffing ---

        private void DiffPanels(ModeDefinition newMode)
        {
            // Build set of panels in the new mode
            var newPanelSet = new HashSet<PanelDefinition>();
            var newPanelEntries = new Dictionary<PanelDefinition, PanelEntry>();
            foreach (var entry in newMode.Panels)
            {
                if (entry.panel != null)
                {
                    newPanelSet.Add(entry.panel);
                    newPanelEntries[entry.panel] = entry;
                }
            }

            // Find panels to remove (in current but not in new)
            var toRemove = new List<PanelDefinition>();
            foreach (var kvp in _activePanels)
            {
                if (!newPanelSet.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }

            // Remove old panels
            foreach (var panelDef in toRemove)
            {
                var instance = _activePanels[panelDef];
                RemovePanelFromSlot(instance);
                instance.Dispose();
                _activePanels.Remove(panelDef);
            }

            // Add or reuse panels
            foreach (var entry in newMode.Panels)
            {
                if (entry.panel == null) continue;

                if (_activePanels.TryGetValue(entry.panel, out var existing))
                {
                    // Shared panel — move to new slot if needed
                    if (existing.SlotName != entry.targetSlot || existing.SortOrder != entry.sortOrder)
                    {
                        RemovePanelFromSlot(existing);
                        existing.SlotName = entry.targetSlot;
                        existing.SortOrder = entry.sortOrder;
                        AddPanelToSlot(existing);
                    }
                }
                else
                {
                    // New panel — instantiate
                    var instance = new PanelInstance(entry.panel);
                    instance.SlotName = entry.targetSlot;
                    instance.SortOrder = entry.sortOrder;
                    instance.Instantiate(MosaicUI.Services);
                    _activePanels[entry.panel] = instance;
                    AddPanelToSlot(instance);
                    instance.Show();
                }
            }
        }

        private void AddPanelToSlot(PanelInstance instance)
        {
            if (string.IsNullOrEmpty(instance.SlotName))
            {
                Debug.LogWarning($"[MosaicUI] Panel '{instance.Definition.PanelName}' has no target slot.");
                return;
            }

            if (_slots.TryGetValue(instance.SlotName, out var slot))
            {
                slot.AddPanel(instance, instance.SortOrder);
            }
            else
            {
                Debug.LogWarning($"[MosaicUI] Slot '{instance.SlotName}' not found in current layout for panel '{instance.Definition.PanelName}'.");
            }
        }

        private void RemovePanelFromSlot(PanelInstance instance)
        {
            if (!string.IsNullOrEmpty(instance.SlotName) && _slots.TryGetValue(instance.SlotName, out var slot))
            {
                slot.RemovePanel(instance);
            }
        }

        private void DisposeAllPanels()
        {
            foreach (var kvp in _activePanels)
            {
                kvp.Value.Dispose();
            }
            _activePanels.Clear();
        }

        // --- World Features ---

        private void DiffWorldFeatures(ModeDefinition newMode)
        {
            var newPrefabSet = new HashSet<GameObject>();
            foreach (var entry in newMode.WorldFeatures)
            {
                if (entry.prefab != null)
                    newPrefabSet.Add(entry.prefab);
            }

            // Remove features not in new mode
            var toRemove = new List<GameObject>();
            foreach (var kvp in _activeWorldFeatures)
            {
                if (!newPrefabSet.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var prefab in toRemove)
            {
                var instance = _activeWorldFeatures[prefab];
                var feature = instance.GetComponent<WorldFeature>();
                if (feature != null)
                    feature.Dispose();
                else
                    Destroy(instance);
                _activeWorldFeatures.Remove(prefab);
            }

            // Add new features
            foreach (var entry in newMode.WorldFeatures)
            {
                if (entry.prefab == null) continue;
                if (_activeWorldFeatures.ContainsKey(entry.prefab)) continue;

                var parent = worldSpaceRoot != null ? worldSpaceRoot : transform;
                var instance = Instantiate(entry.prefab, parent);
                _activeWorldFeatures[entry.prefab] = instance;

                var feature = instance.GetComponent<WorldFeature>();
                if (feature != null)
                {
                    feature.Initialize(MosaicUI.Services);
                    feature.OnShow();
                }
            }
        }

        private void DisposeAllWorldFeatures()
        {
            foreach (var kvp in _activeWorldFeatures)
            {
                if (kvp.Value != null)
                {
                    var feature = kvp.Value.GetComponent<WorldFeature>();
                    if (feature != null)
                        feature.Dispose();
                    else
                        Destroy(kvp.Value);
                }
            }
            _activeWorldFeatures.Clear();
        }

        // --- World Controllers ---

        private void DiffWorldControllers(ModeDefinition newMode)
        {
            var newPrefabSet = new HashSet<GameObject>();
            foreach (var entry in newMode.WorldControllers)
            {
                if (entry.prefab != null)
                    newPrefabSet.Add(entry.prefab);
            }

            // Remove controllers not in new mode
            var toRemove = new List<GameObject>();
            foreach (var kvp in _activeWorldControllers)
            {
                if (!newPrefabSet.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var prefab in toRemove)
            {
                var instance = _activeWorldControllers[prefab];
                var controller = instance.GetComponent<WorldController>();
                if (controller != null)
                    controller.Dispose();
                else
                    Destroy(instance);
                _activeWorldControllers.Remove(prefab);
            }

            // Add new controllers
            foreach (var entry in newMode.WorldControllers)
            {
                if (entry.prefab == null) continue;
                if (_activeWorldControllers.ContainsKey(entry.prefab)) continue;

                var parent = controllerRoot != null ? controllerRoot : transform;
                var instance = Instantiate(entry.prefab, parent);
                _activeWorldControllers[entry.prefab] = instance;

                var controller = instance.GetComponent<WorldController>();
                if (controller != null)
                {
                    controller.Initialize(MosaicUI.Services);
                    controller.OnActivated();
                }
            }
        }

        private void DisposeAllWorldControllers()
        {
            foreach (var kvp in _activeWorldControllers)
            {
                if (kvp.Value != null)
                {
                    var controller = kvp.Value.GetComponent<WorldController>();
                    if (controller != null)
                        controller.Dispose();
                    else
                        Destroy(kvp.Value);
                }
            }
            _activeWorldControllers.Clear();
        }

        // --- Action Maps ---

        /// <summary>
        /// Per-mode action-map diff step for <see cref="SetMode(ModeDefinition)"/>/<see cref="Back"/>.
        /// <para>
        /// <b>No-asset behavior (documented):</b> if no <c>inputActions</c> asset was wired on this
        /// manager, the diff is skipped entirely — a mode that declares maps is simply inert (no
        /// exception). This mirrors the null-guard on <c>SetAsset</c> in <see cref="Start"/>: an unset
        /// asset means the input source has nothing to enable, and calling <c>EnableMap</c> with no
        /// asset would throw. Skipping is the least-surprising option (no crash from leaving the field
        /// unset). When an asset <em>is</em> assigned, the actual diff runs in the static
        /// <see cref="DiffActionMaps(IReadOnlyList{string}, HashSet{string}, InputService)"/> overload.
        /// </para>
        /// </summary>
        private void DiffActionMaps(ModeDefinition mode)
        {
            // Skip when no asset is wired — see the no-asset behavior note above.
            if (inputActions == null)
                return;

            DiffActionMaps(mode.ActionMaps, _activeActionMaps, MosaicUI.Input);
        }

        /// <summary>
        /// Pure, testable core of the per-mode action-map diff: enables maps entering the new set and
        /// disables maps leaving it, then reconciles <paramref name="active"/> to the new set.
        /// Mirrors the set-arithmetic shape of <see cref="DiffPanels"/>/<see cref="DiffWorldControllers"/>
        /// (non-transactional like its siblings — a mid-diff exception could leave maps half-toggled;
        /// kept consistent with the existing diff style rather than introducing rollback for maps only).
        /// <para>
        /// Extracted as <c>internal static</c> so EditMode tests can drive it directly without standing
        /// up the <see cref="MosaicUIManager"/> MonoBehaviour / its <see cref="UIDocument"/>.
        /// </para>
        /// </summary>
        /// <param name="newMaps">The map names the new mode declares (null entries / empty strings are skipped; a null list is treated as empty).</param>
        /// <param name="active">The currently-active map set; mutated in place to equal the new set after the diff.</param>
        /// <param name="input">The input source whose <c>EnableMap</c>/<c>DisableMap</c> are driven.</param>
        internal static void DiffActionMaps(IReadOnlyList<string> newMaps, HashSet<string> active, InputService input)
        {
            // Build the new set, skipping null/empty names.
            var newSet = new HashSet<string>();
            if (newMaps != null)
            {
                foreach (var m in newMaps)
                {
                    if (!string.IsNullOrEmpty(m))
                        newSet.Add(m);
                }
            }

            // Snapshot the maps leaving the active set BEFORE disabling, so we don't mutate `active`
            // while iterating it (and so reconciling `active` below is unaffected by the disable loop).
            var toDisable = new List<string>();
            foreach (var m in active)
            {
                if (!newSet.Contains(m))
                    toDisable.Add(m);
            }
            foreach (var m in toDisable)
                input.DisableMap(m);

            // Enable maps entering the active set.
            foreach (var m in newSet)
            {
                if (!active.Contains(m))
                    input.EnableMap(m);
            }

            // Reconcile the tracked set to the new set.
            active.Clear();
            active.UnionWith(newSet);
        }
    }
}
