using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// The accessibility of every generated method must not exceed the effective accessibility of the
/// source and destination types; otherwise the generated code fails with CS0050/CS0051.
/// These tests compile the generated code, not just inspect its text.
/// </summary>
public class AccessibilityGeneratorTests
{
    private static string Join(System.Collections.Generic.IReadOnlyList<string> sources) => string.Join("\n", sources);

    private static void AssertCompiles(System.Collections.Generic.IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Compilation errors:\n" + string.Join("\n", errors.Select(e => e.ToString())));

    // ------------------------------------------------------------------
    // Top-level types: [MapTo] and MappingProfile
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("internal", "public")]
    [InlineData("public", "internal")]
    [InlineData("internal", "internal")]
    public void MapToAttribute_InternalSourceOrDestination_GeneratesInternalMethodsThatCompile(string sourceModifier, string destModifier)
    {
        var source = $$"""
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(UserDto))]
            {{sourceModifier}} class UserEntity
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            {{destModifier}} class UserDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        var code = Join(generated);
        Assert.Contains("internal static global::App.UserDto MapToUserDto(this global::App.UserEntity source)", code);
        Assert.DoesNotContain("public static global::App.UserDto", code);
    }

    [Theory]
    [InlineData("internal", "public")]
    [InlineData("public", "internal")]
    [InlineData("internal", "internal")]
    public void Profile_InternalSourceOrDestination_GeneratesInternalMethodsThatCompile(string sourceModifier, string destModifier)
    {
        var source = $$"""
            using IronMapper.Configuration;

            namespace App;

            {{sourceModifier}} class UserEntity
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            {{destModifier}} class UserDto
            {
                public int Id { get; set; }
                public string Name { get; set; } = "";
            }

            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<UserEntity, UserDto>().ReverseMap();
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        var code = Join(generated);
        Assert.Contains("internal static global::App.UserDto MapToUserDto(this global::App.UserEntity source)", code);
        Assert.Contains("internal static global::App.UserEntity MapToUserEntity(this global::App.UserDto source)", code);
    }

