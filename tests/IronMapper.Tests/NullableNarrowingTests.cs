using System;
using System.Collections.Generic;
using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Tests.NullableNarrowing;
using Xunit;

namespace IronMapper.Tests.NullableNarrowing
{
    public class ContactRow
    {
        public string? Email { get; set; }
        public List<PhoneRow?> Phones { get; set; } = new();
    }

    public class PhoneRow { public string Number { get; set; } = string.Empty; }
    public class Phone { public string Number { get; set; } = string.Empty; }

    public class Contact
    {
        public string Email { get; set; } = string.Empty;
        public List<Phone?> Phones { get; set; } = new();
    }

    public record ContactCard(string Email);

    public class NarrowingProfile : MappingProfile
    {
        public NarrowingProfile()
        {
            CreateMap<PhoneRow, Phone>();
            // This project builds with TreatWarningsAsErrors: IM0023 is reported here, at the line of the mapping, and is
            // suppressed the standard way; the generated file has no CS8601/CS8604 (they would fail the build).
#pragma warning disable IM0023
            CreateMap<ContactRow, Contact>();
            CreateMap<ContactRow, ContactCard>();
#pragma warning restore IM0023
        }
    }
}

namespace IronMapper.Tests
{
    /// <summary>
    /// IM0023 (issue #54): a nullable source value for a non-nullable member is mapped as before (null passes through, a
    /// null element throws in the element mapping), in a project with <c>TreatWarningsAsErrors</c>.
    /// </summary>
    public class NullableNarrowingTests
    {
        [Fact]
        public void NullPassesThrough_PropertyAndParameter_AsBefore()
        {
            var row = new ContactRow { Email = null, Phones = new List<PhoneRow?> { new() { Number = "1" } } };

            var contact = row.MapToContact();

            Assert.Null(contact.Email);
            Assert.Null(row.MapToContactCard().Email);
            Assert.Equal("1", contact.Phones[0]!.Number);
        }

        [Fact]
        public void NullElement_ThrowsArgumentNullException_AsBefore()
            => Assert.Throws<ArgumentNullException>(() => new ContactRow { Phones = new List<PhoneRow?> { null } }.MapToContact());
    }
}
