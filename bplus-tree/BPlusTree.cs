using System;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using System.Text;
// ─────────────────────────────────────────────────────────────
//  BPlusTree.cs  —  公開 API + 核心演算法
//
//  Phase 1 設計原則：
//  ① 正確性 > 效能
//  ② 每個方法只做一件事，名稱即文件
//  ③ 沒有任何 Phase 2+ 的最佳化，避免過早優化
//
//  公開 API：
//    Insert(key, value)   → 插入或更新
//    TryGet(key, value)   → 點查詢
//    Delete(key)          → 刪除
//    Range(from, to)      → 範圍迭代
//    Count                → 總筆數
// 
//  Bug fix log:
//  v0.1.1 - 內部節點路徑選擇改用 UpperBound（非 LowerBound）
//           LowerBound 在 key==Keys[i] 時回傳 i，走錯子樹
//           UpperBound 在 key==Keys[i] 時回傳 i+1，才正確
// ─────────────────────────────────────────────────────────────
namespace bplus_tree
{
    public sealed class BPlusTree<TKey, TValue> where TKey : IComparable<TKey>
    {
        // Order（階數）= 一個節點最多能有幾個 children
        // 每個節點最多 Order-1 個 key
        // 每個非根節點最少 ⌈Order/2⌉ - 1 個 key
        // 預設 Order=4 在小資料集下好測試；實際應設 64~256
        private readonly int _order;
        private Node<TKey, TValue> _root;
        private int _count;

        public int Count => _count;

        public BPlusTree(int order = 4)
        {
            if (order < 3) throw new ArgumentOutOfRangeException(nameof(order), "Order must be >= 3");

            _order = order;

            // 一棵空樹：根就是一個空的葉節點
            var emptyLeaf = new LeafNode<TKey, TValue>(order);
            _root = emptyLeaf;
        }

        // 插入或更新。若 key 已存在則覆蓋 value
        public void Insert(TKey key, TValue value)
        {
            // 嘗試在現有樹插入，若根發生 split 則長高一層
            var splitResult = InsertRecursive(_root, key, value);

            if (splitResult is not null)
            {
                // 根被分裂，建立新的根
                var newRoot = new InternalNode<TKey, TValue>(_order);
                newRoot.Keys[0] = splitResult.PromotedKey;
                newRoot.Children[0] = _root;
                newRoot.Children[1] = splitResult.NewRight;
                newRoot.KeyCount = 1;
                _root = newRoot;
            }
        }

        /// <summary>刪除。回傳 true 表示確實有刪到。</summary>
        public bool Delete(TKey key)
        {
            bool deleted = DeleteRecursive(_root, key, parent: null, parentIdx: 0);
            if (deleted)
            {
                _count--;

                // 若根是內部節點且已無 key，把唯一的 child 提升為新根
                if (_root is InternalNode<TKey, TValue> internalRoot  && internalRoot.KeyCount == 0)
                {
                    _root = internalRoot.Children[0]!;
                }
            }
            return deleted;
        }

        /// <summary>
        /// 範圍掃描 [from, to]（含兩端）。
        /// 利用葉節點的 linked list，O(log n + k)，k 為結果筆數。
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> Range(TKey from, TKey to)
        {
            if (from.CompareTo(to) > 0) yield break;

            // 找到第一個 >= from 的葉節點
            var leaf = FindLeaf(from);
            int i = LowerBound(leaf.Keys, leaf.KeyCount, from);

            // 沿 linked list 往右走，直到超過 to
            var current = leaf;

            while (current is not null)
            {
                while (i < current.KeyCount)
                {
                    if (current.Keys[i].CompareTo(to) > 0) yield break;
                    yield return new KeyValuePair<TKey, TValue>(current.Keys[i], current.Values[i]);
                    i++;
                }
                current = current.Next;
                i = 0;
            }
        }

        // 回傳值：null 表示無 split；非 null 表示此節點發生了 split
        private SplitResult<TKey, TValue>? InsertRecursive(Node<TKey, TValue> node, TKey key, TValue value)
        {
            if (node is LeafNode<TKey, TValue> leaf) return InsertIntoLeaf(leaf, key, value);

            var internalNode = (InternalNode<TKey, TValue>)node;
            int idx = UpperBound(internalNode.Keys, internalNode.KeyCount, key);

            var childSplit = InsertRecursive(internalNode.Children[idx]!, key, value);

            if (childSplit is null) return null;

            // 子節點分裂了，把 promotedKey 插入當前節點
            return InsertIntoInternal(internalNode, idx, childSplit);
        }

