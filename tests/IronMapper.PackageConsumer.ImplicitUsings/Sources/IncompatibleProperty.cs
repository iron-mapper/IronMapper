// An attribute mapping whose destination property cannot be assigned from its source (int? -> int). eng/verify-package.ps1
// checks that the build fails with IM0002 at OrderDto.Count below, and with no error inside a generated file (up to 2.0:
// CS0266 in Order_MapTo_OrderDto.g.cs).
using IronMapper.Attributes;

var dto = new Order { Id = 7, Count = 2 }.MapToOrderDto();
Console.WriteLine(dto.Id == 7 ? "IncompatibleProperty: OK" : "FAIL");
return dto.Id == 7 ? 0 : 1;

[MapTo(typeof(OrderDto))]
public class Order
{
    public int Id { get; set; }
    public int? Count { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public int Count { get; set; }
}
