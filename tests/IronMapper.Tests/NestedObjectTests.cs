using System.Collections.Generic;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Tests.Nested;
using Xunit;

namespace IronMapper.Tests.Nested
{
    [MapTo(typeof(ShipmentDto))]
    public class Shipment
    {
        public int Id { get; set; }
        public Address Origin { get; set; } = new();
        public Address? Destination { get; set; }
        public List<Parcel> Parcels { get; set; } = new();
    }

    public class ShipmentDto
    {
        public int Id { get; set; }
        public AddressDto Origin { get; set; } = new();
        public AddressDto? Destination { get; set; } = new();
        public List<ParcelDto> Parcels { get; set; } = new();
    }

    [MapTo(typeof(AddressDto))]
    public class Address
    {
        public string City { get; set; } = string.Empty;
        public GeoPoint Point { get; set; }
    }

    public class AddressDto
    {
        public string City { get; set; } = string.Empty;
        public GeoPointDto Point { get; set; } = new();
    }

    public struct GeoPoint
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public record GeoPointDto
    {
        public double Lat { get; init; }
        public double Lon { get; init; }
    }

    public class Parcel
    {
        public string Label { get; set; } = string.Empty;
        public Address ReturnTo { get; set; } = new();
    }

    public class ParcelDto
    {
        public string Label { get; set; } = string.Empty;
        public AddressDto ReturnTo { get; set; } = new();
    }

    // DTO → entity, as in an EF update: the existing (tracked) nested objects must be kept.
    [MapTo(typeof(InvoiceEntity))]
    public class InvoiceDto
    {
        public int Number { get; set; }
        public PartyDto Buyer { get; set; } = null!;
        public PartyDto? Seller { get; set; }
    }

    public class InvoiceEntity
    {
        public int Number { get; set; }
        public PartyEntity Buyer { get; set; } = null!;
        public PartyEntity? Seller { get; set; }
    }

    [MapTo(typeof(PartyEntity))]
    public class PartyDto
    {
        public string Name { get; set; } = string.Empty;
        public LocationDto Location { get; set; } = null!;
    }

    public class PartyEntity
    {
        [Ignore] public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public LocationEntity Location { get; set; } = null!;
    }

    [MapTo(typeof(LocationEntity))]
    public class LocationDto
    {
        public string City { get; set; } = string.Empty;
    }

    public class LocationEntity
    {
        [Ignore] public int Id { get; set; }
        public string City { get; set; } = string.Empty;
    }

    public class Employee
    {
        public string Name { get; set; } = string.Empty;
        public Employee? Manager { get; set; }
    }

    public class EmployeeDto
    {
        public string Name { get; set; } = string.Empty;
        public EmployeeDto? Manager { get; set; }
    }

    public class NestedProfile : MappingProfile
    {
        public NestedProfile()
        {
            CreateMap<GeoPoint, GeoPointDto>();
            CreateMap<Parcel, ParcelDto>();
            // A manager chain always ends: the cycle of types (IM0004) is intended. The build has TreatWarningsAsErrors,
            // so this also checks that #pragma suppresses IM0004.
#pragma warning disable IM0004
            CreateMap<Employee, EmployeeDto>();
#pragma warning restore IM0004
        }
    }
}

namespace IronMapper.Tests
{
    /// <summary>
    /// Nested objects mapped by the mappings of their type pairs (attributes and a profile), in the real build of this
    /// project with the generator attached.
    /// </summary>
    public class NestedObjectTests
    {
        private static Shipment CreateShipment() => new()
        {
            Id = 3,
            Origin = new Address { City = "Oslo", Point = new GeoPoint { Lat = 59.9, Lon = 10.7 } },
            Parcels = { new Parcel { Label = "a", ReturnTo = new Address { City = "Bergen" } } },
        };

        [Fact]
        public void NestedObjects_AreMapped()
        {
            var dto = CreateShipment().MapToShipmentDto();

            Assert.Equal(3, dto.Id);
            Assert.Equal("Oslo", dto.Origin.City);
            Assert.Equal(59.9, dto.Origin.Point.Lat);
            Assert.Equal(10.7, dto.Origin.Point.Lon);
            Assert.Null(dto.Destination);
            Assert.Equal("Bergen", Assert.Single(dto.Parcels).ReturnTo.City);
        }

