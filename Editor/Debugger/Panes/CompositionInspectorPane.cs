using System;
using System.Text;
using Mosaic.UI.Windows;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Editor
{
    /// <summary>
    /// Composition Inspector pane — shows the active MosaicUIManager state:
    ///   • Current mode + history back-stack
    ///   • Active panels grouped by slot (SlotName + SortOrder)
    ///   • Active world features and world controllers
    ///   • Open windows (best-effort via Services discovery)
    ///
    /// Polling mechanism:
    ///   Attach() starts a 500ms scheduled item that calls Poll().
    ///   Detach() pauses the scheduled item (handle is reused on re-attach).
    ///   Poll() computes a cheap signature string each tick and only rebuilds
    ///   section content when the signature changes — avoiding per-frame GC churn.
    ///
    /// Manager discovery:
    ///   FindFirstObjectByType&lt;MosaicUIManager&gt;() is called each Poll() (or on
    ///   the cached reference going stale/null). Shows "No MosaicUIManager in scene."
    ///   when no manager is found.
    ///
    /// WindowManager discovery:
    ///   Best-effort — scans MosaicUI.Services.Entries for a value that is a
    ///   Mosaic.UI.Windows.WindowManager. If found, lists OpenWindows; otherwise
    ///   shows exactly "No WindowManager registered." (no error).
    ///
    /// Panels display:
    ///   Flat list with slot name + sort order shown per panel line.
    ///   Format: "PanelName  →  slot: slotName   (sort N)"
    /// </summary>
    public class CompositionInspectorPane : DebuggerPane
    {
        public override string Title => "Composition";

        // ── Section containers (built once, content swapped on change) ──────────

        private Label _noManagerLabel;
        private VisualElement _sectionsContent;

        // Mode section
        private Label _currentModeLabel;
        private VisualElement _historyContainer;

        // Panels section
        private VisualElement _panelsContainer;

        // World section
        private VisualElement _worldFeaturesContainer;
        private VisualElement _worldControllersContainer;

        // Windows section
        private VisualElement _windowsContainer;

        // ── Poll state ────────────────────────────────────────────────────────

        private IVisualElementScheduledItem _pollSchedule;

        /// <summary>
        /// Cached manager reference. Re-resolved when null/destroyed.
        /// </summary>
        private MosaicUIManager _manager;

        /// <summary>
        /// Last computed signature; content rebuilt only when this changes.
        /// </summary>
        private string _lastSignature;

        // ── DebuggerPane overrides ─────────────────────────────────────────────

        protected override void BuildUI()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.style.flexDirection = FlexDirection.Column;

            // Header
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.paddingLeft = 8;
            headerRow.style.paddingRight = 8;
            headerRow.style.paddingTop = 8;
            headerRow.style.paddingBottom = 4;
            Root.Add(headerRow);

            var header = new Label("Composition Inspector");
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.flexGrow = 1;
            headerRow.Add(header);

            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            sep.style.marginBottom = 4;
            Root.Add(sep);

            // "No manager" message — shown when FindFirstObjectByType returns null.
            _noManagerLabel = new Label("No MosaicUIManager in scene.");
            _noManagerLabel.style.paddingLeft = 8;
            _noManagerLabel.style.paddingTop = 8;
            _noManagerLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            _noManagerLabel.style.display = DisplayStyle.None;
            Root.Add(_noManagerLabel);

            // Scrollable content holding all sections
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            Root.Add(scrollView);

            _sectionsContent = new VisualElement();
            _sectionsContent.style.paddingLeft = 8;
            _sectionsContent.style.paddingRight = 8;
            _sectionsContent.style.paddingBottom = 8;
            _sectionsContent.style.display = DisplayStyle.None; // hidden until first manager found
            scrollView.Add(_sectionsContent);

            var content = _sectionsContent;

            // ── Mode section ──────────────────────────────────────────────────
            var modeContent = BuildSection("Mode", content);

            var currentModeRow = new VisualElement();
            currentModeRow.style.flexDirection = FlexDirection.Row;
            currentModeRow.style.marginBottom = 4;
            modeContent.Add(currentModeRow);

            var currentModePrefix = new Label("Current: ");
            currentModePrefix.style.color = new Color(0.55f, 0.55f, 0.55f);
            currentModePrefix.style.fontSize = 11;
            currentModeRow.Add(currentModePrefix);

            _currentModeLabel = new Label("(none)");
            _currentModeLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _currentModeLabel.style.fontSize = 11;
            currentModeRow.Add(_currentModeLabel);

            var historyHeader = new Label("History (back-stack):");
            historyHeader.style.fontSize = 11;
            historyHeader.style.color = new Color(0.55f, 0.55f, 0.55f);
            historyHeader.style.marginTop = 4;
            historyHeader.style.marginBottom = 2;
            modeContent.Add(historyHeader);

            _historyContainer = new VisualElement();
            modeContent.Add(_historyContainer);

            // ── Panels section ────────────────────────────────────────────────
            var panelsContent = BuildSection("Panels", content);
            _panelsContainer = new VisualElement();
            panelsContent.Add(_panelsContainer);

            // ── World section ─────────────────────────────────────────────────
            var worldContent = BuildSection("World Features", content);

            var worldFeaturesHeader = new Label("Features:");
            worldFeaturesHeader.style.fontSize = 11;
            worldFeaturesHeader.style.color = new Color(0.55f, 0.55f, 0.55f);
            worldFeaturesHeader.style.marginBottom = 2;
            worldContent.Add(worldFeaturesHeader);

            _worldFeaturesContainer = new VisualElement();
            worldContent.Add(_worldFeaturesContainer);

            var worldControllersHeader = new Label("Controllers:");
            worldControllersHeader.style.fontSize = 11;
            worldControllersHeader.style.color = new Color(0.55f, 0.55f, 0.55f);
            worldControllersHeader.style.marginTop = 6;
            worldControllersHeader.style.marginBottom = 2;
            worldContent.Add(worldControllersHeader);

            _worldControllersContainer = new VisualElement();
            worldContent.Add(_worldControllersContainer);

            // ── Windows section ───────────────────────────────────────────────
            var windowsContent = BuildSection("Windows", content);
            _windowsContainer = new VisualElement();
            windowsContent.Add(_windowsContainer);

            // Initially hide all live sections (shown after first successful poll)
            SetSectionsVisible(false);
        }

        public override void Attach()
        {
            if (!MosaicUI.IsInitialized)
                return;

            // Force an immediate poll so content appears before the first 500ms tick.
            _lastSignature = null;
            Poll();

            // Start or resume the 500ms scheduled poll.
            if (_pollSchedule == null)
            {
                _pollSchedule = Root.schedule.Execute(Poll).Every(500);
            }
            else
            {
                _pollSchedule.Resume();
            }
        }

        public override void Detach()
        {
            // Pause the poll — hold the handle so Attach() can resume it cheaply.
            _pollSchedule?.Pause();

            // Drop cached references.
            _manager = null;
            _lastSignature = null;

            // Return UI to no-manager state.
            _noManagerLabel.style.display = DisplayStyle.None;
            SetSectionsVisible(false);
        }

        public override void Refresh()
        {
            // Called on-demand; delegate to Poll which is already null-safe.
            Poll();
        }

        // ── Poll / change detection ────────────────────────────────────────────

        private void Poll()
        {
            // Guard: framework must be running.
            if (!MosaicUI.IsInitialized || MosaicUI.Services == null)
            {
                SetSectionsVisible(false);
                _noManagerLabel.style.display = DisplayStyle.None;
                _manager = null;
                _lastSignature = null;
                return;
            }

            // Re-resolve manager if the cached reference went stale.
            if (_manager == null || !_manager)
            {
                _manager = UnityEngine.Object.FindFirstObjectByType<MosaicUIManager>();
            }

            if (_manager == null)
            {
                // No manager in scene.
                _noManagerLabel.style.display = DisplayStyle.Flex;
                SetSectionsVisible(false);
                _lastSignature = null;
                return;
            }

            _noManagerLabel.style.display = DisplayStyle.None;
            SetSectionsVisible(true);

            // Compute a cheap signature covering all visible state.
            var sig = ComputeSignature(_manager);
            if (string.Equals(sig, _lastSignature, StringComparison.Ordinal))
                return; // Nothing changed — skip rebuild.

            _lastSignature = sig;
            RebuildContent(_manager);
        }

        // ── Signature ─────────────────────────────────────────────────────────

        private string ComputeSignature(MosaicUIManager manager)
        {
            // Use a per-call StringBuilder (small, bounded by scene complexity).
            var sb = new StringBuilder(256);

            // Current mode
            sb.Append(manager.CurrentMode?.ModeName ?? "(none)");
            sb.Append('|');

            // History count (sufficient — changing the stack changes count or top item)
            sb.Append(manager.History.Count);
            sb.Append('|');

            // History items (top-to-bottom = most-recent first)
            try
            {
                foreach (var item in manager.History.Items)
                {
                    sb.Append(item?.ModeName ?? "?");
                    sb.Append(',');
                }
            }
            catch { /* play-exit race — leave partial */ }

            sb.Append('|');

            // Active panels: PanelName:SlotName:SortOrder (order by definition reference is stable)
            try
            {
                foreach (var kvp in manager.ActivePanels)
                {
                    sb.Append(kvp.Key?.PanelName ?? "?");
                    sb.Append(':');
                    sb.Append(kvp.Value?.SlotName ?? "");
                    sb.Append(':');
                    sb.Append(kvp.Value?.SortOrder.ToString() ?? "0");
                    sb.Append(',');
                }
            }
            catch { /* play-exit race */ }

            sb.Append('|');

            // World features: instance name (or prefab name)
            try
            {
                foreach (var kvp in manager.ActiveWorldFeatures)
                {
                    sb.Append(kvp.Value != null ? kvp.Value.name : (kvp.Key != null ? kvp.Key.name : "?"));
                    sb.Append(',');
                }
            }
            catch { /* play-exit race */ }

            sb.Append('|');

            // World controllers
            try
            {
                foreach (var kvp in manager.ActiveWorldControllers)
                {
                    sb.Append(kvp.Value != null ? kvp.Value.name : (kvp.Key != null ? kvp.Key.name : "?"));
                    sb.Append(',');
                }
            }
            catch { /* play-exit race */ }

            sb.Append('|');

            // Windows (best-effort)
            var wm = FindWindowManager();
            if (wm != null)
            {
                try
                {
                    foreach (var info in wm.OpenWindows)
                    {
                        sb.Append(info.Key);
                        sb.Append(',');
                    }
                }
                catch { /* play-exit race */ }
            }
            else
            {
                sb.Append("no-wm");
            }

            return sb.ToString();
        }

        // ── Content rebuild ────────────────────────────────────────────────────

        private void RebuildContent(MosaicUIManager manager)
        {
            RebuildModeSection(manager);
            RebuildPanelsSection(manager);
            RebuildWorldSection(manager);
            RebuildWindowsSection();
        }

        private void RebuildModeSection(MosaicUIManager manager)
        {
            _currentModeLabel.text = manager.CurrentMode?.ModeName ?? "(none)";

            _historyContainer.Clear();

            bool hasHistory = false;
            try
            {
                foreach (var item in manager.History.Items)
                {
                    hasHistory = true;
                    var row = new Label($"  {item?.ModeName ?? "?"}");
                    row.style.fontSize = 11;
                    row.style.color = new Color(0.7f, 0.7f, 0.7f);
                    row.style.paddingLeft = 8;
                    _historyContainer.Add(row);
                }
            }
            catch { /* play-exit race */ }

            if (!hasHistory)
            {
                var empty = new Label("  (empty)");
                empty.style.fontSize = 11;
                empty.style.color = new Color(0.45f, 0.45f, 0.45f);
                empty.style.paddingLeft = 8;
                _historyContainer.Add(empty);
            }
        }

        private void RebuildPanelsSection(MosaicUIManager manager)
        {
            _panelsContainer.Clear();

            bool hasPanels = false;
            try
            {
                foreach (var kvp in manager.ActivePanels)
                {
                    hasPanels = true;
                    var def = kvp.Key;
                    var inst = kvp.Value;

                    var panelName = def?.PanelName ?? "?";
                    var slotName = inst?.SlotName ?? "(no slot)";
                    var sortOrder = inst?.SortOrder ?? 0;

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;
                    row.style.paddingLeft = 4;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new Color(0.18f, 0.18f, 0.18f);

                    // Panel name (accent colour)
                    var nameLabel = new Label(panelName);
                    nameLabel.style.fontSize = 11;
                    nameLabel.style.color = new Color(0.6f, 0.8f, 1f);
                    nameLabel.style.minWidth = 120;
                    row.Add(nameLabel);

                    // Arrow separator
                    var arrow = new Label("  →  ");
                    arrow.style.fontSize = 11;
                    arrow.style.color = new Color(0.45f, 0.45f, 0.45f);
                    row.Add(arrow);

                    // Slot + sort info
                    var slotLabel = new Label($"slot: {slotName}   (sort {sortOrder})");
                    slotLabel.style.fontSize = 11;
                    slotLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                    slotLabel.style.flexGrow = 1;
                    row.Add(slotLabel);

                    _panelsContainer.Add(row);
                }
            }
            catch { /* play-exit race */ }

            if (!hasPanels)
            {
                AddEmptyLabel(_panelsContainer, "(none)");
            }
        }

        private void RebuildWorldSection(MosaicUIManager manager)
        {
            _worldFeaturesContainer.Clear();
            _worldControllersContainer.Clear();

            bool hasFeatures = false;
            try
            {
                foreach (var kvp in manager.ActiveWorldFeatures)
                {
                    hasFeatures = true;
                    var displayName = kvp.Value != null ? kvp.Value.name : (kvp.Key != null ? kvp.Key.name : "?");
                    AddWorldRow(_worldFeaturesContainer, displayName);
                }
            }
            catch { /* play-exit race */ }

            if (!hasFeatures)
                AddEmptyLabel(_worldFeaturesContainer, "(none)");

            bool hasControllers = false;
            try
            {
                foreach (var kvp in manager.ActiveWorldControllers)
                {
                    hasControllers = true;
                    var displayName = kvp.Value != null ? kvp.Value.name : (kvp.Key != null ? kvp.Key.name : "?");
                    AddWorldRow(_worldControllersContainer, displayName);
                }
            }
            catch { /* play-exit race */ }

            if (!hasControllers)
                AddEmptyLabel(_worldControllersContainer, "(none)");
        }

        private void RebuildWindowsSection()
        {
            _windowsContainer.Clear();

            var wm = FindWindowManager();

            if (wm == null)
            {
                var noWm = new Label("No WindowManager registered.");
                noWm.style.fontSize = 11;
                noWm.style.color = new Color(0.55f, 0.55f, 0.55f);
                noWm.style.paddingLeft = 4;
                _windowsContainer.Add(noWm);
                return;
            }

            bool hasWindows = false;
            try
            {
                foreach (var info in wm.OpenWindows)
                {
                    hasWindows = true;

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;
                    row.style.paddingLeft = 4;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new Color(0.18f, 0.18f, 0.18f);

                    // Window key
                    var keyLabel = new Label(info.Key);
                    keyLabel.style.fontSize = 11;
                    keyLabel.style.color = new Color(0.6f, 0.8f, 1f);
                    keyLabel.style.minWidth = 120;
                    row.Add(keyLabel);

                    // Arrow separator
                    var arrow = new Label("  →  ");
                    arrow.style.fontSize = 11;
                    arrow.style.color = new Color(0.45f, 0.45f, 0.45f);
                    row.Add(arrow);

                    // Window name from definition
                    var windowName = info.Definition?.WindowName ?? "?";
                    var nameLabel = new Label(windowName);
                    nameLabel.style.fontSize = 11;
                    nameLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
                    nameLabel.style.flexGrow = 1;
                    row.Add(nameLabel);

                    _windowsContainer.Add(row);
                }
            }
            catch { /* play-exit race */ }

            if (!hasWindows)
                AddEmptyLabel(_windowsContainer, "(none)");
        }

        // ── WindowManager discovery ────────────────────────────────────────────

        /// <summary>
        /// Best-effort: scans MosaicUI.Services.Entries for a value that is a
        /// Mosaic.UI.Windows.WindowManager. Returns null (no error) if not found.
        /// </summary>
        private static WindowManager FindWindowManager()
        {
            if (!MosaicUI.IsInitialized || MosaicUI.Services == null)
                return null;

            try
            {
                foreach (var kvp in MosaicUI.Services.Entries)
                {
                    if (kvp.Value is WindowManager wm)
                        return wm;
                }
            }
            catch
            {
                // Play-exit race: Services may be cleared mid-enumeration.
            }

            return null;
        }

        // ── Layout helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Builds a titled section container and appends it to <paramref name="parent"/>.
        /// Returns the content area inside the section (below the header).
        /// </summary>
        private static VisualElement BuildSection(string title, VisualElement parent)
        {
            var section = new VisualElement();
            section.style.marginBottom = 12;
            parent.Add(section);

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 12;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            titleLabel.style.marginBottom = 4;
            section.Add(titleLabel);

            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
            divider.style.marginBottom = 4;
            section.Add(divider);

            // Content container that callers populate
            var content = new VisualElement();
            section.Add(content);

            return content;
        }

        private static void AddWorldRow(VisualElement container, string displayName)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.paddingLeft = 4;

            var dot = new Label("•");
            dot.style.fontSize = 10;
            dot.style.color = new Color(0.55f, 0.55f, 0.55f);
            dot.style.marginRight = 4;
            row.Add(dot);

            var nameLabel = new Label(displayName);
            nameLabel.style.fontSize = 11;
            nameLabel.style.color = new Color(0.75f, 0.75f, 0.75f);
            nameLabel.style.flexGrow = 1;
            row.Add(nameLabel);

            container.Add(row);
        }

        private static void AddEmptyLabel(VisualElement container, string text)
        {
            var label = new Label(text);
            label.style.fontSize = 11;
            label.style.color = new Color(0.45f, 0.45f, 0.45f);
            label.style.paddingLeft = 4;
            container.Add(label);
        }

        /// <summary>
        /// Show or hide all live-data sections (the "no manager" path hides them all).
        /// </summary>
        private void SetSectionsVisible(bool visible)
        {
            _sectionsContent.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
