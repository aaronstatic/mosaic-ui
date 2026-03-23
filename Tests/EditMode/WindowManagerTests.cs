using System;
using Mosaic.UI;
using Mosaic.UI.Windows;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mosaic.UI.Tests
{
    [TestFixture]
    public class WindowManagerTests
    {
        private VisualElement _windowLayer;
        private ServiceRegistry _services;
        private WindowManager _manager;

        [SetUp]
        public void SetUp()
        {
            _windowLayer = new VisualElement();
            _services = new ServiceRegistry();
            _manager = new WindowManager(_windowLayer, _services);
        }

        [TearDown]
        public void TearDown()
        {
            _services.Clear();
        }

        // --- Constructor: null windowLayer throws ArgumentNullException ---

        [Test]
        public void Constructor_NullWindowLayer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new WindowManager(null, _services));
        }

        // --- Constructor: null services throws ArgumentNullException ---

        [Test]
        public void Constructor_NullServices_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new WindowManager(_windowLayer, null));
        }

        // --- Open with null definition throws ArgumentNullException ---

        [Test]
        public void Open_WithNullDefinition_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.Open(null));
        }

        // --- Toggle with null definition throws ArgumentNullException ---

        [Test]
        public void Toggle_WithNullDefinition_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _manager.Toggle(null));
        }

        // --- IsOpen returns false for unknown window name ---

        [Test]
        public void IsOpen_ReturnsFalse_ForUnknownWindowName()
        {
            Assert.That(_manager.IsOpen("NonExistentWindow"), Is.False);
        }

        // --- CloseAll on empty manager does not throw ---

        [Test]
        public void CloseAll_OnEmptyManager_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _manager.CloseAll());
        }

        // --- Close with unknown key does not throw ---

        [Test]
        public void Close_WithUnknownKey_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _manager.Close("no-such-window"));
        }

        // --- IsOpen returns false after manager is freshly constructed ---

        [Test]
        public void IsOpen_ReturnsFalse_AfterFreshConstruction()
        {
            var definition = ScriptableObject.CreateInstance<WindowDefinition>();
            try
            {
                Assert.That(_manager.IsOpen(definition.WindowName), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        // --- Open with definition that has no panel throws InvalidOperationException via PanelInstance ---
        // The WindowDefinition.Panel is null by default (no UXML), so PanelInstance.Instantiate
        // will throw ArgumentNullException when the PanelDefinition itself is null.

        [Test]
        public void Open_WithDefinitionHavingNullPanel_ThrowsDueToNullPanelDefinition()
        {
            var definition = ScriptableObject.CreateInstance<WindowDefinition>();
            try
            {
                // Panel field is null because the ScriptableObject was created without
                // serialized values. PanelInstance constructor will throw ArgumentNullException.
                Assert.Throws<ArgumentNullException>(() => _manager.Open(definition));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }
    }
}
