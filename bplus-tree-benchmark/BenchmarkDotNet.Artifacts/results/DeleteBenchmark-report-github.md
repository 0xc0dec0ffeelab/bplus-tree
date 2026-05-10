```

BenchmarkDotNet v0.15.2, Windows 11 (10.0.26200.8246)
11th Gen Intel Core i7-11800H 2.30GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-CNUJVU : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

InvocationCount=1  UnrollFactor=1  

```
| Method             | N       | Mean       | Error     | StdDev     | Median     | Ratio | RatioSD | Rank | Allocated | Alloc Ratio |
|------------------- |-------- |-----------:|----------:|-----------:|-----------:|------:|--------:|-----:|----------:|------------:|
| **&#39;BPT  Delete rand&#39;** | **10000**   |   **1.869 ms** | **0.3670 ms** |  **1.0821 ms** |   **1.061 ms** |  **1.32** |    **1.02** |    **2** |         **-** |          **NA** |
| &#39;SD   Delete rand&#39; | 10000   |   1.866 ms | 0.0286 ms |  0.0239 ms |   1.862 ms |  1.32 |    0.58 |    1 |         - |          NA |
|                    |         |            |           |            |            |       |         |      |           |             |
| **&#39;BPT  Delete rand&#39;** | **100000**  |  **11.511 ms** | **0.2242 ms** |  **0.3684 ms** |  **11.395 ms** |  **1.00** |    **0.04** |    **1** |         **-** |          **NA** |
| &#39;SD   Delete rand&#39; | 100000  |  19.802 ms | 0.2949 ms |  0.2462 ms |  19.744 ms |  1.72 |    0.06 |    2 |         - |          NA |
|                    |         |            |           |            |            |       |         |      |           |             |
| **&#39;BPT  Delete rand&#39;** | **1000000** | **174.692 ms** | **3.2680 ms** |  **3.3560 ms** | **173.401 ms** |  **1.00** |    **0.03** |    **1** |         **-** |          **NA** |
| &#39;SD   Delete rand&#39; | 1000000 | 492.445 ms | 9.7725 ms | 24.5173 ms | 491.623 ms |  2.82 |    0.15 |    2 |         - |          NA |