    [Fact]
    public void MapToAttribute_PublicSourceAndDestination_KeepsPublicMethods()
    {
        var source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(UserDto))]
            public class UserEntity { public int Id { get; set; } }

            public class UserDto { public int Id { get; set; } }
            """;

        var (generated, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertCompiles(errors);
        var code = Join(generated);
        Assert.Contains("public static global::App.UserDto MapToUserDto(this global::App.UserEntity source)", code);
        Assert.DoesNotContain("internal static", code);
    }

    // ------------------------------------------------------------------
    // Companion methods: in-place, List, Array, hooks, transformers
    // ------------------------------------------------------------------

    [Fact]
    public void InternalTypes_AllCompanionMethodsUseInternalAccessibility()
    {
        var source = """
            using IronMapper.Configuration;

            namespace App;

            internal class OrderEntity
            {
                public int Id { get; set; }
                public string Note { get; set; } = "";
                public decimal Total { get; set; }
            }

            internal class OrderDto
            {
                public int Id { get; set; }
                public string Note { get; set; } = "";
                public decimal Total { get; set; }
                public bool Mapped { get; set; }
            }

            internal class OrderProfile : MappingProfile
            {
                public OrderProfile()
                {
                    AddTransformer<string>(v => v.Trim());
                    AddTransformer<decimal>(v => System.Math.Round(v, 2));
                    CreateMap<OrderEntity, OrderDto>()
                        .BeforeMap((s, d) => d.Mapped = false)
                        .AfterMap((s, d) => d.Mapped = true);
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        var code = Join(generated);
        Assert.Contains("internal static global::App.OrderDto MapToOrderDto(this global::App.OrderEntity source)", code);
        Assert.Contains("internal static void MapToOrderDto(this global::App.OrderEntity source, global::App.OrderDto destination)", code);
        Assert.Contains("internal static global::System.Collections.Generic.List<global::App.OrderDto> MapToOrderDtoList(", code);
        Assert.Contains("internal static global::App.OrderDto[] MapToOrderDtoArray(", code);
        // Hook and transformer helpers stay private (already the least accessible level).
        Assert.Contains("private static void BeforeMap_OrderEntity_OrderDto(", code);
        Assert.Contains("private static void AfterMap_OrderEntity_OrderDto(", code);
        Assert.Contains("private static string TransformValue_String", code);
        Assert.DoesNotContain("public static global::", code);
        Assert.DoesNotContain("public static void", code);
    }

    [Fact]
    public void InternalElementType_NestedCollectionMapping_Compiles()
    {
        var source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(CommentDto))]
            internal class Comment { public string Text { get; set; } = ""; }
            internal class CommentDto { public string Text { get; set; } = ""; }

            [MapTo(typeof(PostDto))]
            public class Post { internal List<Comment> Hidden { get; set; } = new(); public int Id { get; set; } }
            public class PostDto { public int Id { get; set; } }

            [MapTo(typeof(ThreadDto))]
            internal class Thread { public List<Comment> Comments { get; set; } = new(); }
            internal class ThreadDto { public List<CommentDto> Comments { get; set; } = new(); }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        var code = Join(generated);
        Assert.Contains("public static global::App.PostDto MapToPostDto(this global::App.Post source)", code);
        Assert.Contains("internal static global::App.ThreadDto MapToThreadDto(this global::App.Thread source)", code);
    }

    // ------------------------------------------------------------------
    // Nested types
    // ------------------------------------------------------------------

    [Fact]
    public void PublicTypesNestedInInternalClass_GenerateInternalMethodsWithFullNames()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;

            namespace App;

            internal class Container
            {
                [MapTo(typeof(ItemDto))]
                public class Item { public int Id { get; set; } }

                public class ItemDto { public int Id { get; set; } }

                public class Other { public int Id { get; set; } }
                public class OtherDto { public int Id { get; set; } }
            }

            public class ContainerProfile : MappingProfile
            {
                public ContainerProfile()
                {
                    CreateMap<Container.Other, Container.OtherDto>();
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        var code = Join(generated);
        Assert.Contains("internal static global::App.Container.ItemDto MapToItemDto(this global::App.Container.Item source)", code);
        Assert.Contains("internal static global::App.Container.OtherDto MapToOtherDto(this global::App.Container.Other source)", code);
    }

    [Fact]
    public void PublicTypesNestedInPublicClass_KeepPublicMethods()
    {
        var source = """
            using IronMapper.Attributes;

            namespace App;

            public class Container
            {
                [MapTo(typeof(ItemDto))]
                public class Item { public int Id { get; set; } }

                public class ItemDto { public int Id { get; set; } }
            }
            """;

        var (generated, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertCompiles(errors);
        Assert.Contains("public static global::App.Container.ItemDto MapToItemDto(this global::App.Container.Item source)", Join(generated));
    }

    [Fact]
    public void ProtectedInternalNestedType_GeneratesInternalMethods()
    {
        var source = """
            using IronMapper.Attributes;

            namespace App;

            public class Container
            {
                [MapTo(typeof(ItemDto))]
                protected internal class Item { public int Id { get; set; } }

                public class ItemDto { public int Id { get; set; } }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        Assert.Contains("internal static global::App.Container.ItemDto MapToItemDto(this global::App.Container.Item source)", Join(generated));
    }

    [Theory]
    [InlineData("private")]
    [InlineData("protected")]
    [InlineData("private protected")]
    public void InaccessibleNestedType_ReportsIM0010_AndOtherMappingsStillGenerate(string modifier)
    {
        var source = $$"""
            using IronMapper.Attributes;

            namespace App;

            public class Container
            {
                [MapTo(typeof(SecretDto))]
                {{modifier}} class Secret { public int Id { get; set; } }

                public class SecretDto { public int Id { get; set; } }
            }

            [MapTo(typeof(UserDto))]
            public class User { public int Id { get; set; } }
            public class UserDto { public int Id { get; set; } }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0010", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var message = diagnostic.GetMessage();
        Assert.Contains("App.Container.Secret", message);
        Assert.Contains("App.Container.SecretDto", message);

        // The inaccessible mapping produces no code, so nothing references the private type.
        AssertCompiles(errors);
        var code = Join(generated);
        Assert.DoesNotContain("MapToSecretDto", code);
        Assert.Contains("public static global::App.UserDto MapToUserDto(this global::App.User source)", code);
    }

    [Fact]
    public void Profile_PrivateNestedDestination_ReportsIM0010_AndOtherMappingsStillGenerate()
    {
        var source = """
            using IronMapper.Configuration;

            namespace App;

            public class User { public int Id { get; set; } }
            public class UserDto { public int Id { get; set; } }

            public class Outer
            {
                private class HiddenDto { public int Id { get; set; } }

                public class OuterProfile : MappingProfile
                {
                    public OuterProfile()
                    {
                        CreateMap<User, HiddenDto>();
                        CreateMap<User, UserDto>();
                    }
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        var diagnostic = Assert.Single(generatorDiagnostics);
        Assert.Equal("IM0010", diagnostic.Id);
        Assert.Contains("App.Outer.HiddenDto", diagnostic.GetMessage());
        AssertCompiles(errors);
        var code = Join(generated);
        Assert.DoesNotContain("MapToHiddenDto", code);
        Assert.Contains("public static global::App.UserDto MapToUserDto(this global::App.User source)", code);
    }

    // ------------------------------------------------------------------
    // Type arguments count towards effective accessibility
    // ------------------------------------------------------------------

    [Fact]
    public void GenericSourceWithInternalTypeArgument_GeneratesInternalMethods()
    {
        var source = """
            using IronMapper.Configuration;

            namespace App;

            public class Box<T> { public int Id { get; set; } }
            internal class Payload { }
            public class BoxDto { public int Id { get; set; } }

            public class BoxProfile : MappingProfile
            {
                public BoxProfile()
                {
                    CreateMap<Box<Payload>, BoxDto>();
                }
            }
            """;

        var (generated, generatorDiagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        Assert.Empty(generatorDiagnostics);
        AssertCompiles(errors);
        Assert.Contains("internal static global::App.BoxDto MapToBoxDto(this global::App.Box<global::App.Payload> source)", Join(generated));
    }
}
