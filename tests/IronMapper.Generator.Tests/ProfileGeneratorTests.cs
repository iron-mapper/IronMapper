using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Verifies that <see cref="IronMapperGenerator"/> correctly discovers
/// <c>MappingProfile</c> subclasses and emits the expected mapping code.
/// Also covers edge-case branches in <c>ProfileAnalyzer</c>.
/// </summary>
public class ProfileGeneratorTests
{
    // ------------------------------------------------------------------
    // Profile detection
    // ------------------------------------------------------------------

    [Fact]
    public void ProfileDetection_ClassInheritingMappingProfile_GeneratesMapper()
    {
        var source = """
            using IronMapper.Configuration;

            public class ProductEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class ProductDto    { public int Id { get; set; } public string Name { get; set; } = ""; }

            public class ProductProfile : MappingProfile
            {
                public ProductProfile()
                {
                    CreateMap<ProductEntity, ProductDto>();
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToProductDto", code);
        Assert.Contains("Id = source.Id", code);
        Assert.Contains("Name = source.Name", code);
    }

    // ------------------------------------------------------------------
    // ForMember + MapFrom lambda
    // ------------------------------------------------------------------

    [Fact]
    public void ForMemberInCode_ForMemberMapFrom_EmitsLambdaBodyInGeneratedCode()
    {
        var source = """
            using IronMapper.Configuration;

            public class UserEntity
            {
                public string FirstName { get; set; } = "";
                public string LastName  { get; set; } = "";
            }

            public class UserDto
            {
                public string FullName { get; set; } = "";
            }

            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<UserEntity, UserDto>()
                        .ForMember(dest => dest.FullName,
                                   opt  => opt.MapFrom(src => src.FirstName + " " + src.LastName));
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToUserDto", code);
        // The lambda body should be emitted verbatim (with "source" as the param name).
        Assert.Contains("source.FirstName", code);
        Assert.Contains("source.LastName", code);
        Assert.Contains("FullName =", code);
    }

    // ------------------------------------------------------------------
    // ForMember + Ignore
    // ------------------------------------------------------------------

