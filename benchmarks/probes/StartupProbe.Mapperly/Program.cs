// Generated static mapper; there is nothing to configure at runtime.
using Comparison.Mappers.Mapperly;
using Comparison.Models.Data;

var order = new TestDataGenerator().CreateOrder();
var dto = ComparisonMapperlyMapper.MapNested(order);
Console.WriteLine(OrderChecksum.Of(dto));