        private bool DeleteRecursive(Node<TKey, TValue> node, TKey key, InternalNode<TKey, TValue>? parent, int parentIdx)
        {
            if (node is LeafNode<TKey, TValue> leaf) return DeleteFromLeaf(leaf, key, parent, parentIdx);

            var internalNode = (InternalNode<TKey, TValue>)node;
            int idx = UpperBound(internalNode.Keys, internalNode.KeyCount, key);

            bool deleted = DeleteRecursive(internalNode.Children[idx]!, key, internalNode, idx);

            if (deleted) RebalanceIfNeeded(internalNode, idx);

            return deleted;
        }

        private SplitResult<TKey, TValue>? InsertIntoInternal(InternalNode<TKey, TValue> node, int childIdx, SplitResult<TKey, TValue> childSplit)
        {
            TKey promotedKey = childSplit.PromotedKey;
            var newChild = childSplit.NewRight;

            // 內部節點有空間
            if (node.KeyCount < _order - 1)
            {
                ShiftRight(node.Keys, childIdx, node.KeyCount);
                ShiftRightChildren(node.Children, childIdx + 1, node.KeyCount + 1);
                node.Keys[childIdx] = promotedKey;
                node.Children[childIdx + 1] = newChild;
                node.KeyCount++;
                return null;
            }

            // 內部節點已滿 → 分裂
            return SplitInternal(node, childIdx, promotedKey, newChild);
        }

        private SplitResult<TKey, TValue>? InsertIntoLeaf(LeafNode<TKey, TValue> leaf, TKey key, TValue value)
        {
            int idx = LowerBound(leaf.Keys, leaf.KeyCount, key);

            // 已存在 → 更新，不動 count
            if (idx < leaf.KeyCount && leaf.Keys[idx].CompareTo(key) == 0)
            {
                leaf.Values[idx] = value;
                return null;  // 更新不增加 count
            }

            _count++;  // 確認是新 key 才加

            // 葉節點有空間，直接插入
            if (leaf.KeyCount < _order - 1)
            {
                ShiftRight(leaf.Keys, idx, leaf.KeyCount);
                ShiftRight(leaf.Values, idx, leaf.KeyCount);
                leaf.Keys[idx] = key;
                leaf.Values[idx] = value;
                leaf.KeyCount++;
                return null;
            }

            // 葉節點已滿 → 分裂
            return SplitLeaf(leaf, idx, key, value);
        }

        private bool DeleteFromLeaf(LeafNode<TKey, TValue> leaf, TKey key, InternalNode<TKey, TValue>? parent, int parentIdx)
        {
            int idx = LowerBound(leaf.Keys, leaf.KeyCount, key);

            if (idx >= leaf.KeyCount || leaf.Keys[idx].CompareTo(key) != 0)
                return false;  // key 不存在

            // 移除 key/value（往左移）
            ShiftLeft(leaf.Keys, idx, leaf.KeyCount);
            ShiftLeft(leaf.Values, idx, leaf.KeyCount);
            leaf.KeyCount--;
            // 清除尾端殘留
            Array.Clear(leaf.Keys, leaf.KeyCount, 1);
            Array.Clear(leaf.Values, leaf.KeyCount, 1);

            // 若是根節點（parent == null），允許空
            if (parent is null) return true;

            int minKeys = (_order - 1) / 2;
            if (leaf.KeyCount >= minKeys) return true;

            // 不足最小數，嘗試借位或合併
            RebalanceLeaf(leaf, parent, parentIdx);
            return true;
        }


