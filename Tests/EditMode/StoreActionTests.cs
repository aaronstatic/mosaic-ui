using Mosaic.UI;
using NUnit.Framework;
using Unity.Properties;

namespace Mosaic.UI.Tests
{
    /// <summary>
    /// Proves the Layer-1 action convention: backing setters are <c>private</c> and state
    /// is mutated exclusively through named action methods. Each action must:
    /// <list type="bullet">
    ///   <item><description>Change the relevant property to the expected value.</description></item>
    ///   <item><description>Fire <c>propertyChanged</c> exactly once per real state change.</description></item>
    ///   <item><description>Not fire <c>propertyChanged</c> when the value is already equal
    ///   (the <c>SetProperty</c> equality gate).</description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The test assembly (Mosaic.UI.Tests) cannot reference Assembly-CSharp, so the
    /// feature-local <c>MosaicHarness.Composition.CounterStore</c> cannot be used here.
    /// A structurally identical <c>ActionTestStore</c> is defined inside this file,
    /// mirroring the pattern used by <see cref="StoreTests"/>.
    /// </remarks>
    [TestFixture]
    public class StoreActionTests
    {
        private ActionTestStore _store;

        [SetUp]
        public void SetUp()
        {
            _store = new ActionTestStore();
        }

        // ── Increment ─────────────────────────────────────────────────────────────

        [Test]
        public void Increment_IncreasesCountByOne()
        {
            _store.Increment();
            Assert.That(_store.Count, Is.EqualTo(1));
        }

        [Test]
        public void Increment_FiresPropertyChangedExactlyOnce()
        {
            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.Increment();

            Assert.That(eventCount, Is.EqualTo(1));
        }

        [Test]
        public void Increment_MultipleTimesFiresOncePerCall()
        {
            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.Increment();
            _store.Increment();
            _store.Increment();

            Assert.That(_store.Count, Is.EqualTo(3));
            Assert.That(eventCount, Is.EqualTo(3));
        }

        // ── Decrement ─────────────────────────────────────────────────────────────

        [Test]
        public void Decrement_DecreasesCountByOne()
        {
            _store.Increment(); // Count = 1
            _store.Decrement(); // Count = 0
            Assert.That(_store.Count, Is.EqualTo(0));
        }

        [Test]
        public void Decrement_FiresPropertyChangedExactlyOnce()
        {
            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.Decrement();

            Assert.That(eventCount, Is.EqualTo(1));
        }

        // ── Reset ─────────────────────────────────────────────────────────────────

        [Test]
        public void Reset_SetsCountToZero()
        {
            _store.Increment();
            _store.Increment();
            _store.Reset();
            Assert.That(_store.Count, Is.EqualTo(0));
        }

        [Test]
        public void Reset_FiresPropertyChangedWhenCountWasNonZero()
        {
            _store.Increment(); // Count = 1; consumes one event

            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.Reset(); // Count → 0; value changed → fires

            Assert.That(eventCount, Is.EqualTo(1));
        }

        [Test]
        public void Reset_DoesNotFirePropertyChanged_WhenCountIsAlreadyZero()
        {
            // Count starts at 0 (default). Calling Reset() must not fire propertyChanged
            // because SetProperty's equality gate rejects an assignment of the same value.
            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.Reset(); // 0 → 0: no real change

            Assert.That(eventCount, Is.EqualTo(0));
        }

        // ── SetNote ───────────────────────────────────────────────────────────────

        [Test]
        public void SetNote_SetsNoteToExpectedValue()
        {
            _store.SetNote("hello");
            Assert.That(_store.Note, Is.EqualTo("hello"));
        }

        [Test]
        public void SetNote_FiresPropertyChangedExactlyOnce()
        {
            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.SetNote("hello");

            Assert.That(eventCount, Is.EqualTo(1));
        }

        [Test]
        public void SetNote_DoesNotFirePropertyChanged_WhenValueIsUnchanged()
        {
            _store.SetNote("hello"); // initial set

            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.SetNote("hello"); // same value — equality gate must block the event

            Assert.That(eventCount, Is.EqualTo(0));
        }

        // ── Selector subscription via actions ─────────────────────────────────────

        [Test]
        public void Subscribe_ToCount_ReceivesValueAfterIncrement()
        {
            int received = -1;
            using var sub = _store.Subscribe(s => s.Count, v => received = v);

            _store.Increment();

            Assert.That(received, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_ToCount_DoesNotFire_WhenResetOnAlreadyZero()
        {
            int callCount = 0;
            using var sub = _store.Subscribe(s => s.Count, _ => callCount++);

            _store.Reset(); // 0 → 0: equality gate; selector should not fire

            Assert.That(callCount, Is.EqualTo(0));
        }

        [Test]
        public void Subscribe_ToNote_DoesNotFire_WhenCountChanges()
        {
            int callCount = 0;
            using var sub = _store.Subscribe(s => s.Note, _ => callCount++);

            _store.Increment(); // different property; Note subscription must not fire

            Assert.That(callCount, Is.EqualTo(0));
        }

        // ── Test-local store (cross-assembly constraint: cannot use Assembly-CSharp) ─

        /// <summary>
        /// Structural equivalent of <c>MosaicHarness.Composition.CounterStore</c>
        /// with private setters and explicit action methods, defined here because
        /// <c>Mosaic.UI.Tests</c> cannot reference <c>Assembly-CSharp</c>.
        /// </summary>
        private class ActionTestStore : Store<ActionTestStore>
        {
            private int _count;
            private string _note = "";

            [CreateProperty]
            public int Count { get => _count; private set => SetProperty(ref _count, value); }

            [CreateProperty]
            public string Note { get => _note; private set => SetProperty(ref _note, value); }

            public void Increment() => Count++;
            public void Decrement() => Count--;
            public void Reset()     => Count = 0;
            public void SetNote(string value) => Note = value;
        }
    }
}
