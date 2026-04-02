# Migrating from AutoMapper

IronMapper was designed to make migration from AutoMapper as low-friction as possible. The fluent API is intentionally similar.

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
| `ReverseMap()` | Not yet supported | Declare both directions explicitly |
| `BeforeMap` / `AfterMap` | Not yet supported | Use `ConvertUsing` lambda as a workaround |
| `IncludeMembers` | Not yet supported | Map included members explicitly |
| `ValueTransformers` | Not yet supported | Use per-property `[MapConverter]` |

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
