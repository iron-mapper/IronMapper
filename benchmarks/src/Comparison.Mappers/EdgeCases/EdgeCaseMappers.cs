using AutoMapper;
using Comparison.Mappers.IronMapper;
using Comparison.Models.EdgeCases;
using Comparison.Models.Nested;
using IronMapper.Configuration;
using IronMapper.Extensions.DI;
using IronMapper.Generated;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using IIronMapper = IronMapper.Interfaces.IMapper;

namespace Comparison.Mappers.EdgeCases;

public static class EdgeCaseMappers
{
    public static IReadOnlyList<IEdgeCaseMapper> CreateAll() =>
    [
        new ManualEdgeCaseMapper(),
        new IronMapperEdgeCaseMapper(),
        new IronMapperDiEdgeCaseMapper(),
        new MapperlyEdgeCaseMapper(),
        new MapsterEdgeCaseMapper(),
        new AutoMapperEdgeCaseMapper(),
    ];

    /// <summary>
    /// IronMapper cannot map to a positional record: the generated <c>new PointRecordDto { ... }</c> fails with
    /// CS7036 (docs/advanced.md, "Record types"; confirmed with 1.1.3, no IronMapper diagnostic is reported).
    /// The mapping is therefore not declared, otherwise the project does not build.
    /// </summary>
    internal const string IronMapperPositionalRecord =
        "does not compile: CS7036 in the generated file (new T { ... } without constructor arguments)";
}

/// <summary>Reference behavior: same-type collections are copied, records built via the constructor.</summary>
public sealed class ManualEdgeCaseMapper : IEdgeCaseMapper
{
    public string Name => MapperNames.Manual;
    public TagsDto MapTags(TagsEntity src) => new() { Id = src.Id, Tags = new List<string>(src.Tags) };
    public PointRecordDto MapPoint(PointEntity src) => new(src.X, src.Y);

    public OrderFlatDto MapFlattening(OrderEntity src) => new()
    {
        Id = src.Id,
        Number = src.Number,
        CustomerName = src.Customer.Name,
        CustomerAddressCity = src.Customer.Address.City,
    };
}

public sealed class IronMapperEdgeCaseProfile : MappingProfile
{
    public IronMapperEdgeCaseProfile()
    {
        CreateMap<TagsEntity, TagsDto>();
        // No ForMember: the fact is whether flattening happens by naming convention.
        CreateMap<OrderEntity, OrderFlatDto>();
        // CreateMap<PointEntity, PointRecordDto>() does not compile, see EdgeCaseMappers.IronMapperPositionalRecord.
    }
}

public sealed class IronMapperEdgeCaseMapper : IEdgeCaseMapper
{
    public string Name => MapperNames.IronMapper;
    public TagsDto MapTags(TagsEntity src) => src.MapToTagsDto();
    public PointRecordDto MapPoint(PointEntity src) => throw new NotSupportedException(EdgeCaseMappers.IronMapperPositionalRecord);
    public OrderFlatDto MapFlattening(OrderEntity src) => src.MapToOrderFlatDto();
}

public sealed class IronMapperDiEdgeCaseMapper : IEdgeCaseMapper
{
    private readonly IIronMapper _mapper = CreateMapper();

    public string Name => MapperNames.IronMapperIMapper;

    private static IIronMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(o => o.AddProfile<ComparisonProfile>().AddProfile<IronMapperEdgeCaseProfile>());
        return services.BuildServiceProvider().GetRequiredService<IIronMapper>();
    }

    public TagsDto MapTags(TagsEntity src) => _mapper.Map<TagsEntity, TagsDto>(src);
    public PointRecordDto MapPoint(PointEntity src) => throw new NotSupportedException(EdgeCaseMappers.IronMapperPositionalRecord);
    public OrderFlatDto MapFlattening(OrderEntity src) => _mapper.Map<OrderEntity, OrderFlatDto>(src);
}

public sealed class MapsterEdgeCaseMapper : IEdgeCaseMapper
{
    private readonly TypeAdapterConfig _config = CreateConfig();

    public string Name => MapperNames.Mapster;

    private static TypeAdapterConfig CreateConfig()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<TagsEntity, TagsDto>();
        config.NewConfig<PointEntity, PointRecordDto>();
        config.NewConfig<OrderEntity, OrderFlatDto>();
        config.Compile();
        return config;
    }

    public TagsDto MapTags(TagsEntity src) => src.Adapt<TagsEntity, TagsDto>(_config);
    public PointRecordDto MapPoint(PointEntity src) => src.Adapt<PointEntity, PointRecordDto>(_config);
    public OrderFlatDto MapFlattening(OrderEntity src) => src.Adapt<OrderEntity, OrderFlatDto>(_config);
}

public sealed class AutoMapperEdgeCaseMapper : IEdgeCaseMapper
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg =>
    {
        cfg.LicenseKey = Environment.GetEnvironmentVariable(global::Comparison.Mappers.AutoMapper.AutoMapperSetup.LicenseKeyVariable);
        cfg.CreateMap<TagsEntity, TagsDto>();
        cfg.CreateMap<PointEntity, PointRecordDto>();
        cfg.CreateMap<OrderEntity, OrderFlatDto>();
    }, NullLoggerFactory.Instance).CreateMapper();

    public string Name => MapperNames.AutoMapper;
    public TagsDto MapTags(TagsEntity src) => _mapper.Map<TagsEntity, TagsDto>(src);
    public PointRecordDto MapPoint(PointEntity src) => _mapper.Map<PointEntity, PointRecordDto>(src);
    public OrderFlatDto MapFlattening(OrderEntity src) => _mapper.Map<OrderEntity, OrderFlatDto>(src);
}
