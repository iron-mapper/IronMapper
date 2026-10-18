using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// The accessibility of <c>IronMapper.Generated.GeneratedMappers</c> follows the mappings of the assembly
/// (internal unless at least one generated method is public), and two projects that both use IronMapper,
/// one referencing the other, build with <c>TreatWarningsAsErrors</c>.
/// </summary>
public class GeneratedClassVisibilityTests
{
    private const string ClassName = "IronMapper.Generated.GeneratedMappers";

    private static void AssertClean(IReadOnlyList<Diagnostic> diagnostics)
        => Assert.True(diagnostics.Count == 0, "Warnings/errors:\n" + string.Join("\n", diagnostics.Select(d => d.ToString())));

    private static INamedTypeSymbol GetGeneratedClass(Compilation compilation)
        => compilation.Assembly.GetTypeByMetadataName(ClassName)
           ?? throw new InvalidOperationException($"{ClassName} was not generated.");

    // ------------------------------------------------------------------
    // Accessibility of the class in one assembly
    // ------------------------------------------------------------------

    [Fact]
    public void AllMappingsInternal_ClassIsInternal()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;

            namespace App;

            [MapTo(typeof(LedgerDto))]
            internal class Ledger { public string Code { get; set; } = ""; }
            internal class LedgerDto { public string Code { get; set; } = ""; }

            public class Entry { public decimal Amount { get; set; } }
            internal class EntryDto { public decimal Amount { get; set; } }

            internal class EntryProfile : MappingProfile
            {
                public EntryProfile() { CreateMap<Entry, EntryDto>(); }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.CompileProject("App", source);

        AssertClean(diagnostics);
        Assert.Equal(Accessibility.Internal, GetGeneratedClass(compilation).DeclaredAccessibility);
    }

