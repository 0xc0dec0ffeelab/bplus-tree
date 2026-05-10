using System;
using System.Collections.Generic;
using System.Text;

namespace bplus_tree
{
    internal sealed record SplitResult<TKey, TValue>(TKey PromotedKey, Node<TKey, TValue> NewRight);
}