        private SplitResult<TKey, TValue> SplitInternal(InternalNode<TKey, TValue> left, int insertIdx, TKey insertKey, Node<TKey, TValue> insertChild)
        {
            int total = _order;  // order-1 個舊 key + 1 個新 key
            var tmpKeys = new TKey[total];
            var tmpChildren = new Node<TKey, TValue>?[total + 1];

            // 複製 insertIdx 之前
            Array.Copy(left.Keys, 0, tmpKeys, 0, insertIdx);
            Array.Copy(left.Children, 0, tmpChildren, 0, insertIdx + 1);

            // 放入新 key / child
            tmpKeys[insertIdx] = insertKey;
            tmpChildren[insertIdx + 1] = insertChild;

            // 複製 insertIdx 之後
            Array.Copy(left.Keys, insertIdx, tmpKeys, insertIdx + 1, left.KeyCount - insertIdx);
            Array.Copy(left.Children, insertIdx + 1, tmpChildren, insertIdx + 2, left.KeyCount - insertIdx);

            // 內部節點分裂：中位 key 提升，不留在子節點
            int mid = total / 2;
            TKey midKey = tmpKeys[mid];

            // 左節點：[0, mid)
            Array.Copy(tmpKeys, 0, left.Keys, 0, mid);
            Array.Copy(tmpChildren, 0, left.Children, 0, mid + 1);
            left.KeyCount = mid;
            Array.Clear(left.Keys, mid, left.Keys.Length - mid);
            Array.Clear(left.Children, mid + 1, left.Children.Length - mid - 1);

            // 右節點：(mid, total)，即 [mid+1, total)
            var right = new InternalNode<TKey, TValue>(_order);
            int rightKeyCount = total - mid - 1;
            Array.Copy(tmpKeys, mid + 1, right.Keys, 0, rightKeyCount);
            Array.Copy(tmpChildren, mid + 1, right.Children, 0, rightKeyCount + 1);
            right.KeyCount = rightKeyCount;

            return new SplitResult<TKey, TValue>(midKey, right);
        }

        private SplitResult<TKey, TValue> SplitLeaf(LeafNode<TKey, TValue> left, int insertIdx, TKey key, TValue value)
        {
            // 把 left + 新 key 的全部 key/value 暫存，再重新分配
            int total = _order;  // order-1 個舊的 + 1 個新的
            var tmpKeys = new TKey[total];
            var tmpVals = new TValue[total];

            // 複製 insertIdx 之前的
            Array.Copy(left.Keys, 0, tmpKeys, 0, insertIdx);
            Array.Copy(left.Values, 0, tmpVals, 0, insertIdx);

            // 放入新 key
            tmpKeys[insertIdx] = key;
            tmpVals[insertIdx] = value;

            // 複製 insertIdx 之後的
            Array.Copy(left.Keys, insertIdx, tmpKeys, insertIdx + 1, left.KeyCount - insertIdx);
            Array.Copy(left.Values, insertIdx, tmpVals, insertIdx + 1, left.KeyCount - insertIdx);

            // 分割點：左半 [0, mid)，右半 [mid, total)
            int mid = total / 2;

            // 左節點保留 [0, mid)
            Array.Copy(tmpKeys, 0, left.Keys, 0, mid);
            Array.Copy(tmpVals, 0, left.Values, 0, mid);
            left.KeyCount = mid;
            // 清除左節點舊的尾端（避免 GC root 殘留）
            Array.Clear(left.Keys, mid, left.Keys.Length - mid);
            Array.Clear(left.Values, mid, left.Values.Length - mid);

            // 右節點存放 [mid, total)
            var right = new LeafNode<TKey, TValue>(_order);
            int rightCount = total - mid;
            Array.Copy(tmpKeys, mid, right.Keys, 0, rightCount);
            Array.Copy(tmpVals, mid, right.Values, 0, rightCount);
            right.KeyCount = rightCount;

            // 維護 linked list
            right.Next = left.Next;
            right.Prev = left;
            left.Next?.Prev = right;
            left.Next = right;

            // B+Tree 的葉分裂：右半最小 key 提升給父節點（但右半仍保留該 key）
            return new SplitResult<TKey, TValue>(right.Keys[0], right);
        }

        // 把 arr[idx..count-1] 往右移一格，空出 arr[idx]
        private static void ShiftRight<T>(T[] arr, int idx, int count)
        {
            Array.Copy(arr, idx, arr, idx + 1, count - idx);
        }

        // 把 arr[idx+1..count-1] 往左移一格，覆蓋 arr[idx]
        private static void ShiftLeft<T>(T[] arr, int idx, int count)
        {
            Array.Copy(arr, idx + 1, arr, idx, count - idx - 1);
        }

        private static void ShiftRightChildren<TK, TV>(Node<TK, TV>?[] arr, int idx, int count) where TK : IComparable<TK>
        {
            Array.Copy(arr, idx, arr, idx + 1, count - idx);
        }

        private static void ShiftLeftChildren<TK, TV>(Node<TK, TV>?[] arr, int idx, int count) where TK : IComparable<TK>
        {
            Array.Copy(arr, idx + 1, arr, idx, count - idx - 1);
        }

