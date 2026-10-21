// DI path: ServiceProvider + IMapper (up to 1.1.x reflection + MethodInfo.Invoke inside; from 1.2.0 the generated registry).
using Comparison.Mappers.IronMapper;
using Comparison.Models.Data;
using Comparison.Models.Nested;

var order = new TestDataGenerator().CreateOrder();
var mapper = IronMapperDiSetup.CreateMapper();
var dto = mapper.Map<OrderEntity, OrderDto>(order);
Console.WriteLine(OrderChecksum.Of(dto));
