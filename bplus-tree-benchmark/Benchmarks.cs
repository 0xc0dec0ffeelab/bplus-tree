using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using bplus_tree;
using System;
using System.Collections.Generic;
using System.Linq;

// ─────────────────────────────────────────────────────────────
//  執行方式（必須 Release）：
//    dotnet run -c Release --project bplus-tree-benchmark.csproj
//
//  快速冒煙測試（跳過 warmup，確認程式碼能跑）：
//    dotnet run -c Release --project bplus-tree-benchmark.csproj -- --job short
//
//  結果輸出到：
//    BenchmarkDotNet.Artifacts/results/
// ─────────────────────────────────────────────────────────────

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).RunAll(
    DefaultConfig.Instance
        .AddDiagnoser(MemoryDiagnoser.Default)   // 顯示 GC alloc 和 Gen0/1/2
        .AddColumn(RankColumn.Arabic)             // 每個 benchmark 顯示名次
        .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(20))
);

// ═════════════════════════════════════════════════════════════
//  共用設定
//
//  N = 10_000 / 100_000 / 1_000_000 三個規模
//  order = 64（接近生產環境；測試用的預設 4 不具代表性）
//
//  GlobalSetup：建樹（不計入測量）
//  IterationSetup：Delete benchmark 每次 iteration 前重建樹，
//                  避免刪光後下一次 iteration 無東西可刪
// ═════════════════════════════════════════════════════════════

public class BenchmarkBase
{
    [Params(10_000, 100_000, 1_000_000)]
    public int N;

    protected const int Order = 64;

    // 亂序 keys（同一份，所有 benchmark 共用）
    protected int[] RandKeys = null!;
    // 循序 keys
    protected int[] SeqKeys = null!;

    [GlobalSetup]
    public virtual void GlobalSetup()
    {
        SeqKeys = Enumerable.Range(0, N).ToArray();

        // 固定 seed 確保可重現
        var rng = new Random(42);
        RandKeys = SeqKeys.OrderBy(_ => rng.Next()).ToArray();
    }

    // 建好的樹（供 Get / Range 使用，GlobalSetup 初始化）
    protected BPlusTree<int, int> BPT = null!;
    protected SortedDictionary<int, int> SD = null!;

    protected void BuildTrees()
    {
        BPT = new BPlusTree<int, int>(Order);
        SD = new SortedDictionary<int, int>();
        foreach (var k in RandKeys)
        {
            BPT.Insert(k, k);
            SD[k] = k;
        }
    }
}

// ═════════════════════════════════════════════════════════════
//  1. Insert — 循序 & 亂序
//
//  重點：每次 benchmark iteration 都建新樹，避免 duplicate key
//        走 update 路徑（不公平）
//  做法：[IterationSetup] 不用，直接在 [Benchmark] 裡 new
//        BenchmarkDotNet 會控制 iteration 次數
// ═════════════════════════════════════════════════════════════

[SimpleJob]
[MemoryDiagnoser]
public class InsertBenchmark : BenchmarkBase
{
    // ── 循序插入 ─────────────────────────────────────────
    [Benchmark(Baseline = true, Description = "BPT  Insert seq")]
    public BPlusTree<int, int> BPT_Insert_Sequential()
    {
        var tree = new BPlusTree<int, int>(Order);
        foreach (var k in SeqKeys) tree.Insert(k, k);
        return tree;
    }

    [Benchmark(Description = "SD   Insert seq")]
    public SortedDictionary<int, int> SD_Insert_Sequential()
    {
        var dict = new SortedDictionary<int, int>();
        foreach (var k in SeqKeys) dict[k] = k;
        return dict;
    }

    // ── 亂序插入 ─────────────────────────────────────────
    [Benchmark(Description = "BPT  Insert rand")]
    public BPlusTree<int, int> BPT_Insert_Random()
    {
        var tree = new BPlusTree<int, int>(Order);
        foreach (var k in RandKeys) tree.Insert(k, k);
        return tree;
    }

    [Benchmark(Description = "SD   Insert rand")]
    public SortedDictionary<int, int> SD_Insert_Random()
    {
        var dict = new SortedDictionary<int, int>();
        foreach (var k in RandKeys) dict[k] = k;
        return dict;
    }
}

// ═════════════════════════════════════════════════════════════
//  2. Get（點查詢）
//
//  樹在 GlobalSetup 建好，benchmark 只測查詢本身
//  用亂序查詢（最壞情況，避免 branch predictor 被 sequential 騙）
// ═════════════════════════════════════════════════════════════

