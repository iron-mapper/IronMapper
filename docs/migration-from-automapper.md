# Migrating from AutoMapper

> 📖 **[Русская версия](migration-from-automapper.ru.md)**

IronMapper was designed to make migration from AutoMapper as low-friction as possible. The fluent API is intentionally similar.

---

## Key differences

| | AutoMapper | IronMapper |
|---|---|---|
| Mapping code location | Runtime, reflection-based | Compile time, generated C# |
| Build errors on misconfiguration | No (runtime `AutoMapperConfigurationException`) | Yes (compiler errors / IM00xx warnings) |
| NativeAOT / trimming | Requires extra annotations | Fully compatible out of the box |
| License | MIT | MIT |
| Package size | ~500 KB | ~30 KB |
| Performance | ~2–5 µs per object (reflection) | ~0 µs overhead (plain property assignment) |

---

## API equivalence table

| AutoMapper | IronMapper | Notes |
|---|---|---|
| `[assembly: AutoMapper.AutoMapAttribute]` | `[MapTo(typeof(TDest))]` on source | Attribute on the source type |
| `CreateMap<Src, Dest>()` in a `Profile` | `CreateMap<Src, Dest>()` in a `MappingProfile` | Same method name |
| `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | Identical syntax |
| `ForMember(d => d.X, o => o.Ignore())` | `ForMember(d => d.X, o => o.Ignore())` | Identical syntax |
| `o.MapFrom<TConverter>()` | `opt.UseConverter<TConverter>()` | Slight name difference |
| `ConvertUsing<TConverter>()` | `ConvertUsing<TConverter>()` | Same method name |
| `cfg.CreateMapper()` | `services.AddIronMapper(assembly)` | One-line DI registration |
| `mapper.Map<Dest>(source)` | `source.MapToDest()` **or** `mapper.Map<Dest>(source)` | Extension method is preferred |
| `mapper.Map(source, destination)` | `source.MapToDest(destination)` **or** `mapper.Map<Src, Dest>(source, dest)` | In-place update |
| `mapper.Map<IEnumerable<Dest>>(list)` | `list.MapToDestList()` / `list.MapToDestArray()` | Dedicated collection helpers |
| `ReverseMap()` | `.ReverseMap()` | Same method name; generates both directions |
| `BeforeMap((src, dest) => ...)` | `.BeforeMap((src, dest) => ...)` | Same method name |
| `AfterMap((src, dest) => ...)` | `.AfterMap((src, dest) => ...)` | Same method name |
| `IncludeMembers(s => s.Sub)` | `.IncludeMembers(s => s.Sub, ...)` | Flattens nested object properties into the destination by name |
| `ValueTransformers` (global) | `AddTransformer<T>(v => ...)` in a profile | Profile-scoped; applied to every property of the matching type |
| `ForMember(d => d.X, o => o.NullSubstitute("default"))` | `ForMember(d => d.X, o => o.MapFrom(s => s.X ?? "default"))` | Use `MapFrom` with null-coalescing as a workaround |
| `ForMember(d => d.X, o => o.UseDestinationValue())` | **Not supported** | Set default values in the destination constructor instead |
| `ForMember(d => d.X, o => o.Condition(s => s.Active))` | **Not supported** per-member — use `MapFrom` with a conditional expression | `.When()` applies to the entire mapping, not individual members |
| `.Include<SrcChild, DestChild>()` | **Not supported** — declare each mapping separately | Mapping inheritance is not implemented |
| `ProjectTo<Dest>(queryable)` | **Not supported** | Compile-time generator cannot rewrite IQueryable expression trees |

---

## Step-by-step migration

### 1. Replace NuGet packages

```bash
dotnet remove package AutoMapper
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

The `IMapper` interface in IronMapper exposes `Map<TDest>(source)`, `Map<TSource, TDest>(source)`, `Map<TSource, TDest>(source, destination)`, and `MapCollection<TSource, TDest>(source)` — all commonly-used AutoMapper overloads.

### 5. Optionally switch to extension methods

AutoMapper requires `IMapper` injection everywhere. IronMapper generates strongly-typed extension methods on the source type, so you can replace DI-injected calls with direct calls where the types are known at compile time:

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
// Generates both MapToOrderDto() and MapToOrderEntity()
```

#### BeforeMap / AfterMap

```csharp
// AutoMapper
cfg.CreateMap<OrderEntity, OrderDto>()
   .BeforeMap((src, dest) => dest.MappedAt = DateTime.UtcNow)
   .AfterMap((src,  dest) => dest.IsHighValue = dest.Total > 1000m);

// IronMapper — identical syntax
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
// Priority: direct properties > ForMember > IncludeMembers (first-member-wins on duplicates).
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
        AddTransformer<string>(v => v.Trim());   // applied to every string property in this profile
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
