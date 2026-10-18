// MapperConfiguration (license key from AUTOMAPPER_LICENSE_KEY or ~/.automapper/license.txt) + CreateMapper + first Map.
using Comparison.Mappers.AutoMapper;
using Comparison.Models.Data;
using Comparison.Models.Nested;

var order = new TestDataGenerator().CreateOrder();
var mapper = AutoMapperSetup.CreateMapper();
var dto = mapper.Map<OrderEntity, OrderDto>(order);
Console.WriteLine(OrderChecksum.Of(dto));
