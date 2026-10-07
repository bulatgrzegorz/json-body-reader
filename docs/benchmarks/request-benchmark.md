```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.2 (25F84) [Darwin 25.5.0]
Apple M2, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host] : .NET 10.0.3 (10.0.3, 10.0.326.7603), Arm64 RyuJIT armv8.0-a

Job=ShortRun  Toolchain=InProcessEmitToolchain  IterationCount=3
LaunchCount=1  WarmupCount=3

```
| Method                        | Position | ChunkSize | PaddingLength | Mean       | Error        | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------- |---------- |-------------- |-----------:|-------------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| **FullBodyReadAndDeserialize**    | **First**    | **64**        | **16**            |   **361.6 ns** |     **11.43 ns** |   **0.63 ns** |  **1.00** |    **0.00** |  **0.1106** |      **-** |     **928 B** |        **1.00** |
| JsonSerializerAsync           | First    | 64        | 16            |   380.7 ns |      3.88 ns |   0.21 ns |  1.05 |    0.00 |  0.0582 |      - |     488 B |        0.53 |
| JsonSerializerDtoAsync        | First    | 64        | 16            |   218.6 ns |      7.15 ns |   0.39 ns |  0.60 |    0.00 |  0.0210 |      - |     176 B |        0.19 |
| IncrementalWithFullValidation | First    | 64        | 16            |   317.5 ns |     27.20 ns |   1.49 ns |  0.88 |    0.00 |  0.0486 |      - |     408 B |        0.44 |
| IncrementalEarlyExtraction    | First    | 64        | 16            |   179.1 ns |     52.23 ns |   2.86 ns |  0.50 |    0.01 |  0.0486 |      - |     408 B |        0.44 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **First**    | **64**        | **16384**         | **8,482.2 ns** |  **2,863.69 ns** | **156.97 ns** |  **1.00** |    **0.02** | **11.7950** | **1.4648** |   **98856 B** |       **1.000** |
| JsonSerializerAsync           | First    | 64        | 16384         | 4,948.3 ns |    450.70 ns |  24.70 ns |  0.58 |    0.01 |  2.0065 |      - |   16856 B |       0.171 |
| JsonSerializerDtoAsync        | First    | 64        | 16384         | 3,341.5 ns |     94.62 ns |   5.19 ns |  0.39 |    0.01 |  0.0191 |      - |     176 B |       0.002 |
| IncrementalWithFullValidation | First    | 64        | 16384         | 5,036.4 ns |    100.94 ns |   5.53 ns |  0.59 |    0.01 |  0.0763 |      - |     696 B |       0.007 |
| IncrementalEarlyExtraction    | First    | 64        | 16384         |   187.2 ns |    137.34 ns |   7.53 ns |  0.02 |    0.00 |  0.0486 |      - |     408 B |       0.004 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **First**    | **4096**      | **16**            |   **371.8 ns** |    **416.26 ns** |  **22.82 ns** |  **1.00** |    **0.07** |  **0.1106** |      **-** |     **928 B** |        **1.00** |
| JsonSerializerAsync           | First    | 4096      | 16            |   412.6 ns |     96.11 ns |   5.27 ns |  1.11 |    0.06 |  0.0582 |      - |     488 B |        0.53 |
| JsonSerializerDtoAsync        | First    | 4096      | 16            |   258.7 ns |    742.13 ns |  40.68 ns |  0.70 |    0.10 |  0.0210 |      - |     176 B |        0.19 |
| IncrementalWithFullValidation | First    | 4096      | 16            |   316.2 ns |     25.99 ns |   1.42 ns |  0.85 |    0.04 |  0.0486 |      - |     408 B |        0.44 |
| IncrementalEarlyExtraction    | First    | 4096      | 16            |   183.0 ns |     41.13 ns |   2.25 ns |  0.49 |    0.03 |  0.0486 |      - |     408 B |        0.44 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **First**    | **4096**      | **16384**         | **5,604.9 ns** |  **1,050.24 ns** |  **57.57 ns** |  **1.00** |    **0.01** | **11.3144** | **1.6098** |   **94920 B** |       **1.000** |
| JsonSerializerAsync           | First    | 4096      | 16384         | 3,929.9 ns |    764.30 ns |  41.89 ns |  0.70 |    0.01 |  2.0065 |      - |   16856 B |       0.178 |
| JsonSerializerDtoAsync        | First    | 4096      | 16384         | 2,332.1 ns |    358.62 ns |  19.66 ns |  0.42 |    0.00 |  0.0191 |      - |     176 B |       0.002 |
| IncrementalWithFullValidation | First    | 4096      | 16384         | 2,686.3 ns |    585.91 ns |  32.12 ns |  0.48 |    0.01 |  0.0687 |      - |     600 B |       0.006 |
| IncrementalEarlyExtraction    | First    | 4096      | 16384         |   261.9 ns |     83.87 ns |   4.60 ns |  0.05 |    0.00 |  0.0486 |      - |     408 B |       0.004 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **Last**     | **64**        | **16**            |   **366.0 ns** |     **24.23 ns** |   **1.33 ns** |  **1.00** |    **0.00** |  **0.1106** |      **-** |     **928 B** |        **1.00** |
| JsonSerializerAsync           | Last     | 64        | 16            |   389.4 ns |     32.80 ns |   1.80 ns |  1.06 |    0.01 |  0.0582 |      - |     488 B |        0.53 |
| JsonSerializerDtoAsync        | Last     | 64        | 16            |   235.6 ns |     78.18 ns |   4.29 ns |  0.64 |    0.01 |  0.0210 |      - |     176 B |        0.19 |
| IncrementalWithFullValidation | Last     | 64        | 16            |   320.3 ns |     26.19 ns |   1.44 ns |  0.88 |    0.00 |  0.0486 |      - |     408 B |        0.44 |
| IncrementalEarlyExtraction    | Last     | 64        | 16            |   292.4 ns |    114.17 ns |   6.26 ns |  0.80 |    0.02 |  0.0486 |      - |     408 B |        0.44 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **Last**     | **64**        | **16384**         | **8,462.6 ns** |  **1,506.38 ns** |  **82.57 ns** |  **1.00** |    **0.01** | **11.7950** | **1.4648** |   **98856 B** |       **1.000** |
| JsonSerializerAsync           | Last     | 64        | 16384         | 4,920.1 ns |    904.56 ns |  49.58 ns |  0.58 |    0.01 |  2.0065 |      - |   16856 B |       0.171 |
| JsonSerializerDtoAsync        | Last     | 64        | 16384         | 3,379.7 ns |    821.94 ns |  45.05 ns |  0.40 |    0.01 |  0.0191 |      - |     176 B |       0.002 |
| IncrementalWithFullValidation | Last     | 64        | 16384         | 5,117.1 ns |  1,153.01 ns |  63.20 ns |  0.60 |    0.01 |  0.0763 |      - |     696 B |       0.007 |
| IncrementalEarlyExtraction    | Last     | 64        | 16384         | 5,022.5 ns |    586.02 ns |  32.12 ns |  0.59 |    0.01 |  0.0763 |      - |     696 B |       0.007 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **Last**     | **4096**      | **16**            |   **358.2 ns** |     **58.36 ns** |   **3.20 ns** |  **1.00** |    **0.01** |  **0.1106** |      **-** |     **928 B** |        **1.00** |
| JsonSerializerAsync           | Last     | 4096      | 16            |   388.5 ns |    133.04 ns |   7.29 ns |  1.08 |    0.02 |  0.0582 |      - |     488 B |        0.53 |
| JsonSerializerDtoAsync        | Last     | 4096      | 16            |   228.0 ns |     31.11 ns |   1.71 ns |  0.64 |    0.01 |  0.0210 |      - |     176 B |        0.19 |
| IncrementalWithFullValidation | Last     | 4096      | 16            |   335.6 ns |     69.27 ns |   3.80 ns |  0.94 |    0.01 |  0.0486 |      - |     408 B |        0.44 |
| IncrementalEarlyExtraction    | Last     | 4096      | 16            |   218.3 ns |     25.94 ns |   1.42 ns |  0.61 |    0.01 |  0.0486 |      - |     408 B |        0.44 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **Last**     | **4096**      | **16384**         | **6,675.8 ns** | **14,860.06 ns** | **814.53 ns** |  **1.01** |    **0.15** | **11.3144** | **1.6098** |   **94920 B** |       **1.000** |
| JsonSerializerAsync           | Last     | 4096      | 16384         | 4,723.0 ns |  1,141.72 ns |  62.58 ns |  0.71 |    0.07 |  2.0065 |      - |   16856 B |       0.178 |
| JsonSerializerDtoAsync        | Last     | 4096      | 16384         | 2,848.8 ns |    392.67 ns |  21.52 ns |  0.43 |    0.04 |  0.0191 |      - |     176 B |       0.002 |
| IncrementalWithFullValidation | Last     | 4096      | 16384         | 3,077.8 ns |    727.59 ns |  39.88 ns |  0.47 |    0.05 |  0.0687 |      - |     600 B |       0.006 |
| IncrementalEarlyExtraction    | Last     | 4096      | 16384         | 2,983.1 ns |  1,180.48 ns |  64.71 ns |  0.45 |    0.05 |  0.0687 |      - |     600 B |       0.006 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **Missing**  | **64**        | **16**            |   **271.9 ns** |     **62.80 ns** |   **3.44 ns** |  **1.00** |    **0.02** |  **0.0763** |      **-** |     **640 B** |        **1.00** |
| JsonSerializerAsync           | Missing  | 64        | 16            |   319.2 ns |    356.28 ns |  19.53 ns |  1.17 |    0.06 |  0.0286 |      - |     240 B |        0.38 |
| JsonSerializerDtoAsync        | Missing  | 64        | 16            |   166.4 ns |      2.88 ns |   0.16 ns |  0.61 |    0.01 |  0.0076 |      - |      64 B |        0.10 |
| IncrementalWithFullValidation | Missing  | 64        | 16            |   253.5 ns |      3.39 ns |   0.19 ns |  0.93 |    0.01 |  0.0439 |      - |     368 B |        0.57 |
| IncrementalEarlyExtraction    | Missing  | 64        | 16            |   252.8 ns |     12.54 ns |   0.69 ns |  0.93 |    0.01 |  0.0439 |      - |     368 B |        0.57 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **Missing**  | **64**        | **16384**         | **8,269.6 ns** |  **1,136.33 ns** |  **62.29 ns** |  **1.00** |    **0.01** | **11.7645** | **2.3499** |   **98568 B** |       **1.000** |
| JsonSerializerAsync           | Missing  | 64        | 16384         | 4,834.2 ns |    164.46 ns |   9.01 ns |  0.58 |    0.00 |  1.9760 |      - |   16608 B |       0.168 |
| JsonSerializerDtoAsync        | Missing  | 64        | 16384         | 3,290.8 ns |     89.43 ns |   4.90 ns |  0.40 |    0.00 |  0.0076 |      - |      64 B |       0.001 |
| IncrementalWithFullValidation | Missing  | 64        | 16384         | 4,973.0 ns |    288.46 ns |  15.81 ns |  0.60 |    0.00 |  0.0763 |      - |     656 B |       0.007 |
| IncrementalEarlyExtraction    | Missing  | 64        | 16384         | 4,985.0 ns |    458.26 ns |  25.12 ns |  0.60 |    0.00 |  0.0763 |      - |     656 B |       0.007 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **Missing**  | **4096**      | **16**            |   **250.2 ns** |      **3.37 ns** |   **0.18 ns** |  **1.00** |    **0.00** |  **0.0763** |      **-** |     **640 B** |        **1.00** |
| JsonSerializerAsync           | Missing  | 4096      | 16            |   286.4 ns |      1.24 ns |   0.07 ns |  1.14 |    0.00 |  0.0286 |      - |     240 B |        0.38 |
| JsonSerializerDtoAsync        | Missing  | 4096      | 16            |   166.3 ns |      4.15 ns |   0.23 ns |  0.66 |    0.00 |  0.0076 |      - |      64 B |        0.10 |
| IncrementalWithFullValidation | Missing  | 4096      | 16            |   255.0 ns |      9.58 ns |   0.53 ns |  1.02 |    0.00 |  0.0439 |      - |     368 B |        0.57 |
| IncrementalEarlyExtraction    | Missing  | 4096      | 16            |   253.3 ns |     17.38 ns |   0.95 ns |  1.01 |    0.00 |  0.0439 |      - |     368 B |        0.57 |
|                               |          |           |               |            |              |           |       |         |         |        |           |             |
| **FullBodyReadAndDeserialize**    | **Missing**  | **4096**      | **16384**         | **5,212.6 ns** |    **347.53 ns** |  **19.05 ns** |  **1.00** |    **0.00** | **11.2762** | **1.8768** |   **94632 B** |       **1.000** |
| JsonSerializerAsync           | Missing  | 4096      | 16384         | 3,761.3 ns |    135.80 ns |   7.44 ns |  0.72 |    0.00 |  1.9798 |      - |   16608 B |       0.176 |
| JsonSerializerDtoAsync        | Missing  | 4096      | 16384         | 2,241.6 ns |     82.73 ns |   4.53 ns |  0.43 |    0.00 |  0.0076 |      - |      64 B |       0.001 |
| IncrementalWithFullValidation | Missing  | 4096      | 16384         | 2,581.8 ns |     62.19 ns |   3.41 ns |  0.50 |    0.00 |  0.0648 |      - |     560 B |       0.006 |
| IncrementalEarlyExtraction    | Missing  | 4096      | 16384         | 2,568.2 ns |     23.92 ns |   1.31 ns |  0.49 |    0.00 |  0.0648 |      - |     560 B |       0.006 |
