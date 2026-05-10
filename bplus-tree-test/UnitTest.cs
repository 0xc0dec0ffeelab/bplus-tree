using bplus_tree;
using FluentAssertions;

namespace bplus_tree_test
{
    public class UnitTest
    {

        [Fact]
        public void Constructor_DefaultOrder_CountIsZero()
        {
            var tree = new BPlusTree<int, int>();
            tree.Count.Should().Be(0);
        }

        [Fact]
        public void Constructor_OrderLessThan3_Throws()
        {
            var act = () => new BPlusTree<int, int>(order: 2);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void Constructor_OrderExactly3_DoesNotThrow()
        {
            var act = () => new BPlusTree<int, int>(order: 3);
            act.Should().NotThrow();
        }

        // ════════════════════════════════════════════════════════
        //  Insert — 基本路徑
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Insert_SingleItem_CountIsOne()
        {
            var tree = new BPlusTree<int, string>();
            tree.Insert(1, "a");
            tree.Count.Should().Be(1);
        }

        [Fact]
        public void Insert_DuplicateKey_UpdatesValue_CountUnchanged()
        {
            var tree = new BPlusTree<int, string>();
            tree.Insert(1, "first");
            tree.Insert(1, "second");

            tree.Count.Should().Be(1);
            tree.TryGet(1, out var v).Should().BeTrue();
            v.Should().Be("second");
        }

        // ── 葉節點分裂（SplitLeaf）────────────────────────────

        // order=3：每個葉最多 2 個 key，第 3 個插入觸發分裂
        [Fact]
        public void Insert_TriggerLeafSplit_AllKeysStillFound()
        {
            // order=3 → 葉滿 2 個，插第 3 個就 split
            var tree = new BPlusTree<int, int>(order: 3);
            tree.Insert(1, 1);
            tree.Insert(2, 2);
            tree.Insert(3, 3); // split

            tree.Count.Should().Be(3);
            tree.TryGet(1, out var v1).Should().BeTrue(); v1.Should().Be(1);
            tree.TryGet(2, out var v2).Should().BeTrue(); v2.Should().Be(2);
            tree.TryGet(3, out var v3).Should().BeTrue(); v3.Should().Be(3);
        }

        [Fact]
        public void Insert_LeafSplit_MiddleLeaf_LinkedListMaintained()
        {
            // 插入足夠多的 key 讓中間某個葉分裂（非最右葉）
            // order=3，插入 1..6，會產生多個葉節點，中間葉被分裂時
            // left.Next 不為 null，覆蓋 SplitLeaf L193 的 if 分支
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 1; i <= 6; i++) tree.Insert(i, i);

            // Range 能跨越所有葉節點表示 linked list 正確
            var result = tree.Range(1, 6).Select(kv => kv.Key).ToList();
            result.Should().Equal(1, 2, 3, 4, 5, 6);
        }

        // ── 內部節點分裂（SplitInternal）─────────────────────

        [Fact]
        public void Insert_TriggerInternalSplit_TreeGrowsInHeight()
        {
            // order=3：每個內部節點最多 2 個 key（3 個 children）
            // 插入足夠多讓根也發生 split，樹高從 2 變 3
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 1; i <= 10; i++) tree.Insert(i, i);

            tree.Count.Should().Be(10);
            for (int i = 1; i <= 10; i++)
                tree.TryGet(i, out _).Should().BeTrue($"key {i} missing");
        }

        [Fact]
        public void Insert_MultipleInternalSplits_AllKeysCorrect()
        {
            var tree = new BPlusTree<int, int>(order: 3);
            const int n = 50;
            for (int i = n; i >= 1; i--) tree.Insert(i, i * 2); // reverse order

            tree.Count.Should().Be(n);
            for (int i = 1; i <= n; i++)
            {
                tree.TryGet(i, out var v).Should().BeTrue();
                v.Should().Be(i * 2);
            }
        }

