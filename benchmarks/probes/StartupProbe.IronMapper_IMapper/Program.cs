// DI path: ServiceProvider + IMapper (reflection + MethodInfo.Invoke inside).
using Comparison.Mappers.IronMapper;
using Comparison.Models.Data;
using Comparison.Models.Nested;

var order = new TestDataGenerator().CreateOrder();
var mapper = IronMapperDiSetup.CreateMapper();
var dto = mapper.Map<OrderEntity, OrderDto>(order);
Console.WriteLine(OrderChecksum.Of(dto));
