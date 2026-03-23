using System.Collections.Generic;

namespace Mosaic.UI
{
    public class ModeHistory
    {
        private readonly Stack<ModeDefinition> _stack = new();

        public int Count => _stack.Count;

        public void Push(ModeDefinition mode)
        {
            _stack.Push(mode);
        }

        public ModeDefinition Pop()
        {
            return _stack.Count > 0 ? _stack.Pop() : null;
        }

        public ModeDefinition Peek()
        {
            return _stack.Count > 0 ? _stack.Peek() : null;
        }

        public void Clear()
        {
            _stack.Clear();
        }
    }
}
