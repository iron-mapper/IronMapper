using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace IronMapper.CodeFixes.Tests;

/// <summary>
/// IM0001: <c>[Ignore]</c> on the destination property of an attribute mapping, <c>ForMember(..., opt =&gt; opt.Ignore())</c>
/// in a profile mapping (profiles do not read <c>[Ignore]</c>). Markup <c>{|IM0001:...|}</c> is where the generator's
/// diagnostic must point.
/// </summary>
public class IM0001CodeFixTests
{
    [Fact]
    public async Task AttributeMapping_AddsIgnoreToDestinationProperty()
    {
        await new CodeFixTest<IM0001CodeFixProvider>("""
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }

            public class Dest
            {
                public int Id { get; set; }
                public int {|IM0001:Extra|} { get; set; }
            }
            """, """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }

            public class Dest
            {
                public int Id { get; set; }
                [Ignore]
                public int Extra { get; set; }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task DestinationInFileWithoutUsing_AddsUsing()
    {
        var test = new CodeFixTest<IM0001CodeFixProvider>("""
            namespace App;

            public class Dest
            {
                public int Id { get; set; }
                public int {|IM0001:Extra|} { get; set; }
            }
            """, """
            using IronMapper.Attributes;

            namespace App;

            public class Dest
            {
                public int Id { get; set; }
                [Ignore]
                public int Extra { get; set; }
            }
            """);
        test.AddUnchangedSource("/0/Source.cs", """
            namespace App;

            [IronMapper.Attributes.MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task PositionalRecordParameter_AddsPropertyTargetedIgnore()
    {
        // A positional record is mapped through its parameterless constructor (object initializer; see IM0009).
        await new CodeFixTest<IM0001CodeFixProvider>("""
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }

            public record Dest(int Id, int {|IM0001:{|IM0009:Extra|}|}) { public Dest() : this(0, 0) { } }
            """, """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }

            public record Dest(int Id, [property: Ignore] int {|IM0009:Extra|}) { public Dest() : this(0, 0) { } }
            """).RunAsync();
    }

    [Fact]
    public async Task FixAll_AttributeMapping_AllProperties()
    {
        await new CodeFixTest<IM0001CodeFixProvider>("""
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }

            public class Dest
            {
                public int Id { get; set; }
                public int {|IM0001:First|} { get; set; }
                public int {|IM0001:Second|} { get; set; }
            }
            """, """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source { public int Id { get; set; } }

            public class Dest
            {
                public int Id { get; set; }
                [Ignore]
                public int First { get; set; }
                [Ignore]
                public int Second { get; set; }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task ProfileMapping_AddsForMemberIgnore_AndFixAllAddsEach()
    {
        var test = new CodeFixTest<IM0001CodeFixProvider>("""
            using IronMapper.Configuration;

            namespace App;

            public class Source { public int Id { get; set; } }
            public class Dest { public int Id { get; set; } public int First { get; set; } public int Second { get; set; } }

            public class P : MappingProfile
            {
                public P()
                {
                    {|IM0001:{|IM0001:CreateMap<Source, Dest>()|}|}
                        .ReverseMap();
                }
            }
            """, """
            using IronMapper.Configuration;

            namespace App;

            public class Source { public int Id { get; set; } }
            public class Dest { public int Id { get; set; } public int First { get; set; } public int Second { get; set; } }

            public class P : MappingProfile
            {
                public P()
                {
                    CreateMap<Source, Dest>().ForMember(dest => dest.First, opt => opt.Ignore()).ForMember(dest => dest.Second, opt => opt.Ignore())
                        .ReverseMap();
                }
            }
            """);
        test.SetBuildProperty("IronMapperReportUnmappedInProfiles", "true");
        await test.RunAsync();
    }

    [Fact]
    public async Task DestinationFromAnotherAssembly_NoFix()
    {
        // The diagnostic points to the attribute: there is no property declaration to change.
        var source = """
            namespace App;

            [{|IM0001:{|IM0001:{|IM0001:{|IM0001:{|IM0001:{|IM0001:{|IM0001:IronMapper.Attributes.MapTo(typeof(System.UriBuilder))|}|}|}|}|}|}|}]
            public class Source { public string Host { get; set; } = ""; }
            """;
        await new CodeFixTest<IM0001CodeFixProvider>(source, source).RunAsync();
    }
}
