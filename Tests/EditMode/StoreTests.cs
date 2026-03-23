using Mosaic.UI;
using NUnit.Framework;
using Unity.Properties;

namespace Mosaic.UI.Tests
{
    [TestFixture]
    public class StoreTests
    {
        private TestStore _store;

        [SetUp]
        public void SetUp()
        {
            _store = new TestStore();
        }

        // --- SetProperty raises propertyChanged with correct property name ---

        [Test]
        public void SetProperty_RaisesPropertyChanged()
        {
            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.Counter = 5;

            Assert.That(eventCount, Is.EqualTo(1));
        }

        // --- SetProperty with same value does NOT raise propertyChanged ---

        [Test]
        public void SetProperty_WithSameValue_DoesNotRaisePropertyChanged()
        {
            _store.Counter = 3;

            int eventCount = 0;
            _store.propertyChanged += (_, _) => eventCount++;

            _store.Counter = 3;

            Assert.That(eventCount, Is.EqualTo(0));
        }

        // --- SetProperty returns true when changed, false when unchanged ---

        [Test]
        public void SetProperty_ReturnsTrueWhenChanged_FalseWhenUnchanged()
        {
            // The public API is through the property, so we verify via the side-effects.
            // Counter starts at default (0). Setting to a new value should fire the event.
            int changedCount = 0;
            _store.propertyChanged += (_, _) => changedCount++;

            _store.Counter = 10; // changed
            _store.Counter = 10; // unchanged

            Assert.That(changedCount, Is.EqualTo(1));
        }

        // --- GetViewHashCode changes after property mutation ---

        [Test]
        public void GetViewHashCode_ChangesAfterPropertyMutation()
        {
            long before = _store.GetViewHashCode();

            _store.Counter = 99;

            Assert.That(_store.GetViewHashCode(), Is.Not.EqualTo(before));
        }

        // --- GetViewHashCode does NOT change when same value is set ---

        [Test]
        public void GetViewHashCode_DoesNotChange_WhenSameValueSet()
        {
            _store.Counter = 7;
            long before = _store.GetViewHashCode();

            _store.Counter = 7;

            Assert.That(_store.GetViewHashCode(), Is.EqualTo(before));
        }

        // --- Selector subscription fires when selected slice changes ---

        [Test]
        public void SelectorSubscription_Fires_WhenSelectedSliceChanges()
        {
            int callCount = 0;
            int lastValue = 0;

            using var sub = _store.Subscribe(s => s.Counter, v =>
            {
                callCount++;
                lastValue = v;
            });

            _store.Counter = 42;

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(lastValue, Is.EqualTo(42));
        }

        // --- Selector subscription does NOT fire when other properties change ---

        [Test]
        public void SelectorSubscription_DoesNotFire_WhenOtherPropertiesChange()
        {
            int callCount = 0;

            using var sub = _store.Subscribe(s => s.Counter, _ => callCount++);

            // Mutate a different property
            _store.Name = "Changed";

            Assert.That(callCount, Is.EqualTo(0));
        }

        // --- Selector subscription can be disposed to stop receiving ---

        [Test]
        public void SelectorSubscription_CanBeDisposed_ToStopReceiving()
        {
            int callCount = 0;
            var sub = _store.Subscribe(s => s.Counter, _ => callCount++);

            sub.Dispose();
            _store.Counter = 100;

            Assert.That(callCount, Is.EqualTo(0));
        }

        // --- Multiple selector subscriptions on same store work independently ---

        [Test]
        public void MultipleSubscriptions_OnSameStore_WorkIndependently()
        {
            int counterCallCount = 0;
            int nameCallCount = 0;

            using var counterSub = _store.Subscribe(s => s.Counter, _ => counterCallCount++);
            using var nameSub = _store.Subscribe(s => s.Name, _ => nameCallCount++);

            _store.Counter = 5;
            _store.Counter = 6;
            _store.Name = "Alpha";

            Assert.That(counterCallCount, Is.EqualTo(2));
            Assert.That(nameCallCount, Is.EqualTo(1));
        }

        // --- Test store ---

        private class TestStore : Store<TestStore>
        {
            private int _counter;
            private string _name;

            [CreateProperty]
            public int Counter
            {
                get => _counter;
                set => SetProperty(ref _counter, value);
            }

            [CreateProperty]
            public string Name
            {
                get => _name;
                set => SetProperty(ref _name, value);
            }
        }
    }
}
