using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mosaic.UI;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Properties;
using UnityEngine;
// 'Object' is ambiguous between System.Object and UnityEngine.Object (both namespaces are imported);
// a bare 'Object.DestroyImmediate' call below means the Unity one.
using Object = UnityEngine.Object;

namespace Mosaic.UI.Tests
{
    /// <summary>
    /// Covers the public inspection facade (<c>MosaicInspector</c>) and its internal helpers
    /// (<c>InspectionValueFormatter</c>, <c>StoreReflectionCache</c>). The internals are reachable
    /// here because <c>Mosaic.UI</c> exposes them via
    /// <c>[assembly: InternalsVisibleTo("Mosaic.UI.Tests")]</c>.
    ///
    /// <para><b>Gotcha:</b> NUnit <c>Has.Count</c> throws on array-typed collections (project memory:
    /// <c>nunit-has-count-array-gotcha</c>). These tests assert <c>.Count</c> / <c>.Length</c> directly.</para>
    /// </summary>
    [TestFixture]
    public class MosaicInspectorTests
    {
        private AlphaStore _alpha;
        private BetaStore _beta;
        private PlainService _plain;

        [SetUp]
        public void SetUpFramework()
        {
            // A fresh registry, command registry, and event buffer for every test.
            MosaicUI.Shutdown();
            MosaicUI.Initialize();

            _alpha = new AlphaStore();
            _beta = new BetaStore();
            _plain = new PlainService();

            MosaicUI.Services.Register(_alpha);
            MosaicUI.Services.Register(_beta);
            MosaicUI.Services.Register(_plain);
        }

        [TearDown]
        public void TearDownFramework()
        {
            MosaicUI.Shutdown();
            // Unity never calls Awake or OnDestroy outside play mode, so the static is cleared here.
            MosaicUIManager.Instance = null;
        }

        private static string FullNameOf<T>() => typeof(T).FullName;

        // ── Formatter ─────────────────────────────────────────────────────────

        [Test]
        public void Formatter_ProducesDocumentedTextForEveryRule()
        {
            Assert.That(InspectionValueFormatter.Format(null), Is.EqualTo("null"));
            Assert.That(InspectionValueFormatter.Format(true), Is.EqualTo("true"));
            Assert.That(InspectionValueFormatter.Format(false), Is.EqualTo("false"));
            Assert.That(InspectionValueFormatter.Format(42), Is.EqualTo("42"));
            Assert.That(InspectionValueFormatter.Format(3.5f), Is.EqualTo("3.5"));
            Assert.That(InspectionValueFormatter.Format(3.5d), Is.EqualTo("3.5"));
            Assert.That(InspectionValueFormatter.Format(7L), Is.EqualTo("7"));
            Assert.That(InspectionValueFormatter.Format('A'), Is.EqualTo("A"));
            Assert.That(InspectionValueFormatter.Format("hello world"), Is.EqualTo("hello world"));
            Assert.That(InspectionValueFormatter.Format(SampleMode.Idle), Is.EqualTo("Idle"));

            Assert.That(InspectionValueFormatter.Format(new Vector2(1f, 2f)), Is.EqualTo("[1, 2]"));
            Assert.That(InspectionValueFormatter.Format(new Vector3(1f, 2f, 3f)), Is.EqualTo("[1, 2, 3]"));
            Assert.That(InspectionValueFormatter.Format(new Vector4(1f, 2f, 3f, 4f)), Is.EqualTo("[1, 2, 3, 4]"));
            Assert.That(InspectionValueFormatter.Format(new Quaternion(0f, 0f, 0f, 1f)), Is.EqualTo("[0, 0, 0, 1]"));
            Assert.That(InspectionValueFormatter.Format(new float2(1f, 2f)), Is.EqualTo("[1, 2]"));
            Assert.That(InspectionValueFormatter.Format(new float3(1f, 2f, 3f)), Is.EqualTo("[1, 2, 3]"));
            Assert.That(InspectionValueFormatter.Format(new float4(1f, 2f, 3f, 4f)), Is.EqualTo("[1, 2, 3, 4]"));

            Assert.That(InspectionValueFormatter.Format(new[] { 1, 2, 3 }),
                Is.EqualTo("{count: 3, items: [1, 2, 3]}"));
            Assert.That(InspectionValueFormatter.Format(new List<string> { "a", "b" }),
                Is.EqualTo("{count: 2, items: [a, b]}"));

            // Any other object: "TypeName: ToString()".
            Assert.That(InspectionValueFormatter.Format(new PlayerData()), Is.EqualTo("PlayerData: Player(3)"));
        }

