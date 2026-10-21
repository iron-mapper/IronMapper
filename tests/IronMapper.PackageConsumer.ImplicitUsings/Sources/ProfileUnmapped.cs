// A profile mapping with a destination property that nothing maps (OrderDto.Missing). IM0001 is reported for it only
// with <IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>, at the CreateMap call below
// (eng/verify-package.ps1 checks the file and line in the build output, and .editorconfig severities).
using IronMapper.Configuration;

var dto = new Order { Id = 7 }.MapToOrderDto();
if (dto.Id != 7 || dto.Missing != string.Empty)
{
    Console.Error.WriteLine("FAIL: mapped values");
    return 1;
}

Console.WriteLine("ProfileUnmapped: OK");
return 0;

public class Order
{
    public int Id { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public string Missing { get; set; } = string.Empty;
}

public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<Order, OrderDto>();
    }
}
