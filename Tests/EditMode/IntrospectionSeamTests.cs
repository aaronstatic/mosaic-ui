using System;
using System.Collections.Generic;
using System.Linq;
using Mosaic.UI;
using Mosaic.UI.Windows;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
// 'Object' is ambiguous between System.Object and UnityEngine.Object (both namespaces are imported);
// the bare 'Object.DestroyImmediate' calls below mean the Unity one.
using Object = UnityEngine.Object;

namespace Mosaic.UI.Tests
{
    /// <summary>
    /// Covers the read-only <c>internal</c> introspection seams added in foundation/tooling Phase 1
    /// (consumed by the <c>Mosaic.UI.Editor</c> debugger). These are accessible here because
    /// <c>Mosaic.UI</c> exposes internals via <c>[assembly: InternalsVisibleTo("Mosaic.UI.Tests")]</c>.
    ///
    /// <para><b>Gotcha:</b> NUnit <c>Has.Count</c> throws on array-typed collections (project memory:
    /// <c>nunit-has-count-array-gotcha</c>). These tests assert <c>.Count</c> directly instead.</para>
    /// </summary>
    [TestFixture]
    public class IntrospectionSeamTests
    {
        // ── ServiceRegistry.Entries ───────────────────────────────────────────────────

        [Test]
        public void ServiceRegistry_Entries_EnumeratesEveryRegisteredTypeAndObject()
        {
            var registry = new ServiceRegistry();
            var service = new FakeService();
            var store = new FakeStore();

            registry.Register(service);
            registry.Register(store);

            var entries = registry.Entries;

            // Assert .Count directly (do NOT use Has.Count).
            Assert.That(entries.Count, Is.EqualTo(2));

            Assert.That(entries.ContainsKey(typeof(FakeService)), Is.True);
            Assert.That(entries.ContainsKey(typeof(FakeStore)), Is.True);
            Assert.That(entries[typeof(FakeService)], Is.SameAs(service));
            Assert.That(entries[typeof(FakeStore)], Is.SameAs(store));
        }

        [Test]
        public void ServiceRegistry_Entries_ReflectsRegistrationMadeAfterFirstAccess()
        {
            var registry = new ServiceRegistry();
            registry.Register(new FakeService());

            // Access the view BEFORE the later registration — proves it is a live view, not a snapshot/copy.
            var entries = registry.Entries;
            Assert.That(entries.Count, Is.EqualTo(1));

            var addedLater = new FakeStore();
            registry.Register(addedLater);

            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries.ContainsKey(typeof(FakeStore)), Is.True);
            Assert.That(entries[typeof(FakeStore)], Is.SameAs(addedLater));
        }

        // ── EventBus.Published ────────────────────────────────────────────────────────

        [Test]
        public void EventBus_Published_FiresWithCorrectTypeAndPayload()
        {
            var bus = new EventBus();

            Type firedType = null;
            object firedPayload = null;
            int fireCount = 0;

            bus.Published += (t, p) =>
            {
                firedType = t;
                firedPayload = p;
                fireCount++;
            };

            var message = new TestEvent { Value = 42 };
            bus.Publish(message);

            Assert.That(fireCount, Is.EqualTo(1), "Published must fire exactly once per publish.");
            Assert.That(firedType, Is.EqualTo(typeof(TestEvent)));
            Assert.That(firedPayload, Is.EqualTo((object)message));
        }

        [Test]
        public void EventBus_Published_FiresAfterRealSubscribersRun()
        {
            var bus = new EventBus();
            var order = new List<string>();

            // Real subscriber appends "handler"; the editor observer appends "hook".
            bus.Subscribe<TestEvent>(_ => order.Add("handler"));
            bus.Published += (_, __) => order.Add("hook");

            bus.Publish(new TestEvent { Value = 1 });

            Assert.That(order.Count, Is.EqualTo(2));
            Assert.That(order[0], Is.EqualTo("handler"), "Real subscribers must run before the Published hook.");
            Assert.That(order[1], Is.EqualTo("hook"), "Published must fire AFTER dispatch.");
        }

        [Test]
        public void EventBus_Published_FiresEvenWithZeroRealSubscribers()
        {
            var bus = new EventBus();

            Type firedType = null;
            int fireCount = 0;
            bus.Published += (t, _) =>
            {
                firedType = t;
                fireCount++;
            };

            // No Subscribe<T>() call at all — the old early-return path must NOT skip the hook.
            bus.Publish(new TestEvent { Value = 7 });

            Assert.That(fireCount, Is.EqualTo(1), "Published must fire for zero-subscriber publishes.");
            Assert.That(firedType, Is.EqualTo(typeof(TestEvent)));
        }

        [Test]
        public void EventBus_Published_DoesNotFireAfterObserverUnsubscribes()
        {
            var bus = new EventBus();

            int fireCount = 0;
            Action<Type, object> observer = (_, __) => fireCount++;

            bus.Published += observer;
            bus.Publish(new TestEvent { Value = 1 });
            Assert.That(fireCount, Is.EqualTo(1));

            bus.Published -= observer;
            bus.Publish(new TestEvent { Value = 2 });
            Assert.That(fireCount, Is.EqualTo(1), "Detached observer must not receive further publishes.");
        }

