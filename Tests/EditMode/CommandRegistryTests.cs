using System;
using System.Collections.Generic;
using Mosaic.UI;
using NUnit.Framework;

namespace Mosaic.UI.Tests
{
    [TestFixture]
    public class CommandRegistryTests
    {
        private CommandRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new CommandRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Clear();
        }

        // --- Register + Invoke (parameterless) invokes handler exactly once ---

        [Test]
        public void Register_AndInvoke_InvokesHandlerExactlyOnce()
        {
            int callCount = 0;
            _registry.Register("test/action", () => callCount++);

            _registry.Invoke("test/action");

            Assert.That(callCount, Is.EqualTo(1));
        }

        // --- Register<T> + Invoke<T> delivers the payload ---

        [Test]
        public void RegisterTyped_AndInvokeTyped_DeliversPayload()
        {
            int received = 0;
            _registry.Register<int>("set/score", value => received = value);

            _registry.Invoke("set/score", 42);

            Assert.That(received, Is.EqualTo(42));
        }

        [Test]
        public void RegisterTyped_AndInvokeTyped_DeliversStructPayload()
        {
            TestPayload received = default;
            _registry.Register<TestPayload>("test/payload", p => received = p);

            _registry.Invoke("test/payload", new TestPayload { Value = 99 });

            Assert.That(received.Value, Is.EqualTo(99));
        }

        // --- Unknown id throws InvalidOperationException ---

