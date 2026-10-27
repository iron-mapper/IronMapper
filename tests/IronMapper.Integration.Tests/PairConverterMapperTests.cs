using System;
using System.Collections.Generic;
using IronMapper.Configuration;
using IronMapper.Extensions.DI;
using IronMapper.Integration.Tests.PairConverters;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests.PairConverters
{
    public readonly record struct CustomerId
    {
        private CustomerId(Guid value) => Value = value;
        public Guid Value { get; }
        public static CustomerId From(Guid value) => value == Guid.Empty ? throw new ArgumentException("The id is empty.", nameof(value)) : new(value);
    }

    public class InvoiceRow { public Guid CustomerId { get; set; } public Guid? PayerId { get; set; } public decimal Total { get; set; } }
    public record Invoice(CustomerId CustomerId, CustomerId? PayerId, decimal Total);

    public class InvoiceProfile : MappingProfile
    {
        public InvoiceProfile()
        {
            CreateMap<Guid, CustomerId>().ConvertUsing(CustomerId.From);
            CreateMap<CustomerId, Guid>().ConvertUsing(v => v.Value);
            CreateMap<InvoiceRow, Invoice>();
        }
    }
}

namespace IronMapper.Integration.Tests
{
    /// <summary>Converters through the mappings of type pairs, through <see cref="IMapper"/> (the generated registry).</summary>
    public class PairConverterMapperTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(PairConverterMapperTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        [Fact]
        public void Map_ValueObjects_PairMappingsThemselves_Collections_ExceptionAsItIs()
        {
            var mapper = CreateMapper();
            var row = new InvoiceRow { CustomerId = Guid.NewGuid(), PayerId = null, Total = 10m };

            var invoice = mapper.Map<InvoiceRow, Invoice>(row);

            Assert.Equal(new Invoice(CustomerId.From(row.CustomerId), null, 10m), invoice);
            // The pair mappings are mappings: IMapper maps the pair itself, and collections of it.
            Assert.Equal(row.CustomerId, mapper.Map<CustomerId, Guid>(invoice.CustomerId));
            Assert.Equal(invoice.CustomerId, mapper.Map<Guid, CustomerId>(row.CustomerId));
            Assert.Equal(2, mapper.Map<List<Invoice>>(new[] { row, row }).Count);
            // IMapper does not wrap exceptions (2.0.0): the converter's exception arrives as it is.
            Assert.Throws<ArgumentException>(() => mapper.Map<InvoiceRow, Invoice>(new InvoiceRow { CustomerId = Guid.Empty }));
        }
    }
}
