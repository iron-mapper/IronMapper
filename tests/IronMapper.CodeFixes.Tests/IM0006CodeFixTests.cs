using System.Threading.Tasks;
using Xunit;

namespace IronMapper.CodeFixes.Tests;

/// <summary>IM0006: replace a misspelled <c>[MapProperty]</c> destination with the closest destination property.</summary>
public class IM0006CodeFixTests
{
    [Fact]
    public async Task MisspelledDestination_ReplacedWithClosestProperty()
    {
        await new CodeFixTest<IM0006CodeFixProvider>("""
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source
            {
                [{|IM0006:MapProperty("FulName")|}]
                public string Name { get; set; } = "";
            }

            public class Dest { public string {|IM0001:FullName|} { get; set; } = ""; }
            """, """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source
            {
                [MapProperty("FullName")]
                public string Name { get; set; } = "";
            }

            public class Dest { public string FullName { get; set; } = ""; }
            """).RunAsync();
    }

    [Fact]
    public async Task DestinationInAnotherNamespaceWithSameSimpleName_UsesTheMappedType()
    {
        // The candidates come from the mapped destination type (diagnostic property), not from a type found by name.
        await new CodeFixTest<IM0006CodeFixProvider>("""
            using IronMapper.Attributes;

            namespace App
            {
                [MapTo(typeof(B.Dest))]
                public class Source
                {
                    [{|IM0006:MapProperty("Totl")|}]
                    public int Amount { get; set; }
                }
            }

            namespace App.A { public class Dest { public int Tote { get; set; } } }
            namespace App.B { public class Dest { public int {|IM0001:Total|} { get; set; } } }
            """, """
            using IronMapper.Attributes;

            namespace App
            {
                [MapTo(typeof(B.Dest))]
                public class Source
                {
                    [MapProperty("Total")]
                    public int Amount { get; set; }
                }
            }

            namespace App.A { public class Dest { public int Tote { get; set; } } }
            namespace App.B { public class Dest { public int Total { get; set; } } }
            """).RunAsync();
    }

    [Fact]
    public async Task NoCloseName_NoFix()
    {
        var source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source
            {
                [{|IM0006:MapProperty("Something")|}]
                public string Name { get; set; } = "";
            }

            public class Dest { public string {|IM0001:FullName|} { get; set; } = ""; }
            """;
        await new CodeFixTest<IM0006CodeFixProvider>(source, source).RunAsync();
    }
}
