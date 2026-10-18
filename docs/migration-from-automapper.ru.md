# Миграция с AutoMapper

> 🌐 **[English version](migration-from-automapper.md)**

Fluent API IronMapper намеренно похож на AutoMapper, но это не прямая замена. В этом руководстве перечислено, что переносится один к одному, что совпадает лишь частично и чего нет.

---

## Ключевые отличия

| | AutoMapper | IronMapper |
|---|---|---|
| Откуда берётся код маппинга | План маппинга строится во время выполнения и компилируется из expression tree ([docs](https://docs.automapper.io/en/latest/Configuration.html)) | C#-код, сгенерированный при компиляции |
| Ошибки конфигурации | Сообщает `AssertConfigurationIsValid()`, обычно вызываемый в unit-тесте ([docs](https://docs.automapper.io/en/latest/Configuration-validation.html)) | Часть — при сборке: диагностики IM00xx или ошибки C# в сгенерированном коде. Несколько объявленных диагностик пока не выдаются, см. [диагностики](diagnostics.ru.md) |
| Сервис `IMapper` | Основной API | Необязательный (`IronMapper.Extensions.DI`); вызывает сгенерированные методы через reflection |
| NativeAOT / trimming | — | Не проверялось. Сгенерированные extension-методы не используют reflection; `IMapper` использует |
| Лицензия | 14.x и ранее: MIT. 15.0+: RPL-1.5 или коммерческая лицензия, бесплатная Community-лицензия для подходящих организаций ([automapper.io](https://automapper.io/)) | MIT |

Сравнение производительности и размера пакета пока не публикуется.

---

## Таблица соответствия API

Обозначения в колонке **Статус**: **Поддерживается** — есть тест, который выполняет сгенерированный маппинг;
**Частично** — работает с ограничениями или тестируется только текст сгенерированного кода; **Не поддерживается**.

| AutoMapper | IronMapper | Статус | Тесты / примечания |
|---|---|---|---|
| `CreateMap<Src, Dest>()` в `Profile` | `CreateMap<Src, Dest>()` в `MappingProfile` | Поддерживается | `ProfileTests.SimpleProfileMapping_MatchingProperties_AreCopiedByName` |
| `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | то же | Поддерживается | `ProfileTests.FluentMapping_ForMemberMapFrom_ConcatenatesSourceProperties`, `ProfileTests.LambdaExpression_MapFromLambda_IsEvaluatedCorrectly`, `ProfileLambdaGeneratorTests`. Лямбда переписывается в сгенерированный код с полными именами; без членов экземпляра профиля и локальных переменных конструктора (IM0012), без private/protected членов (IM0011), см. [Лямбды в профилях](configuration.ru.md#лямбды-в-профилях) |
| `ForMember(d => d.X, o => o.Ignore())` | то же | Поддерживается | `ProfileTests.ForMemberIgnore_IgnoredProperty_RemainsDefault`, `ProfileGeneratorTests.ForMemberIgnore_IgnoredProperty_NotEmittedInGeneratedCode` |
| `[AutoMap(typeof(Src))]` на destination ([docs](https://docs.automapper.io/en/latest/Attribute-mapping.html)) | `[MapTo(typeof(Dest))]` на source | Поддерживается | `SimpleTypesTests.PrimitivesMapping_*`, `EdgeCaseTests.DogEntity_OwnAndInheritedProperties_AllMapped` |
| `[AutoMap(typeof(Src))]` на destination | `[MapFrom(typeof(Src))]` на destination | Частично | Тестируется только сгенерированный код: `BasicGeneratorTests.MapFromAttribute_WhenAppliedToDestClass_GeneratesMapToExtensionMethod` |
| `services.AddAutoMapper(...)` | `services.AddIronMapper(assembly)` | Поддерживается | `DiIntegrationTests.AddIronMapper_RegistersIMapper`. Регистрирует `IMapper` и типы профилей; никакой конфигурации маппинга не строит |
| `mapper.Map<Dest>(source)` | `source.MapToDest()` **или** `mapper.Map<Dest>(source)` | Поддерживается | `DiIntegrationTests.Mapper_MapsCorrectly_ViaIMapper`. `IMapper` сопоставляет точный runtime-тип source |
| `mapper.Map(source, destination)` | `source.MapToDest(destination)` **или** `mapper.Map<Src, Dest>(source, dest)` | Частично | `CollectionMappingTests.InPlace_UpdatesExistingObject`, `DiIntegrationTests.RuntimeMapper_MapInPlace_UpdatesExistingObject`. Игнорирует `When`, `ConvertUsing`, `BeforeMap`/`AfterMap`; пропускает свойства `init` |
| `mapper.Map<List<Dest>>(list)` | `list.MapToDestList()` / `list.MapToDestArray()` / `mapper.MapCollection<Src, Dest>(list)` | Частично | `CollectionMappingTests.MapToList_ReturnsAllMappedItems`, `CollectionMappingTests.MapToArray_ReturnsCorrectArray`, `DiIntegrationTests.RuntimeMapper_MapCollection_MapsAllItems`. `mapper.Map<List<Dest>>(list)` компилируется, но бросает `MappingException` ([#8](https://github.com/iron-mapper/IronMapper/issues/8)) |
| `ReverseMap()` | `.ReverseMap()` | Частично | `ProfileTests.ReverseMap_ReverseExtensionMethod_MapsDestToSource`. Обратное направление сопоставляет только по именам; `ForMember`, `When`, хуки и `IncludeMembers` не переносятся |
| `BeforeMap((src, dest) => ...)` / `AfterMap(...)` | `.BeforeMap(...)` / `.AfterMap(...)` | Поддерживается | `HookTests.HookOrder_BeforeMapSeesEmptyDest_AfterMapSeesFullDest` и другие `HookTests`. Не вызываются in-place перегрузкой |
| `IncludeMembers(s => s.Sub)` | `.IncludeMembers(s => s.Sub, ...)` | Поддерживается | `IncludeMembersTests` (например, `IncludeMembers_ForMemberWins_OverIncludedMember`, `IncludeMembers_FirstMemberWins_OnDuplicatePropertyName`) |
| `cfg.ValueTransformers.Add<T>(...)` (глобально) | `AddTransformer<T>(v => ...)` в профиле | Поддерживается | `TransformerTests.AddTransformer_StringProperty_ValueIsTrimmed`, `TransformerTests.AddTransformer_DoesNotAffectForMemberProperties`. Действует в пределах профиля; к свойствам из `ForMember` не применяется |
| `opt.ConvertUsing<TValueConverter, TSrcMember>()` ([value converters](https://docs.automapper.io/en/latest/Value-converters.html)) | `opt.UseConverter<TConverter>()` с `ITypeConverter<TSrcMember, TDestMember>` | Частично | Тестируется только сгенерированный код: `ProfileGeneratorTests.UseConverter_SingleProperty_EmitsConverterCallForThatProperty`. Converter создаётся через `new` |
| `ConvertUsing<TTypeConverter>()` ([type converters](https://docs.automapper.io/en/latest/Custom-type-converters.html)) | `ConvertUsing<TConverter>()` с `ITypeConverter<TSource, TDest>` (только `Convert(source)`) | Частично | Тестируется только сгенерированный код: `ProfileGeneratorTests.ConvertUsing_WholeObjectConverter_EmitsConverterCallInsteadOfInitializer`. Не применяется in-place перегрузкой |
| — (только в IronMapper: атрибут конвертера для свойства) | `[MapConverter(typeof(TConverter))]` | Поддерживается | `DiIntegrationTests.Mapper_WithCustomConverter_ViaServiceProvider` (converter всё равно создаётся через `new`, а не берётся из DI) |
| `opt.NullSubstitute("x")` | `opt.MapFrom(s => s.X ?? "x")` | Обходной путь | Через `MapFrom` |
| `opt.Condition(s => ...)` | **Не поддерживается** на уровне члена; `MapFrom(s => cond ? s.X : default)` или `.When(...)` для всего маппинга | Обходной путь | `.When`: `ProfileTests.WhenCondition_ConditionFalse_ReturnsDefault` |
| `opt.MapFrom<TValueResolver>()` ([resolvers](https://docs.automapper.io/en/latest/Custom-value-resolvers.html)) | **Не поддерживается** | — | Используйте лямбду в `MapFrom` |
| `opt.UseDestinationValue()` | **Не поддерживается** | — | |
| `Include<,>()` / `IncludeBase<,>()` | **Не поддерживается** | — | Объявляйте каждый маппинг отдельно |
| `ResolutionContext.Items` / `opts.Items[...]` | **Не поддерживается** | — | [#7](https://github.com/iron-mapper/IronMapper/issues/7) |
| `ProjectTo<Dest>(queryable)` | **Пока не поддерживается** | Запланировано | [#9](https://github.com/iron-mapper/IronMapper/issues/9) |
| `AssertConfigurationIsValid()` | Прямого аналога нет | — | Часть проверок выполняется при сборке, см. [диагностики](diagnostics.ru.md) |

---

## Пошаговая миграция

### 1. Замени NuGet-пакеты

```bash
dotnet remove package AutoMapper
# только для AutoMapper < 13; начиная с 13.0 AddAutoMapper входит в основной пакет
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

Интерфейс `IMapper` в IronMapper предоставляет `Map<TDest>(source)`, `Map<TSource, TDest>(source)`, `Map<TSource, TDest>(source, destination)` и `MapCollection<TSource, TDest>(source)`. Перегрузки `Map` для коллекций нет (`Map<List<Dest>>(list)` бросает исключение, [#8](https://github.com/iron-mapper/IronMapper/issues/8)), передачи опций/контекста в вызов тоже нет ([#7](https://github.com/iron-mapper/IronMapper/issues/7)).

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
// Генерирует и MapToOrderDto(), и MapToOrderEntity().
// Обратное направление сопоставляет свойства только по именам.
```

#### BeforeMap / AfterMap

```csharp
// AutoMapper
cfg.CreateMap<OrderEntity, OrderDto>()
   .BeforeMap((src, dest) => dest.MappedAt = DateTime.UtcNow)
   .AfterMap((src,  dest) => dest.IsHighValue = dest.Total > 1000m);

// IronMapper — идентичный синтаксис (in-place перегрузка хуки не вызывает)
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
// Приоритет: ForMember > прямые свойства source > IncludeMembers (при конфликте выигрывает первый указанный член).
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
        AddTransformer<string>(v => v.Trim());   // применяется к string-свойствам, сопоставленным по имени, в маппингах этого профиля (не к ForMember)
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
