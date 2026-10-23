using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace IronMapper.Integration.Tests;

/// <summary>
/// Layer rules of ArchUnitNET over two assemblies with mappings: IronMapper.Integration.Application and this test assembly.
/// Before 2.0 every assembly declared <c>IronMapper.Generated.GeneratedMappers</c>. With the registration for
/// <c>IMapper</c> (members and nested types with the same full name in every assembly: <c>RegisterIronMapperMappingsCore</c>,
/// its lambdas in <c>&lt;&gt;c</c>, <c>&lt;&gt;O</c>, and <c>ConverterCache`1</c>, <c>IElementMapper`2</c>) ArchUnitNET,
/// which identifies types by full name, attributed the dependencies of one assembly's class to the other's, and a
/// dependency rule between the layers failed (with 1.1.3 it passed). Each assembly now has its own class,
/// <c>GeneratedMappers_{AssemblyName}</c>.
/// </summary>
/// <remarks>
/// The assemblies are read from the output folder (Mono.Cecil), not loaded into the process, and no type of the
/// Application assembly is named here: <see cref="MapperRegistryAssemblyTests"/> needs that assembly not used yet.
/// </remarks>
public class GeneratedTypeNamesArchitectureTests
{
    private const string ApplicationName = "IronMapper.Integration.Application";
    private const string TestsName = "IronMapper.Integration.Tests";

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadFilteredDirectory(AppContext.BaseDirectory, "IronMapper.Integration.*.dll")
        .Build();

    private static Assembly Named(string name)
        => Architecture.Assemblies.Single(a => a.Name.Split(',')[0] == name);

    [Fact]
    public void Application_DoesNotDependOnTheTestAssembly()
        => Types().That().ResideInAssembly(Named(ApplicationName)).As("Application")
            .Should().NotDependOnAny(Types().That().ResideInAssembly(Named(TestsName)).As("Tests"))
            .Check(Architecture);

    [Fact]
    public void GeneratedClassOfTheApplication_DependsOnlyOnItsOwnMappings()
    {
        var generated = Architecture.Types.Single(t => t.FullName == "IronMapper.Generated.GeneratedMappers_IronMapper_Integration_Application");

        Assert.Equal(Named(ApplicationName), generated.Assembly);
        Assert.Contains(generated.Dependencies, d => d.Target.FullName == "IronMapper.Integration.Application.InvoiceDto");
        Assert.DoesNotContain(generated.Dependencies, d => d.Target.Assembly.Equals(Named(TestsName)));
    }
}
