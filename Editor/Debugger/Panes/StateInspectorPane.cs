using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Editor
{
    /// <summary>
    /// State Inspector pane — lists every entry in MosaicUI.Services, distinguished by whether
    /// the entry is a store (INotifyBindablePropertyChanged). For stores, a value table is shown
    /// for all members decorated with [CreateProperty], updated live via propertyChanged.
    ///
    /// Data source (since 0.4.0):
    ///   The value table is built from the public MosaicInspector.GetStore(fullName) facade —
    ///   the same door the CLI uses — so the walk and the formatting cannot drift between
    ///   the two readers. The pane still enumerates Services.Entries itself, because it needs
    ///   the live store object to subscribe to propertyChanged.
    ///
    /// Live-update mechanism (push, not poll):
    ///   Attach() subscribes each store's INotifyBindablePropertyChanged.propertyChanged.
    ///   On fire, only that store's value table is refreshed (targeted, not full rebuild).
    ///   Detach() unsubscribes all handlers.
    ///
    /// Services-rescan mechanism (poll):
    ///   A 500ms scheduled item re-enumerates Services.Entries so stores registered after
    ///   Attach() appear and (if entries disappeared) stale rows are removed.
    ///   The poll does NOT re-read store values — that is push-driven only.
    /// </summary>
    public class StateInspectorPane : DebuggerPane
    {
        public override string Title => "State";

        // ── Per-store row tracking ─────────────────────────────────────────────

        private struct StoreRow
        {
            /// <summary>The store object (value) — we hold the reference for re-read.</summary>
            public object StoreObject;
            /// <summary>The INotifyBindablePropertyChanged handle used to subscribe/unsubscribe.</summary>
            public INotifyBindablePropertyChanged Notifier;
            /// <summary>Handler we attached — kept so we can -= it precisely.</summary>
            public EventHandler<BindablePropertyChangedEventArgs> Handler;
            /// <summary>Container VisualElement that holds the value table rows.</summary>
            public VisualElement ValueTableContainer;
        }

        // Keyed by the Type key used in ServiceRegistry.Entries (one per service).
        private readonly Dictionary<Type, StoreRow> _storeRows = new();
        // All service row containers keyed by Type (stores and plain services alike).
        private readonly Dictionary<Type, VisualElement> _serviceRows = new();

        // ── UI structure ──────────────────────────────────────────────────────

        private ScrollView _scrollView;
        private VisualElement _listContainer;
        private Label _emptyLabel;

        // ── Scheduling ────────────────────────────────────────────────────────

        private IVisualElementScheduledItem _rescanSchedule;

        // ── DebuggerPane overrides ─────────────────────────────────────────────

        protected override void BuildUI()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.style.flexDirection = FlexDirection.Column;

            // Header
            var header = new Label("State Inspector");
            header.style.fontSize = 14;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.paddingLeft = 8;
            header.style.paddingTop = 8;
            header.style.paddingBottom = 4;
            Root.Add(header);

            // Separator
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            sep.style.marginBottom = 4;
            Root.Add(sep);

            // Empty message (shown when no services are registered)
            _emptyLabel = new Label("No services registered.");
            _emptyLabel.style.paddingLeft = 8;
            _emptyLabel.style.paddingTop = 4;
            _emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            _emptyLabel.style.display = DisplayStyle.None;
            Root.Add(_emptyLabel);

            // Scrollable list
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            Root.Add(_scrollView);

            _listContainer = new VisualElement();
            _listContainer.style.paddingBottom = 8;
            _scrollView.Add(_listContainer);
        }

        public override void Attach()
        {
            // Rebuild from scratch — remove stale subscriptions first (re-entrant safe).
            UnsubscribeAll();

            if (!MosaicUI.IsInitialized || MosaicUI.Services == null)
                return;

            RebuildServicesList();

            // Start 500ms poll to detect newly-registered (or removed) entries.
            if (_rescanSchedule == null)
            {
                _rescanSchedule = Root.schedule.Execute(RescanServices).Every(500);
            }
            else
            {
                _rescanSchedule.Resume();
            }
        }

        public override void Detach()
        {
            // Stop polling
            _rescanSchedule?.Pause();

            // Unsubscribe all store propertyChanged handlers and clear tracking state.
            UnsubscribeAll();

            // Clear UI rows so Attach rebuilds cleanly next time.
            _listContainer?.Clear();
            _serviceRows.Clear();
            _emptyLabel.style.display = DisplayStyle.None;
        }

        public override void Refresh()
        {
            if (!MosaicUI.IsInitialized || MosaicUI.Services == null)
                return;

            RebuildServicesList();
        }

        // ── Services list building ─────────────────────────────────────────────

        /// <summary>
        /// Builds (or rebuilds) the full service list from Services.Entries.
        /// Subscribes propertyChanged for any store not already tracked.
        /// </summary>
        private void RebuildServicesList()
        {
            if (MosaicUI.Services == null)
                return;

            var entries = MosaicUI.Services.Entries;

            if (entries.Count == 0)
            {
                _listContainer.Clear();
                _serviceRows.Clear();
                UnsubscribeAll();
                _emptyLabel.style.display = DisplayStyle.Flex;
                return;
            }

            _emptyLabel.style.display = DisplayStyle.None;

            // Collect current keys to detect removed entries.
            var currentKeys = new HashSet<Type>(entries.Keys);

            // Remove rows for keys that no longer exist.
            var keysToRemove = new List<Type>();
            foreach (var key in _serviceRows.Keys)
            {
                if (!currentKeys.Contains(key))
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
            {
                var row = _serviceRows[key];
                _listContainer.Remove(row);
                _serviceRows.Remove(key);

                // Unsubscribe if it was a store.
                if (_storeRows.TryGetValue(key, out var storeRow))
                {
                    storeRow.Notifier.propertyChanged -= storeRow.Handler;
                    _storeRows.Remove(key);
                }
            }

            // Add rows for new keys; skip keys we already display.
            foreach (var kvp in entries)
            {
                Type keyType = kvp.Key;
                object value = kvp.Value;

                if (_serviceRows.ContainsKey(keyType))
                    continue; // already displayed

                var rowEl = BuildServiceRow(keyType, value);
                _listContainer.Add(rowEl);
                _serviceRows[keyType] = rowEl;
            }
        }

        /// <summary>
        /// Builds a single service row element. For stores, also wires propertyChanged.
        /// </summary>
        private VisualElement BuildServiceRow(Type keyType, object value)
        {
            bool isStore = value is INotifyBindablePropertyChanged;

            // Outer container with bottom border acting as separator
            var container = new VisualElement();
            container.style.paddingLeft = 8;
            container.style.paddingRight = 8;
            container.style.paddingTop = 6;
            container.style.paddingBottom = 6;
            container.style.borderBottomWidth = 1;
            container.style.borderBottomColor = new Color(0.18f, 0.18f, 0.18f);

            // Header row: type name + badge
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = isStore ? 4 : 0;
            container.Add(headerRow);

            // Type name label
            var typeLabel = new Label(keyType.Name);
            typeLabel.style.fontSize = 12;
            typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            typeLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
            typeLabel.style.flexGrow = 1;
            typeLabel.tooltip = keyType.FullName;
            headerRow.Add(typeLabel);

            if (isStore)
            {
                // "STORE" badge
                var badge = new Label("STORE");
                badge.style.fontSize = 9;
                badge.style.color = new Color(0.3f, 0.85f, 0.5f);
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                badge.style.paddingLeft = 4;
                badge.style.paddingRight = 4;
                badge.style.paddingTop = 2;
                badge.style.paddingBottom = 2;
                badge.style.borderTopLeftRadius = 2;
                badge.style.borderTopRightRadius = 2;
                badge.style.borderBottomLeftRadius = 2;
                badge.style.borderBottomRightRadius = 2;
                badge.style.backgroundColor = new Color(0.1f, 0.25f, 0.15f);
                headerRow.Add(badge);
            }
            else
            {
                // For plain services, show the concrete type name as secondary info
                var implLabel = new Label(value?.GetType().Name ?? "null");
                implLabel.style.fontSize = 10;
                implLabel.style.color = new Color(0.55f, 0.55f, 0.55f);
                implLabel.style.marginLeft = 6;
                headerRow.Add(implLabel);
            }

            // For stores: value table + subscription
            if (isStore)
            {
                var notifier = (INotifyBindablePropertyChanged)value;
                var tableContainer = new VisualElement();
                container.Add(tableContainer);

                PopulateValueTable(tableContainer, keyType);

                // Subscribe push update — capture tableContainer and keyType for targeted refresh.
                // The sender/args are not needed; only the fact that a property changed matters.
                EventHandler<BindablePropertyChangedEventArgs> handler = (sender, args) =>
                {
                    PopulateValueTable(tableContainer, keyType);
                };
                notifier.propertyChanged += handler;

                _storeRows[keyType] = new StoreRow
                {
                    StoreObject = value,
                    Notifier = notifier,
                    Handler = handler,
                    ValueTableContainer = tableContainer,
                };
            }

            return container;
        }

        // ── Value table ────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the store's [CreateProperty] members as a three-column table
        /// (member name → CLR type → formatted value). Clears and rebuilds each call.
        ///
        /// The rows come from MosaicInspector.GetStore(fullName), which never throws: a
        /// framework that is down, a store that disappeared, or a store with no annotated
        /// member all arrive as an empty value list, and a throwing getter arrives as the
        /// inline "&lt;error: TypeName&gt;" placeholder in the value column.
        /// </summary>
        private void PopulateValueTable(VisualElement container, Type keyType)
        {
            container.Clear();

            if (keyType == null)
                return;

            // Read through the public facade — the same path the CLI takes.
            var info = MosaicInspector.GetStore(keyType.FullName);

            if (!info.found || info.values.Count == 0)
            {
                var noProps = new Label("  (no [CreateProperty] members)");
                noProps.style.fontSize = 10;
                noProps.style.color = new Color(0.5f, 0.5f, 0.5f);
                noProps.style.paddingLeft = 8;
                container.Add(noProps);
                return;
            }

            foreach (var value in info.values)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingLeft = 8;
                row.style.paddingTop = 1;
                row.style.paddingBottom = 1;

                var nameLabel = new Label(value.name);
                nameLabel.style.fontSize = 11;
                nameLabel.style.color = new Color(0.7f, 0.85f, 1f);
                nameLabel.style.width = 140;
                nameLabel.style.minWidth = 80;
                nameLabel.style.flexShrink = 0;
                row.Add(nameLabel);

                var typeLabel = new Label(value.type ?? string.Empty);
                typeLabel.style.fontSize = 10;
                typeLabel.style.color = new Color(0.5f, 0.55f, 0.6f);
                typeLabel.style.width = 110;
                typeLabel.style.minWidth = 60;
                typeLabel.style.flexShrink = 0;
                typeLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(typeLabel);

                var valueLabel = new Label(value.value ?? string.Empty);
                valueLabel.style.fontSize = 11;
                valueLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
                valueLabel.style.flexGrow = 1;
                valueLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(valueLabel);

                container.Add(row);
            }
        }

        // ── Services rescan poll ───────────────────────────────────────────────

        /// <summary>
        /// Cheap 500ms poll: re-enumerates Services.Entries and rebuilds rows for new entries
        /// or removes rows for disappeared entries. Does NOT refresh store values (push-driven).
        /// </summary>
        private void RescanServices()
        {
            if (!MosaicUI.IsInitialized || MosaicUI.Services == null)
                return;

            RebuildServicesList();
        }

        // ── Subscription cleanup ──────────────────────────────────────────────

        private void UnsubscribeAll()
        {
            foreach (var kvp in _storeRows)
            {
                try
                {
                    kvp.Value.Notifier.propertyChanged -= kvp.Value.Handler;
                }
                catch
                {
                    // Swallow: notifier may have been destroyed (play-exit race).
                }
            }
            _storeRows.Clear();
        }
    }
}
