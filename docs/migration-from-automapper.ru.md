# Миграция с AutoMapper

> 🌐 **[English version](migration-from-automapper.md)**

IronMapper спроектирован так, чтобы миграция с AutoMapper была максимально безболезненной. Fluent API намеренно схож.

---

## Ключевые отличия

| | AutoMapper | IronMapper |
|---|---|---|
| Расположение кода маппинга | Runtime, на основе reflection | Compile time, сгенерированный C# |
| Ошибки конфигурации | Нет (runtime `AutoMapperConfigurationException`) | Да (ошибки компилятора / предупреждения IM00xx) |
| NativeAOT / trimming | Требует дополнительных аннотаций | Полностью совместим из коробки |
| Лицензия | MIT | MIT |
| Размер пакета | ~500 КБ | ~30 КБ |
| Производительность | ~2–5 мкс / объект (reflection) | ~0 нс накладных расходов (обычное присваивание свойств) |

---

## Таблица соответствия API

| AutoMapper | IronMapper | Примечание |
|---|---|---|
| `[assembly: AutoMapper.AutoMapAttribute]` | `[MapTo(typeof(TDest))]` на source | Атрибут на source-типе |
| `CreateMap<Src, Dest>()` в `Profile` | `CreateMap<Src, Dest>()` в `MappingProfile` | Одинаковое имя метода |
| `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | Идентичный синтаксис |
| `ForMember(d => d.X, o => o.Ignore())` | `ForMember(d => d.X, o => o.Ignore())` | Идентичный синтаксис |
| `o.MapFrom<TConverter>()` | `opt.UseConverter<TConverter>()` | Небольшое отличие в имени |
| `ConvertUsing<TConverter>()` | `ConvertUsing<TConverter>()` | Одинаковое имя метода |
| `cfg.CreateMapper()` | `services.AddIronMapper(assembly)` | Однострочная DI-регистрация |
| `mapper.Map<Dest>(source)` | `source.MapToDest()` **или** `mapper.Map<Dest>(source)` | Extension method предпочтителен |
| `mapper.Map(source, destination)` | `source.MapToDest(destination)` **или** `mapper.Map<Src, Dest>(source, dest)` | Обновление существующего объекта |
| `mapper.Map<IEnumerable<Dest>>(list)` | `list.MapToDestList()` / `list.MapToDestArray()` | Специализированные collection helpers |
| `ReverseMap()` | `.ReverseMap()` | Одинаковое имя метода; генерирует оба направления |
| `BeforeMap((src, dest) => ...)` | `.BeforeMap((src, dest) => ...)` | Одинаковое имя метода |
| `AfterMap((src, dest) => ...)` | `.AfterMap((src, dest) => ...)` | Одинаковое имя метода |
| `IncludeMembers(s => s.Sub)` | `.IncludeMembers(s => s.Sub, ...)` | Разворачивает свойства вложенного объекта в destination по имени |
| `ValueTransformers` (глобальные) | `AddTransformer<T>(v => ...)` в профиле | Область действия — профиль; применяется ко всем свойствам совпадающего типа |
| `ForMember(d => d.X, o => o.NullSubstitute("default"))` | `ForMember(d => d.X, o => o.MapFrom(s => s.X ?? "default"))` | Используй `MapFrom` с null-coalescing в качестве обходного решения |
| `ForMember(d => d.X, o => o.UseDestinationValue())` | **Не поддерживается** | Задавай значения по умолчанию в конструкторе destination-типа |
| `ForMember(d => d.X, o => o.Condition(s => s.Active))` | **Не поддерживается** на уровне члена — используй `MapFrom` с условным выражением | `.When()` применяется ко всему маппингу, не к отдельным членам |
| `.Include<SrcChild, DestChild>()` | **Не поддерживается** — объявляй каждый маппинг отдельно | Наследование маппингов не реализовано |
| `ProjectTo<Dest>(queryable)` | **Не поддерживается** | Генератор компил-тайм не может переписывать expression tree IQueryable |

---

## Пошаговая миграция

### 1. Замени NuGet-пакеты

```bash
dotnet remove package AutoMapper
dotnet remove package AutoMapper.Extensions.Microsoft.DependencyInjection

