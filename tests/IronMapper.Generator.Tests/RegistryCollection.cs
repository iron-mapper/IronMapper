using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Tests that run compiled code through the process-wide <c>MapperRegistry</c> (<c>IMapper</c>, mapping by type). They do
/// not run in parallel with each other: <see cref="RegistrationGeneratorTests"/> turns the feature switch
/// <c>IronMapper.Registration.IsSupported</c> off for the whole process for one test, and a compiled assembly whose
/// module initializer runs meanwhile would not register its mappings.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RegistryCollection
{
    public const string Name = "MapperRegistry";
}
