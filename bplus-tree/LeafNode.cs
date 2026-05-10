using System;
using System.Collections.Generic;
using System.Text;

namespace bplus_tree
{
    internal sealed class LeafNode<TKey, TValue> : Node<TKey, TValue>
    {
        // 與 Keys[i] 對應的 Value
        public readonly TValue[] Values;

        // 葉節點串成雙向鏈結 → Range scan 不需要回到父節點
        public LeafNode<TKey, TValue>? Next;
        public LeafNode<TKey, TValue>? Prev;

        public LeafNode(int order) : base(order)
        {
            Values = new TValue[order - 1];
        }

        public override bool IsLeaf => true;
    }
}