    [Theory]
    [InlineData("[MapTo(typeof(UserDto))]", "")]
    [InlineData("", "CreateMap<User, UserDto>();")]
    public void OnePublicMapping_ClassIsPublic(string attribute, string profileMap)
    {
        var source = $$"""
            using IronMapper.Attributes;
            using IronMapper.Configuration;

            namespace App;

            {{attribute}}
            public class User { public int Id { get; set; } }
            public class UserDto { public int Id { get; set; } }

            [MapTo(typeof(LedgerDto))]
            internal class Ledger { public string Code { get; set; } = ""; }
            internal class LedgerDto { public string Code { get; set; } = ""; }

            internal class AppProfile : MappingProfile
            {
                public AppProfile() { {{profileMap}} }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.CompileProject("App", source);

        // An empty profile is reported as IM0005 when the map comes from the attribute.
        AssertClean(diagnostics.Where(d => d.Id != "IM0005").ToList());
        Assert.Equal(Accessibility.Public, GetGeneratedClass(compilation).DeclaredAccessibility);
    }

    [Fact]
    public void AccessibilityIsDeclaredOnce_InASeparateFile()
    {
        var source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(ADto))] public class A { public int X { get; set; } }
            public class ADto { public int X { get; set; } }

            [MapTo(typeof(BDto))] internal class B { public int X { get; set; } }
            internal class BDto { public int X { get; set; } }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        Assert.Empty(errors);
        var declarations = generated
            .SelectMany(s => s.Split('\n'))
            .Where(line => line.Contains("partial class GeneratedMappers"))
            .Select(line => line.Trim())
            .ToList();
        Assert.Equal(3, declarations.Count);
        Assert.Single(declarations, "public static partial class GeneratedMappers");
        Assert.Equal(2, declarations.Count(d => d == "static partial class GeneratedMappers"));
    }

    [Fact]
    public void NoGeneratedMappings_NoClass()
    {
        // The only mapping is skipped with IM0010, so there is nothing to declare.
        var source = """
            using IronMapper.Attributes;

            namespace App;

            public class Outer
            {
                [MapTo(typeof(Dto))] private class Hidden { public int X { get; set; } }
                private class Dto { public int X { get; set; } }
            }
            """;

        var (compilation, diagnostics) = GeneratorTestHelper.CompileProject("App", source);

        Assert.Equal(new[] { "IM0010" }, diagnostics.Select(d => d.Id));
        Assert.Null(compilation.Assembly.GetTypeByMetadataName(ClassName));
    }

    // ------------------------------------------------------------------
    // Two assemblies: B references A, both use IronMapper
    // ------------------------------------------------------------------

    private static string LibraryInternalOnly(string assemblyAttributes = "") => $$"""
        using IronMapper.Attributes;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        {{assemblyAttributes}}

        namespace Lib;

        [MapTo(typeof(SecretDto))]
        internal class Secret { public string Code { get; set; } = ""; }
        internal class SecretDto { public string Code { get; set; } = ""; }

        internal class Entry { public int Amount { get; set; } }
        internal class EntryDto { public int Amount { get; set; } }
        internal class LibProfile : MappingProfile
        {
            public LibProfile() { CreateMap<Entry, EntryDto>(); }
        }

        public static class LibApi
        {
            public static string Describe(string code, int amount)
                => new Secret { Code = code }.MapToSecretDto().Code + ":" + new Entry { Amount = amount }.MapToEntryDto().Amount;
        }
        """;

    private const string LibraryWithPublicMaps = """
        using IronMapper.Attributes;
        using IronMapper.Configuration;
        using IronMapper.Generated;

        namespace Lib;

        [MapTo(typeof(ProductDto))]
        public class Product { public string Name { get; set; } = ""; }
        public class ProductDto { public string Name { get; set; } = ""; }

        public class Price { public decimal Amount { get; set; } }
        public class PriceDto { public decimal Amount { get; set; } }
        public class LibProfile : MappingProfile
        {
            public LibProfile() { CreateMap<Price, PriceDto>(); }
        }

        [MapTo(typeof(SecretDto))]
        internal class Secret { public string Code { get; set; } = ""; }
        internal class SecretDto { public string Code { get; set; } = ""; }

        public static class LibApi
        {
            public static string Describe(string code, int amount)
                => new Secret { Code = code }.MapToSecretDto().Code + ":" + amount;
        }
        """;

    private static string App(string libraryCalls) => $$"""
        using IronMapper.Attributes;
        using IronMapper.Generated;

        namespace App;

        [MapTo(typeof(OrderDto))]
        public class Order { public int Id { get; set; } }
        public class OrderDto { public int Id { get; set; } }

        [MapTo(typeof(NoteDto))]
        internal class Note { public string Text { get; set; } = ""; }
        internal class NoteDto { public string Text { get; set; } = ""; }

        public static class Program
        {
            public static string Run()
            {
                var parts = new System.Collections.Generic.List<string>
                {
                    new Order { Id = 7 }.MapToOrderDto().Id.ToString(),
                    new[] { new Order { Id = 8 } }.MapToOrderDtoList()[0].Id.ToString(),
                    new Note { Text = "n" }.MapToNoteDto().Text,
                    Lib.LibApi.Describe("s", 3),
                };
                {{libraryCalls}}
                return string.Join(",", parts);
            }
        }
        """;

    private const string PublicLibraryCalls = """
        parts.Add(new Lib.Product { Name = "p" }.MapToProductDto().Name);
        parts.Add(new[] { new Lib.Price { Amount = 2.5m } }.MapToPriceDtoArray()[0].Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var target = new Lib.ProductDto();
        new Lib.Product { Name = "q" }.MapToProductDto(target);
        parts.Add(target.Name);
        """;

    [Fact]
    public void ReferencedAssemblyWithInternalMapsOnly_BuildsWithoutWarnings_AndBothAssembliesMap()
    {
        var (library, libraryDiagnostics) = GeneratorTestHelper.CompileProject("Lib", LibraryInternalOnly());
        AssertClean(libraryDiagnostics);
        Assert.Equal(Accessibility.Internal, GetGeneratedClass(library).DeclaredAccessibility);

        var (libraryRef, libraryImage) = GeneratorTestHelper.EmitReference(library);
        var (app, appDiagnostics) = GeneratorTestHelper.CompileProject("App", App(""), libraryRef);

        AssertClean(appDiagnostics);
        Assert.Equal("7,8,n,s:3", Run(app, ("Lib", libraryImage)));
    }

    [Fact]
    public void ReferencedAssemblyWithPublicMaps_BuildsWithoutWarnings_AndCallsBothAssemblies()
    {
        var (library, libraryDiagnostics) = GeneratorTestHelper.CompileProject("Lib", LibraryWithPublicMaps);
        AssertClean(libraryDiagnostics);
        Assert.Equal(Accessibility.Public, GetGeneratedClass(library).DeclaredAccessibility);

        var (libraryRef, libraryImage) = GeneratorTestHelper.EmitReference(library);
        var (app, appDiagnostics) = GeneratorTestHelper.CompileProject("App", App(PublicLibraryCalls), libraryRef);

        AssertClean(appDiagnostics);
        Assert.Equal("7,8,n,s:3,p,2.5,q", Run(app, ("Lib", libraryImage)));
    }

    [Fact]
    public void ReferencedAssemblyWithInternalMaps_AndInternalsVisibleTo_BuildsWithoutWarnings()
    {
        // Test projects usually see the internals of the project under test.
        var (library, libraryDiagnostics) = GeneratorTestHelper.CompileProject("Lib",
            LibraryInternalOnly("[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"App\")]"));
        AssertClean(libraryDiagnostics);

        var (libraryRef, libraryImage) = GeneratorTestHelper.EmitReference(library);
        var (app, appDiagnostics) = GeneratorTestHelper.CompileProject("App",
            App("parts.Add(new Lib.Secret { Code = \"v\" }.MapToSecretDto().Code);"), libraryRef);

        AssertClean(appDiagnostics);
        Assert.Equal("7,8,n,s:3,v", Run(app, ("Lib", libraryImage)));
    }

    [Fact]
    public void ExplicitClassName_ReferencedAssemblyWithInternalMapsOnly_BindsToOwnClassWithoutCS0436()
    {
        // 1.1.2 declared the class public in every assembly, so naming it in B reported CS0436.
        var (library, _) = GeneratorTestHelper.CompileProject("Lib", LibraryInternalOnly());
        var (libraryRef, libraryImage) = GeneratorTestHelper.EmitReference(library);

        var (app, appDiagnostics) = GeneratorTestHelper.CompileProject("App",
            App("parts.Add(GeneratedMappers.MapToOrderDto(new Order { Id = 9 }).Id.ToString());"), libraryRef);

        AssertClean(appDiagnostics);
        Assert.Equal("7,8,n,s:3,9", Run(app, ("Lib", libraryImage)));
    }

    [Fact]
    public void ExplicitClassName_ReferencedAssemblyWithPublicMaps_ReportsCS0436()
    {
        // Known limitation (documented): both assemblies declare a public IronMapper.Generated.GeneratedMappers.
        // Extension method calls are unaffected; naming the class explicitly binds to the current assembly's class
        // with warning CS0436.
        var (library, _) = GeneratorTestHelper.CompileProject("Lib", LibraryWithPublicMaps);
        var (libraryRef, _) = GeneratorTestHelper.EmitReference(library);

        var (_, appDiagnostics) = GeneratorTestHelper.CompileProject("App",
            App("parts.Add(GeneratedMappers.MapToOrderDto(new Order { Id = 9 }).Id.ToString());"), libraryRef);

        Assert.Equal(new[] { "CS0436" }, appDiagnostics.Select(d => d.Id));
    }

    [Fact]
    public void SamePairMappedInBothAssemblies_GeneratedCodeOfReferencingAssemblyCompiles()
    {
        // B maps a pair that A already exposes publicly. B's own generated List/Array methods must call
        // B's method, not the ambiguous extension method (1.1.2: CS0121 inside the generated file).
        var (library, _) = GeneratorTestHelper.CompileProject("Lib", LibraryWithPublicMaps);
        var (libraryRef, _) = GeneratorTestHelper.EmitReference(library);

        var app = """
            using IronMapper.Configuration;

            namespace App;

            public class AppProfile : MappingProfile
            {
                public AppProfile() { CreateMap<Lib.Product, Lib.ProductDto>(); }
            }
            """;
        var (_, appDiagnostics) = GeneratorTestHelper.CompileProject("App", app, libraryRef);

        AssertClean(appDiagnostics);
    }

    [Fact]
    public void SamePairMappedInBothAssemblies_ExtensionCallIsAmbiguous()
    {
        // Known limitation (documented): two visible MapToProductDto(this Product) methods in the same namespace.
        var (library, _) = GeneratorTestHelper.CompileProject("Lib", LibraryWithPublicMaps);
        var (libraryRef, _) = GeneratorTestHelper.EmitReference(library);

        var app = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class AppProfile : MappingProfile
            {
                public AppProfile() { CreateMap<Lib.Product, Lib.ProductDto>(); }
            }

            public static class Program
            {
                public static string Run() => new Lib.Product().MapToProductDto().Name;
            }
            """;
        var (_, appDiagnostics) = GeneratorTestHelper.CompileProject("App", app, libraryRef);

        Assert.Equal(new[] { "CS0121" }, appDiagnostics.Select(d => d.Id));
    }

    [Fact]
    public void SamePairMappedInBothAssemblies_AsNestedCollectionElement_IsAmbiguousInGeneratedCode()
    {
        // Known limitation (documented): the element mapping of a nested collection is called as an extension
        // method, because it may be declared in a referenced assembly.
        var (library, _) = GeneratorTestHelper.CompileProject("Lib", LibraryWithPublicMaps);
        var (libraryRef, _) = GeneratorTestHelper.EmitReference(library);

        var app = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Configuration;

            namespace App;

            public class AppProfile : MappingProfile
            {
                public AppProfile() { CreateMap<Lib.Product, Lib.ProductDto>(); }
            }

            [MapTo(typeof(CartDto))]
            public class Cart { public List<Lib.Product> Items { get; set; } = new(); }
            public class CartDto { public List<Lib.ProductDto> Items { get; set; } = new(); }
            """;
        var (_, appDiagnostics) = GeneratorTestHelper.CompileProject("App", app, libraryRef);

        Assert.NotEmpty(appDiagnostics);
        Assert.All(appDiagnostics, d => Assert.Equal("CS0121", d.Id));
        Assert.All(appDiagnostics, d => Assert.EndsWith("App_Cart_MapToCartDto.g.cs", d.Location.SourceTree!.FilePath));
    }

    // ------------------------------------------------------------------
    // IMapper (RuntimeMapper) with an internal generated class
    // ------------------------------------------------------------------

    [Fact]
    public void RuntimeMapper_FindsMethodsOfInternalGeneratedClass()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;

            namespace Internals;

            [MapTo(typeof(SecretDto))]
            internal class Secret { public string Code { get; set; } = ""; }
            internal class SecretDto { public string Code { get; set; } = ""; }

            internal class Entry { public int Amount { get; set; } }
            internal class EntryDto { public int Amount { get; set; } }
            internal class EntryProfile : MappingProfile
            {
                public EntryProfile() { CreateMap<Entry, EntryDto>(); }
            }

            public static class Program
            {
                private sealed class NoServices : System.IServiceProvider
                {
                    public object? GetService(System.Type serviceType) => null;
                }

                public static string Run()
                {
                    IronMapper.Interfaces.IMapper mapper = new IronMapper.Extensions.DI.RuntimeMapper(new NoServices());
                    var secret = mapper.Map<Secret, SecretDto>(new Secret { Code = "c" });
                    var entry = mapper.Map<EntryDto>(new Entry { Amount = 4 });
                    var target = new SecretDto();
                    mapper.Map(new Secret { Code = "d" }, target);
                    return secret.Code + "," + entry.Amount + "," + target.Code;
                }
            }
            """;

        var diReference = MetadataReference.CreateFromFile(typeof(IronMapper.Extensions.DI.RuntimeMapper).Assembly.Location);
        var (compilation, diagnostics) = GeneratorTestHelper.CompileProject("Internals", source, diReference);

        AssertClean(diagnostics);
        Assert.Equal(Accessibility.Internal, GetGeneratedClass(compilation).DeclaredAccessibility);
        Assert.Equal("c,4,d", Run(compilation));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Loads the referenced images and <paramref name="compilation"/> into an isolated context and calls <c>Program.Run()</c>.</summary>
    private static string Run(Compilation compilation, params (string name, byte[] image)[] dependencies)
    {
        var context = new AssemblyLoadContext($"run-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            foreach (var (_, image) in dependencies)
                context.LoadFromStream(new MemoryStream(image));

            var (_, appImage) = GeneratorTestHelper.EmitReference(compilation);
            var assembly = context.LoadFromStream(new MemoryStream(appImage));
            var program = assembly.GetTypes().Single(t => t.Name == "Program");
            return (string)program.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
