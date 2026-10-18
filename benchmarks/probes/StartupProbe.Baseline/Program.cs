// Baseline: the same data and checksum as the other probes, but no mapper (BENCHMARK_SPEC.md, 8.2).
using Comparison.Models.Data;

var order = new TestDataGenerator().CreateOrder();
Console.WriteLine(OrderChecksum.Of(order));