        [Test]
        public void Formatter_LongObjectText_IsTrimmedTo200Chars()
        {
            var text = InspectionValueFormatter.Format(new LongToStringObject());
            Assert.That(text.Length, Is.EqualTo(200));
        }

        [Test]
        public void Formatter_FriendlyTypeName_RendersGenericsAndArrays()
        {
            Assert.That(InspectionValueFormatter.FriendlyTypeName(typeof(int)), Is.EqualTo("Int32"));
            Assert.That(InspectionValueFormatter.FriendlyTypeName(typeof(List<int>)), Is.EqualTo("List<Int32>"));
            Assert.That(InspectionValueFormatter.FriendlyTypeName(typeof(Dictionary<string, List<int>>)),
                Is.EqualTo("Dictionary<String, List<Int32>>"));
            Assert.That(InspectionValueFormatter.FriendlyTypeName(typeof(int[])), Is.EqualTo("Int32[]"));
        }

        [Test]
        public void Formatter_ThrowingGetter_RendersErrorPlaceholder()
        {
            var store = new FormatterStore();
            var members = StoreReflectionCache.GetMembers(typeof(FormatterStore));

            var throwing = members.First(m => m.Name == nameof(FormatterStore.Explodes));
            var read = InspectionValueFormatter.Read(store, throwing);

            Assert.That(read.name, Is.EqualTo(nameof(FormatterStore.Explodes)));
            Assert.That(read.value, Is.EqualTo("<error: InvalidOperationException>"));
            // The declared type stands in when the getter throws.
            Assert.That(read.type, Is.EqualTo("Int32"));

            // The other values survive the throwing neighbour.
            var count = InspectionValueFormatter.Read(store, members.First(m => m.Name == nameof(FormatterStore.Count)));
            Assert.That(count.value, Is.EqualTo("42"));
            Assert.That(count.type, Is.EqualTo("Int32"));
        }

        [Test]
        public void Formatter_Read_NullValue_ReportsDeclaredType()
        {
            var store = new FormatterStore();
            var members = StoreReflectionCache.GetMembers(typeof(FormatterStore));
            var entry = members.First(m => m.Name == nameof(FormatterStore.Payload));

            var read = InspectionValueFormatter.Read(store, entry);

            Assert.That(read.value, Is.EqualTo("null"));
            Assert.That(read.type, Is.EqualTo("PlayerData"));
        }

        [Test]
        public void Formatter_Collection_CapsItemsAt20_ButCountIsTrue()
        {
            var items = Enumerable.Range(1, 25).ToList();

            var text = InspectionValueFormatter.Format(items);

            Assert.That(text, Does.StartWith("{count: 25, items: ["));

            var inner = text.Substring(text.IndexOf('[') + 1);
            inner = inner.Substring(0, inner.IndexOf(']'));
            var rendered = inner.Split(new[] { ", " }, StringSplitOptions.None);

            Assert.That(rendered.Length, Is.EqualTo(20));
            Assert.That(rendered[0], Is.EqualTo("1"));
            Assert.That(rendered[19], Is.EqualTo("20"));
        }

        [Test]
        public void Formatter_NestedCollection_FallsBackToObjectForm()
        {
            var outer = new List<List<int>> { new List<int> { 1, 2 } };

            var text = InspectionValueFormatter.Format(outer);

            // The nested list uses the object form, not a second collection expansion.
            Assert.That(text, Does.StartWith("{count: 1, items: [List`1: "));
        }

        // ── Reflection cache ──────────────────────────────────────────────────

