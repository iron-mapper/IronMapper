# Quick A/B: IronMapper 2.0.0 (nuget.org) vs local sources

- Run: 20261024-113550, Intel(R) Core(TM) Ultra 7 155H (16 cores, 22 threads) · 15 GB RAM · Microsoft Windows 10.0.26300 (win-x64) · .NET SDK 10.0.402 · .NET runtime 10.0.13
- Local sources: main, 253e90c
- 2.0.0: nuget.org, tag v2.0.0 (2510efa)
- Job: Dry (one iteration; numbers mean nothing); filter: *.FlatBenchmark.*
- Ratio: Mean / Mean of `Manual` of the same run (and category). Δ: local against published.
- **Dry run**: one iteration, the numbers mean nothing.

## net10.0

Published: ok. Local: ok.

| Benchmark | 2.0.0 Mean | 2.0.0 Ratio | Local Mean | Local Ratio | Δ Ratio | 2.0.0 Allocated | Local Allocated | Δ Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| FlatBenchmark.AutoMapper | 4323600.0 ns | 3.15 | 6262200.0 ns | 3.61 | +14.9 % | 512 B | 256 B | -256 B |
| FlatBenchmark.IronMapper | 1526200.0 ns | 1.11 | 2286400.0 ns | 1.32 | +18.8 % | 312 B | 168 B | -144 B |
| FlatBenchmark.IronMapper_IMapper | 1387800.0 ns | 1.01 | 1256100.0 ns | 0.72 | -28.2 % | 168 B | 168 B | 0 B |
| FlatBenchmark.Manual | 1374100.0 ns | 1.00 | 1732800.0 ns | 1.00 | 0.0 % | 168 B | 168 B | 0 B |
| FlatBenchmark.Mapperly | 1478600.0 ns | 1.08 | 1099000.0 ns | 0.63 | -41.1 % | 168 B | 168 B | 0 B |
| FlatBenchmark.Mapster | 569500.0 ns | 0.41 | 763200.0 ns | 0.44 | +6.3 % | 168 B | 168 B | 0 B |
