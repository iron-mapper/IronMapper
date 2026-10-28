using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Tests.HooksAndWhen;
using Xunit;

namespace IronMapper.Tests.HooksAndWhen
{
    public class Ticket
    {
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Priority { get; set; }
        public bool Open { get; set; }
    }

    // required and init properties with BeforeMap/AfterMap (issue #60): up to 2.0.0 CS9035 in the generated file and Title
    // not mapped.
    public class TicketDto
    {
        public required string Code { get; set; }
        public string Title { get; init; } = string.Empty;
        public int Priority { get; set; }
        public string Log { get; set; } = string.Empty;
    }

    public class TicketRow
    {
        public string Title { get; set; } = string.Empty;
        public int Priority { get; set; }
        public bool Open { get; set; }
    }

    public class TicketView
    {
        public string Title { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string Log { get; set; } = string.Empty;
    }

    public class TicketProfile : MappingProfile
    {
        public TicketProfile()
        {
            // IM0025 (an error with TreatWarningsAsErrors in this project): the in-place overload keeps Title (init-only).
#pragma warning disable IM0025
            CreateMap<Ticket, TicketDto>()
#pragma warning restore IM0025
                .ForMember(d => d.Log, o => o.Ignore())
                .BeforeMap((s, d) => d.Log += $"before:{d.Code}/{d.Title}/{d.Priority};")
                .AfterMap((s, d) => d.Log += $"after:{d.Priority};");

            // In place (issue #62): When, then BeforeMap, the properties, AfterMap.
            CreateMap<TicketRow, TicketView>()
                .ForMember(d => d.Log, o => o.Ignore())
                .When(s => s.Open)
                .BeforeMap((s, d) => d.Log += "before;")
                .AfterMap((s, d) => d.Log += "after;");
        }
    }
}

namespace IronMapper.Tests
{
    /// <summary>
    /// <c>BeforeMap</c>/<c>AfterMap</c> with <c>required</c> and <c>init</c> properties (issue #60) and hooks and <c>When</c>
    /// in the in-place overload (issue #62), built with this project's warnings as errors.
    /// </summary>
    public class HooksAndWhenTests
    {
        [Fact]
        public void NewObject_RequiredAndInitSetBeforeBeforeMap_OthersBetweenHooks()
        {
            var dto = new Ticket { Code = "T-1", Title = "Bug", Priority = 3 }.MapToTicketDto();

            Assert.Equal("T-1", dto.Code);
            Assert.Equal("Bug", dto.Title);
            Assert.Equal(3, dto.Priority);
            Assert.Equal("before:T-1/Bug/0;after:3;", dto.Log);
        }

        [Fact]
        public void InPlace_HooksRun_InitKept()
        {
            var existing = new TicketDto { Code = "old", Title = "old", Priority = 1 };

            new Ticket { Code = "T-2", Title = "new", Priority = 5 }.MapToTicketDto(existing);

            Assert.Equal("T-2", existing.Code);
            Assert.Equal("old", existing.Title);
            Assert.Equal(5, existing.Priority);
            Assert.Equal("before:old/old/1;after:5;", existing.Log);
        }

        [Fact]
        public void InPlace_WhenFalse_DestinationUnchanged()
        {
            var existing = new TicketView { Title = "old", Priority = 1 };

            new TicketRow { Open = false, Title = "new", Priority = 2 }.MapToTicketView(existing);

            Assert.Equal("old", existing.Title);
            Assert.Equal(1, existing.Priority);
            Assert.Equal(string.Empty, existing.Log);
        }

        [Fact]
        public void InPlace_WhenTrue_UpdatedBetweenHooks()
        {
            var existing = new TicketView { Title = "old", Priority = 1 };

            new TicketRow { Open = true, Title = "new", Priority = 2 }.MapToTicketView(existing);

            Assert.Equal("new", existing.Title);
            Assert.Equal(2, existing.Priority);
            Assert.Equal("before;after;", existing.Log);
        }
    }
}
