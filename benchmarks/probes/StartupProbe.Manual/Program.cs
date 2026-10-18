using Comparison.Mappers.Manual;
using Comparison.Models.Data;

var order = new TestDataGenerator().CreateOrder();
var dto = ManualMappings.MapNested(order);
Console.WriteLine(OrderChecksum.Of(dto));
