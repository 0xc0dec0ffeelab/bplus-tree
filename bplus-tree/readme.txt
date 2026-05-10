
https://en.wikipedia.org/wiki/B%2B_tree


Concurrent B+ Trees
https://medium.com/@mkrebser/concurrent-b-trees-concurrentsorteddictionary-c-net-f7c1c2a84141

Basics:

① 每個節點最多 2t−1 個 key（t 稱為最小度，minimum degree）
② 每個非根節點最少 t−1 個 key（防止節點過稀）
③ 所有葉節點在同一層（高度嚴格平衡）
④ n 個 key 的節點有 n+1 個指標（children）
→ 違反任何一條，樹就壞了。PostgreSQL、MySQL InnoDB、Linux ext4 都依賴這些不變量。


Feature List:

1. 支援繁體中文搜尋 (自訂 Comparator 例如: Binary、用 ICU 函式庫)

1-1 新增/刪除/修改的功能都會重新排序


Phase 2（下一步）：
  1. Span<T> 取代 Array.Copy             ← 漸進式，不改架構
     Comparer<TKey>.Default  (Comparer<TKey>.Default（比 IComparable 快）)   
     Binary/Linear 混合 search (實測建議：Order ≤ 32 用 linear，Order > 64 用 binary。)
  2. NativeMemory + Slab allocator       ← 改變最大、風險可控
  3. Reader-Writer lock                  ← Thread safe 的最小可行版本

Phase 3：
  4. Optimistic latch（OLFIT）           ← 替換 RW lock

Phase 4：
  5. 評估 Bw-Tree lock-free              ← 看 Phase 3 benchmark 結果再決定


Others


ArrayPool<T>
不想用 NativeMemory 但又想避免 GC allocation 時的中間地帶。從共享池借陣列，用完還回去。
split 操作需要暫存陣列（你現在的 tmpKeys、tmpVals）。
現在每次 split 都 new TKey[total]，改成 ArrayPool.Rent 可以消除這個 allocation。

Bulk loading（大量資料一次載入）
如果資料是預先排序好的，不要一筆一筆 Insert。直接從葉節點底層建起，再往上構建內部節點，
時間複雜度從 O(n log n) 降到 O(n)，而且產生的樹結構 cache 利用率最佳（節點幾乎全滿）。
bgen 的 push_last benchmark 跑到 116M op/sec 就是這個原理

Lazy deletion（tombstone）
刪除時不立即 rebalance，只打一個 deleted flag。Rebalance 延遲到下一次訪問到這個節點時才做，
或是定期批次執行。對「刪除後馬上重新插入」的場景（像資料庫的 update = delete + insert）特別有效，
省掉大量 merge/split 操作。