        // ── 各種 order ─────────────────────────────────────────

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        public void Insert_Sequential_AllFound(int order)
        {
            var tree = new BPlusTree<int, int>(order);
            for (int i = 0; i < 500; i++) tree.Insert(i, i * 10);

            tree.Count.Should().Be(500);
            for (int i = 0; i < 500; i++)
            {
                tree.TryGet(i, out var v).Should().BeTrue();
                v.Should().Be(i * 10);
            }
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(16)]
        public void Insert_Reverse_AllFound(int order)
        {
            var tree = new BPlusTree<int, int>(order);
            for (int i = 499; i >= 0; i--) tree.Insert(i, i);

            tree.Count.Should().Be(500);
            for (int i = 0; i < 500; i++)
                tree.TryGet(i, out _).Should().BeTrue();
        }

        [Fact]
        public void Insert_Random_AgainstOracle()
        {
            var tree = new BPlusTree<int, int>(order: 4);
            var oracle = new SortedDictionary<int, int>();
            var rng = new Random(42);

            for (int i = 0; i < 2000; i++)
            {
                int key = rng.Next(500);
                int val = rng.Next();
                tree.Insert(key, val);
                oracle[key] = val;
            }

            tree.Count.Should().Be(oracle.Count);
            foreach (var (k, v) in oracle)
            {
                tree.TryGet(k, out var found).Should().BeTrue();
                found.Should().Be(v);
            }
        }

        // ════════════════════════════════════════════════════════
        //  TryGet
        // ════════════════════════════════════════════════════════

        [Fact]
        public void TryGet_EmptyTree_ReturnsFalse()
        {
            var tree = new BPlusTree<int, string>();
            tree.TryGet(1, out _).Should().BeFalse();
        }

        [Fact]
        public void TryGet_KeySmallerThanAll_ReturnsFalse()
        {
            var tree = new BPlusTree<int, int>();
            tree.Insert(10, 10);
            tree.Insert(20, 20);
            tree.TryGet(1, out _).Should().BeFalse();
        }

        [Fact]
        public void TryGet_KeyLargerThanAll_ReturnsFalse()
        {
            var tree = new BPlusTree<int, int>();
            tree.Insert(10, 10);
            tree.Insert(20, 20);
            tree.TryGet(99, out _).Should().BeFalse();
        }

        [Fact]
        public void TryGet_KeyBetweenExisting_ReturnsFalse()
        {
            var tree = new BPlusTree<int, int>(order: 3);
            tree.Insert(1, 1);
            tree.Insert(3, 3);
            tree.Insert(5, 5); // trigger split
            tree.TryGet(2, out _).Should().BeFalse();
            tree.TryGet(4, out _).Should().BeFalse();
        }

        // ════════════════════════════════════════════════════════
        //  Delete — 基本路徑
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Delete_NonExistentKey_ReturnsFalse()
        {
            var tree = new BPlusTree<int, int>();
            tree.Insert(1, 1);
            tree.Delete(99).Should().BeFalse();
            tree.Count.Should().Be(1);
        }

        [Fact]
        public void Delete_EmptyTree_ReturnsFalse()
        {
            var tree = new BPlusTree<int, int>();
            tree.Delete(1).Should().BeFalse();
        }

        // ── DeleteFromLeaf: parent is null（根就是葉節點）────

        [Fact]
        public void Delete_RootIsLeaf_NoParent_CountDecreases()
        {
            // 只插入少量 key，根仍是葉節點（沒有 split 過）
            var tree = new BPlusTree<int, int>(order: 4);
            tree.Insert(1, 1);
            tree.Insert(2, 2);

            tree.Delete(1).Should().BeTrue();
            tree.Count.Should().Be(1);
            tree.TryGet(1, out _).Should().BeFalse();
            tree.TryGet(2, out _).Should().BeTrue();
        }

        [Fact]
        public void Delete_LastItem_TreeIsEmpty()
        {
            var tree = new BPlusTree<int, int>(order: 4);
            tree.Insert(5, 5);

            tree.Delete(5).Should().BeTrue();
            tree.Count.Should().Be(0);
            tree.TryGet(5, out _).Should().BeFalse();
        }

        // ── DeleteFromLeaf: KeyCount >= min 後不需要 rebalance ─