        [Test]
        public void Invoke_UnknownId_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => _registry.Invoke("does/not/exist"));
        }

        [Test]
        public void InvokeTyped_UnknownId_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => _registry.Invoke("does/not/exist", 5));
        }

        // --- Arity mismatch throws ---

        [Test]
        public void Invoke_Parameterless_OnTypedHandler_ThrowsMismatch()
        {
            _registry.Register<int>("counter/increment", _ => { });

            var ex = Assert.Throws<InvalidOperationException>(() => _registry.Invoke("counter/increment"));

            // Message must name the id
            Assert.That(ex.Message, Does.Contain("counter/increment"));
        }

        [Test]
        public void InvokeTyped_OnParameterlessHandler_ThrowsMismatch()
        {
            _registry.Register("counter/reset", () => { });

            var ex = Assert.Throws<InvalidOperationException>(() => _registry.Invoke("counter/reset", 1));

            // Message must name the id and the supplied type
            Assert.That(ex.Message, Does.Contain("counter/reset"));
            Assert.That(ex.Message, Does.Contain("Int32"));
        }

        [Test]
        public void InvokeTyped_WrongTypeParameter_ThrowsMismatch()
        {
            _registry.Register<int>("set/value", _ => { });

            var ex = Assert.Throws<InvalidOperationException>(() => _registry.Invoke("set/value", "wrong type"));

            // Message must name the id, the expected type (Int32), and the supplied type (String)
            Assert.That(ex.Message, Does.Contain("set/value"));
            Assert.That(ex.Message, Does.Contain("Int32"));
            Assert.That(ex.Message, Does.Contain("String"));
        }

        // --- Duplicate id registration throws ---

        [Test]
        public void Register_DuplicateId_ThrowsInvalidOperationException()
        {
            _registry.Register("counter/increment", () => { });

            var ex = Assert.Throws<InvalidOperationException>(() => _registry.Register("counter/increment", () => { }));

            Assert.That(ex.Message, Does.Contain("counter/increment"));
        }

        [Test]
        public void RegisterTyped_DuplicateId_ThrowsInvalidOperationException()
        {
            _registry.Register<int>("set/score", _ => { });

            Assert.Throws<InvalidOperationException>(() => _registry.Register<int>("set/score", _ => { }));
        }

        // --- Dispose token: Has(id) becomes false and Invoke throws ---

        [Test]
        public void DisposeToken_HasReturnsFalse()
        {
            var token = _registry.Register("temp/action", () => { });

            token.Dispose();

            Assert.That(_registry.Has("temp/action"), Is.False);
        }

        [Test]
        public void DisposeToken_SubsequentInvoke_Throws()
        {
            var token = _registry.Register("temp/action", () => { });

            token.Dispose();

            Assert.Throws<InvalidOperationException>(() => _registry.Invoke("temp/action"));
        }

        [Test]
        public void DisposeToken_Twice_DoesNotThrow()
        {
            var token = _registry.Register("temp/action", () => { });
            token.Dispose();

            // Second dispose should be safe (null guard on _unregister)
            Assert.DoesNotThrow(() => token.Dispose());
        }

        [Test]
        public void DisposeToken_AllowsReregistration()
        {
            var token = _registry.Register("temp/action", () => { });
            token.Dispose();

            // After disposal the id is free; re-registering must succeed
            Assert.DoesNotThrow(() => _registry.Register("temp/action", () => { }));
        }

        // --- Has / RegisteredIds accurately reflect current registrations ---

        [Test]
        public void Has_RegisteredId_ReturnsTrue()
        {
            _registry.Register("counter/increment", () => { });

            Assert.That(_registry.Has("counter/increment"), Is.True);
        }

        [Test]
        public void Has_UnregisteredId_ReturnsFalse()
        {
            Assert.That(_registry.Has("does/not/exist"), Is.False);
        }

        [Test]
        public void RegisteredIds_ReflectsCurrentRegistrations()
        {
            _registry.Register("action/a", () => { });
            _registry.Register("action/b", () => { });

            var ids = _registry.RegisteredIds;

            // Assert on the IReadOnlyCollection<string>.Count property directly: NUnit's
            // Has.Count constraint reflects on the runtime type, which is a string[] snapshot
            // (arrays expose Length, not Count), so Has.Count throws "Property Count was not found".
            Assert.That(ids.Count, Is.EqualTo(2));
            Assert.That(ids, Has.Member("action/a"));
            Assert.That(ids, Has.Member("action/b"));
        }

        [Test]
        public void RegisteredIds_IsSnapshot_NotLiveReference()
        {
            _registry.Register("action/a", () => { });
            var snapshotBefore = _registry.RegisteredIds;

            _registry.Register("action/b", () => { });

            // The snapshot taken before should not reflect the new addition.
            // Use .Count (the IReadOnlyCollection property), not NUnit's Has.Count, which
            // reflects on the runtime string[] type and would throw "Property Count was not found".
            Assert.That(snapshotBefore.Count, Is.EqualTo(1));
        }

        // --- Clear() empties the registry ---

        [Test]
        public void Clear_EmptiesRegistry()
        {
            _registry.Register("action/a", () => { });
            _registry.Register("action/b", () => { });

            _registry.Clear();

            Assert.That(_registry.RegisteredIds, Is.Empty);
        }

        [Test]
        public void Clear_MakesInvokeThrow()
        {
            _registry.Register("action/a", () => { });
            _registry.Clear();

            Assert.Throws<InvalidOperationException>(() => _registry.Invoke("action/a"));
        }

        [Test]
        public void Clear_AllowsReregistration()
        {
            _registry.Register("action/a", () => { });
            _registry.Clear();

            Assert.DoesNotThrow(() => _registry.Register("action/a", () => { }));
        }

        // --- Null / empty id throws ArgumentNullException / ArgumentException ---

        [Test]
        public void Register_NullId_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _registry.Register(null, () => { }));
        }

        [Test]
        public void Register_EmptyId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _registry.Register("", () => { }));
        }

        [Test]
        public void RegisterTyped_NullId_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _registry.Register<int>(null, _ => { }));
        }

        [Test]
        public void RegisterTyped_EmptyId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _registry.Register<int>("", _ => { }));
        }

        // --- Null handler throws ArgumentNullException ---

        [Test]
        public void Register_NullHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _registry.Register("action/a", (Action)null));
        }

        [Test]
        public void RegisterTyped_NullHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _registry.Register<int>("action/a", null));
        }

        // --- MosaicUI facade: Commands is populated/nulled by Initialize/Shutdown ---

        [Test]
        public void MosaicUI_Initialize_PopulatesCommands()
        {
            MosaicUI.Initialize();

            Assert.That(MosaicUI.Commands, Is.Not.Null);

            MosaicUI.Shutdown();
        }

        [Test]
        public void MosaicUI_Shutdown_NullsCommands()
        {
            MosaicUI.Initialize();
            MosaicUI.Shutdown();

            Assert.That(MosaicUI.Commands, Is.Null);
        }

        [Test]
        public void MosaicUI_SecondInitialize_IsNoOp()
        {
            MosaicUI.Initialize();
            var first = MosaicUI.Commands;

            MosaicUI.Initialize(); // second call — must not replace the instance
            var second = MosaicUI.Commands;

            Assert.That(second, Is.SameAs(first));

            MosaicUI.Shutdown();
        }

        // --- Private nested payload types ---

        private struct TestPayload { public int Value; }
    }
}
