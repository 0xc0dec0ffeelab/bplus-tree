namespace bplus_tree
{
    // ─────────────────────────────────────────────────────────────
    //  Node.cs  —  B+Tree 的兩種節點
    //
    //  設計決策：
    //  1. 用 sealed class 而非 struct：
    //     Phase 1 優先正確性，class 讓 parent pointer 好處理。
    //     Phase 3 再評估是否改成 unmanaged struct + NativeMemory。
    //
    //  2. InternalNode 和 LeafNode 繼承自 Node：
    //     避免在 BPlusTree.cs 到處做 is/as 判斷，
    //     用虛擬方法把多型封裝在節點內部。
    //
    //  3. Keys 用 TKey[]，固定配置 Order-1 個空間：
    //     Order 在編譯期已知，不用 List<T> 的動態擴張 overhead。
    // ─────────────────────────────────────────────────────────────
    internal abstract class Node<TKey, TValue>
    {

        // 節點內目前存放的 key 數量
        public int KeyCount;

        // 預配置 Order-1 個 key 的空間（最多 Order-1 個 key）
        public readonly TKey[] Keys;

        protected Node(int order)
        {
            Keys = new TKey[order - 1];
        }

        public abstract bool IsLeaf { get; }
    }
}