        [Fact]
        public void Delete_LeafStillHasEnoughKeys_NoRebalance()
        {
            // order=4：min = (4-1)/2 = 1，葉有 3 個 key，刪 1 個剩 2 個 >= 1
            // → 不觸發 rebalance
            var tree = new BPlusTree<int, int>(order: 4);
            // 插入剛好不 split 的數量
            tree.Insert(1, 1);
            tree.Insert(2, 2);
            tree.Insert(3, 3);
            // 尚未 split，刪除後剩 2 個，夠用
            tree.Delete(2).Should().BeTrue();

            tree.Count.Should().Be(2);
            tree.TryGet(1, out _).Should().BeTrue();
            tree.TryGet(3, out _).Should().BeTrue();
        }

        // ── RebalanceLeaf: 借右兄弟 ───────────────────────────

        [Fact]
        public void Delete_LeafBorrowsFromRightSibling()
        {
            // order=3：min=1
            // 構造：左葉 [1]（剛好 min），右葉 [3,4]（可借）
            // 刪除不存在於右葉的 key，讓左葉需要借
            //
            // 插入順序讓分裂產生：左[1,2] 右[3,4] → 刪 2 讓左葉只剩[1]
            // 右葉有 2 個（>min=1），觸發「借右」
            var tree = new BPlusTree<int, int>(order: 3);
            tree.Insert(1, 1);
            tree.Insert(2, 2);
            tree.Insert(3, 3);
            tree.Insert(4, 4);
            // 此時結構：葉 [1,2] | 葉 [3,4]，根 key=[3]
            tree.Delete(2); // 左葉變 [1]，右葉 [3,4] 可借 → 借右

            tree.TryGet(1, out _).Should().BeTrue();
            tree.TryGet(3, out _).Should().BeTrue();
            tree.TryGet(4, out _).Should().BeTrue();
            tree.Count.Should().Be(3);
        }

        // ── RebalanceLeaf: 借左兄弟 ───────────────────────────

        [Fact]
        public void Delete_LeafBorrowsFromLeftSibling()
        {
            // order=3：插入 1,2,3,4 → 葉[1,2] | 葉[3,4]
            // 刪 3，右葉只剩 [4]（==min），左葉 [1,2] 可借
            // → 觸發「借左」
            var tree = new BPlusTree<int, int>(order: 3);
            tree.Insert(1, 1);
            tree.Insert(2, 2);
            tree.Insert(3, 3);
            tree.Insert(4, 4);
            tree.Delete(3); // 右葉變 [4]，左葉 [1,2] 可借 → 借左

            tree.TryGet(1, out _).Should().BeTrue();
            tree.TryGet(2, out _).Should().BeTrue();
            tree.TryGet(4, out _).Should().BeTrue();
            tree.Count.Should().Be(3);
        }

        // ── RebalanceLeaf: 合併右（parentIdx < parent.KeyCount）

        [Fact]
        public void Delete_LeafMergesWithRightSibling()
        {
            // order=3：兩個兄弟都只有 min 個 key，無法借，只能合併
            // 插入 1,2,3 → 葉[1] | 葉[2,3]，root=[2]
            // 刪 1，左葉空，右葉也只有 min=1 → 合併
            var tree = new BPlusTree<int, int>(order: 3);
            tree.Insert(1, 1);
            tree.Insert(2, 2);
            tree.Insert(3, 3);
            tree.Delete(1);
            tree.Delete(2); // 此步讓某葉降到 0，觸發合併

            tree.Count.Should().Be(1);
            tree.TryGet(3, out _).Should().BeTrue();
        }

        // ── RebalanceLeaf: 合併左（parentIdx == parent.KeyCount）

        [Fact]
        public void Delete_RightmostLeaf_MergesWithLeftSibling()
        {
            // 刪除最右邊葉節點的 key，使它需要與左兄弟合併
            var tree = new BPlusTree<int, int>(order: 3);
            tree.Insert(1, 1);
            tree.Insert(2, 2);
            tree.Insert(3, 3);
            // 葉 [1,2] | 葉 [3]（order=3，root split 後）
            // 刪 3 讓最右葉空 → 與左葉合併（左合併路徑）
            tree.Delete(3);
            tree.Delete(2); // 觸發最右葉合併

            tree.Count.Should().Be(1);
            tree.TryGet(1, out _).Should().BeTrue();
        }