dotnet add package IronMapper
dotnet add package IronMapper.Extensions.DI
```

### 2. Замени базовый класс profile

```diff
- using AutoMapper;
+ using IronMapper.Configuration;

- public class OrderProfile : Profile
+ public class OrderProfile : MappingProfile
```

### 3. Замени DI-регистрацию

```diff
- services.AddAutoMapper(typeof(Program).Assembly);
+ services.AddIronMapper(typeof(Program).Assembly);
```

### 4. Замени namespace для `IMapper`

```diff
- using AutoMapper;
+ using IronMapper.Interfaces;
```

Интерфейс `IMapper` в IronMapper предоставляет `Map<TDest>(source)`, `Map<TSource, TDest>(source)`, `Map<TSource, TDest>(source, destination)` и `MapCollection<TSource, TDest>(source)` — все часто используемые перегрузки AutoMapper.

### 5. Опционально — переходи на extension methods

AutoMapper требует внедрения `IMapper` везде. IronMapper генерирует строго типизированные extension methods на source-типе, поэтому там, где типы известны во время компиляции, можно убрать DI-зависимость:

```diff
- var dto = _mapper.Map<OrderDto>(entity);
+ var dto = entity.MapToOrderDto();
```

### 6. Миграция продвинутых возможностей

#### ReverseMap

```csharp
// AutoMapper
cfg.CreateMap<OrderEntity, OrderDto>().ReverseMap();

// IronMapper — идентичный синтаксис
CreateMap<OrderEntity, OrderDto>().ReverseMap();
// Генерирует и MapToOrderDto(), и MapToOrderEntity()
```

#### BeforeMap / AfterMap

```csharp
// AutoMapper
cfg.CreateMap<OrderEntity, OrderDto>()
   .BeforeMap((src, dest) => dest.MappedAt = DateTime.UtcNow)
   .AfterMap((src,  dest) => dest.IsHighValue = dest.Total > 1000m);

// IronMapper — идентичный синтаксис
CreateMap<OrderEntity, OrderDto>()
    .BeforeMap((src, dest) => dest.MappedAt = DateTime.UtcNow)
    .AfterMap((src,  dest) => dest.IsHighValue = dest.Total > 1000m);
```

#### IncludeMembers (разворачивание вложенных объектов)

```csharp
// AutoMapper
cfg.CreateMap<CustomerOrder, CustomerOrderDto>()
   .IncludeMembers(s => s.Contact, s => s.Shipping);

// IronMapper — идентичный синтаксис
CreateMap<CustomerOrder, CustomerOrderDto>()
    .IncludeMembers(s => s.Contact, s => s.Shipping);
// Свойства Contact и Shipping сопоставляются по имени с полями CustomerOrderDto.
// Приоритет: прямые свойства > ForMember > IncludeMembers (первый член выигрывает при конфликте).
```

#### ValueTransformers (AddTransformer)

```csharp
// AutoMapper (глобально)
cfg.ValueTransformers.Add<string>(v => v?.Trim());

// IronMapper (область действия — профиль)
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        AddTransformer<string>(v => v.Trim());   // применяется ко всем string-свойствам в этом профиле
        AddTransformer<decimal>(v => Math.Round(v, 2));
        CreateMap<OrderEntity, OrderDto>();
    }
}
```

#### Обходное решение для NullSubstitute

```csharp
// AutoMapper
.ForMember(d => d.Name, o => o.NullSubstitute("(неизвестно)"))

// IronMapper — обходное решение
.ForMember(d => d.Name, o => o.MapFrom(s => s.Name ?? "(неизвестно)"))
```

#### Обходное решение для условия на уровне члена

```csharp
// AutoMapper — условие на уровне члена
.ForMember(d => d.Discount, o => o.Condition(s => s.IsVip))

// IronMapper — используй условное выражение в MapFrom
.ForMember(d => d.Discount, o => o.MapFrom(s => s.IsVip ? s.Discount : 0m))
```
