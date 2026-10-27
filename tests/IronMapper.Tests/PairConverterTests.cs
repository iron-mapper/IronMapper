using System;
using System.Collections.Generic;
using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Tests.PairConverters;
using Xunit;

namespace IronMapper.Tests.PairConverters
{
    // A value object from a Guid: a private constructor, a factory that rejects Guid.Empty; back through Value.
    public readonly record struct AccountId
    {
        private AccountId(Guid value) => Value = value;

        public Guid Value { get; }

        public static AccountId From(Guid value)
            => value == Guid.Empty ? throw new ArgumentException("The id is empty.", nameof(value)) : new AccountId(value);
    }

    public class LedgerRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid OwnerId { get; set; }
        public Guid? AuditorId { get; set; }
        public Guid[] MemberIds { get; set; } = Array.Empty<Guid>();
        public Guid? ClosedBy { get; set; }
    }

    // A domain type: private constructor, a factory with value objects, a property with a public set after the call.
    public sealed class Ledger
    {
        private Ledger(Guid id, string name, AccountId ownerId, AccountId? auditorId, IReadOnlyList<AccountId> memberIds)
        {
            Id = id;
            Name = name;
            OwnerId = ownerId;
            AuditorId = auditorId;
            MemberIds = memberIds;
        }

        public Guid Id { get; }
        public string Name { get; }
        public AccountId OwnerId { get; }
        public AccountId? AuditorId { get; }
        public IReadOnlyList<AccountId> MemberIds { get; }
        public AccountId? ClosedBy { get; set; }

        public static Ledger Restore(Guid id, string name, AccountId ownerId, AccountId? auditorId, IReadOnlyList<AccountId> memberIds)
            => new(id, name, ownerId, auditorId, memberIds);
    }

    // A contract of the application layer: a positional record.
    public record LedgerCard(Guid Id, string Name, AccountId OwnerId, AccountId? AuditorId);

    // Both directions in one line: ConvertUsing and ReverseMap with a converter.
    public readonly record struct SlotId
    {
        private SlotId(int value) => Value = value;

        public int Value { get; }

        public static SlotId From(int value) => value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : new SlotId(value);
    }

    public class SlotRow { public int Slot { get; set; } public int? Backup { get; set; } }

    public record Booking(SlotId Slot, SlotId? Backup);

    public class SlotProfile : MappingProfile
    {
        public SlotProfile()
        {
            CreateMap<int, SlotId>().ConvertUsing(SlotId.From).ReverseMap(v => v.Value);
            CreateMap<SlotRow, Booking>();
            CreateMap<Booking, SlotRow>();
        }
    }

    public class PairConverterProfile : MappingProfile
    {
        public PairConverterProfile()
        {
            CreateMap<Guid, AccountId>().ConvertUsing(AccountId.From);
            CreateMap<AccountId, Guid>().ConvertUsing(v => v.Value);
            CreateMap<LedgerRow, Ledger>().ConstructWith(Ledger.Restore);
            CreateMap<LedgerRow, LedgerCard>();
            CreateMap<Ledger, LedgerRow>();
        }
    }
}

namespace IronMapper.Tests
{
    /// <summary>
    /// Converters through the mappings of type pairs (<c>CreateMap&lt;Guid, AccountId&gt;().ConvertUsing(AccountId.From)</c>
    /// and back) in a project that builds with the generator and <c>TreatWarningsAsErrors</c>: factory and constructor
    /// parameters, properties, collection elements, <c>Nullable&lt;T&gt;</c> lifted by the same pair.
    /// </summary>
    public class PairConverterTests
    {
        private static LedgerRow Row() => new()
        {
            Id = Guid.NewGuid(),
            Name = "main",
            OwnerId = Guid.NewGuid(),
            AuditorId = null,
            MemberIds = new[] { Guid.NewGuid(), Guid.NewGuid() },
            ClosedBy = Guid.NewGuid(),
        };

        [Fact]
        public void Factory_ValueObjectParameters_NullableLifted_ElementsConverted_PropertyAfterTheCall()
        {
            var row = Row();

            var ledger = row.MapToLedger();

            Assert.Equal(row.OwnerId, ledger.OwnerId.Value);
            Assert.Null(ledger.AuditorId);
            Assert.Equal(new[] { row.MemberIds[0], row.MemberIds[1] }, new[] { ledger.MemberIds[0].Value, ledger.MemberIds[1].Value });
            Assert.Equal(row.ClosedBy, ledger.ClosedBy?.Value);
        }

        [Fact]
        public void PositionalRecord_ValueObjectParameters()
        {
            var row = Row();
            row.AuditorId = Guid.NewGuid();

            var card = row.MapToLedgerCard();

            Assert.Equal(row.OwnerId, card.OwnerId.Value);
            Assert.Equal(row.AuditorId, card.AuditorId?.Value);
        }

        [Fact]
        public void BackToPrimitives_ValueObjectsAndNullable_ElementsToArray()
        {
            var row = Row();

            var back = row.MapToLedger().MapToLedgerRow();

            Assert.Equal(row.OwnerId, back.OwnerId);
            Assert.Null(back.AuditorId);
            Assert.Equal(row.MemberIds, back.MemberIds);
            Assert.Equal(row.ClosedBy, back.ClosedBy);
        }

        [Fact]
        public void ReverseMapWithConverter_BothDirections_NullableLifted()
        {
            var booking = new SlotRow { Slot = 3, Backup = null }.MapToBooking();
            var back = new Booking(SlotId.From(4), SlotId.From(5)).MapToSlotRow();

            Assert.Equal((3, null), (booking.Slot.Value, booking.Backup));
            Assert.Equal((4, 5), (back.Slot, back.Backup));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlotRow { Slot = -1 }.MapToBooking());
        }

        [Fact]
        public void ExceptionOfTheConverter_ReachesTheCallerAsItIs()
        {
            var row = Row();
            row.OwnerId = Guid.Empty;

            var exception = Assert.Throws<ArgumentException>(() => row.MapToLedger());

            Assert.Equal("value", exception.ParamName);
            Assert.Throws<ArgumentException>(() => new[] { Guid.Empty }.MapToAccountIdList());
        }
    }
}