        [Test]
        public void EventBus_SubscriberCount_ReflectsHandlerCount()
        {
            var bus = new EventBus();

            Assert.That(bus.SubscriberCount(typeof(TestEvent)), Is.EqualTo(0));

            var subA = bus.Subscribe<TestEvent>(_ => { });
            var subB = bus.Subscribe<TestEvent>(_ => { });
            Assert.That(bus.SubscriberCount(typeof(TestEvent)), Is.EqualTo(2));
            Assert.That(bus.SubscriberCount(typeof(OtherEvent)), Is.EqualTo(0));
            Assert.That(bus.SubscriberCount(null), Is.EqualTo(0));

            subA.Dispose();
            Assert.That(bus.SubscriberCount(typeof(TestEvent)), Is.EqualTo(1));
            subB.Dispose();
            Assert.That(bus.SubscriberCount(typeof(TestEvent)), Is.EqualTo(0));
        }

        // ── ModeHistory.Items ─────────────────────────────────────────────────────────

        [Test]
        public void ModeHistory_Items_EnumeratesTopToBottom()
        {
            var history = new ModeHistory();
            var modeA = ScriptableObject.CreateInstance<ModeDefinition>();
            var modeB = ScriptableObject.CreateInstance<ModeDefinition>();

            try
            {
                history.Push(modeA);
                history.Push(modeB);

                var items = history.Items.ToList();

                // Stack<T> enumerates most-recent-first → B then A (the back-stack display order).
                Assert.That(items.Count, Is.EqualTo(2));
                Assert.That(items[0], Is.SameAs(modeB));
                Assert.That(items[1], Is.SameAs(modeA));

                // The view must not have disturbed the stack.
                Assert.That(history.Count, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(modeA);
                Object.DestroyImmediate(modeB);
            }
        }

        // ── WindowManager.OpenWindows ─────────────────────────────────────────────────

        [Test]
        public void WindowManager_OpenWindows_ListsOpenWindowsWithKeyDefinitionAndInstance()
        {
            var layer = new VisualElement();
            var services = new ServiceRegistry();
            var manager = new WindowManager(layer, services);

            var panelDef = CreatePanelDefinition("InspectorPanel");
            var windowDef = CreateWindowDefinition("Inspector", panelDef);

            try
            {
                // Empty before opening anything.
                Assert.That(manager.OpenWindows.Count(), Is.EqualTo(0));

                var instance = manager.Open(windowDef);

                var open = manager.OpenWindows.ToList();
                Assert.That(open.Count, Is.EqualTo(1));
                Assert.That(open[0].Key, Is.EqualTo(windowDef.WindowName));
                Assert.That(open[0].Definition, Is.SameAs(windowDef));
                Assert.That(open[0].Instance, Is.SameAs(instance));
            }
            finally
            {
                manager.CloseAll();
                var uxml = panelDef.UXML;
                Object.DestroyImmediate(windowDef);
                Object.DestroyImmediate(panelDef);
                if (uxml != null)
                    Object.DestroyImmediate(uxml);
                services.Clear();
            }
        }

        [Test]
        public void WindowManager_OpenWindows_RemovesWindowAfterClose()
        {
            var layer = new VisualElement();
            var services = new ServiceRegistry();
            var manager = new WindowManager(layer, services);

            var panelDef = CreatePanelDefinition("InspectorPanel");
            var windowDef = CreateWindowDefinition("Inspector", panelDef);

            try
            {
                manager.Open(windowDef);
                Assert.That(manager.OpenWindows.Count(), Is.EqualTo(1));

                manager.Close(windowDef.WindowName);

                Assert.That(manager.OpenWindows.Count(), Is.EqualTo(0));
            }
            finally
            {
                manager.CloseAll();
                var uxml = panelDef.UXML;
                Object.DestroyImmediate(windowDef);
                Object.DestroyImmediate(panelDef);
                if (uxml != null)
                    Object.DestroyImmediate(uxml);
                services.Clear();
            }
        }

        // ── Fixtures / helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="PanelDefinition"/> with a non-null (empty) UXML so that
        /// <see cref="PanelInstance.Instantiate"/> succeeds (it throws only when UXML is null).
        /// Private serialized fields are populated via <see cref="SerializedObject"/> — no runtime
        /// setters are added for testing. No controller type is set, so reflection is skipped.
        /// </summary>
        private static PanelDefinition CreatePanelDefinition(string panelName)
        {
            var uxml = ScriptableObject.CreateInstance<VisualTreeAsset>();

            var panelDef = ScriptableObject.CreateInstance<PanelDefinition>();
            var so = new SerializedObject(panelDef);
            so.FindProperty("panelName").stringValue = panelName;
            so.FindProperty("uxml").objectReferenceValue = uxml;
            so.ApplyModifiedPropertiesWithoutUndo();

            return panelDef;
        }

        private static WindowDefinition CreateWindowDefinition(string windowName, PanelDefinition panel)
        {
            var windowDef = ScriptableObject.CreateInstance<WindowDefinition>();
            var so = new SerializedObject(windowDef);
            so.FindProperty("windowName").stringValue = windowName;
            so.FindProperty("panel").objectReferenceValue = panel;
            so.ApplyModifiedPropertiesWithoutUndo();

            return windowDef;
        }

        private class FakeService { }
        private class FakeStore { }

        private struct TestEvent { public int Value; }
        private struct OtherEvent { public string Text; }
    }
}
