using System;
using System.Collections.Generic;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Tests.Constructors;
using Xunit;

namespace IronMapper.Tests.Constructors
{
    // Positional record → positional record, widening to nullable.
    [MapTo(typeof(PendingChangeState))]
    public record PendingChange(string NewAddress, DateTimeOffset ExpiresAt);
    public record PendingChangeState(string? NewAddress, DateTimeOffset? ExpiresAt);

    // A record with a default value and a parameter computed by MapFrom (profile).
    public class MemberRecord
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public DateTimeOffset? BlockedAt { get; set; }
        public string Plan { get; set; } = string.Empty;
    }

    public record Member(Guid Id, string Email, bool Blocked, string Theme = Member.DefaultTheme)
    {
        public const string DefaultTheme = "system";
    }

    // A class with a constructor, a nested object, collections and a property set after the constructor.
    [MapTo(typeof(Shipment))]
    public class ShipmentRow
    {
        public Guid Id { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
        public List<ParcelRow> Parcels { get; set; } = new();
        public Contact Sender { get; set; } = new();
        public string Note { get; set; } = string.Empty;
    }

    public class Shipment
    {
        public Shipment(Guid id, IReadOnlyList<string> tags, IReadOnlyList<Parcel> parcels, ContactCard sender)
        {
            if (id == Guid.Empty)
            {
                throw new ArgumentException("The id is empty.", nameof(id));
            }

            Id = id;
            Tags = tags;
            Parcels = parcels;
            Sender = sender;
        }

        public Guid Id { get; }
        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyList<Parcel> Parcels { get; }
        public ContactCard Sender { get; }
        public string Note { get; set; } = string.Empty;
    }

    [MapTo(typeof(Parcel))]
    public class ParcelRow { public int Weight { get; set; } }
    public record Parcel(int Weight);

    [MapTo(typeof(ContactCard))]
    public class Contact { public string Name { get; set; } = string.Empty; }
    public record ContactCard(string Name);

    // A public constructor and a static factory: the constructor is used.
    [MapTo(typeof(ChatTarget))]
    public class ChatTargetRow { public long ChatId { get; set; } public string Title { get; set; } = string.Empty; }

    public record ChatTarget(long ChatId, string Title)
    {
        public static ChatTarget Create(long chatId, string title) => new(chatId, title.Trim());
    }

    public class ConstructorProfile : MappingProfile
    {
        public ConstructorProfile()
        {
            CreateMap<MemberRecord, Member>()
                .ForMember(d => d.Blocked, o => o.MapFrom(s => s.BlockedAt != null));
        }
    }
}

namespace IronMapper.Tests
{
    /// <summary>
    /// Constructors and positional records (issue #24) in a project that builds with the generator and
    /// <c>TreatWarningsAsErrors</c>: no warning in the generated code.
    /// </summary>
    public class ConstructorMappingTests
    {
        [Fact]
        public void PositionalRecordToPositionalRecord_WideningToNullable()
        {
            var expires = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

            var state = new PendingChange("new@example.com", expires).MapToPendingChangeState();

            Assert.Equal(new PendingChangeState("new@example.com", expires), state);
        }

        [Fact]
        public void Profile_ComputedParameterAndDefaultValue()
        {
            var record = new MemberRecord { Id = Guid.NewGuid(), Email = "a@b.c", BlockedAt = DateTimeOffset.UnixEpoch, Plan = "pro" };

            var member = record.MapToMember();

            Assert.Equal(new Member(record.Id, "a@b.c", Blocked: true), member);
            Assert.Equal(Member.DefaultTheme, member.Theme);
            Assert.False(new MemberRecord { Id = record.Id }.MapToMember().Blocked);
        }

        [Fact]
        public void ClassConstructor_NestedObjectCollectionsAndAPropertyAfterTheConstructor()
        {
            var row = new ShipmentRow
            {
                Id = Guid.NewGuid(),
                Tags = new[] { "fragile" },
                Parcels = { new ParcelRow { Weight = 3 }, new ParcelRow { Weight = 5 } },
                Sender = new Contact { Name = "Ann" },
                Note = "leave at the door",
            };

            var shipment = row.MapToShipment();

            Assert.Equal(row.Id, shipment.Id);
            Assert.Same(row.Tags, shipment.Tags);
            Assert.Equal(new[] { new Parcel(3), new Parcel(5) }, shipment.Parcels);
            Assert.Equal(new ContactCard("Ann"), shipment.Sender);
            Assert.Equal("leave at the door", shipment.Note);
            Assert.Equal(2, new[] { row, row }.MapToShipmentList().Count);
        }

        [Fact]
        public void ExceptionFromTheConstructor_ReachesTheCallerAsItIs()
        {
            var exception = Assert.Throws<ArgumentException>(() => new ShipmentRow().MapToShipment());

            Assert.Equal("id", exception.ParamName);
        }

        [Fact]
        public void NestedObjectParameter_Null_InvalidOperationExceptionNamingBothMembers()
        {
            var row = new ShipmentRow { Id = Guid.NewGuid(), Sender = null! };

            var exception = Assert.Throws<InvalidOperationException>(() => row.MapToShipment());

            Assert.StartsWith("ShipmentRow.Sender is null, but Shipment.sender is not nullable.", exception.Message);
        }

        [Fact]
        public void PublicConstructorAndFactory_TheConstructorIsUsed()
        {
            Assert.Equal(" chat ", new ChatTargetRow { ChatId = 1, Title = " chat " }.MapToChatTarget().Title);
        }
    }
}