[SimpleJob]
[MemoryDiagnoser]
public class GetBenchmark : BenchmarkBase
{
    public override void GlobalSetup()
    {
        base.GlobalSetup();
        BuildTrees();
    }

    [Benchmark(Baseline = true, Description = "BPT  Get rand")]
    public int BPT_Get_Random()
    {
        int sum = 0;
        foreach (var k in RandKeys)
        {
            BPT.TryGet(k, out var v);
            sum += v;
        }
        return sum;    // 回傳避免 JIT 把整個迴圈優化掉
    }

    [Benchmark(Description = "SD   Get rand")]
    public int SD_Get_Random()
    {
        int sum = 0;
        foreach (var k in RandKeys)
        {
            SD.TryGetValue(k, out var v);
            sum += v;
        }
        return sum;
    }

    // 循序查詢（展示 cache 友好的差異）
    [Benchmark(Description = "BPT  Get seq")]
    public int BPT_Get_Sequential()
    {
        int sum = 0;
        foreach (var k in SeqKeys)
        {
            BPT.TryGet(k, out var v);
            sum += v;
        }
        return sum;
    }

    [Benchmark(Description = "SD   Get seq")]
    public int SD_Get_Sequential()
    {
        int sum = 0;
        foreach (var k in SeqKeys)
        {
            SD.TryGetValue(k, out var v);
            sum += v;
        }
        return sum;
    }
}

// ═════════════════════════════════════════════════════════════
//  3. Delete
//
//  注意：刪完就沒了，所以用 [IterationSetup] 每次 iteration
//        前重建樹。這會讓 overhead 稍高，但沒有其他辦法。
//        BenchmarkDotNet 會把 IterationSetup 時間排除在外。
// ═════════════════════════════════════════════════════════════

[SimpleJob]
[MemoryDiagnoser]
public class DeleteBenchmark : BenchmarkBase
{
    public override void GlobalSetup()
    {
        base.GlobalSetup();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        BuildTrees();    // 每次 iteration 前重建，確保有東西可刪
    }

    [Benchmark(Baseline = true, Description = "BPT  Delete rand")]
    public void BPT_Delete_Random()
    {
        foreach (var k in RandKeys) BPT.Delete(k);
    }

    [Benchmark(Description = "SD   Delete rand")]
    public void SD_Delete_Random()
    {
        foreach (var k in RandKeys) SD.Remove(k);
    }
}

// ═════════════════════════════════════════════════════════════
//  4. Range scan
//
//  B+Tree 最大的結構優勢：linked list 讓 range 是 O(log n + k)
//  SortedDictionary 沒有 native range，要從頭掃或用 LINQ Skip/Take
//
//  測三種 range 寬度：
//    1%  → 小範圍查詢（典型 OLTP）
//    10% → 中範圍
//    50% → 大範圍（接近全表 scan）
// ═════════════════════════════════════════════════════════════

[SimpleJob]
[MemoryDiagnoser]
public class RangeBenchmark : BenchmarkBase
{
    // 0.01 = 1%, 0.10 = 10%, 0.50 = 50%
    [Params(0.01, 0.10, 0.50)]
    public double Ratio;

    private int _lo, _hi;

    public override void GlobalSetup()
    {
        base.GlobalSetup();
        BuildTrees();

        _lo = 0;
        _hi = (int)(N * Ratio) - 1;
    }

    // ── B+Tree Range：走 linked list ─────────────────────
    [Benchmark(Baseline = true, Description = "BPT  Range")]
    public long BPT_Range()
    {
        long sum = 0;
        foreach (var kv in BPT.Range(_lo, _hi))
            sum += kv.Value;
        return sum;
    }

    // ── SortedDictionary：沒有 native range，
    //    慣用做法是 foreach + 手動 break
    //    （和你在實際程式碼裡寫法一致，不用 LINQ 以免被 overhead 干擾）
    [Benchmark(Description = "SD   Range (foreach+break)")]
    public long SD_Range_ForEachBreak()
    {
        long sum = 0;
        foreach (var (k, v) in SD)
        {
            if (k > _hi) break;
            if (k >= _lo) sum += v;
        }
        return sum;
    }

    // ── SortedDictionary + LINQ（展示常見但較慢的寫法）
    [Benchmark(Description = "SD   Range (LINQ)")]
    public long SD_Range_Linq()
    {
        return SD.Where(kv => kv.Key >= _lo && kv.Key <= _hi)
                 .Sum(kv => (long)kv.Value);
    }
}