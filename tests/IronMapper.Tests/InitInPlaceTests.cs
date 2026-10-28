using IronMapper.Attributes;
using IronMapper.Generated;
using IronMapper.Tests.InitInPlace;
using Xunit;

namespace IronMapper.Tests.InitInPlace
{
    // IM0025 (a warning, an error with TreatWarningsAsErrors in this project) is reported at the attribute: the in-place
    // overload skips Code and updates Customer only when it exists. Suppressed here, as a project would.
#pragma warning disable IM0025
    [MapTo(typeof(InvoiceDto))]
#pragma warning restore IM0025
    public class Invoice
    {
        public string Number { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public Payer? Payer { get; set; }
    }

    public class InvoiceDto
    {
        public string Number { get; set; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public PayerDto? Payer { get; init; }
    }

    [MapTo(typeof(PayerDto))]
    public class Payer
    {
        public string Name { get; set; } = string.Empty;
    }

    public class PayerDto
    {
        public string Name { get; set; } = string.Empty;
    }
}

namespace IronMapper.Tests
{
    /// <summary>
    /// <c>init</c>-only properties in the in-place overload (issue #58), built with this project's warnings as errors and
    /// IM0025 suppressed with <c>#pragma</c>: settable properties are updated, <c>init</c>-only ones keep their values, an
    /// existing <c>init</c>-only nested object is updated in place.
    /// </summary>
    public class InitInPlaceTests
    {
        [Fact]
        public void InPlace_SettableUpdated_InitKept_ExistingNestedUpdatedInPlace()
        {
            var payer = new PayerDto { Name = "old" };
            var existing = new InvoiceDto { Number = "1", Code = "old", Payer = payer };

            new Invoice { Number = "2", Code = "new", Payer = new Payer { Name = "new" } }.MapToInvoiceDto(existing);

            Assert.Equal("2", existing.Number);
            Assert.Equal("old", existing.Code);
            Assert.Same(payer, existing.Payer);
            Assert.Equal("new", payer.Name);
        }

        [Fact]
        public void InPlace_NoNestedObject_StaysNull()
        {
            var existing = new InvoiceDto();

            new Invoice { Number = "2", Payer = new Payer { Name = "new" } }.MapToInvoiceDto(existing);

            Assert.Equal("2", existing.Number);
            Assert.Null(existing.Payer);
        }

        [Fact]
        public void NewObject_GetsEveryProperty()
        {
            var dto = new Invoice { Number = "3", Code = "c", Payer = new Payer { Name = "p" } }.MapToInvoiceDto();

            Assert.Equal("c", dto.Code);
            Assert.Equal("p", dto.Payer!.Name);
        }
    }
}
