```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M2, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host] : .NET 10.0.3 (10.0.3, 10.0.326.7603), Arm64 RyuJIT armv8.0-a

Job=ShortRun  Toolchain=InProcessEmitToolchain  IterationCount=3
LaunchCount=1  WarmupCount=3

```
| Method         | PropertyCount | Missing | Mean        | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------- |-------------- |-------- |------------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **GetStringNames** | **1**             | **False**   |    **65.47 ns** |  **18.728 ns** |  **1.027 ns** |  **1.00** |    **0.02** | **0.0181** |     **152 B** |        **1.00** |
| NameEquals     | 1             | False   |    39.27 ns |   8.052 ns |  0.441 ns |  0.60 |    0.01 | 0.0048 |      40 B |        0.26 |
| SequenceReader | 1             | False   |   100.14 ns |  15.236 ns |  0.835 ns |  1.53 |    0.02 | 0.0048 |      40 B |        0.26 |
| SpanReader     | 1             | False   |   100.74 ns |  88.923 ns |  4.874 ns |  1.54 |    0.07 | 0.0048 |      40 B |        0.26 |
|                |               |         |             |            |           |       |         |        |           |             |
| **GetStringNames** | **1**             | **True**    |    **27.49 ns** |   **2.031 ns** |  **0.111 ns** |  **1.00** |    **0.00** | **0.0048** |      **40 B** |        **1.00** |
| NameEquals     | 1             | True    |    18.03 ns |   3.441 ns |  0.189 ns |  0.66 |    0.01 |      - |         - |        0.00 |
| SequenceReader | 1             | True    |    72.51 ns |  15.875 ns |  0.870 ns |  2.64 |    0.03 |      - |         - |        0.00 |
| SpanReader     | 1             | True    |    64.86 ns |  12.668 ns |  0.694 ns |  2.36 |    0.02 |      - |         - |        0.00 |
|                |               |         |             |            |           |       |         |        |           |             |
| **GetStringNames** | **64**            | **False**   | **1,485.42 ns** | **377.852 ns** | **20.711 ns** |  **1.00** |    **0.02** | **0.3185** |    **2672 B** |        **1.00** |
| NameEquals     | 64            | False   |   796.52 ns | 230.243 ns | 12.620 ns |  0.54 |    0.01 | 0.0048 |      40 B |        0.01 |
| SequenceReader | 64            | False   | 1,289.56 ns | 301.982 ns | 16.553 ns |  0.87 |    0.01 | 0.0038 |      40 B |        0.01 |
| SpanReader     | 64            | False   | 1,287.24 ns | 262.750 ns | 14.402 ns |  0.87 |    0.01 | 0.0038 |      40 B |        0.01 |
|                |               |         |             |            |           |       |         |        |           |             |
| **GetStringNames** | **64**            | **True**    | **1,363.34 ns** | **248.701 ns** | **13.632 ns** |  **1.00** |    **0.01** | **0.3052** |    **2560 B** |        **1.00** |
| NameEquals     | 64            | True    |   769.71 ns | 112.860 ns |  6.186 ns |  0.56 |    0.01 |      - |         - |        0.00 |
| SequenceReader | 64            | True    | 1,252.10 ns | 109.689 ns |  6.012 ns |  0.92 |    0.01 |      - |         - |        0.00 |
| SpanReader     | 64            | True    | 1,241.84 ns | 263.628 ns | 14.450 ns |  0.91 |    0.01 |      - |         - |        0.00 |
