// Generated extension method; there is nothing to configure at runtime.
using Comparison.Models.Data;
using IronMapper.Generated;

var order = new TestDataGenerator().CreateOrder();
var dto = order.MapToOrderDto();
Console.WriteLine(OrderChecksum.Of(dto));
