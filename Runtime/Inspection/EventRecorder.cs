#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Mosaic.UI
{
    /// <summary>
    /// Editor-only ring buffer over <c>EventBus.Published</c>. Keeps the last
    /// <see cref="Capacity"/> published events as plain text, never as a payload reference, so the
    /// recorder retains nothing that belongs to consumer state.
    ///
    /// <para>The whole file compiles out in a player build, together with the
    /// <c>EventBus.Published</c> hook it attaches to.</para>
    ///
    /// <para><b>Main thread only.</b> The buffer is a plain array and takes no lock.</para>
    /// </summary>
    internal static class EventRecorder
    {
        /// <summary>Number of events the ring buffer holds before it overwrites the oldest.</summary>
        internal const int Capacity = 64;

        /// <summary>Maximum length of a recorded payload summary.</summary>
        internal const int MaxSummaryLength = 200;

        private static readonly MosaicInspector.EventInfo[] Buffer = new MosaicInspector.EventInfo[Capacity];

        // Index of the next slot to write.
        private static int _writeIndex;

        // Number of filled slots, capped at Capacity.
        private static int _count;

        // Monotonic within a session. The first record after an Attach gets sequence 1.
        private static long _sequence;

        private static EventBus _bus;

        /// <summary>Subscribes to the bus publish hook and restarts the sequence at 1.</summary>
        internal static void Attach(EventBus bus)
        {
            if (bus == null)
                return;

            // Never double-subscribe, and never leave an old bus attached.
            Detach(_bus);

            _bus = bus;
            _bus.Published += Record;
        }

        /// <summary>Unsubscribes, clears the buffer, and resets the sequence.</summary>
        internal static void Detach(EventBus bus)
        {
            if (bus != null)
                bus.Published -= Record;

            if (_bus == bus || bus == null)
                _bus = null;

            Array.Clear(Buffer, 0, Buffer.Length);
            _writeIndex = 0;
            _count = 0;
            _sequence = 0;
        }

        /// <summary>Records one publish. Formats the summary once and drops the payload reference.</summary>
        internal static void Record(Type type, object payload)
        {
            var entry = new MosaicInspector.EventInfo
            {
                typeName = type != null ? type.FullName : "null",
                summary = Summarize(payload),
                sequence = ++_sequence
            };

            Buffer[_writeIndex] = entry;
            _writeIndex = (_writeIndex + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }

        /// <summary>
        /// Copies the newest <paramref name="max"/> entries into <paramref name="target"/> in
        /// ascending sequence order (oldest first). A non-positive <paramref name="max"/> copies
        /// nothing.
        /// </summary>
        internal static void CopyTo(List<MosaicInspector.EventInfo> target, int max)
        {
            if (target == null || max <= 0 || _count == 0)
                return;

            var take = max < _count ? max : _count;

            // The oldest stored entry sits at (_writeIndex - _count); skip forward to the newest
            // 'take' of them, then walk forward in sequence order.
            var start = ((_writeIndex - _count + (Capacity * 2)) + (_count - take)) % Capacity;

            for (int i = 0; i < take; i++)
            {
                var entry = Buffer[(start + i) % Capacity];
                if (entry != null)
                    target.Add(entry);
            }
        }

        private static string Summarize(object payload)
        {
            if (payload == null)
                return "null";

            string text;
            try
            {
                text = payload.ToString() ?? "null";
            }
            catch (Exception ex)
            {
                return "<error: " + ex.GetType().Name + ">";
            }

            return text.Length > MaxSummaryLength ? text.Substring(0, MaxSummaryLength) : text;
        }
    }
}
#endif
