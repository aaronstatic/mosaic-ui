using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Editor
{
    /// <summary>
    /// Event Monitor pane — a ring-buffered, timestamped log of every message published through
    /// MosaicUI.Events during play mode.
    ///
    /// Mechanism:
    ///   Attach() subscribes MosaicUI.Events.Published (the #if UNITY_EDITOR introspection hook).
    ///   OnPublished() builds an EventEntry and prepends a row to the log (newest-first).
    ///   When the ring buffer exceeds its cap, the oldest entry and its UI row are trimmed.
    ///   Detach() unsubscribes and clears both the buffer and the UI list.
    ///
    /// Filter:
    ///   A text field performs case-insensitive substring matching on the event type name.
    ///   Changing the filter triggers a full UI rebuild from the in-memory buffer.
    ///
    /// Sort order: newest-first (rows prepended; most recent event is always at the top).
    ///
    /// Ring buffer cap: 500 entries (Decision 5, implementation.md).
    /// No cross-session persistence — buffer is cleared on Detach() / play-exit.
    /// </summary>
    public class EventMonitorPane : DebuggerPane
    {
        public override string Title => "Events";

        // ── Ring buffer ───────────────────────────────────────────────────────

        private const int RingBufferCap = 500;

        /// <summary>
        /// Ordered oldest-to-newest; we enqueue at the back, dequeue from the front.
        /// When building newest-first UI we iterate in reverse.
        /// </summary>
        private readonly Queue<EventEntry> _buffer = new();

        // ── UI elements ───────────────────────────────────────────────────────

        private VisualElement _toolbar;
        private TextField _filterField;
        private Label _countLabel;
        private ScrollView _scrollView;

        /// <summary>
        /// Direct container of row elements. Rows are inserted at index 0 (newest first).
        /// The number of children is capped at RingBufferCap to mirror the buffer.
        /// </summary>
        private VisualElement _listContainer;

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>Current filter text (lowercased for comparison).</summary>
        private string _filterLower = string.Empty;

        /// <summary>Guards against double-subscribe across attach/detach cycles.</summary>
        private bool _subscribed = false;

        // ── Entry model ───────────────────────────────────────────────────────

        private sealed class EventEntry
        {
            /// <summary>Short name used for filtering and display.</summary>
            public readonly string TypeName;

            /// <summary>Full type name shown in tooltip.</summary>
            public readonly string TypeFullName;

            /// <summary>Human-readable payload summary.</summary>
            public readonly string PayloadSummary;

            /// <summary>UnityEngine.Time.frameCount at publish time.</summary>
            public readonly int Frame;

            /// <summary>Wall-clock timestamp formatted "HH:mm:ss.fff".</summary>
            public readonly string Timestamp;

            public EventEntry(Type type, object message)
            {
                TypeName = type.Name;
                TypeFullName = type.FullName ?? type.Name;
                PayloadSummary = EventMonitorPane.FormatPayload(message);
                Frame = Time.frameCount;
                Timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            }
        }

        // ── Payload formatter ─────────────────────────────────────────────────

        /// <summary>
        /// Produces a concise, human-readable summary of a published event payload.
        ///
        /// Strategy (in order):
        ///   1. null → "null"
        ///   2. If the concrete type overrides ToString (i.e. the declaring type is not
        ///      System.Object, System.ValueType, or a generic struct without override) →
        ///      use ToString(), capped at MaxPayloadLength.
        ///   3. Otherwise, reflect public fields + public readable properties and format as
        ///      "Name=Value, ..." up to MaxMemberCount members and MaxPayloadLength chars total.
        ///      Each member read is wrapped in its own try/catch.
        /// </summary>
        private static string FormatPayload(object message)
        {
            if (message == null)
                return "null";

            Type type = message.GetType();

            // Check whether ToString is overridden (declaring type is not object / ValueType).
            if (HasCustomToString(type))
            {
                try
                {
                    string s = message.ToString();
                    return Truncate(s, MaxPayloadLength);
                }
                catch (Exception ex)
                {
                    return $"<ToString error: {ex.GetType().Name}>";
                }
            }

            // Reflect public fields + public readable properties.
            return ReflectPayload(message, type);
        }

        private const int MaxPayloadLength = 200;
        private const int MaxMemberCount = 8;

        private static bool HasCustomToString(Type type)
        {
            // Walk up to find the actual declaring type of ToString().
            var method = type.GetMethod(
                "ToString",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);

            if (method != null)
                return true; // declared on this exact type

            // Walk up one level at a time; stop at object or ValueType.
            var t = type.BaseType;
            while (t != null && t != typeof(object) && t != typeof(ValueType))
            {
                method = t.GetMethod(
                    "ToString",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method != null)
                    return true;
                t = t.BaseType;
            }

            return false; // only object.ToString / ValueType.ToString
        }

        private static string ReflectPayload(object message, Type type)
        {
            var sb = new StringBuilder();
            int count = 0;

            // Public fields
            foreach (var fi in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (count >= MaxMemberCount)
                    break;
                AppendMember(sb, fi.Name, () => fi.GetValue(message), ref count);
            }

            // Public readable properties (excluding indexers)
            foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (count >= MaxMemberCount)
                    break;
                if (!pi.CanRead || pi.GetIndexParameters().Length > 0)
                    continue;
                AppendMember(sb, pi.Name, () => pi.GetValue(message), ref count);
            }

            if (sb.Length == 0)
                return type.Name;

            return Truncate(sb.ToString(), MaxPayloadLength);
        }

        private static void AppendMember(StringBuilder sb, string name, Func<object> getValue, ref int count)
        {
            string valueStr;
            try
            {
                object val = getValue();
                valueStr = val?.ToString() ?? "null";
            }
            catch (Exception ex)
            {
                valueStr = $"<{ex.GetType().Name}>";
            }

            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(name);
            sb.Append('=');
            sb.Append(valueStr);
            count++;
        }

        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            return s.Length <= maxLength ? s : s.Substring(0, maxLength - 1) + "…";
        }

        // ── DebuggerPane overrides ─────────────────────────────────────────────

        protected override void BuildUI()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.style.flexDirection = FlexDirection.Column;

            // ── Toolbar (filter + count + Clear) ──────────────────────────────
            _toolbar = new VisualElement();
            _toolbar.style.flexDirection = FlexDirection.Row;
            _toolbar.style.alignItems = Align.Center;
            _toolbar.style.paddingLeft = 8;
            _toolbar.style.paddingRight = 8;
            _toolbar.style.paddingTop = 6;
            _toolbar.style.paddingBottom = 6;
            _toolbar.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            Root.Add(_toolbar);

            // Title label
            var titleLabel = new Label("Event Monitor");
            titleLabel.style.fontSize = 13;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.9f, 0.9f, 0.9f);
            titleLabel.style.marginRight = 10;
            _toolbar.Add(titleLabel);

            // Filter field (text, case-insensitive substring match on type name)
            var filterLabel = new Label("Filter:");
            filterLabel.style.fontSize = 11;
            filterLabel.style.color = new Color(0.65f, 0.65f, 0.65f);
            filterLabel.style.marginRight = 4;
            _toolbar.Add(filterLabel);

            _filterField = new TextField();
            _filterField.style.width = 140;
            _filterField.style.marginRight = 8;
            _filterField.style.flexShrink = 1;
            _filterField.tooltip = "Substring filter on event type name (case-insensitive)";
            _filterField.RegisterValueChangedCallback(evt =>
            {
                _filterLower = (evt.newValue ?? string.Empty).ToLowerInvariant();
                RebuildListFromBuffer();
            });
            _toolbar.Add(_filterField);

            // Spacer
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            _toolbar.Add(spacer);

            // Entry count label
            _countLabel = new Label("0 events");
            _countLabel.style.fontSize = 11;
            _countLabel.style.color = new Color(0.55f, 0.55f, 0.55f);
            _countLabel.style.marginRight = 8;
            _toolbar.Add(_countLabel);

            // Clear button
            var clearBtn = new Button(OnClearClicked) { text = "Clear" };
            clearBtn.style.paddingLeft = 8;
            clearBtn.style.paddingRight = 8;
            clearBtn.style.paddingTop = 3;
            clearBtn.style.paddingBottom = 3;
            clearBtn.style.fontSize = 11;
            _toolbar.Add(clearBtn);

            // ── Separator ─────────────────────────────────────────────────────
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            Root.Add(sep);

            // ── Column header ─────────────────────────────────────────────────
            var colHeader = new VisualElement();
            colHeader.style.flexDirection = FlexDirection.Row;
            colHeader.style.paddingLeft = 8;
            colHeader.style.paddingRight = 8;
            colHeader.style.paddingTop = 3;
            colHeader.style.paddingBottom = 3;
            colHeader.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            Root.Add(colHeader);

            void AddColHeader(string text, float width, bool grow = false)
            {
                var lbl = new Label(text);
                lbl.style.fontSize = 10;
                lbl.style.color = new Color(0.5f, 0.5f, 0.5f);
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                if (grow)
                    lbl.style.flexGrow = 1;
                else
                    lbl.style.width = width;
                colHeader.Add(lbl);
            }

            AddColHeader("TIME", 80);
            AddColHeader("FRAME", 50);
            AddColHeader("TYPE", 120);
            AddColHeader("PAYLOAD", 0, grow: true);

            // ── Scrollable list ───────────────────────────────────────────────
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1;
            Root.Add(_scrollView);

            _listContainer = new VisualElement();
            _listContainer.style.paddingBottom = 8;
            _scrollView.Add(_listContainer);
        }

        public override void Attach()
        {
            // Re-entrant safe: always unsubscribe first, then re-subscribe.
            UnsubscribeHook();

            if (!MosaicUI.IsInitialized || MosaicUI.Events == null)
                return;

            MosaicUI.Events.Published += OnPublished;
            _subscribed = true;
        }

        public override void Detach()
        {
            // Unsubscribe from the event bus hook.
            UnsubscribeHook();

            // Clear in-memory buffer — no cross-session persistence (T4.4).
            _buffer.Clear();

            // Clear the UI list.
            _listContainer?.Clear();

            // Reset count label and filter.
            if (_countLabel != null)
                _countLabel.text = "0 events";

            if (_filterField != null)
            {
                _filterField.SetValueWithoutNotify(string.Empty);
                _filterLower = string.Empty;
            }
        }

        public override void Refresh()
        {
            // Called externally (e.g. after a state change); rebuild from buffer.
            RebuildListFromBuffer();
        }

        // ── Hook subscription ─────────────────────────────────────────────────

        private void UnsubscribeHook()
        {
            if (_subscribed)
            {
                try
                {
                    // Guard: MosaicUI.Events may be null if Shutdown() already ran.
                    if (MosaicUI.Events != null)
                        MosaicUI.Events.Published -= OnPublished;
                }
                catch
                {
                    // Swallow any race-condition exception on play-exit.
                }
                _subscribed = false;
            }
        }

        // ── Publish handler ───────────────────────────────────────────────────

        /// <summary>
        /// Called on the main thread after every MosaicUI.Events.Publish&lt;T&gt; dispatch.
        /// Builds an entry, appends to the ring buffer, and — if the filter passes —
        /// prepends a row to the UI list (newest-first) and trims the oldest row if over cap.
        /// </summary>
        private void OnPublished(Type type, object message)
        {
            // Guard: pane may not yet be fully built (Root null), though in practice
            // Attach() is called after BuildUI() so this is a belt-and-suspenders check.
            if (_listContainer == null)
                return;

            var entry = new EventEntry(type, message);

            // Enqueue — dequeue oldest if over cap.
            _buffer.Enqueue(entry);
            if (_buffer.Count > RingBufferCap)
                _buffer.Dequeue();

            // Check filter — only update UI if this entry passes.
            if (!PassesFilter(entry))
            {
                // Still update count label to reflect buffer size (not filtered count).
                UpdateCountLabel();
                return;
            }

            // Prepend the row (newest-first).
            var row = BuildRow(entry);
            _listContainer.Insert(0, row);

            // Trim the UI list if it exceeds the cap (keeps count in sync with ring buffer).
            while (_listContainer.childCount > RingBufferCap)
                _listContainer.RemoveAt(_listContainer.childCount - 1);

            UpdateCountLabel();
        }

        // ── List rendering ────────────────────────────────────────────────────

        /// <summary>
        /// Full rebuild of the UI list from the current in-memory buffer.
        /// Called when the filter changes or Clear is pressed.
        /// Iterates the buffer newest-first (reversed).
        /// </summary>
        private void RebuildListFromBuffer()
        {
            if (_listContainer == null)
                return;

            _listContainer.Clear();

            // Build an array so we can iterate in reverse (newest-first).
            var arr = _buffer.ToArray(); // oldest→newest
            for (int i = arr.Length - 1; i >= 0; i--)
            {
                var entry = arr[i];
                if (!PassesFilter(entry))
                    continue;

                _listContainer.Add(BuildRow(entry));

                // Cap the UI list at RingBufferCap rows even when filtering produces more visible rows
                // than the buffer holds (shouldn't normally happen, but guard for correctness).
                if (_listContainer.childCount >= RingBufferCap)
                    break;
            }

            UpdateCountLabel();
        }

        /// <summary>
        /// Builds a single log row VisualElement for the given entry.
        /// Columns: timestamp | frame | type name (with full name tooltip) | payload summary.
        /// </summary>
        private static VisualElement BuildRow(EventEntry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.17f, 0.17f, 0.17f);

            // Timestamp column
            var timeLabel = new Label(entry.Timestamp);
            timeLabel.style.fontSize = 10;
            timeLabel.style.color = new Color(0.5f, 0.65f, 0.5f);
            timeLabel.style.width = 80;
            timeLabel.style.minWidth = 80;
            row.Add(timeLabel);

            // Frame column
            var frameLabel = new Label(entry.Frame.ToString());
            frameLabel.style.fontSize = 10;
            frameLabel.style.color = new Color(0.55f, 0.55f, 0.7f);
            frameLabel.style.width = 50;
            frameLabel.style.minWidth = 50;
            frameLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            frameLabel.style.marginRight = 8;
            row.Add(frameLabel);

            // Type name column (tooltip shows full name)
            var typeLabel = new Label(entry.TypeName);
            typeLabel.style.fontSize = 10;
            typeLabel.style.color = new Color(0.85f, 0.75f, 0.45f);
            typeLabel.style.width = 120;
            typeLabel.style.minWidth = 80;
            typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            typeLabel.tooltip = entry.TypeFullName;
            row.Add(typeLabel);

            // Payload summary column (flex-grow, tooltip shows full summary)
            var payloadLabel = new Label(entry.PayloadSummary);
            payloadLabel.style.fontSize = 10;
            payloadLabel.style.color = new Color(0.75f, 0.75f, 0.75f);
            payloadLabel.style.flexGrow = 1;
            payloadLabel.style.overflow = Overflow.Hidden;
            payloadLabel.tooltip = entry.PayloadSummary;
            row.Add(payloadLabel);

            return row;
        }

        // ── Filter ────────────────────────────────────────────────────────────

        private bool PassesFilter(EventEntry entry)
        {
            if (string.IsNullOrEmpty(_filterLower))
                return true;

            return entry.TypeName.ToLowerInvariant().Contains(_filterLower)
                || entry.TypeFullName.ToLowerInvariant().Contains(_filterLower);
        }

        // ── Clear ─────────────────────────────────────────────────────────────

        private void OnClearClicked()
        {
            _buffer.Clear();
            _listContainer?.Clear();
            UpdateCountLabel();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void UpdateCountLabel()
        {
            if (_countLabel == null)
                return;

            int total = _buffer.Count;
            if (total == RingBufferCap)
                _countLabel.text = $"{total} events (cap reached)";
            else
                _countLabel.text = total == 1 ? "1 event" : $"{total} events";
        }
    }
}
