// Dedicated TypeAdapterConfig, Compile() of all maps (same setup as the benchmarks), then the first Adapt.
using Comparison.Mappers.Mapster;
using Comparison.Models.Data;
using Comparison.Models.Nested;
using Mapster;

var order = new TestDataGenerator().CreateOrder();
var config = MapsterSetup.CreateConfig();
var dto = order.Adapt<OrderEntity, OrderDto>(config);
Console.WriteLine(OrderChecksum.Of(dto));
