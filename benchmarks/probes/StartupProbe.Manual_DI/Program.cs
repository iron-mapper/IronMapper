// Hand-written mapping behind the same DI container as the IronMapper_IMapper probe (ServiceCollection + provider).
using Comparison.Mappers.Manual;
using Comparison.Models.Data;

var order = new TestDataGenerator().CreateOrder();
var dto = ManualDiSetup.CreateMapper().Map(order);
Console.WriteLine(OrderChecksum.Of(dto));
