using Microsoft.CodeAnalysis;

namespace IronMapper.Generator;

/// <summary>
/// Roslyn Incremental Source Generator that produces compile-time mapping code
/// from [MapTo] / [MapFrom] attribute annotations and MappingProfile declarations.
/// </summary>
[Generator]
public sealed class IronMapperGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Implementation will be added in subsequent prompts.
    }
}
