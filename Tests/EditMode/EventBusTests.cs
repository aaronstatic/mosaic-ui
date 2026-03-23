using System;
using Mosaic.UI;
using NUnit.Framework;

namespace Mosaic.UI.Tests
{
    [TestFixture]
    public class EventBusTests
    {
        private EventBus _bus;

        [SetUp]
        public void SetUp()
        {
            _bus = new EventBus();
        }

        [TearDown]
        public void TearDown()
        {
            _bus.Clear();
        }

        // --- Subscribe and Publish delivers event ---

        [Test]
        public void SubscribeAndPublish_DeliversEventToHandler()
        {
            int received = 0;
            _bus.Subscribe<TestEvent>(e => received = e.Value);

            _bus.Publish(new TestEvent { Value = 42 });

            Assert.That(received, Is.EqualTo(42));
        }

        // --- Disposed subscription no longer receives ---

        [Test]
        public void DisposedSubscription_NoLongerReceivesEvents()
        {
            int callCount = 0;
            var sub = _bus.Subscribe<TestEvent>(e => callCount++);

            sub.Dispose();
            _bus.Publish(new TestEvent { Value = 1 });

            Assert.That(callCount, Is.EqualTo(0));
        }

        // --- Multiple subscribers all receive ---

        [Test]
        public void MultipleSubscribers_AllReceiveEvent()
        {
            int countA = 0;
            int countB = 0;
            int countC = 0;

            _bus.Subscribe<TestEvent>(_ => countA++);
            _bus.Subscribe<TestEvent>(_ => countB++);
            _bus.Subscribe<TestEvent>(_ => countC++);

            _bus.Publish(new TestEvent { Value = 1 });

            Assert.That(countA, Is.EqualTo(1));
            Assert.That(countB, Is.EqualTo(1));
            Assert.That(countC, Is.EqualTo(1));
        }

        // --- Publishing with no subscribers does not throw ---

        [Test]
        public void Publish_WithNoSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _bus.Publish(new TestEvent { Value = 99 }));
        }

        // --- Different event types don't cross-talk ---

        [Test]
        public void DifferentEventTypes_DoNotCrossTalk()
        {
            int testEventCount = 0;
            int otherEventCount = 0;

            _bus.Subscribe<TestEvent>(_ => testEventCount++);
            _bus.Subscribe<OtherEvent>(_ => otherEventCount++);

            _bus.Publish(new TestEvent { Value = 1 });

            Assert.That(testEventCount, Is.EqualTo(1));
            Assert.That(otherEventCount, Is.EqualTo(0));
        }

        // --- SubscriptionGroup.Dispose disposes all contained subscriptions ---

        [Test]
        public void SubscriptionGroup_Dispose_DisposesAllSubscriptions()
        {
            int callCount = 0;
            var group = new SubscriptionGroup();
            group.Add(_bus.Subscribe<TestEvent>(_ => callCount++));
            group.Add(_bus.Subscribe<TestEvent>(_ => callCount++));
            group.Add(_bus.Subscribe<TestEvent>(_ => callCount++));

            group.Dispose();
            _bus.Publish(new TestEvent { Value = 1 });

            Assert.That(callCount, Is.EqualTo(0));
        }

        // --- SubscriptionGroup.Add with null throws ArgumentNullException ---

        [Test]
        public void SubscriptionGroup_Add_WithNull_ThrowsArgumentNullException()
        {
            var group = new SubscriptionGroup();

            Assert.Throws<ArgumentNullException>(() => group.Add(null));
        }

        // --- EventBus.Clear removes all subscribers ---

        [Test]
        public void EventBusClear_RemovesAllSubscribers()
        {
            int callCount = 0;
            _bus.Subscribe<TestEvent>(_ => callCount++);
            _bus.Subscribe<OtherEvent>(_ => callCount++);

            _bus.Clear();
            _bus.Publish(new TestEvent { Value = 1 });
            _bus.Publish(new OtherEvent { Text = "hello" });

            Assert.That(callCount, Is.EqualTo(0));
        }

        // --- Test event types ---

        private struct TestEvent { public int Value; }
        private struct OtherEvent { public string Text; }
    }
}
