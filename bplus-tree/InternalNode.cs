using System;
using System.Collections.Generic;
using System.Text;

namespace bplus_tree
{
    internal sealed class InternalNode<TKey, TValue> : Node<TKey, TValue>
    {
        // n 個 key → n+1 個 children
        // 預配置 Order 個指標空間
        public readonly Node<TKey, TValue>?[] Children;

        public InternalNode(int order) : base(order)
        {
            Children = new Node<TKey, TValue>?[order];
        }
        public override bool IsLeaf => false;
    }
}
