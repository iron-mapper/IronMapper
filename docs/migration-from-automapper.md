# Migrating from AutoMapper

> 📖 **[Русская версия](migration-from-automapper.ru.md)**

IronMapper's fluent API is intentionally similar to AutoMapper's, but it is not a drop-in replacement. This guide lists what maps one-to-one, what only partly matches, and what is missing.

---

## Key differences

| | AutoMapper | IronMapper |
|---|---|---|
| Where mapping code comes from | Mapping plans built at runtime and compiled from expression trees ([docs](https://docs.automapper.io/en/latest/Configuration.html)) | C# generated at compile time |
| Configuration errors | Reported by `AssertConfigurationIsValid()`, typically called in a unit test ([docs](https://docs.automapper.io/en/latest/Configuration-validation.html)) | Some at build time: IM00xx diagnostics, or C# errors in the generated code. Several declared diagnostics are not reported yet, see [diagnostics](diagnostics.md) |
| `IMapper` service | Core API | Optional (`IronMapper.Extensions.DI`); calls the generated methods through delegates of a generated registry (up to 1.1.4 via reflection) |
| NativeAOT / trimming | Not supported: warnings on publish, fails at run time ([results](../benchmarks/results/aot.md)) | Generated extension methods: no warnings, work. `IMapper`: no warnings, works from 1.2.0; up to 1.1.4 it failed at run time ([details](advanced.md#nativeaot-and-trimming)) |
| License | 14.x and earlier: MIT. 15.0+: RPL-1.5 or commercial license, free Community license for eligible organizations ([automapper.io](https://automapper.io/)) | MIT |

Performance and size, IronMapper 1.1.3 vs AutoMapper 16.2.0 (.NET 10, one machine; ratio to hand-written code):
flat object 11.4 ns (×0.98) vs 34.6 ns (×2.97); nested order 209.8 ns (×1.26) vs 270.7 ns (×1.63); cold start
+2.2 ms vs +73.5 ms over a console app without a mapper; framework-dependent publish +28.2 KB vs +1.17 MB (AutoMapper
adds 9 assemblies, IronMapper one). Through `IMapper` IronMapper is slower than its extension methods (flat object 31.0 ns,
×2.66). Full results, including Mapperly and Mapster: [`benchmarks/results/RESULTS.md`](../benchmarks/results/RESULTS.md).

---

## API equivalence table

**Status** legend: **Supported** — covered by a test that runs the generated mapping; **Partial** — works with
limitations, or only the emitted code text is tested; **Not supported**.

| AutoMapper | IronMapper | Status | Tests / notes |
|---|---|---|---|
| `CreateMap<Src, Dest>()` in a `Profile` | `CreateMap<Src, Dest>()` in a `MappingProfile` | Supported | `ProfileTests.SimpleProfileMapping_MatchingProperties_AreCopiedByName` |
| `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | same | Supported | `ProfileTests.FluentMapping_ForMemberMapFrom_ConcatenatesSourceProperties`, `ProfileTests.LambdaExpression_MapFromLambda_IsEvaluatedCorrectly`, `ProfileLambdaGeneratorTests`. The lambda is rewritten into generated code with fully qualified names; no profile instance members or constructor locals (IM0012), no private/protected members (IM0011), see [Profile lambdas](configuration.md#profile-lambdas) |
| `ForMember(d => d.X, o => o.Ignore())` | same | Supported | `ProfileTests.ForMemberIgnore_IgnoredProperty_RemainsDefault`, `ProfileGeneratorTests.ForMemberIgnore_IgnoredProperty_NotEmittedInGeneratedCode` |
| `[AutoMap(typeof(Src))]` on the destination ([docs](https://docs.automapper.io/en/latest/Attribute-mapping.html)) | `[MapTo(typeof(Dest))]` on the source | Supported | `SimpleTypesTests.PrimitivesMapping_*`, `EdgeCaseTests.DogEntity_OwnAndInheritedProperties_AllMapped` |
| `[AutoMap(typeof(Src))]` on the destination | `[MapFrom(typeof(Src))]` on the destination | Partial | Only the emitted code is tested: `BasicGeneratorTests.MapFromAttribute_WhenAppliedToDestClass_GeneratesMapToExtensionMethod` |
| `services.AddAutoMapper(...)` | `services.AddIronMapper(assembly)` | Supported | `DiIntegrationTests.AddIronMapper_RegistersIMapper`, `MapperRegistryAssemblyTests`. Registers `IMapper` and the generated mappings of the assemblies; it does not build any mapping configuration and does not add the profiles to the container |
| `mapper.Map<Dest>(source)` | `source.MapToDest()` **or** `mapper.Map<Dest>(source)` | Supported | `DiIntegrationTests.Mapper_MapsCorrectly_ViaIMapper`, `MapperRegistryTests`. `IMapper` uses the runtime type of the source, then its base types |
| `mapper.Map(source, destination)` | `source.MapToDest(destination)` **or** `mapper.Map<Src, Dest>(source, dest)` | Partial | `CollectionMappingTests.InPlace_UpdatesExistingObject`, `DiIntegrationTests.RuntimeMapper_MapInPlace_UpdatesExistingObject`. Ignores `When`, `ConvertUsing`, `BeforeMap`/`AfterMap`; skips `init` properties |
| `mapper.Map<List<Dest>>(list)` | `list.MapToDestList()` / `list.MapToDestArray()` / `mapper.MapCollection<Src, Dest>(list)` | Partial | `CollectionMappingTests.MapToList_ReturnsAllMappedItems`, `CollectionMappingTests.MapToArray_ReturnsCorrectArray`, `DiIntegrationTests.RuntimeMapper_MapCollection_MapsAllItems`, `MapperRegistryTests.MapByRuntimeType_ListToList`. `mapper.Map<List<Dest>>(list)`, `Map<Dest[]>` and `Map<IEnumerable<Dest>>` work from 1.2.0 ([#8](https://github.com/iron-mapper/IronMapper/issues/8)); up to 1.1.4 they threw `MappingException` |
| `ReverseMap()` | `.ReverseMap()` | Partial | `ProfileTests.ReverseMap_ReverseExtensionMethod_MapsDestToSource`. The reverse direction uses name matching only; `ForMember`, `When`, hooks and `IncludeMembers` are not reversed |
| `BeforeMap((src, dest) => ...)` / `AfterMap(...)` | `.BeforeMap(...)` / `.AfterMap(...)` | Supported | `HookTests.HookOrder_BeforeMapSeesEmptyDest_AfterMapSeesFullDest` and other `HookTests`. Not called by the in-place overload |
| `IncludeMembers(s => s.Sub)` | `.IncludeMembers(s => s.Sub, ...)` | Supported | `IncludeMembersTests` (e.g. `IncludeMembers_ForMemberWins_OverIncludedMember`, `IncludeMembers_FirstMemberWins_OnDuplicatePropertyName`) |
| `cfg.ValueTransformers.Add<T>(...)` (global) | `AddTransformer<T>(v => ...)` in a profile | Supported | `TransformerTests.AddTransformer_StringProperty_ValueIsTrimmed`, `TransformerTests.AddTransformer_DoesNotAffectForMemberProperties`. Profile-scoped; not applied to `ForMember` properties |
| `opt.ConvertUsing<TValueConverter, TSrcMember>()` ([value converters](https://docs.automapper.io/en/latest/Value-converters.html)) | `opt.UseConverter<TConverter>()` with `ITypeConverter<TSrcMember, TDestMember>` | Partial | `ConverterCacheTests.UseConverter_StatelessCachedAndStatefulNew`. Created by the generated code, not resolved from DI: one instance of a converter without state, `new` per call otherwise ([Converter instances](configuration.md#converter-instances)) |
| `ConvertUsing<TTypeConverter>()` ([type converters](https://docs.automapper.io/en/latest/Custom-type-converters.html)) | `ConvertUsing<TConverter>()` with `ITypeConverter<TSource, TDest>` (`Convert(source)` only) | Partial | `ConverterCacheTests.ConvertUsing_StatelessCachedAndStatefulNew`. Not applied by the in-place overload |
| — (IronMapper-specific: per-property converter attribute) | `[MapConverter(typeof(TConverter))]` | Supported | `DiIntegrationTests.Mapper_WithCustomConverter_ViaServiceProvider`, `ConverterCacheTests` (the converter is created by the generated code, not resolved from DI) |
| `opt.NullSubstitute("x")` | `opt.MapFrom(s => s.X ?? "x")` | Workaround | Uses `MapFrom` |
| `opt.Condition(s => ...)` | **Not supported** per member; `MapFrom(s => cond ? s.X : default)` or `.When(...)` for the whole mapping | Workaround | `.When`: `ProfileTests.WhenCondition_ConditionFalse_ReturnsDefault` |
| `opt.MapFrom<TValueResolver>()` ([resolvers](https://docs.automapper.io/en/latest/Custom-value-resolvers.html)) | **Not supported** | — | Use a `MapFrom` lambda |
| `opt.UseDestinationValue()` | **Not supported** | — | |
| `Include<,>()` / `IncludeBase<,>()` | **Not supported** | — | Declare each mapping separately |
| `ResolutionContext.Items` / `opts.Items[...]` | **Not supported** | — | [#7](https://github.com/iron-mapper/IronMapper/issues/7) |
| `ProjectTo<Dest>(queryable)` | **Not supported yet** | Planned | [#9](https://github.com/iron-mapper/IronMapper/issues/9) |
| Flattening by convention (`CustomerAddressCity` from `Customer.Address.City`) | **Not supported** | — | Use `MapFrom` or `IncludeMembers`; the property is silently left unset in profile mappings ([#25](https://github.com/iron-mapper/IronMapper/issues/25)) |
| Nested single objects mapped by the configured map | **Not supported** | Workaround | `ForMember(d => d.X, o => o.MapFrom(s => s.X.MapToXDto()))`, see [Nested objects](advanced.md#nested-objects) ([#23](https://github.com/iron-mapper/IronMapper/issues/23)) |
| Mapping to a constructor (positional records) | **Not supported** | — | `CS7036` in the generated file; use `init` properties ([#24](https://github.com/iron-mapper/IronMapper/issues/24)) |
| `AssertConfigurationIsValid()` | No direct equivalent | — | Part of the checks happen at build time, see [diagnostics](diagnostics.md) |

---

## Step-by-step migration

### 1. Replace NuGet packages

```bash
dotnet remove package AutoMapper
# only for AutoMapper < 13; since 13.0 AddAutoMapper is part of the core package
dotnet remove package AutoMapper.Extensions.Microsoft.DependencyInjection

dotnet add package IronMapper
dotnet add package IronMapper.Extensions.DI
```

### 2. Replace profile base class

```diff
- using AutoMapper;
+ using IronMapper.Configuration;

- public class OrderProfile : Profile
+ public class OrderProfile : MappingProfile
```

### 3. Replace DI registration

```diff
- services.AddAutoMapper(typeof(Program).Assembly);
+ services.AddIronMapper(typeof(Program).Assembly);
```

### 4. Replace `IMapper` namespace

```diff
- using AutoMapper;
+ using IronMapper.Interfaces;
```

The `IMapper` interface in IronMapper exposes `Map<TDest>(source)`, `Map<TSource, TDest>(source)`, `Map<TSource, TDest>(source, destination)`, and `MapCollection<TSource, TDest>(source)`. Collections are mapped by the destination type (`Map<List<Dest>>(list)`, from 1.2.0, [#8](https://github.com/iron-mapper/IronMapper/issues/8)). There are no per-call options/context ([#7](https://github.com/iron-mapper/IronMapper/issues/7)).

### 5. Optionally switch to extension methods

AutoMapper requires `IMapper` injection everywhere. IronMapper generates strongly-typed extension methods on the source type, so you can replace DI-injected calls with direct calls where the types are known at compile time:

With implicit usings the package imports the namespace of the generated methods (`IronMapper.Generated`) itself;
without them, add `using IronMapper.Generated;` to the file.

```diff
- var dto = _mapper.Map<OrderDto>(entity);
+ var dto = entity.MapToOrderDto();
```

### 6. Migrate advanced features

#### ReverseMap

```csharp
// AutoMapper
cfg.CreateMap<OrderEntity, OrderDto>().ReverseMap();

// IronMapper — identical syntax
CreateMap<OrderEntity, OrderDto>().ReverseMap();
// Generates both MapToOrderDto() and MapToOrderEntity().
// The reverse direction matches properties by name only.
```

#### BeforeMap / AfterMap

```csharp
// AutoMapper
cfg.CreateMap<OrderEntity, OrderDto>()
   .BeforeMap((src, dest) => dest.MappedAt = DateTime.UtcNow)
   .AfterMap((src,  dest) => dest.IsHighValue = dest.Total > 1000m);

// IronMapper — identical syntax (not called by the in-place overload)
CreateMap<OrderEntity, OrderDto>()
    .BeforeMap((src, dest) => dest.MappedAt = DateTime.UtcNow)
    .AfterMap((src,  dest) => dest.IsHighValue = dest.Total > 1000m);
```

#### IncludeMembers (flatten nested objects)

```csharp
// AutoMapper
cfg.CreateMap<CustomerOrder, CustomerOrderDto>()
   .IncludeMembers(s => s.Contact, s => s.Shipping);

// IronMapper — identical syntax
CreateMap<CustomerOrder, CustomerOrderDto>()
    .IncludeMembers(s => s.Contact, s => s.Shipping);
// Properties of Contact and Shipping are matched by name to CustomerOrderDto fields.
// Priority: ForMember > direct source properties > IncludeMembers (first listed member wins on duplicates).
```

#### ValueTransformers (AddTransformer)

```csharp
// AutoMapper (global)
cfg.ValueTransformers.Add<string>(v => v?.Trim());

// IronMapper (profile-scoped)
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        AddTransformer<string>(v => v.Trim());   // applied to name-matched string properties of this profile's mappings (not to ForMember)
        AddTransformer<decimal>(v => Math.Round(v, 2));
        CreateMap<OrderEntity, OrderDto>();
    }
}
```

#### NullSubstitute workaround

```csharp
// AutoMapper
.ForMember(d => d.Name, o => o.NullSubstitute("(unknown)"))

// IronMapper workaround
.ForMember(d => d.Name, o => o.MapFrom(s => s.Name ?? "(unknown)"))
```

#### Per-member Condition workaround

```csharp
// AutoMapper — per-member condition
.ForMember(d => d.Discount, o => o.Condition(s => s.IsVip))

// IronMapper workaround — use conditional expression in MapFrom
.ForMember(d => d.Discount, o => o.MapFrom(s => s.IsVip ? s.Discount : 0m))
```