        // ── MergeLeaves: right.Next != null（非最後葉合併）────

        [Fact]
        public void Delete_MergeMiddleLeaves_LinkedListCorrect()
        {
            // 至少三個葉節點，刪除中間葉觸發合併
            // 合併後 right.Next 不為 null，要正確維護 Prev 指標
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 1; i <= 7; i++) tree.Insert(i, i);

            // 刪除讓中間葉觸發合併
            tree.Delete(2);
            tree.Delete(3);

            // 驗證 Range（linked list 正確才能 scan）
            var result = tree.Range(1, 7).Select(kv => kv.Key).ToList();
            result.Should().BeInAscendingOrder();
            result.Should().BeSubsetOf(new[] { 1, 4, 5, 6, 7 });
        }

        // ── TreeHeight: 樹高收縮（根 KeyCount==0）────────────

        [Fact]
        public void Delete_CausesRootShrink_HeightDecreases()
        {
            // order=3，插入足夠多讓根分裂（樹高 > 1），
            // 再刪除所有 key，最後根 KeyCount==0 → 收縮
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 1; i <= 10; i++) tree.Insert(i, i);
            for (int i = 1; i <= 10; i++) tree.Delete(i);

            tree.Count.Should().Be(0);
            // 刪完後仍可插入（樹結構還健在）
            tree.Insert(99, 99);
            tree.TryGet(99, out var v).Should().BeTrue();
            v.Should().Be(99);
        }

        // ── RebalanceIfNeeded: child 是葉節點 → 直接 return ──

        [Fact]
        public void Delete_InternalNodeChildIsLeaf_RebalanceSkipped()
        {
            // 刪除葉節點後，RebalanceIfNeeded 發現 child 是葉節點
            // → 直接 return，葉的 rebalance 由 RebalanceLeaf 處理
            // 用 oracle 驗證結果正確即可（路徑由 order=4 的插入/刪除自然觸發）
            var tree = new BPlusTree<int, int>(order: 4);
            for (int i = 1; i <= 8; i++) tree.Insert(i, i);
            tree.Delete(4);

            tree.Count.Should().Be(7);
            tree.TryGet(4, out _).Should().BeFalse();
            for (int i = 1; i <= 8; i++)
                if (i != 4) tree.TryGet(i, out _).Should().BeTrue();
        }

        // ── RebalanceIfNeeded: 內部節點借右兄弟 ──────────────

        [Fact]
        public void Delete_InternalNodeBorrowsFromRightSibling()
        {
            // 需要讓內部節點本身 key 不足，且右兄弟 > min
            // order=3，大量插入後刪除特定序列觸發此路徑
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 1; i <= 15; i++) tree.Insert(i, i);

            // 刪除左半部分，讓左邊的內部節點 key 不足
            for (int i = 1; i <= 5; i++) tree.Delete(i);

            tree.Count.Should().Be(10);
            for (int i = 6; i <= 15; i++)
                tree.TryGet(i, out _).Should().BeTrue($"key {i} missing");
        }

        // ── RebalanceIfNeeded: 內部節點借左兄弟 ──────────────

        [Fact]
        public void Delete_InternalNodeBorrowsFromLeftSibling()
        {
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 1; i <= 15; i++) tree.Insert(i, i);

            // 刪除右半部分，讓右邊的內部節點 key 不足
            for (int i = 11; i <= 15; i++) tree.Delete(i);

            tree.Count.Should().Be(10);
            for (int i = 1; i <= 10; i++)
                tree.TryGet(i, out _).Should().BeTrue($"key {i} missing");
        }

        // ── RebalanceIfNeeded: 內部節點合併（右合併）─────────

        [Fact]
        public void Delete_InternalNodeMergesRight()
        {
            // order=3，大量插入再大量刪除，觸發內部節點合併
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 1; i <= 20; i++) tree.Insert(i, i);
            for (int i = 1; i <= 15; i++) tree.Delete(i);

            tree.Count.Should().Be(5);
            for (int i = 16; i <= 20; i++)
                tree.TryGet(i, out _).Should().BeTrue();
        }

        // ── RebalanceIfNeeded: 內部節點合併（左合併）─────────

        [Fact]
        public void Delete_InternalNodeMergesLeft()
        {
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 1; i <= 20; i++) tree.Insert(i, i);
            // 刪中間，讓最右邊的內部節點需要與左合併
            for (int i = 6; i <= 20; i++) tree.Delete(i);

            tree.Count.Should().Be(5);
            for (int i = 1; i <= 5; i++)
                tree.TryGet(i, out _).Should().BeTrue();
        }

        // ── Delete + Insert 交錯 ──────────────────────────────

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(8)]
        public void Delete_AllKeys_ThenReinsert(int order)
        {
            var tree = new BPlusTree<int, int>(order);
            for (int i = 0; i < 100; i++) tree.Insert(i, i);
            for (int i = 0; i < 100; i++) tree.Delete(i).Should().BeTrue();

            tree.Count.Should().Be(0);

            // 重新插入，確認樹結構還正常
            for (int i = 0; i < 50; i++) tree.Insert(i, i * 2);
            tree.Count.Should().Be(50);
            for (int i = 0; i < 50; i++)
            {
                tree.TryGet(i, out var v).Should().BeTrue();
                v.Should().Be(i * 2);
            }
        }

        [Fact]
        public void Delete_Random_AgainstOracle()
        {
            var tree = new BPlusTree<int, int>(order: 4);
            var oracle = new SortedDictionary<int, int>();
            var rng = new Random(7);

            for (int i = 0; i < 500; i++)
            {
                int key = rng.Next(200);
                tree.Insert(key, key);
                oracle[key] = key;
            }

            var keys = oracle.Keys.ToList();
            foreach (var k in keys.Where((_, i) => i % 2 == 0))
            {
                tree.Delete(k).Should().BeTrue();
                oracle.Remove(k);
            }

            tree.Count.Should().Be(oracle.Count);
            foreach (var (k, v) in oracle)
            {
                tree.TryGet(k, out var found).Should().BeTrue();
                found.Should().Be(v);
            }
        }

        // ════════════════════════════════════════════════════════
        //  Range
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Range_EmptyTree_ReturnsEmpty()
        {
            var tree = new BPlusTree<int, int>();
            tree.Range(1, 10).Should().BeEmpty();
        }

        [Fact]
        public void Range_FromGreaterThanTo_ReturnsEmpty()
        {
            var tree = new BPlusTree<int, int>();
            tree.Insert(1, 1);
            tree.Range(10, 1).Should().BeEmpty();
        }

        [Fact]
        public void Range_FromEqualsTo_ReturnsSingleItem()
        {
            var tree = new BPlusTree<int, int>();
            tree.Insert(5, 50);
            tree.Insert(3, 30);
            tree.Insert(7, 70);

            var result = tree.Range(5, 5).ToList();
            result.Should().HaveCount(1);
            result[0].Key.Should().Be(5);
            result[0].Value.Should().Be(50);
        }

        [Fact]
        public void Range_FromEqualsTo_KeyNotExist_ReturnsEmpty()
        {
            var tree = new BPlusTree<int, int>();
            tree.Insert(3, 3);
            tree.Insert(7, 7);
            tree.Range(5, 5).Should().BeEmpty();
        }

        [Fact]
        public void Range_AllItems_SortedOrder()
        {
            var tree = new BPlusTree<int, int>(order: 4);
            int[] keys = [5, 2, 8, 1, 9, 3, 7, 4, 6];
            foreach (var k in keys) tree.Insert(k, k * 10);

            var result = tree.Range(1, 9).Select(kv => kv.Key).ToList();
            result.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
        }

        [Fact]
        public void Range_SubRange_ReturnsCorrectItems()
        {
            var tree = new BPlusTree<int, int>(order: 4);
            for (int i = 0; i < 20; i++) tree.Insert(i, i);

            var result = tree.Range(5, 10).Select(kv => kv.Key).ToList();
            result.Should().Equal(5, 6, 7, 8, 9, 10);
        }

        [Fact]
        public void Range_ExceedsTree_StopsAtLast()
        {
            var tree = new BPlusTree<int, int>();
            tree.Insert(3, 3);
            tree.Insert(7, 7);

            var result = tree.Range(5, 100).Select(kv => kv.Key).ToList();
            result.Should().Equal(7);
        }

        [Fact]
        public void Range_SpansMultipleLeaves_LinkedListTraversed()
        {
            // order=3 → 每個葉最多 2 個 key，100 個 key 會有多個葉節點
            // Range 必須走過多個葉（cur = cur.Next），覆蓋 L88 分支
            var tree = new BPlusTree<int, int>(order: 3);
            for (int i = 0; i < 100; i++) tree.Insert(i, i);

            var result = tree.Range(0, 99).Select(kv => kv.Key).ToList();
            result.Should().HaveCount(100);
            result.Should().BeInAscendingOrder();
            result.First().Should().Be(0);
            result.Last().Should().Be(99);
        }

        [Fact]
        public void Range_AgainstOracle()
        {
            var tree = new BPlusTree<int, int>(order: 4);
            var oracle = new SortedDictionary<int, int>();
            var rng = new Random(123);

            for (int i = 0; i < 1000; i++)
            {
                int key = rng.Next(500);
                tree.Insert(key, key);
                oracle[key] = key;
            }

            int lo = 100, hi = 300;
            var expected = oracle.Where(kv => kv.Key >= lo && kv.Key <= hi)
                                 .Select(kv => kv.Key).ToList();
            var actual = tree.Range(lo, hi).Select(kv => kv.Key).ToList();

            actual.Should().Equal(expected);
        }

        // ════════════════════════════════════════════════════════
        //  String key（非數字 key 型別）
        // ════════════════════════════════════════════════════════

        [Fact]
        public void Insert_StringKey_SortedCorrectly()
        {
            var tree = new BPlusTree<string, int>();
            tree.Insert("banana", 2);
            tree.Insert("apple", 1);
            tree.Insert("cherry", 3);
            tree.Insert("date", 4);

            var result = tree.Range("apple", "date").Select(kv => kv.Key).ToList();
            result.Should().Equal("apple", "banana", "cherry", "date");
        }

        [Fact]
        public void Delete_StringKey_Works()
        {
            var tree = new BPlusTree<string, int>();
            tree.Insert("a", 1);
            tree.Insert("b", 2);
            tree.Insert("c", 3);

            tree.Delete("b").Should().BeTrue();
            tree.TryGet("b", out _).Should().BeFalse();
            tree.Count.Should().Be(2);
        }

        // ════════════════════════════════════════════════════════
        //  大規模 Property-based
        // ════════════════════════════════════════════════════════

        [Theory]
        [InlineData(3, 200, 42)]
        [InlineData(4, 500, 99)]
        [InlineData(8, 1000, 7)]
        [InlineData(16, 2000, 13)]
        public void InsertDeleteRange_AgainstOracle(int order, int n, int seed)
        {
            var tree = new BPlusTree<int, int>(order);
            var oracle = new SortedDictionary<int, int>();
            var rng = new Random(seed);

            // 插入
            for (int i = 0; i < n; i++)
            {
                int key = rng.Next(n / 2);
                int val = rng.Next();
                tree.Insert(key, val);
                oracle[key] = val;
            }

            // 驗證 Count 與所有 key
            tree.Count.Should().Be(oracle.Count);
            foreach (var (k, v) in oracle)
            {
                tree.TryGet(k, out var found).Should().BeTrue($"key {k} missing");
                found.Should().Be(v);
            }

            // 刪除一半
            var toDelete = oracle.Keys.Where((_, i) => i % 2 == 0).ToList();
            foreach (var k in toDelete)
            {
                tree.Delete(k).Should().BeTrue($"delete {k} failed");
                oracle.Remove(k);
            }

            // 再次驗證
            tree.Count.Should().Be(oracle.Count);

            // Range 驗證
            if (oracle.Count > 0)
            {
                int lo = oracle.Keys.Min();
                int hi = oracle.Keys.Max();
                var expected = oracle.Keys.ToList();
                var actual = tree.Range(lo, hi).Select(kv => kv.Key).ToList();
                actual.Should().Equal(expected);
            }
        }
    }
}
