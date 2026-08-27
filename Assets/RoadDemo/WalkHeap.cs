namespace RoadDemo
{
    /// <summary>The open set of WalkRoute's search: lattice squares, the cheapest by
    /// guessed total cost first. A binary heap on two flat arrays that grow once and
    /// stay, so a search allocates nothing after the first. Duplicates are the
    /// caller's business - a square pushed twice comes up twice.</summary>
    sealed class WalkHeap
    {
        int[] _node = new int[256];
        float[] _key = new float[256];
        int _count;

        public int Count => _count;

        public void Clear() => _count = 0;

        public void Push(int node, float key)
        {
            if (_count == _node.Length)
            {
                System.Array.Resize(ref _node, _count * 2);
                System.Array.Resize(ref _key, _count * 2);
            }
            // sift the hole up from the tail until its parent is no dearer
            int i = _count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_key[parent] <= key) break;
                _node[i] = _node[parent];
                _key[i] = _key[parent];
                i = parent;
            }
            _node[i] = node;
            _key[i] = key;
        }

        /// <summary>The cheapest square, taken off. Empty is the caller's to check.</summary>
        public int Pop()
        {
            int top = _node[0];
            _count--;
            if (_count > 0)
            {
                // the tail goes to the root and sifts down past its cheaper children
                int node = _node[_count];
                float key = _key[_count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= _count) break;
                    if (child + 1 < _count && _key[child + 1] < _key[child]) child++;
                    if (_key[child] >= key) break;
                    _node[i] = _node[child];
                    _key[i] = _key[child];
                    i = child;
                }
                _node[i] = node;
                _key[i] = key;
            }
            return top;
        }
    }
}
