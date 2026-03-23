using System;
using Mosaic.UI;
using NUnit.Framework;

namespace Mosaic.UI.Tests
{
    [TestFixture]
    public class ServiceRegistryTests
    {
        private ServiceRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new ServiceRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Clear();
        }

        // --- Register and Get ---

        [Test]
        public void RegisterAndGet_ReturnsSameInstance()
        {
            var service = new FakeService();
            _registry.Register(service);

            var result = _registry.Get<FakeService>();

            Assert.That(result, Is.SameAs(service));
        }

        // --- Get when not registered ---

        [Test]
        public void Get_ThrowsInvalidOperationException_WhenNotRegistered()
        {
            Assert.Throws<InvalidOperationException>(() => _registry.Get<FakeService>());
        }

        // --- TryGet when registered ---

        [Test]
        public void TryGet_ReturnsTrueAndInstance_WhenRegistered()
        {
            var service = new FakeService();
            _registry.Register(service);

            var found = _registry.TryGet<FakeService>(out var result);

            Assert.That(found, Is.True);
            Assert.That(result, Is.SameAs(service));
        }

        // --- TryGet when not registered ---

        [Test]
        public void TryGet_ReturnsFalseAndNull_WhenNotRegistered()
        {
            var found = _registry.TryGet<FakeService>(out var result);

            Assert.That(found, Is.False);
            Assert.That(result, Is.Null);
        }

        // --- Clear removes all registrations ---

        [Test]
        public void Clear_RemovesAllRegistrations()
        {
            var service = new FakeService();
            _registry.Register(service);

            _registry.Clear();

            Assert.Throws<InvalidOperationException>(() => _registry.Get<FakeService>());
        }

        // --- CreateStore registers and returns instance ---

        [Test]
        public void CreateStore_CreatesInstanceAndRegistersIt()
        {
            var store = _registry.CreateStore<FakeStore>();

            Assert.That(store, Is.Not.Null);
            Assert.That(_registry.Get<FakeStore>(), Is.SameAs(store));
        }

        // --- Register overwrites previous registration ---

        [Test]
        public void Register_OverwritesPreviousRegistration()
        {
            var first = new FakeService();
            var second = new FakeService();

            _registry.Register(first);
            _registry.Register(second);

            Assert.That(_registry.Get<FakeService>(), Is.SameAs(second));
        }

        // --- Helpers ---

        private class FakeService { }

        private class FakeStore { }
    }
}