    [Fact]
    public void ForMemberIgnore_IgnoredProperty_NotEmittedInGeneratedCode()
    {
        var source = """
            using IronMapper.Configuration;

            public class InvoiceEntity { public int Id { get; set; } public string Secret { get; set; } = ""; }
            public class InvoiceDto    { public int Id { get; set; } public string Secret { get; set; } = ""; }

            public class InvoiceProfile : MappingProfile
            {
                public InvoiceProfile()
                {
                    CreateMap<InvoiceEntity, InvoiceDto>()
                        .ForMember(dest => dest.Secret, opt => opt.Ignore());
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("Id = source.Id", code);
        Assert.DoesNotContain("Secret = source.Secret", code);
    }

    // ------------------------------------------------------------------
    // When condition
    // ------------------------------------------------------------------

    [Fact]
    public void WhenCondition_WhenClause_EmittedAsGuardInGeneratedCode()
    {
        var source = """
            using IronMapper.Configuration;

            public class ItemEntity { public int Id { get; set; } public bool IsActive { get; set; } }
            public class ItemDto    { public int Id { get; set; } public bool IsActive { get; set; } }

            public class ItemProfile : MappingProfile
            {
                public ItemProfile()
                {
                    CreateMap<ItemEntity, ItemDto>().When(src => src.IsActive);
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        // Guard should appear before the return statement.
        Assert.Contains("if (!", code);
        Assert.Contains("source.IsActive", code);
        Assert.Contains("return default", code);
    }

    // ------------------------------------------------------------------
    // ReverseMap
    // ------------------------------------------------------------------

    [Fact]
    public void ReverseMap_ReverseMapCall_GeneratesBothDirections()
    {
        var source = """
            using IronMapper.Configuration;

            public class CatEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class CatDto    { public int Id { get; set; } public string Name { get; set; } = ""; }

            public class CatProfile : MappingProfile
            {
                public CatProfile()
                {
                    CreateMap<CatEntity, CatDto>().ReverseMap();
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToCatDto", code);
        Assert.Contains("MapToCatEntity", code);
    }

    // ------------------------------------------------------------------
    // Branch-coverage: empty constructor / no CreateMap calls
    // ------------------------------------------------------------------

    [Fact]
    public void EmptyConstructor_NoCreateMapCalls_ProducesNoGeneratedSource()
    {
        // A MappingProfile subclass with an empty constructor should not crash
        // and should not produce any generated mapping files.
        var source = """
            using IronMapper.Configuration;

            public class NodeEntity { public int Id { get; set; } }
            public class NodeDto    { public int Id { get; set; } }

            public class EmptyProfile : MappingProfile
            {
                public EmptyProfile() { }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // No CreateMap — nothing should be generated by the profile pipeline.
        Assert.DoesNotContain(generatedSources, s => s.Contains("MapToNodeDto"));
    }

    // ------------------------------------------------------------------
    // Branch-coverage: parenthesised lambda in ForMember dest selector
    // ------------------------------------------------------------------

    [Fact]
    public void ForMember_ParenthesisedDestLambda_ExtractsPropertyNameCorrectly()
    {
        // Uses (dest) => dest.FullName (parenthesised) instead of dest => dest.FullName (simple).
        var source = """
            using IronMapper.Configuration;

            public class EmpEntity { public string First { get; set; } = ""; public string Last { get; set; } = ""; }
            public class EmpDto    { public string FullName { get; set; } = ""; }

            public class EmpProfile : MappingProfile
            {
                public EmpProfile()
                {
                    CreateMap<EmpEntity, EmpDto>()
                        .ForMember((dest) => dest.FullName,
                                   (opt)  => opt.MapFrom((src) => src.First + " " + src.Last));
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("FullName =", code);
        Assert.Contains("source.First", code);
        Assert.Contains("source.Last", code);
    }

    // ------------------------------------------------------------------
    // Branch-coverage: lambda param already named "source" (ReplaceIdentifier no-op)
    // ------------------------------------------------------------------

    [Fact]
    public void ForMember_LambdaParamAlreadyNamedSource_EmitsBodyUnchanged()
    {
        // When the lambda parameter is already called "source", ReplaceIdentifier
        // should be a no-op and the body should still be emitted correctly.
        var source = """
            using IronMapper.Configuration;

            public class BoxEntity { public int Width { get; set; } public int Height { get; set; } }
            public class BoxDto    { public int Area { get; set; } }

            public class BoxProfile : MappingProfile
            {
                public BoxProfile()
                {
                    CreateMap<BoxEntity, BoxDto>()
                        .ForMember(dest => dest.Area,
                                   opt  => opt.MapFrom(source => source.Width * source.Height));
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("Area =", code);
        Assert.Contains("source.Width", code);
        Assert.Contains("source.Height", code);
    }

    // ------------------------------------------------------------------
    // Branch-coverage: three-level profile inheritance
    // ------------------------------------------------------------------

    [Fact]
    public void ProfileDetection_ThreeLevelInheritance_ProfileIsRecognized()
    {
        // GoodsProfile → BaseProfile → MappingProfile (three levels deep).
        // InheritsFrom walks the full base-type chain, so this must be detected.
        var source = """
            using IronMapper.Configuration;

            public class GoodsEntity { public int Id { get; set; } }
            public class GoodsDto    { public int Id { get; set; } }

            public abstract class BaseProfile : MappingProfile { }

            public class GoodsProfile : BaseProfile
            {
                public GoodsProfile()
                {
                    CreateMap<GoodsEntity, GoodsDto>();
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToGoodsDto", code);
        Assert.Contains("Id = source.Id", code);
    }

    // ------------------------------------------------------------------
    // Branch-coverage: standalone chain .Ignore(dest => dest.Prop)
    // ------------------------------------------------------------------

    [Fact]
    public void StandaloneIgnore_TopLevelChainIgnoreCall_IgnoresNamedProperty()
    {
        // Uses the chain-level .Ignore(dest => dest.Prop) shorthand instead of
        // .ForMember(dest => dest.Prop, opt => opt.Ignore()).
        var source = """
            using IronMapper.Configuration;

            public class TaskEntity { public int Id { get; set; } public string InternalRef { get; set; } = ""; }
            public class TaskDto    { public int Id { get; set; } public string InternalRef { get; set; } = ""; }

            public class TaskProfile : MappingProfile
            {
                public TaskProfile()
                {
                    CreateMap<TaskEntity, TaskDto>()
                        .Ignore(dest => dest.InternalRef);
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("Id = source.Id", code);
        Assert.DoesNotContain("InternalRef = source.InternalRef", code);
    }

    // ------------------------------------------------------------------
    // Branch-coverage: MappingAnalyzer — [MapProperty] + [Ignore] on the same source property
    // ------------------------------------------------------------------

    [Fact]
    public void MappingAnalyzer_MapPropertyAndIgnoreOnSameSourceProp_PropertyIsIgnored()
    {
        // When [MapProperty] remaps a source property to a dest property AND [Ignore] is also
        // present, the mapping for that destination property should be skipped in the output.
        var source = """
            using IronMapper.Attributes;

            [MapTo(typeof(ReportDto))]
            public class ReportEntity
            {
                public int Id { get; set; }

                [MapProperty("Title")]
                [Ignore]
                public string InternalCode { get; set; } = "";
            }

            public class ReportDto
            {
                public int Id { get; set; }
                public string Title { get; set; } = "";
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("Id = source.Id", code);
        // Title is mapped from InternalCode which carries [Ignore] — must not appear in output.
        Assert.DoesNotContain("Title = source.InternalCode", code);
    }

    // ------------------------------------------------------------------
    // БЛОК 1 — ConvertUsing<TConverter>() for the whole object
    // ------------------------------------------------------------------

    [Fact]
    public void ConvertUsing_WholeObjectConverter_EmitsConverterCallInsteadOfInitializer()
    {
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Interfaces;

            public class ShapeEntity { public int Width { get; set; } public int Height { get; set; } }
            public class ShapeDto    { public int Area { get; set; } }

            public class ShapeConverter : ITypeConverter<ShapeEntity, ShapeDto>
            {
                public ShapeDto Convert(ShapeEntity source)
                    => new ShapeDto { Area = source.Width * source.Height };
            }

            public class ShapeProfile : MappingProfile
            {
                public ShapeProfile()
                {
                    CreateMap<ShapeEntity, ShapeDto>().ConvertUsing<ShapeConverter>();
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToShapeDto", code);
        // Should delegate to converter, NOT use object initializer
        Assert.Contains("ShapeConverter", code);
        Assert.Contains(".Convert(source)", code);
    }

    // ------------------------------------------------------------------
    // БЛОК 1 — ConvertUsing(lambda)
    // ------------------------------------------------------------------

    [Fact]
    public void ConvertUsing_LambdaBody_EmitsLambdaReturnInsteadOfInitializer()
    {
        var source = """
            using IronMapper.Configuration;
            using System;

            public class TempEntity { public double Celsius { get; set; } }
            public class TempDto    { public double Fahrenheit { get; set; } }

            public class TempProfile : MappingProfile
            {
                public TempProfile()
                {
                    CreateMap<TempEntity, TempDto>()
                        .ConvertUsing(src => new TempDto { Fahrenheit = src.Celsius * 9.0 / 5.0 + 32.0 });
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToTempDto", code);
        // Lambda body must appear verbatim (with source renaming)
        Assert.Contains("source.Celsius", code);
        Assert.Contains("Fahrenheit", code);
    }

    // ------------------------------------------------------------------
    // БЛОК 1 — ForMember UseConverter<T>() on a single property
    // ------------------------------------------------------------------

    [Fact]
    public void UseConverter_SingleProperty_EmitsConverterCallForThatProperty()
    {
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Interfaces;

            public class PaymentEntity { public int Id { get; set; } public decimal Amount { get; set; } }
            public class PaymentDto    { public int Id { get; set; } public string Amount { get; set; } = ""; }

            public class DecimalToStringConverter : ITypeConverter<decimal, string>
            {
                public string Convert(decimal source) => source.ToString("F2");
            }

            public class PaymentProfile : MappingProfile
            {
                public PaymentProfile()
                {
                    CreateMap<PaymentEntity, PaymentDto>()
                        .ForMember(dest => dest.Amount,
                                   opt  => opt.UseConverter<DecimalToStringConverter>());
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToPaymentDto", code);
        Assert.Contains("Id = source.Id", code);
        Assert.Contains("DecimalToStringConverter", code);
        Assert.Contains(".Convert(source.Amount)", code);
    }

    // ------------------------------------------------------------------
    // БЛОК 3 — MapToDestTypeList and MapToDestTypeArray are always generated
    // ------------------------------------------------------------------

    [Fact]
    public void Generator_AlwaysEmitsListAndArrayCollectionMethods()
    {
        var source = """
            using IronMapper.Configuration;

            public class NoteEntity { public int Id { get; set; } }
            public class NoteDto    { public int Id { get; set; } }

            public class NoteProfile : MappingProfile
            {
                public NoteProfile()
                {
                    CreateMap<NoteEntity, NoteDto>();
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToNoteDtoList", code);
        Assert.Contains("MapToNoteDtoArray", code);
        Assert.Contains("IEnumerable", code);
        Assert.Contains("Array.Empty", code);
    }

    // ------------------------------------------------------------------
    // БЛОК 4 — Nested List<T> property mapped via Select
    // ------------------------------------------------------------------

    [Fact]
    public void NestedList_MappedPropertyType_EmitsSelectInGeneratedCode()
    {
        var source = """
            using IronMapper.Attributes;
            using System.Collections.Generic;

            [MapTo(typeof(OrderDto))]
            public class OrderEntity
            {
                public int Id { get; set; }
                public List<LineItemEntity> Items { get; set; } = new();
            }

            [MapTo(typeof(LineItemDto))]
            public class LineItemEntity { public int Quantity { get; set; } }
            public class LineItemDto    { public int Quantity { get; set; } }

            public class OrderDto
            {
                public int Id { get; set; }
                public List<LineItemDto> Items { get; set; } = new();
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToOrderDto", code);
        // Nested collection should emit Select call with MapToLineItemDto
        Assert.Contains("MapToLineItemDto", code);
        Assert.Contains("Select", code);
    }

    // ------------------------------------------------------------------
    // AddTransformer — transformer calls and helper methods are emitted
    // ------------------------------------------------------------------

    [Fact]
    public void AddTransformer_StringAndDecimal_EmitsTransformerCallsAndHelperMethods()
    {
        var source = """
            using IronMapper.Configuration;
            using System;

            public class PriceEntity { public string Name { get; set; } = ""; public decimal Price { get; set; } }
            public class PriceDto    { public string Name { get; set; } = ""; public decimal Price { get; set; } }

            public class PriceProfile : MappingProfile
            {
                public PriceProfile()
                {
                    AddTransformer<string>(v => v.Trim());
                    AddTransformer<decimal>(v => Math.Round(v, 2));
                    CreateMap<PriceEntity, PriceDto>();
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("TransformValue_String_PriceEntity_PriceDto(source.Name)", code);
        Assert.Contains("TransformValue_Decimal_PriceEntity_PriceDto(source.Price)", code);
        Assert.Contains("private static string TransformValue_String_PriceEntity_PriceDto", code);
        Assert.Contains("private static decimal TransformValue_Decimal_PriceEntity_PriceDto", code);
    }

    // ------------------------------------------------------------------
    // IncludeMembers — nested access expression is emitted
    // ------------------------------------------------------------------

    [Fact]
    public void IncludeMembers_SingleMember_EmitsNestedAccessExpression()
    {
        var source = """
            using IronMapper.Configuration;

            public class ContactInfo { public string Email { get; set; } = ""; }
            public class UserEntity  { public int Id { get; set; } public ContactInfo Contact { get; set; } = new(); }
            public class UserDto     { public int Id { get; set; } public string Email { get; set; } = ""; }

            public class UserProfile : MappingProfile
            {
                public UserProfile()
                {
                    CreateMap<UserEntity, UserDto>()
                        .IncludeMembers(s => s.Contact);
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("MapToUserDto", code);
        Assert.Contains("Id = source.Id", code);
        // Email must come from the nested member with null guard.
        Assert.Contains("source.Contact", code);
        Assert.Contains("Email", code);
        Assert.Contains("source.Contact != null", code);
    }

    [Fact]
    public void IncludeMembers_DuplicatePropertyAcrossMembers_FirstMemberWinsInGeneratedCode()
    {
        var source = """
            using IronMapper.Configuration;

            public class ContactInfo  { public string Phone { get; set; } = ""; }
            public class AddressInfo  { public string Phone { get; set; } = ""; public string City { get; set; } = ""; }
            public class CustomerEntity { public ContactInfo Contact { get; set; } = new(); public AddressInfo Address { get; set; } = new(); }
            public class CustomerDto    { public string Phone { get; set; } = ""; public string City { get; set; } = ""; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<CustomerEntity, CustomerDto>()
                        .IncludeMembers(s => s.Contact, s => s.Address);
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        // Phone must come from Contact (first member), not Address.
        Assert.Contains("source.Contact.Phone", code);
        Assert.DoesNotContain("source.Address.Phone", code);
        // City has no conflict — comes from Address.
        Assert.Contains("source.Address.City", code);
    }

    [Fact]
    public void IncludeMembers_ForMemberAlsoPresent_ForMemberTakesPriorityInGeneratedCode()
    {
        var source = """
            using IronMapper.Configuration;

            public class ContactInfo    { public string Email { get; set; } = ""; }
            public class EmployeeEntity { public ContactInfo Contact { get; set; } = new(); }
            public class EmployeeDto    { public string Email { get; set; } = ""; }

            public class EmployeeProfile : MappingProfile
            {
                public EmployeeProfile()
                {
                    CreateMap<EmployeeEntity, EmployeeDto>()
                        .ForMember(dest => dest.Email, opt => opt.MapFrom(src => "hardcoded@example.com"))
                        .IncludeMembers(s => s.Contact);
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("hardcoded@example.com", code);
        Assert.DoesNotContain("source.Contact.Email", code);
    }

    [Fact]
    public void IncludeMembers_ReferenceTypeNestedMember_EmitsNullGuardTernary()
    {
        var source = """
            using IronMapper.Configuration;

            public class AddressInfo  { public string City { get; set; } = ""; }
            public class StoreEntity  { public AddressInfo? Location { get; set; } }
            public class StoreDto     { public string City { get; set; } = ""; }

            public class StoreProfile : MappingProfile
            {
                public StoreProfile()
                {
                    CreateMap<StoreEntity, StoreDto>()
                        .IncludeMembers(s => s.Location);
                }
            }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        Assert.Contains("source.Location != null", code);
        Assert.Contains("source.Location.City", code);
        Assert.Contains("default!", code);
    }

    // ------------------------------------------------------------------
    // БЛОК 2 — In-place void overload is always generated
    // ------------------------------------------------------------------

    [Fact]
    public void Generator_AlwaysEmitsInPlaceVoidOverload()
    {
        var source = """
            using IronMapper.Attributes;

            [MapTo(typeof(WidgetDto))]
            public class WidgetEntity { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class WidgetDto    { public int Id { get; set; } public string Name { get; set; } = ""; }
            """;

        var (generatedSources, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var code = string.Join("\n", generatedSources);
        // In-place method: void return, two parameters (source, destination)
        Assert.Contains("void MapToWidgetDto", code);
        Assert.Contains("destination.Id = source.Id", code);
        Assert.Contains("destination.Name = source.Name", code);
    }
}