        /// <summary>點查詢。找到回傳 true 並填入 value，否則 false。</summary>
        public bool TryGet(TKey key, out TValue? value)
        {
            var leaf = FindLeaf(key);
            int idx = LowerBound(leaf.Keys, leaf.KeyCount, key);

            if (idx < leaf.KeyCount && leaf.Keys[idx].CompareTo(key) == 0)
            {
                value = leaf.Values[idx];
                return true;
            }

            value = default;
            return false;
        }

        // 葉節點查找、Range 起點：第一個 >= key 的 index
        private static int LowerBound(TKey[] keys, int count, TKey key)
        {
            int i = 0;
            while (i < count && keys[i].CompareTo(key) < 0) i++;
            return i;
        }

        // 內部節點路徑選擇：第一個 > key 的 index
        //
        // B+Tree 語意：Children[i] 存 < Keys[i] 的子樹
        //              Children[i+1] 存 >= Keys[i] 的子樹
        // 所以 key == Keys[i] 時必須走 Children[i+1]
        // UpperBound 在此情況回傳 i+1，正確。
        private static int UpperBound(TKey[] keys, int count, TKey key)
        {
            int i = 0;
            while (i < count && keys[i].CompareTo(key) <= 0) i++;
            return i;
        }


        // 從 root 往下走，找到 key 應該在的葉節點
        private LeafNode<TKey, TValue> FindLeaf(TKey key)
        {
            var node = _root;

            while (node is InternalNode<TKey, TValue> internalNode)
            {
                node = internalNode.Children[UpperBound(internalNode.Keys, internalNode.KeyCount, key)]!;
            }

            return (LeafNode<TKey, TValue>)node;
        }

        private void RebalanceLeaf(LeafNode<TKey, TValue> leaf, InternalNode<TKey, TValue> parent, int parentIdx)
        {
            int minKeys = (_order - 1) / 2;

            // 嘗試從右兄弟借
            if (parentIdx < parent.KeyCount)
            {
                var rightSibling = (LeafNode<TKey, TValue>)parent.Children[parentIdx + 1]!;
                if (rightSibling.KeyCount > minKeys)
                {
                    // 借右兄弟的第一個 key/value
                    leaf.Keys[leaf.KeyCount] = rightSibling.Keys[0];
                    leaf.Values[leaf.KeyCount] = rightSibling.Values[0];
                    leaf.KeyCount++;

                    ShiftLeft(rightSibling.Keys, 0, rightSibling.KeyCount);
                    ShiftLeft(rightSibling.Values, 0, rightSibling.KeyCount);
                    rightSibling.KeyCount--;
                    Array.Clear(rightSibling.Keys, rightSibling.KeyCount, 1);
                    Array.Clear(rightSibling.Values, rightSibling.KeyCount, 1);

                    // 更新父節點的分隔 key
                    parent.Keys[parentIdx] = rightSibling.Keys[0];
                    return;
                }
            }

            // 嘗試從左兄弟借
            if (parentIdx > 0)
            {
                var leftSibling = (LeafNode<TKey, TValue>)parent.Children[parentIdx - 1]!;
                if (leftSibling.KeyCount > minKeys)
                {
                    // 借左兄弟的最後一個 key/value
                    ShiftRight(leaf.Keys, 0, leaf.KeyCount);
                    ShiftRight(leaf.Values, 0, leaf.KeyCount);
                    leaf.Keys[0] = leftSibling.Keys[leftSibling.KeyCount - 1];
                    leaf.Values[0] = leftSibling.Values[leftSibling.KeyCount - 1];
                    leaf.KeyCount++;

                    Array.Clear(leftSibling.Keys, leftSibling.KeyCount - 1, 1);
                    Array.Clear(leftSibling.Values, leftSibling.KeyCount - 1, 1);
                    leftSibling.KeyCount--;

                    parent.Keys[parentIdx - 1] = leaf.Keys[0];
                    return;
                }
            }

            // 無法借位 → 合併
            if (parentIdx < parent.KeyCount)
                MergeLeaves(leaf, (LeafNode<TKey, TValue>)parent.Children[parentIdx + 1]!, parent, parentIdx);
            else
                MergeLeaves((LeafNode<TKey, TValue>)parent.Children[parentIdx - 1]!, leaf, parent, parentIdx - 1);
        }