        [Test]
        public void ReflectionCache_ReturnsSameArrayForRepeatedCalls()
        {
            var first = StoreReflectionCache.GetMembers(typeof(DerivedStore));
            var second = StoreReflectionCache.GetMembers(typeof(DerivedStore));

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void ReflectionCache_IncludesBaseTypeMembers_BaseFirst()
        {
            var members = StoreReflectionCache.GetMembers(typeof(DerivedStore));
            var names = members.Select(m => m.Name).ToList();

            Assert.That(names.Contains(nameof(BaseStore.BaseValue)), Is.True);
            Assert.That(names.Contains(nameof(DerivedStore.DerivedValue)), Is.True);

            // Base types are walked first.
            Assert.That(names.IndexOf(nameof(BaseStore.BaseValue)),
                Is.LessThan(names.IndexOf(nameof(DerivedStore.DerivedValue))));

            var baseEntry = members.First(m => m.Name == nameof(BaseStore.BaseValue));
            Assert.That(baseEntry.DeclaredType, Is.EqualTo(typeof(int)));
        }

        [Test]
        public void ReflectionCache_PropertyAndFieldNameCollision_KeepsThePropertyOnce()
        {
            var members = StoreReflectionCache.GetMembers(typeof(DerivedStore));
            var shared = members.Where(m => m.Name == "Shared").ToList();

            Assert.That(shared.Count, Is.EqualTo(1));
            Assert.That(shared[0].Member, Is.InstanceOf<PropertyInfo>());
            Assert.That(shared[0].DeclaredType, Is.EqualTo(typeof(string)));
        }

        [Test]
        public void ReflectionCache_TypeWithNoCreatePropertyMembers_ReturnsEmpty()
        {
            var members = StoreReflectionCache.GetMembers(typeof(PlayerData));
            Assert.That(members.Length, Is.EqualTo(0));
        }


        // ── Services and stores ───────────────────────────────────────────────

        [Test]
        public void GetServices_ListsEveryServiceSortedWithStoreFlag()
        {
            var result = MosaicInspector.GetServices();

            Assert.That(result.initialized, Is.True);
            Assert.That(result.services.Count, Is.EqualTo(3));

            var names = result.services.Select(s => s.typeName).ToList();
            var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.That(names, Is.EqualTo(sorted));

            var alpha = result.services.First(s => s.typeName == FullNameOf<AlphaStore>());
            var beta = result.services.First(s => s.typeName == FullNameOf<BetaStore>());
            var plain = result.services.First(s => s.typeName == FullNameOf<PlainService>());

            Assert.That(alpha.isStore, Is.True);
            Assert.That(beta.isStore, Is.True);
            Assert.That(plain.isStore, Is.False);
            Assert.That(alpha.implTypeName, Is.EqualTo(FullNameOf<AlphaStore>()));
        }

        [Test]
        public void GetStore_ByFullName_ReturnsVersionAndValues()
        {
            var store = MosaicInspector.GetStore(FullNameOf<AlphaStore>());

            Assert.That(store.initialized, Is.True);
            Assert.That(store.found, Is.True);
            Assert.That(store.typeName, Is.EqualTo(FullNameOf<AlphaStore>()));

            var names = store.values.Select(v => v.name).ToList();
            Assert.That(names.Contains(nameof(AlphaStore.Count)), Is.True);
            Assert.That(names.Contains(nameof(AlphaStore.Label)), Is.True);

            var count = store.values.First(v => v.name == nameof(AlphaStore.Count));
            Assert.That(count.value, Is.EqualTo("0"));
            Assert.That(count.type, Is.EqualTo("Int32"));
        }

        [Test]
        public void GetStore_ByShortName_AlsoResolves()
        {
            var exact = MosaicInspector.GetStore(nameof(AlphaStore));
            Assert.That(exact.found, Is.True);
            Assert.That(exact.typeName, Is.EqualTo(FullNameOf<AlphaStore>()));

            // Case-insensitive short name is the last resolution step.
            var insensitive = MosaicInspector.GetStore("alphastore");
            Assert.That(insensitive.found, Is.True);
            Assert.That(insensitive.typeName, Is.EqualTo(FullNameOf<AlphaStore>()));
        }

        [Test]
        public void GetStore_UnknownName_ReturnsFoundFalse()
        {
            foreach (var name in new[] { "NoSuchStore", null, string.Empty })
            {
                var store = MosaicInspector.GetStore(name);

                Assert.That(store.initialized, Is.True, "input: " + (name ?? "<null>"));
                Assert.That(store.found, Is.False, "input: " + (name ?? "<null>"));
                Assert.That(store.values, Is.Not.Null);
                Assert.That(store.values.Count, Is.EqualTo(0));
            }
        }

        [Test]
        public void GetStore_VersionTracksGetViewHashCode()
        {
            var before = MosaicInspector.GetStore(nameof(AlphaStore));
            Assert.That(before.version, Is.EqualTo(_alpha.GetViewHashCode()));

            _alpha.Count = 7;

            var after = MosaicInspector.GetStore(nameof(AlphaStore));
            Assert.That(after.version, Is.EqualTo(_alpha.GetViewHashCode()));
            Assert.That(after.version, Is.GreaterThan(before.version));
            Assert.That(after.values.First(v => v.name == nameof(AlphaStore.Count)).value, Is.EqualTo("7"));
        }

        [Test]
        public void GetStore_WalksBaseTypeCreatePropertyMembers()
        {
            var store = MosaicInspector.GetStore(nameof(AlphaStore));

            var names = store.values.Select(v => v.name).ToList();
            Assert.That(names.Contains(nameof(AlphaStore.BaseValue)), Is.True);

            // Base types come first in the walk.
            Assert.That(names.IndexOf(nameof(AlphaStore.BaseValue)),
                Is.LessThan(names.IndexOf(nameof(AlphaStore.Count))));
        }

        [Test]
        public void GetStores_ReturnsOnlyStores_Sorted()
        {
            var result = MosaicInspector.GetStores();

            Assert.That(result.initialized, Is.True);
            Assert.That(result.stores.Count, Is.EqualTo(2));

            var names = result.stores.Select(s => s.typeName).ToList();
            Assert.That(names.Contains(FullNameOf<PlainService>()), Is.False);
            Assert.That(names, Is.EqualTo(names.OrderBy(n => n, StringComparer.Ordinal).ToList()));

            foreach (var store in result.stores)
            {
                Assert.That(store.initialized, Is.True);
                Assert.That(store.found, Is.True);
            }
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [Test]
        public void GetCommands_ReturnsRegisteredIdsSorted()
        {
            Assert.That(MosaicInspector.GetCommands().commands.Count, Is.EqualTo(0));

            MosaicUI.Commands.Register("zoom.in", () => { });
            MosaicUI.Commands.Register("app.quit", () => { });
            MosaicUI.Commands.Register("map.open", () => { });

            var result = MosaicInspector.GetCommands();

            Assert.That(result.initialized, Is.True);
            Assert.That(result.commands.Count, Is.EqualTo(3));
            Assert.That(result.commands, Is.EqualTo(new List<string> { "app.quit", "map.open", "zoom.in" }));
        }

        // ── Composition ───────────────────────────────────────────────────────

        [Test]
        public void GetComposition_WithNoManager_ReturnsHasManagerFalse()
        {
            var result = MosaicInspector.GetComposition();

            Assert.That(result.initialized, Is.True);
            Assert.That(result.hasManager, Is.False);
            Assert.That(result.currentMode, Is.EqualTo(string.Empty));
            Assert.That(result.history, Is.Not.Null);
            Assert.That(result.history.Count, Is.EqualTo(0));
            Assert.That(result.slots.Count, Is.EqualTo(0));
            Assert.That(result.panels.Count, Is.EqualTo(0));
            Assert.That(result.actionMaps.Count, Is.EqualTo(0));
            Assert.That(result.worldFeatures.Count, Is.EqualTo(0));
            Assert.That(result.worldControllers.Count, Is.EqualTo(0));
        }

        [Test]
        public void GetComposition_WithManager_ReportsManagerPresent()
        {
            var go = new GameObject("TestMosaicUIManager");
            try
            {
                // Unity does not call Awake outside play mode, so the test seam assigns the static.
                MosaicUIManager.Instance = go.AddComponent<MosaicUIManager>();

                var result = MosaicInspector.GetComposition();

                Assert.That(result.initialized, Is.True);
                Assert.That(result.hasManager, Is.True);
                // Awake runs one lifecycle step before Start builds the composition, so an empty
                // composition on a live manager is correct, not an error.
                Assert.That(result.currentMode, Is.EqualTo(string.Empty));
                Assert.That(result.history.Count, Is.EqualTo(0));
                Assert.That(result.slots.Count, Is.EqualTo(0));
                Assert.That(result.panels.Count, Is.EqualTo(0));
                Assert.That(result.actionMaps.Count, Is.EqualTo(0));
                Assert.That(result.worldFeatures.Count, Is.EqualTo(0));
                Assert.That(result.worldControllers.Count, Is.EqualTo(0));
            }
            finally
            {
                MosaicUIManager.Instance = null;
                Object.DestroyImmediate(go);
            }
        }

        // ── Events ────────────────────────────────────────────────────────────

        [Test]
        public void GetRecentEvents_ReturnsPublishedEventsInSequenceOrder()
        {
            MosaicUI.Events.Publish(new PingEvent { Index = 1 });
            MosaicUI.Events.Publish(new PingEvent { Index = 2 });
            MosaicUI.Events.Publish(new PingEvent { Index = 3 });

            var result = MosaicInspector.GetRecentEvents(10);

            Assert.That(result.initialized, Is.True);
            Assert.That(result.events.Count, Is.EqualTo(3));
            Assert.That(result.events.Select(e => e.sequence).ToList(),
                Is.EqualTo(new List<long> { 1, 2, 3 }));
            Assert.That(result.events[0].typeName, Is.EqualTo(FullNameOf<PingEvent>()));
            Assert.That(result.events[0].summary, Is.EqualTo("Ping(1)"));
            Assert.That(result.events[2].summary, Is.EqualTo("Ping(3)"));
        }

        [Test]
        public void GetRecentEvents_RingBufferWrapsAt64()
        {
            for (int i = 1; i <= 100; i++)
                MosaicUI.Events.Publish(new PingEvent { Index = i });

            var result = MosaicInspector.GetRecentEvents(100);

            Assert.That(result.events.Count, Is.EqualTo(64));
            Assert.That(result.events[0].sequence, Is.EqualTo(37));
            Assert.That(result.events[63].sequence, Is.EqualTo(100));
            Assert.That(result.events[0].summary, Is.EqualTo("Ping(37)"));
        }

        [Test]
        public void GetRecentEvents_SummaryTrimmedTo200Chars()
        {
            MosaicUI.Events.Publish(new LongEvent());

            var result = MosaicInspector.GetRecentEvents(1);

            Assert.That(result.events.Count, Is.EqualTo(1));
            Assert.That(result.events[0].summary.Length, Is.EqualTo(200));
        }

        [Test]
        public void GetRecentEvents_MaxIsClampedAndNonPositiveReturnsEmpty()
        {
            for (int i = 1; i <= 5; i++)
                MosaicUI.Events.Publish(new PingEvent { Index = i });

            Assert.That(MosaicInspector.GetRecentEvents(0).events.Count, Is.EqualTo(0));
            Assert.That(MosaicInspector.GetRecentEvents(-5).events.Count, Is.EqualTo(0));
            Assert.That(MosaicInspector.GetRecentEvents(0).initialized, Is.True);

            // A max above the stored count returns everything stored.
            Assert.That(MosaicInspector.GetRecentEvents(500).events.Count, Is.EqualTo(5));

            // A max below the stored count returns the newest entries, oldest first.
            var newest = MosaicInspector.GetRecentEvents(2);
            Assert.That(newest.events.Count, Is.EqualTo(2));
            Assert.That(newest.events[0].sequence, Is.EqualTo(4));
            Assert.That(newest.events[1].sequence, Is.EqualTo(5));
        }

        [Test]
        public void GetRecentEvents_SequenceRestartsAfterShutdownAndInitialize()
        {
            MosaicUI.Events.Publish(new PingEvent { Index = 1 });

            MosaicUI.Shutdown();
            MosaicUI.Initialize();

            MosaicUI.Events.Publish(new PingEvent { Index = 9 });

            var result = MosaicInspector.GetRecentEvents(10);

            Assert.That(result.events.Count, Is.EqualTo(1));
            Assert.That(result.events[0].sequence, Is.EqualTo(1));
            Assert.That(result.events[0].summary, Is.EqualTo("Ping(9)"));
        }

        // ── Guards and serialization ──────────────────────────────────────────

        [Test]
        public void AllMethods_WhenNotInitialized_ReturnInitializedFalse()
        {
            MosaicUI.Shutdown();

            var services = MosaicInspector.GetServices();
            var store = MosaicInspector.GetStore(nameof(AlphaStore));
            var stores = MosaicInspector.GetStores();
            var composition = MosaicInspector.GetComposition();
            var commands = MosaicInspector.GetCommands();
            var events = MosaicInspector.GetRecentEvents(10);

            Assert.That(services.initialized, Is.False);
            Assert.That(services.services.Count, Is.EqualTo(0));

            Assert.That(store.initialized, Is.False);
            Assert.That(store.found, Is.False);
            Assert.That(store.values.Count, Is.EqualTo(0));

            Assert.That(stores.initialized, Is.False);
            Assert.That(stores.stores.Count, Is.EqualTo(0));

            Assert.That(composition.initialized, Is.False);
            Assert.That(composition.hasManager, Is.False);
            Assert.That(composition.history.Count, Is.EqualTo(0));
            Assert.That(composition.panels.Count, Is.EqualTo(0));

            Assert.That(commands.initialized, Is.False);
            Assert.That(commands.commands.Count, Is.EqualTo(0));

            Assert.That(events.initialized, Is.False);
            Assert.That(events.events.Count, Is.EqualTo(0));
        }

        [Test]
        public void AllResults_RoundTripThroughJsonUtility()
        {
            MosaicUI.Commands.Register("app.quit", () => { });
            MosaicUI.Events.Publish(new PingEvent { Index = 1 });

            var services = MosaicInspector.GetServices();
            var servicesBack = JsonUtility.FromJson<MosaicInspector.ServiceListResult>(JsonUtility.ToJson(services));
            Assert.That(servicesBack.initialized, Is.True);
            Assert.That(servicesBack.services.Count, Is.EqualTo(services.services.Count));
            Assert.That(servicesBack.services[0].typeName, Is.EqualTo(services.services[0].typeName));

            var store = MosaicInspector.GetStore(nameof(AlphaStore));
            var storeBack = JsonUtility.FromJson<MosaicInspector.StoreInfo>(JsonUtility.ToJson(store));
            Assert.That(storeBack.found, Is.True);
            Assert.That(storeBack.typeName, Is.EqualTo(store.typeName));
            Assert.That(storeBack.values.Count, Is.EqualTo(store.values.Count));

            var stores = MosaicInspector.GetStores();
            var storesBack = JsonUtility.FromJson<MosaicInspector.StoreListResult>(JsonUtility.ToJson(stores));
            Assert.That(storesBack.stores.Count, Is.EqualTo(stores.stores.Count));

            var composition = MosaicInspector.GetComposition();
            var compositionBack = JsonUtility.FromJson<MosaicInspector.CompositionResult>(JsonUtility.ToJson(composition));
            Assert.That(compositionBack.initialized, Is.True);
            Assert.That(compositionBack.hasManager, Is.False);
            Assert.That(compositionBack.history, Is.Not.Null);
            Assert.That(compositionBack.history.Count, Is.EqualTo(0));
            Assert.That(compositionBack.panels.Count, Is.EqualTo(0));

            var commands = MosaicInspector.GetCommands();
            var commandsBack = JsonUtility.FromJson<MosaicInspector.CommandListResult>(JsonUtility.ToJson(commands));
            Assert.That(commandsBack.commands.Count, Is.EqualTo(1));
            Assert.That(commandsBack.commands[0], Is.EqualTo("app.quit"));

            var events = MosaicInspector.GetRecentEvents(10);
            var eventsBack = JsonUtility.FromJson<MosaicInspector.EventListResult>(JsonUtility.ToJson(events));
            Assert.That(eventsBack.events.Count, Is.EqualTo(events.events.Count));
            Assert.That(eventsBack.events[0].sequence, Is.EqualTo(events.events[0].sequence));
        }

        // ── Fakes ─────────────────────────────────────────────────────────────

        private enum SampleMode { Idle, Running }

        private class PlayerData
        {
            public override string ToString() => "Player(3)";
        }

        private class LongToStringObject
        {
            public override string ToString() => new string('x', 400);
        }

        private class FormatterStore
        {
            [CreateProperty] public int Count => 42;
            [CreateProperty] public int Explodes => throw new InvalidOperationException("boom");
            [CreateProperty] public PlayerData Payload => null;
        }

        private class BaseStore
        {
            [CreateProperty] public int BaseValue => 1;
            [CreateProperty] public string Shared => "from-property";
        }

        private class PlainService
        {
        }

        private abstract class VersionedStoreBase<TSelf> : Store<TSelf>
            where TSelf : VersionedStoreBase<TSelf>
        {
            private int _baseValue;

            [CreateProperty]
            public int BaseValue
            {
                get => _baseValue;
                set => SetProperty(ref _baseValue, value);
            }
        }

        private class AlphaStore : VersionedStoreBase<AlphaStore>
        {
            private int _count;
            private string _label = "alpha";

            [CreateProperty]
            public int Count
            {
                get => _count;
                set => SetProperty(ref _count, value);
            }

            [CreateProperty]
            public string Label
            {
                get => _label;
                set => SetProperty(ref _label, value);
            }
        }

        private class BetaStore : Store<BetaStore>
        {
            private bool _ready;

            [CreateProperty]
            public bool Ready
            {
                get => _ready;
                set => SetProperty(ref _ready, value);
            }
        }

        private struct PingEvent
        {
            public int Index;
            public override string ToString() => "Ping(" + Index + ")";
        }

        private struct LongEvent
        {
            public override string ToString() => new string('y', 300);
        }

        private class DerivedStore : BaseStore
        {
            [CreateProperty] public bool DerivedValue => true;

            // A field that shadows the base property of the same name — the property must win.
            [CreateProperty] public new int Shared = 7;
        }
    }
}
