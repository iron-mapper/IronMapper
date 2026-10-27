using System;
using System.Collections.Generic;
using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Tests.Factories;
using Xunit;

namespace IronMapper.Tests.Factories
{
    // A value object from a Guid: private constructor, a factory that rejects Guid.Empty.
    public readonly record struct OwnerId
    {
        private OwnerId(Guid value) => Value = value;

        public Guid Value { get; }

        public static OwnerId From(Guid value)
            => value == Guid.Empty ? throw new ArgumentException("The id is empty.", nameof(value)) : new OwnerId(value);
    }

    public enum Visibility { Private, Public }

    public class BoardRow
    {
        public Guid OwnerId { get; set; }
        public string Title { get; set; } = string.Empty;
        public Guid[] MemberIds { get; set; } = Array.Empty<Guid>();
        public Visibility? Visibility { get; set; }
        public LayoutRow Layout { get; set; } = new();
        public string? Description { get; set; }
        public string Unused { get; set; } = string.Empty;
    }

    public class LayoutRow
    {
        public int Columns { get; set; }
        public bool Compact { get; set; }
    }

    public sealed class Layout
    {
        private Layout(int columns, bool compact)
        {
            Columns = columns;
            Compact = compact;
        }

        public int Columns { get; }
        public bool Compact { get; }

        public static Layout Create(int columns, bool compact) => new(Math.Max(1, columns), compact);
    }

    public sealed class Board
    {
        private Board(OwnerId owner, string title, IReadOnlyList<Guid> memberIds, Layout layout, Visibility? visibility, int? limit,
            string? color, bool? archived)
        {
            Owner = owner;
            Title = title;
            MemberIds = memberIds;
            Layout = layout;
            Visibility = visibility;
            Limit = limit;
            Color = color;
            Archived = archived;
        }

        public OwnerId Owner { get; }
        public string Title { get; }
        public IReadOnlyList<Guid> MemberIds { get; }
        public Layout Layout { get; }
        public Visibility? Visibility { get; }
        public int? Limit { get; }
        public string? Color { get; }
        public bool? Archived { get; }
        public string? Description { get; set; }

        // Four optional parameters; the source has a value only for visibility.
        public static Board Restore(OwnerId owner, string title, IReadOnlyList<Guid> memberIds, Layout layout,
            Visibility? visibility = null, int? limit = null, string? color = null, bool? archived = null)
            => new(owner, title, memberIds, layout, visibility, limit, color, archived);
    }

    // A public constructor and a factory that normalizes: ConstructWith takes the factory.
    public class LabelRow { public string Text { get; set; } = string.Empty; }

    public sealed class Label
    {
        public Label(string text) => Text = text;

        public string Text { get; }

        public static Label Create(string text) => new(text.Trim().ToUpperInvariant());
    }

    // A parameter without a property of the same name, and a lambda choosing a constructor.
    public class SignatureRow
    {
        public string First { get; set; } = string.Empty;
        public string Last { get; set; } = string.Empty;
        public int Year { get; set; }
    }

    public class Signature
    {
        public Signature(string display) => Text = display;
        public Signature(string display, int year) { Text = display; Year = year; }

        public string Text { get; }
        public int Year { get; }
    }

    public class FactoryProfile : MappingProfile
    {
        public FactoryProfile()
        {
            CreateMap<LayoutRow, Layout>().ConstructWith(Layout.Create);
            CreateMap<BoardRow, Board>()
                .ConstructWith(Board.Restore)
                .ForCtorParam("owner", o => o.MapFrom(s => OwnerId.From(s.OwnerId)));
            CreateMap<LabelRow, Label>().ConstructWith(Label.Create);
            CreateMap<SignatureRow, Signature>()
                .ConstructWith((string display) => new Signature(display))
                .ForCtorParam("display", o => o.MapFrom(s => s.First + " " + s.Last));
        }
    }
}

namespace IronMapper.Tests
{
    /// <summary>
    /// Factories of profiles (<c>ConstructWith</c>, <c>ForCtorParam</c>) in a project that builds with the generator and
    /// <c>TreatWarningsAsErrors</c>: no warning in the generated code.
    /// </summary>
    public class FactoryMappingTests
    {
        private static BoardRow Row() => new()
        {
            OwnerId = Guid.NewGuid(),
            Title = "Roadmap",
            MemberIds = new[] { Guid.NewGuid(), Guid.NewGuid() },
            Visibility = Visibility.Public,
            Layout = new LayoutRow { Columns = 0, Compact = true },
            Description = "Q4",
            Unused = "x",
        };

        [Fact]
        public void Factory_ValueObject_ArrayToReadOnlyList_NestedFactory_OptionalParameters_PropertyAfterTheCall()
        {
            var row = Row();

            var board = row.MapToBoard();

            Assert.Equal(row.OwnerId, board.Owner.Value);
            Assert.Equal("Roadmap", board.Title);
            Assert.Same(row.MemberIds, board.MemberIds);
            Assert.Equal((1, true), (board.Layout.Columns, board.Layout.Compact)); // through Layout.Create
            Assert.Equal(Visibility.Public, board.Visibility);
            Assert.Null(board.Limit);
            Assert.Null(board.Color);
            Assert.Null(board.Archived);
            Assert.Equal("Q4", board.Description);
            Assert.Equal(2, new[] { row, row }.MapToBoardArray().Length);
        }

        [Fact]
        public void ExceptionFromTheFactory_ReachesTheCallerAsItIs()
        {
            var row = Row();
            row.OwnerId = Guid.Empty;

            var exception = Assert.Throws<ArgumentException>(() => row.MapToBoard());

            Assert.Equal("value", exception.ParamName);
        }

        [Fact]
        public void PublicConstructorAndFactory_ConstructWithTakesTheFactory()
            => Assert.Equal("NEW", new LabelRow { Text = " new " }.MapToLabel().Text);

        [Fact]
        public void LambdaChoosesTheConstructor_ForCtorParamForAParameterWithoutProperty()
        {
            var signature = new SignatureRow { First = "Ada", Last = "Lovelace", Year = 1843 }.MapToSignature();

            Assert.Equal(("Ada Lovelace", 0), (signature.Text, signature.Year));
        }
    }
}
