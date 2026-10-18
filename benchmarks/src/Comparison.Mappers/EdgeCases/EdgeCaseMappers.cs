using AutoMapper;
using Comparison.Mappers.AutoMapper;
using Comparison.Mappers.IronMapper;
using Comparison.Mappers.Manual;
using Comparison.Mappers.Mapster;
using Comparison.Models.EdgeCases;
using Comparison.Models.Flat;
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

/// <summary>Reference behavior: same-type collections are copied, records built via the constructor, null → null.</summary>
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

    public UserDto? MapNullableUser(UserEntity? src) => src is null ? null : ManualMappings.MapFlat(src);

    public NullableOrderDto MapNullableOrder(NullableOrderEntity src)
    {
        // Reuse the baseline for the non-null parts: map through a temporary OrderEntity.
        var order = ManualMappings.MapNested(new OrderEntity
        {
            Id = src.Id,
            Number = src.Number,
            Customer = src.Customer ?? new CustomerEntity { Address = new AddressEntity() },
            Lines = src.Lines ?? [],
        });
        return new NullableOrderDto
        {
            Id = order.Id,
            Number = order.Number,
            Customer = src.Customer is null ? null : order.Customer,
            Lines = src.Lines is null ? null : order.Lines,
        };
    }
}

public sealed class IronMapperEdgeCaseProfile : MappingProfile
{
    public IronMapperEdgeCaseProfile()
    {
        CreateMap<TagsEntity, TagsDto>();
        // No ForMember: the fact is whether flattening happens by naming convention.
        CreateMap<OrderEntity, OrderFlatDto>();
        // CreateMap<PointEntity, PointRecordDto>() does not compile, see EdgeCaseMappers.IronMapperPositionalRecord.

        // Same documented nested-object workaround as in ComparisonProfile; the lambda has no null check of its own.
        CreateMap<NullableOrderEntity, NullableOrderDto>()
            .ForMember(d => d.Customer, o => o.MapFrom(s => s.Customer!.MapToCustomerDto()));
    }
}

public sealed class IronMapperEdgeCaseMapper : IEdgeCaseMapper
{
    public string Name => MapperNames.IronMapper;
    public TagsDto MapTags(TagsEntity src) => src.MapToTagsDto();
    public PointRecordDto MapPoint(PointEntity src) => throw new NotSupportedException(EdgeCaseMappers.IronMapperPositionalRecord);
    public OrderFlatDto MapFlattening(OrderEntity src) => src.MapToOrderFlatDto();
    public UserDto? MapNullableUser(UserEntity? src) => src!.MapToUserDto();
    public NullableOrderDto MapNullableOrder(NullableOrderEntity src) => src.MapToNullableOrderDto();
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
    public UserDto? MapNullableUser(UserEntity? src) => _mapper.Map<UserEntity?, UserDto?>(src);
    public NullableOrderDto MapNullableOrder(NullableOrderEntity src) => _mapper.Map<NullableOrderEntity, NullableOrderDto>(src);
}

public sealed class MapsterEdgeCaseMapper : IEdgeCaseMapper
{
    private readonly TypeAdapterConfig _config = CreateConfig();

    public string Name => MapperNames.Mapster;

    private static TypeAdapterConfig CreateConfig()
    {
        var config = MapsterSetup.CreateConfig();
        config.NewConfig<TagsEntity, TagsDto>();
        config.NewConfig<PointEntity, PointRecordDto>();
        config.NewConfig<OrderEntity, OrderFlatDto>();
        config.NewConfig<NullableOrderEntity, NullableOrderDto>();
        config.Compile();
        return config;
    }

    public TagsDto MapTags(TagsEntity src) => src.Adapt<TagsEntity, TagsDto>(_config);
    public PointRecordDto MapPoint(PointEntity src) => src.Adapt<PointEntity, PointRecordDto>(_config);
    public OrderFlatDto MapFlattening(OrderEntity src) => src.Adapt<OrderEntity, OrderFlatDto>(_config);
    public UserDto? MapNullableUser(UserEntity? src) => src.Adapt<UserEntity?, UserDto?>(_config);
    public NullableOrderDto MapNullableOrder(NullableOrderEntity src) => src.Adapt<NullableOrderEntity, NullableOrderDto>(_config);
}

public sealed class AutoMapperEdgeCaseMapper : IEdgeCaseMapper
{
    private readonly IMapper _mapper = new MapperConfiguration(cfg =>
    {
        cfg.LicenseKey = AutoMapperSetup.ResolveLicenseKey();
        cfg.AddProfile<ComparisonAutoMapperProfile>();
        cfg.CreateMap<TagsEntity, TagsDto>();
        cfg.CreateMap<PointEntity, PointRecordDto>();
        cfg.CreateMap<OrderEntity, OrderFlatDto>();
        cfg.CreateMap<NullableOrderEntity, NullableOrderDto>();
    }, NullLoggerFactory.Instance).CreateMapper();

    public string Name => MapperNames.AutoMapper;
    public TagsDto MapTags(TagsEntity src) => _mapper.Map<TagsEntity, TagsDto>(src);
    public PointRecordDto MapPoint(PointEntity src) => _mapper.Map<PointEntity, PointRecordDto>(src);
    public OrderFlatDto MapFlattening(OrderEntity src) => _mapper.Map<OrderEntity, OrderFlatDto>(src);
    public UserDto? MapNullableUser(UserEntity? src) => _mapper.Map<UserEntity?, UserDto?>(src);
    public NullableOrderDto MapNullableOrder(NullableOrderEntity src) => _mapper.Map<NullableOrderEntity, NullableOrderDto>(src);
}
