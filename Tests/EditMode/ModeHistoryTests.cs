using Mosaic.UI;
using NUnit.Framework;
using UnityEngine;

namespace Mosaic.UI.Tests
{
    [TestFixture]
    public class ModeHistoryTests
    {
        private ModeHistory _history;
        private ModeDefinition _modeA;
        private ModeDefinition _modeB;
        private ModeDefinition _modeC;

        [SetUp]
        public void SetUp()
        {
            _history = new ModeHistory();
            _modeA = ScriptableObject.CreateInstance<ModeDefinition>();
            _modeB = ScriptableObject.CreateInstance<ModeDefinition>();
            _modeC = ScriptableObject.CreateInstance<ModeDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_modeA);
            Object.DestroyImmediate(_modeB);
            Object.DestroyImmediate(_modeC);
        }

        // --- Push and Pop returns in LIFO order ---

        [Test]
        public void PushAndPop_ReturnsInLIFOOrder()
        {
            _history.Push(_modeA);
            _history.Push(_modeB);
            _history.Push(_modeC);

            Assert.That(_history.Pop(), Is.SameAs(_modeC));
            Assert.That(_history.Pop(), Is.SameAs(_modeB));
            Assert.That(_history.Pop(), Is.SameAs(_modeA));
        }

        // --- Pop on empty history returns null ---

        [Test]
        public void Pop_OnEmptyHistory_ReturnsNull()
        {
            var result = _history.Pop();

            Assert.That(result, Is.Null);
        }

        // --- Peek returns top without removing ---

        [Test]
        public void Peek_ReturnsTopWithoutRemoving()
        {
            _history.Push(_modeA);
            _history.Push(_modeB);

            var peeked = _history.Peek();

            Assert.That(peeked, Is.SameAs(_modeB));
            Assert.That(_history.Count, Is.EqualTo(2));
        }

        // --- Peek on empty returns null ---

        [Test]
        public void Peek_OnEmpty_ReturnsNull()
        {
            var result = _history.Peek();

            Assert.That(result, Is.Null);
        }

        // --- Count reflects number of items ---

        [Test]
        public void Count_ReflectsNumberOfItems()
        {
            Assert.That(_history.Count, Is.EqualTo(0));

            _history.Push(_modeA);
            Assert.That(_history.Count, Is.EqualTo(1));

            _history.Push(_modeB);
            Assert.That(_history.Count, Is.EqualTo(2));

            _history.Pop();
            Assert.That(_history.Count, Is.EqualTo(1));
        }

        // --- Clear empties the history ---

        [Test]
        public void Clear_EmptiesTheHistory()
        {
            _history.Push(_modeA);
            _history.Push(_modeB);
            _history.Push(_modeC);

            _history.Clear();

            Assert.That(_history.Count, Is.EqualTo(0));
            Assert.That(_history.Pop(), Is.Null);
        }
    }
}
