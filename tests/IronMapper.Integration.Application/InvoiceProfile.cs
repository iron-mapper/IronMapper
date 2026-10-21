using IronMapper.Configuration;
using IronMapper.Integration.Domain;

namespace IronMapper.Integration.Application;

public class InvoiceDto
{
    public int Id { get; set; }
    public string Customer { get; set; } = string.Empty;
    public List<InvoiceLineDto> Lines { get; set; } = new();
}

public class InvoiceLineDto
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class InvoiceProfile : MappingProfile
{
    public InvoiceProfile()
    {
        CreateMap<InvoiceLine, InvoiceLineDto>();
        CreateMap<Invoice, InvoiceDto>();
    }
}