        [Fact]
        public void NullableNestedObject_IsMappedWhenSet()
        {
            var shipment = CreateShipment();
            shipment.Destination = new Address { City = "Tromsø" };

            Assert.Equal("Tromsø", shipment.MapToShipmentDto().Destination!.City);
        }

        [Fact]
        public void InPlace_UpdatesTheExistingNestedObject_RecordWithInitOnlyPropertiesIsReplaced()
        {
            var target = new ShipmentDto();
            var origin = target.Origin;
            var point = target.Origin.Point;

            CreateShipment().MapToShipmentDto(target);

            Assert.Same(origin, target.Origin);
            Assert.Equal("Oslo", target.Origin.City);
            // GeoPointDto has only init properties: the in-place overload cannot update it, so it gets a new instance.
            Assert.NotSame(point, target.Origin.Point);
            Assert.Equal(59.9, target.Origin.Point.Lat);
            Assert.Null(target.Destination);
        }

        private static InvoiceDto CreateInvoice() => new()
        {
            Number = 7,
            Buyer = new PartyDto { Name = "Ann", Location = new LocationDto { City = "Oslo" } },
        };

        [Fact]
        public void InPlace_TwoLevels_KeepsTheExistingNestedObjectsAndUpdatesThem()
        {
            var location = new LocationEntity { Id = 20, City = "old" };
            var buyer = new PartyEntity { Id = 10, Name = "old", Location = location };
            var entity = new InvoiceEntity { Number = 1, Buyer = buyer };

            CreateInvoice().MapToInvoiceEntity(entity);

            Assert.Equal(7, entity.Number);
            Assert.True(ReferenceEquals(buyer, entity.Buyer));
            Assert.True(ReferenceEquals(location, entity.Buyer.Location));
            Assert.Equal("Ann", buyer.Name);
            Assert.Equal("Oslo", location.City);
            // Not mapped: kept.
            Assert.Equal(10, buyer.Id);
            Assert.Equal(20, location.Id);
        }

        [Fact]
        public void InPlace_NullDestinationNestedObject_GetsANewObject()
        {
            var entity = new InvoiceEntity();
            var dto = CreateInvoice();
            dto.Seller = new PartyDto { Name = "Bob", Location = new LocationDto { City = "Bergen" } };

            dto.MapToInvoiceEntity(entity);

            Assert.Equal("Oslo", entity.Buyer.Location.City);
            Assert.Equal("Bergen", entity.Seller!.Location.City);
        }

        [Fact]
        public void InPlace_NullSourceForANullableDestination_SetsNull()
        {
            var entity = new InvoiceEntity { Seller = new PartyEntity { Name = "Bob" } };

            CreateInvoice().MapToInvoiceEntity(entity);

            Assert.Null(entity.Seller);
        }

        [Fact]
        public void NullInANonNullableNestedObject_ThrowsInvalidOperationException_NamingBothProperties()
        {
            var dto = new InvoiceDto { Number = 7 };
            var entity = new InvoiceEntity { Buyer = new PartyEntity() };
            const string message = "InvoiceDto.Buyer is null, but InvoiceEntity.Buyer is not nullable. If it is an EF navigation, "
                                   + "check Include; otherwise declare the destination property nullable (null is then mapped to null) "
                                   + "or configure the property explicitly.";

            Assert.Equal(message, Assert.Throws<System.InvalidOperationException>(() => dto.MapToInvoiceEntity()).Message);
            Assert.Equal(message, Assert.Throws<System.InvalidOperationException>(() => dto.MapToInvoiceEntity(entity)).Message);
        }

        [Fact]
        public void SelfReference_FiniteChain_IsMapped()
        {
            var dto = new Employee { Name = "dev", Manager = new Employee { Name = "lead" } }.MapToEmployeeDto();

            Assert.Equal("lead", dto.Manager!.Name);
            Assert.Null(dto.Manager.Manager);
        }
    }
}
