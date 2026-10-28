using IronMapper.Attributes;
using IronMapper.Extensions.DI;
using IronMapper.Integration.Tests.InitInPlace;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests.InitInPlace
{
    // Settable and init-only properties, an init-only nested object (issue #58). IM0025 is suppressed: this project builds
    // with warnings as errors.
#pragma warning disable IM0025
    [MapTo(typeof(ShipmentDto))]
#pragma warning restore IM0025
    public class Shipment
    {
        public string Status { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public Address? Address { get; set; }
    }

    public class ShipmentDto
    {
        public string Status { get; set; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public AddressDto? Address { get; init; }
    }

    // The public in-place overload turned off: IMapper uses the private MapInto of the registration, with the same body.
    [MapTo(typeof(ParcelDto), GenerateInPlace = false)]
    public class Parcel
    {
        public string Status { get; set; } = string.Empty;
        public Address? Address { get; set; }
    }

    public class ParcelDto
    {
        public string Status { get; set; } = string.Empty;
        public AddressDto? Address { get; init; }
    }

    [MapTo(typeof(AddressDto))]
    public class Address
    {
        public string City { get; set; } = string.Empty;
    }

    public class AddressDto
    {
        public string City { get; set; } = string.Empty;
    }
}

namespace IronMapper.Integration.Tests
{
    /// <summary>
    /// <see cref="IMapper.Map{TSource, TDestination}(TSource, TDestination)"/> with <c>init</c>-only properties: the same
    /// behavior as the generated in-place overload — settable properties updated, <c>init</c>-only ones kept, an existing
    /// <c>init</c>-only nested object updated in place (also when the public overload is turned off).
    /// </summary>
    public class InitInPlaceMapperTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(InitInPlaceMapperTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        [Fact]
        public void MapInPlace_InitKept_ExistingNestedUpdatedInPlace()
        {
            var mapper = CreateMapper();
            var address = new AddressDto { City = "old" };
            var existing = new ShipmentDto { Status = "old", Code = "old", Address = address };

            mapper.Map(new Shipment { Status = "new", Code = "new", Address = new Address { City = "new" } }, existing);

            Assert.Equal("new", existing.Status);
            Assert.Equal("old", existing.Code);
            Assert.Same(address, existing.Address);
            Assert.Equal("new", address.City);
        }

        [Fact]
        public void MapInPlace_PublicOverloadTurnedOff_SameBehavior()
        {
            var mapper = CreateMapper();
            var address = new AddressDto { City = "old" };
            var existing = new ParcelDto { Address = address };
            var empty = new ParcelDto();

            mapper.Map(new Parcel { Status = "s", Address = new Address { City = "new" } }, existing);
            mapper.Map(new Parcel { Status = "s", Address = new Address { City = "x" } }, empty);

            Assert.Same(address, existing.Address);
            Assert.Equal("new", address.City);
            Assert.Equal("s", empty.Status);
            Assert.Null(empty.Address);
        }
    }
}
