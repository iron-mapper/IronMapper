namespace IronMapper.Integration.Domain;

public class Invoice
{
    public int Id { get; set; }
    public string Customer { get; set; } = string.Empty;
    public List<InvoiceLine> Lines { get; set; } = new();
}

public class InvoiceLine
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