        private void MergeLeaves(LeafNode<TKey, TValue> left, LeafNode<TKey, TValue> right, InternalNode<TKey, TValue> parent, int sepIdx)
        {
            // 把 right 的所有 key/value 複製到 left
            Array.Copy(right.Keys, 0, left.Keys, left.KeyCount, right.KeyCount);
            Array.Copy(right.Values, 0, left.Values, left.KeyCount, right.KeyCount);
            left.KeyCount += right.KeyCount;

            // 維護 linked list
            left.Next = right.Next;
            if (right.Next is not null) right.Next.Prev = left;

            // 從父節點移除分隔 key 和指向 right 的 child 指標
            ShiftLeft(parent.Keys, sepIdx, parent.KeyCount);
            ShiftLeftChildren(parent.Children, sepIdx + 1, parent.KeyCount + 1);
            parent.KeyCount--;
            Array.Clear(parent.Keys, parent.KeyCount, 1);
            parent.Children[parent.KeyCount + 1] = null;
        }

        private void RebalanceIfNeeded(InternalNode<TKey, TValue> node, int childIdx)
        {
            if (node.Children[childIdx] is not InternalNode<TKey, TValue> child)
                return;  // 葉節點在 RebalanceLeaf 處理

            int minKeys = (_order - 1) / 2;
            if (child.KeyCount >= minKeys) return;

            // 從右兄弟借
            if (childIdx < node.KeyCount)
            {
                var right = (InternalNode<TKey, TValue>)node.Children[childIdx + 1]!;
                if (right.KeyCount > minKeys)
                {
                    // 把父節點的分隔 key 下移給 child
                    child.Keys[child.KeyCount] = node.Keys[childIdx];
                    child.Children[child.KeyCount + 1] = right.Children[0];
                    child.KeyCount++;

                    node.Keys[childIdx] = right.Keys[0];

                    ShiftLeft(right.Keys, 0, right.KeyCount);
                    ShiftLeftChildren(right.Children, 0, right.KeyCount + 1);
                    right.KeyCount--;
                    Array.Clear(right.Keys, right.KeyCount, 1);
                    right.Children[right.KeyCount + 1] = null;
                    return;
                }
            }

            // 從左兄弟借
            if (childIdx > 0)
            {
                var left = (InternalNode<TKey, TValue>)node.Children[childIdx - 1]!;
                if (left.KeyCount > minKeys)
                {
                    ShiftRight(child.Keys, 0, child.KeyCount);
                    ShiftRightChildren(child.Children, 0, child.KeyCount + 1);

                    child.Keys[0] = node.Keys[childIdx - 1];
                    child.Children[0] = left.Children[left.KeyCount];
                    child.KeyCount++;

                    node.Keys[childIdx - 1] = left.Keys[left.KeyCount - 1];
                    Array.Clear(left.Keys, left.KeyCount - 1, 1);
                    left.Children[left.KeyCount] = null;
                    left.KeyCount--;
                    return;
                }
            }

            // 合併
            if (childIdx < node.KeyCount)
                MergeInternals((InternalNode<TKey, TValue>)node.Children[childIdx]!,
                               (InternalNode<TKey, TValue>)node.Children[childIdx + 1]!,
                               node, childIdx);
            else
                MergeInternals((InternalNode<TKey, TValue>)node.Children[childIdx - 1]!,
                               (InternalNode<TKey, TValue>)node.Children[childIdx]!,
                               node, childIdx - 1);
        }

        private void MergeInternals(InternalNode<TKey, TValue> left, InternalNode<TKey, TValue> right, InternalNode<TKey, TValue> parent, int sepIdx)
        {
            // 把父節點的分隔 key 下移，再把 right 的 key/children 全部移入 left
            left.Keys[left.KeyCount] = parent.Keys[sepIdx];
            Array.Copy(right.Keys, 0, left.Keys, left.KeyCount + 1, right.KeyCount);
            Array.Copy(right.Children, 0, left.Children, left.KeyCount + 1, right.KeyCount + 1);
            left.KeyCount += right.KeyCount + 1;

            ShiftLeft(parent.Keys, sepIdx, parent.KeyCount);
            ShiftLeftChildren(parent.Children, sepIdx + 1, parent.KeyCount + 1);
            parent.KeyCount--;
            Array.Clear(parent.Keys, parent.KeyCount, 1);
            parent.Children[parent.KeyCount + 1] = null;
        }
    }
}
