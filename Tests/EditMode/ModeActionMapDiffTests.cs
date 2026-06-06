using System.Collections.Generic;
using Mosaic.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mosaic.UI.Tests
{
    /// <summary>
    /// EditMode NUnit suite for the per-mode action-map diff
    /// (<see cref="MosaicUIManager.DiffActionMaps(IReadOnlyList{string}, HashSet{string}, InputService)"/>).
    /// <para>
    /// The diff is extracted from the <see cref="MosaicUIManager"/> MonoBehaviour as an
    /// <c>internal static</c> method precisely so it can be unit-tested without standing up the
    /// manager / its <see cref="UnityEngine.UIElements.UIDocument"/> (a MonoBehaviour orchestrating a
    /// UIDocument is not constructible in a plain EditMode unit). These tests drive the static method
    /// directly with a local <see cref="HashSet{T}"/> standing in for the manager's
    /// <c>_activeActionMaps</c>, and assert against <see cref="InputService.EnabledMaps"/> (reachable
    /// because <c>Mosaic.UI</c> grants <c>[assembly: InternalsVisibleTo("Mosaic.UI.Tests")]</c>).
    /// </para>
    /// <para>
    /// This is a plain <see cref="TestFixture"/> — NOT an <c>InputTestFixture</c>. The diff only toggles
    /// action-map <c>Enable()</c>/<c>Disable()</c> state via <see cref="InputService.EnableMap"/>/
    /// <see cref="InputService.DisableMap"/>; it drives no virtual devices and needs no
    /// <c>InputSystem.Update()</c>, so the device-reset discipline of <c>InputTestFixture</c> is not
    /// required here (and avoiding it sidesteps the SetUp/TearDown-hiding gotcha).
    /// </para>
    /// <para>
    /// <b>Gotcha (project memory):</b> <see cref="InputService.EnabledMaps"/> is array-backed, so NUnit
    /// <c>Has.Count</c> throws on it — these tests assert <c>.Count</c> directly.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ModeActionMapDiffTests
    {
        private InputActionAsset _asset;

        /// <summary>
        /// Builds an <see cref="InputActionAsset"/> with three trivially-valid maps (A, B, C — each with
        /// one Button action so the map resolves), initialises the facade, and assigns the asset to the
        /// input source the diff drives (<c>MosaicUI.Input</c>).
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            AddTrivialMap(_asset, "A");
            AddTrivialMap(_asset, "B");
            AddTrivialMap(_asset, "C");

            MosaicUI.Initialize();
            MosaicUI.Input.SetAsset(_asset);
        }

        /// <summary>
        /// Shuts the facade down (disposing the input source — which disables any still-enabled maps
        /// while the asset is still live) and then destroys the fixture asset. Order matches
        /// <c>InputServiceTests</c>: dispose the service before destroying the asset.
        /// </summary>
        [TearDown]
        public void TearDownFixture()
        {
            if (MosaicUI.IsInitialized)
                MosaicUI.Shutdown();

            if (_asset != null)
            {
                UnityEngine.Object.DestroyImmediate(_asset);
                _asset = null;
            }
        }

        /// <summary>Adds a map named <paramref name="mapName"/> with a single Button action so it resolves.</summary>
        private static void AddTrivialMap(InputActionAsset asset, string mapName)
        {
            var map = asset.AddActionMap(mapName);
            // A trivial action makes the map valid/resolvable; binding target is irrelevant (never driven).
            map.AddAction("Act", InputActionType.Button);
        }

        // Convenience: drive the diff under test against the manager's static method.
        private static void Diff(IReadOnlyList<string> newMaps, HashSet<string> active)
            => MosaicUIManager.DiffActionMaps(newMaps, active, MosaicUI.Input);

        private static IReadOnlyCollection<string> Enabled => MosaicUI.Input.EnabledMaps;

        // -----------------------------------------------------------------------------------------
        // Transition sequence (the acceptance scenario)
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Entering a mode whose maps are {A} enables exactly A and tracks it in <c>active</c>.
        /// </summary>
        [Test]
        public void EnterA_enablesOnlyA()
        {
            var active = new HashSet<string>();

            Diff(new List<string> { "A" }, active);

            Assert.That(Enabled.Count, Is.EqualTo(1), "expected exactly one enabled map");
            Assert.That(Enabled, Does.Contain("A"));
            Assert.That(active.Count, Is.EqualTo(1));
            Assert.That(active.Contains("A"), Is.True);
        }

        /// <summary>
        /// {A} → {B} disables the leaving map A and enables the entering map B; EnabledMaps == {B}.
        /// </summary>
        [Test]
        public void AToB_disablesA_enablesB()
        {
            var active = new HashSet<string>();

            Diff(new List<string> { "A" }, active);
            Diff(new List<string> { "B" }, active);

            Assert.That(Enabled.Count, Is.EqualTo(1));
            Assert.That(Enabled, Does.Contain("B"));
            Assert.That(Enabled, Does.Not.Contain("A"));
            Assert.That(active.Count, Is.EqualTo(1));
            Assert.That(active.Contains("B"), Is.True);
            Assert.That(active.Contains("A"), Is.False);
        }

        /// <summary>
        /// {B} → {A,C} disables the leaving map B and enables the entering maps A and C; EnabledMaps == {A,C}.
        /// </summary>
        [Test]
        public void BToAC_disablesB_enablesAandC()
        {
            var active = new HashSet<string>();

            Diff(new List<string> { "B" }, active);
            Diff(new List<string> { "A", "C" }, active);

            Assert.That(Enabled.Count, Is.EqualTo(2));
            Assert.That(Enabled, Does.Contain("A"));
            Assert.That(Enabled, Does.Contain("C"));
            Assert.That(Enabled, Does.Not.Contain("B"));
            Assert.That(active.Count, Is.EqualTo(2));
            Assert.That(active.Contains("A"), Is.True);
            Assert.That(active.Contains("C"), Is.True);
            Assert.That(active.Contains("B"), Is.False);
        }

        /// <summary>
        /// A mapless mode (empty list) disables every previously-active map; EnabledMaps is empty.
        /// </summary>
        [Test]
        public void ToEmpty_disablesAll()
        {
            var active = new HashSet<string>();

            Diff(new List<string> { "A", "C" }, active);
            Assert.That(Enabled.Count, Is.EqualTo(2), "precondition: A and C enabled before the empty transition");

            Diff(new List<string>(), active);

            Assert.That(Enabled.Count, Is.EqualTo(0), "all maps should be disabled by a mapless mode");
            Assert.That(active.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// The full acceptance sequence in one test: {A} → {B} → {A,C} → {} — EnabledMaps matches the
        /// declared maps after each transition.
        /// </summary>
        [Test]
        public void FullSequence_enabledMapsTrackEachTransition()
        {
            var active = new HashSet<string>();

            Diff(new List<string> { "A" }, active);
            Assert.That(Enabled.Count, Is.EqualTo(1));
            Assert.That(Enabled, Does.Contain("A"));

            Diff(new List<string> { "B" }, active);
            Assert.That(Enabled.Count, Is.EqualTo(1));
            Assert.That(Enabled, Does.Contain("B"));

            Diff(new List<string> { "A", "C" }, active);
            Assert.That(Enabled.Count, Is.EqualTo(2));
            Assert.That(Enabled, Does.Contain("A"));
            Assert.That(Enabled, Does.Contain("C"));

            Diff(new List<string>(), active);
            Assert.That(Enabled.Count, Is.EqualTo(0));
            Assert.That(active.Count, Is.EqualTo(0));
        }

        // -----------------------------------------------------------------------------------------
        // Edge cases
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Null and empty-string entries in the declared-maps list are skipped (the diff guards with
        /// <c>string.IsNullOrEmpty</c>); only the valid names A and B are enabled. This proves a sparse
        /// or partly-blank Inspector list does not reach <c>EnableMap</c> with a bad name.
        /// </summary>
        [Test]
        public void NullAndEmptyEntries_areSkipped()
        {
            var active = new HashSet<string>();

            Diff(new List<string> { null, "", "A", "", "B", null }, active);

            Assert.That(Enabled.Count, Is.EqualTo(2));
            Assert.That(Enabled, Does.Contain("A"));
            Assert.That(Enabled, Does.Contain("B"));
            Assert.That(active.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// A null map list is treated as empty: it disables everything previously active.
        /// </summary>
        [Test]
        public void NullMapList_treatedAsEmpty()
        {
            var active = new HashSet<string>();

            Diff(new List<string> { "A" }, active);
            Assert.That(Enabled.Count, Is.EqualTo(1));

            Diff(null, active);

            Assert.That(Enabled.Count, Is.EqualTo(0));
            Assert.That(active.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Re-applying the same map set is a no-op for the enabled set (no churn): {A} → {A} keeps A enabled.
        /// </summary>
        [Test]
        public void SameSet_isNoOp()
        {
            var active = new HashSet<string>();

            Diff(new List<string> { "A", "B" }, active);
            Diff(new List<string> { "A", "B" }, active);

            Assert.That(Enabled.Count, Is.EqualTo(2));
            Assert.That(Enabled, Does.Contain("A"));
            Assert.That(Enabled, Does.Contain("B"));
            Assert.That(active.Count, Is.EqualTo(2));
        }
    }
}
