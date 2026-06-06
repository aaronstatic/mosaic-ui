using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

        public ModeDefinition CurrentMode { get; private set; }
        public ModeHistory History { get; } = new();

        private LayoutDefinition _currentLayout;
        private VisualElement _layoutRoot;
        private readonly Dictionary<string, SlotContainer> _slots = new();
        private readonly Dictionary<PanelDefinition, PanelInstance> _activePanels = new();
        private readonly Dictionary<GameObject, GameObject> _activeWorldFeatures = new();
        private readonly Dictionary<GameObject, GameObject> _activeWorldControllers = new();

        // Read-only introspection views for the editor debugger (composition pane).
        // CurrentMode and History are already public. These views reflect live diff state; mutating
        // through them is impossible (read-only interface).
        internal IReadOnlyDictionary<PanelDefinition, PanelInstance> ActivePanels => _activePanels;
        internal IReadOnlyDictionary<string, SlotContainer> Slots => _slots;
        internal IReadOnlyDictionary<GameObject, GameObject> ActiveWorldFeatures => _activeWorldFeatures;
        internal IReadOnlyDictionary<GameObject, GameObject> ActiveWorldControllers => _activeWorldControllers;

        private IEnumerator Start()
        {
            MosaicUI.Initialize();

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

            // Clone layout UXML
            _layoutRoot = layout.LayoutUxml.CloneTree();
            _layoutRoot.style.flexGrow = 1;
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
    }
}
