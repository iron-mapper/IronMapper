# Миграция с AutoMapper

> 🌐 **[English version](migration-from-automapper.md)**

Fluent API IronMapper намеренно похож на AutoMapper, но это не прямая замена. В этом руководстве перечислено, что переносится один к одному, что совпадает лишь частично и чего нет.

---

## Ключевые отличия

| | AutoMapper | IronMapper |
|---|---|---|
| Откуда берётся код маппинга | План маппинга строится во время выполнения и компилируется из expression tree ([docs](https://docs.automapper.io/en/latest/Configuration.html)) | C#-код, сгенерированный при компиляции |
| Ошибки конфигурации | Сообщает `AssertConfigurationIsValid()`, обычно вызываемый в unit-тесте ([docs](https://docs.automapper.io/en/latest/Configuration-validation.html)) | Часть — при сборке: диагностики IM00xx или ошибки C# в сгенерированном коде. Несколько объявленных диагностик пока не выдаются, см. [диагностики](diagnostics.ru.md) |
| Сервис `IMapper` | Основной API | Необязательный (`IronMapper.Extensions.DI`); вызывает сгенерированные методы через делегаты сгенерированного реестра (до 1.1.4 включительно — через reflection) |
| NativeAOT / trimming | Не поддерживается: предупреждения при публикации, падает при запуске ([результаты](../benchmarks/results/aot.md)) | Сгенерированные extension-методы: без предупреждений, работают. `IMapper`: без предупреждений, работает начиная с 2.0.0; до 1.1.4 включительно падал при запуске ([подробнее](advanced.ru.md#nativeaot-и-trimming)) |
| Лицензия | 14.x и ранее: MIT. 15.0+: RPL-1.5 или коммерческая лицензия, бесплатная Community-лицензия для подходящих организаций ([automapper.io](https://automapper.io/)) | MIT |

Производительность и размер, IronMapper 2.0.0 против AutoMapper 16.2.0 (.NET 10, один компьютер; отношение к ручному
коду): плоский объект 11,4 нс (×0,99) против 34,0 нс (×2,95); вложенный заказ 177,8 нс (×1,07) против 264,0 нс (×1,59;
2.0.0 не маппит вложенные объекты автоматически, поэтому в IronMapper они настроены вручную через `ForMember(... MapFrom(...))`); холодный старт +1,3 мс
против +72,1 мс к консольному приложению без маппера; framework-dependent publish +47,7 КБ против +1,17 МБ (AutoMapper
добавляет 9 сборок, IronMapper — одну). Через `IMapper` из одного и того же DI-контейнера (плоский объект): IronMapper
15,4 нс против 34,5 нс у AutoMapper. Холодный старт `IMapper` IronMapper — +19,9 мс, из них +15,3 мс — сам DI-контейнер
(+72,1 мс AutoMapper выше измерены без контейнера). На .NET 8: плоский объект ×1,01 против ×3,26, вложенный ×1,09 против
×1,49. Полные результаты, включая Mapperly и Mapster: [`benchmarks/results/RESULTS.md`](../benchmarks/results/RESULTS.md) (на английском).

---

## Таблица соответствия API

Обозначения в колонке **Статус**: **Поддерживается** — есть тест, который выполняет сгенерированный маппинг;
**Частично** — работает с ограничениями или тестируется только текст сгенерированного кода; **Не поддерживается**;
**Отличается** — поддерживается, но результат намеренно отличается от AutoMapper.

| AutoMapper | IronMapper | Статус | Тесты / примечания |
|---|---|---|---|
| `CreateMap<Src, Dest>()` в `Profile` | `CreateMap<Src, Dest>()` в `MappingProfile` | Поддерживается | `ProfileTests.SimpleProfileMapping_MatchingProperties_AreCopiedByName` |
| `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | то же | Поддерживается | `ProfileTests.FluentMapping_ForMemberMapFrom_ConcatenatesSourceProperties`, `ProfileTests.LambdaExpression_MapFromLambda_IsEvaluatedCorrectly`, `ProfileLambdaGeneratorTests`. Лямбда переписывается в сгенерированный код с полными именами; без членов экземпляра профиля и локальных переменных конструктора (IM0012), без private/protected членов (IM0011), см. [Лямбды в профилях](configuration.ru.md#лямбды-в-профилях) |
| `ForMember(d => d.X, o => o.MapFrom(Helper.Compute))` (группа методов, `Func<TSource, object?>`) | то же (с 2.1.0) | Поддерживается | `SilentlyIgnoredConfigTests` (генератор и runtime). Статический метод (public или internal), вызывается с source (и в `ForCtorParam`); метод экземпляра, локальная функция или делегат в переменной — `IM0024`. До 2.0.0 включительно молча игнорировалась ([#59](https://github.com/iron-mapper/IronMapper/issues/59)) |
| `[Ignore]` на свойстве типов маппинга профиля ([`[Ignore]`](https://docs.automapper.io/en/latest/Attribute-mapping.html)) | то же (с 2.1.0) | Поддерживается | `SilentlyIgnoredConfigTests` (генератор и runtime): на destination — не заполняется, на source — не читается; новый объект, in-place, `ReverseMap`, `IncludeMembers`, параметры конструктора; `ForMember` этого свойства важнее. До 2.0.0 включительно свойство копировалось ([#61](https://github.com/iron-mapper/IronMapper/issues/61)) |
| `ForMember(d => d.X, o => o.Ignore())` | то же | Поддерживается | `ProfileTests.ForMemberIgnore_IgnoredProperty_RemainsDefault`, `ProfileGeneratorTests.ForMemberIgnore_IgnoredProperty_NotEmittedInGeneratedCode` |
| `[AutoMap(typeof(Src))]` на destination ([docs](https://docs.automapper.io/en/latest/Attribute-mapping.html)) | `[MapTo(typeof(Dest))]` на source | Поддерживается | `SimpleTypesTests.PrimitivesMapping_*`, `EdgeCaseTests.DogEntity_OwnAndInheritedProperties_AllMapped` |
| `[AutoMap(typeof(Src))]` на destination | `[MapFrom(typeof(Src))]` на destination | Частично | Тестируется только сгенерированный код: `BasicGeneratorTests.MapFromAttribute_WhenAppliedToDestClass_GeneratesMapToExtensionMethod` |
| `services.AddAutoMapper(...)` | `services.AddIronMapper(assembly)` | Поддерживается | `DiIntegrationTests.AddIronMapper_RegistersIMapper`, `MapperRegistryAssemblyTests`. Регистрирует `IMapper` и сгенерированные маппинги сборок; никакой конфигурации маппинга не строит и профили в контейнер не добавляет |
| `mapper.Map<Dest>(source)` | `source.MapToDest()` **или** `mapper.Map<Dest>(source)` | Поддерживается | `DiIntegrationTests.Mapper_MapsCorrectly_ViaIMapper`, `MapperRegistryTests`. `IMapper` использует runtime-тип source, затем его базовые типы |
| `mapper.Map(source, destination)` | `source.MapToDest(destination)` **или** `mapper.Map<Src, Dest>(source, dest)` | Частично | `CollectionMappingTests.InPlace_UpdatesExistingObject`, `DiIntegrationTests.RuntimeMapper_MapInPlace_UpdatesExistingObject`. Применяет `When` и `BeforeMap`/`AfterMap` с 2.1.0 (`HooksAndWhenTests`; до 2.0.0 включительно игнорировала их); `ConvertUsing` — нет (`[Obsolete]`); пропускает свойства `init` (предупреждение `IM0025` с 2.1.0; существующий вложенный объект в `init`-свойстве обновляется на месте, `InitInPlaceTests`). Существующие вложенные объекты обновляются, как в AutoMapper (начиная с 2.1.0; `NestedObjectTests.InPlace_TwoLevels_KeepsTheExistingNestedObjectsAndUpdatesThem`, [подробнее](advanced.ru.md#in-place-обновление-вложенных-объектов)) |
| `mapper.Map<List<Dest>>(list)` | `list.MapToDestList()` / `list.MapToDestArray()` / `mapper.MapCollection<Src, Dest>(list)` | Частично | `CollectionMappingTests.MapToList_ReturnsAllMappedItems`, `CollectionMappingTests.MapToArray_ReturnsCorrectArray`, `DiIntegrationTests.RuntimeMapper_MapCollection_MapsAllItems`, `MapperRegistryTests.MapByRuntimeType_ListToList`. `mapper.Map<List<Dest>>(list)`, `Map<Dest[]>` и `Map<IEnumerable<Dest>>` работают начиная с 2.0.0 ([#8](https://github.com/iron-mapper/IronMapper/issues/8)); до 1.1.4 включительно бросали `MappingException` |
| `ReverseMap()` | `.ReverseMap()` | Частично | `ProfileTests.ReverseMap_ReverseExtensionMethod_MapsDestToSource`. Обратное направление сопоставляет только по именам; `ForMember`, `When`, хуки и `IncludeMembers` не переносятся |
| `BeforeMap((src, dest) => ...)` / `AfterMap(...)` | `.BeforeMap(...)` / `.AfterMap(...)` | Поддерживается | `HookTests.HookOrder_BeforeMapSeesEmptyDest_AfterMapSeesFullDest` и другие `HookTests`. С 2.1.0 вызываются и in-place перегрузкой (до 2.0.0 включительно — нет). Свойства `required` и `init` задаёт инициализатор объекта, поэтому `BeforeMap` уже видит их (с 2.1.0; до 2.0.0 включительно — `CS9035` в сгенерированном файле и `init` не маппился). С 2.1.0 — и группа методов статического метода (`AfterMap(Audit.Stamp)`, как `When(Rules.IsActive)`; `SilentlyIgnoredConfigTests`); до 2.0.0 включительно молча игнорировалась |
| `IncludeMembers(s => s.Sub)` | `.IncludeMembers(s => s.Sub, ...)` | Поддерживается | `IncludeMembersTests` (например, `IncludeMembers_ForMemberWins_OverIncludedMember`, `IncludeMembers_FirstMemberWins_OnDuplicatePropertyName`) |
| `cfg.ValueTransformers.Add<T>(...)` (глобально) | `AddTransformer<T>(v => ...)` в профиле | Поддерживается | `TransformerTests.AddTransformer_StringProperty_ValueIsTrimmed`, `TransformerTests.AddTransformer_DoesNotAffectForMemberProperties`. Действует в пределах профиля; к свойствам из `ForMember` не применяется |
| `opt.ConvertUsing<TValueConverter, TSrcMember>()` ([value converters](https://docs.automapper.io/en/latest/Value-converters.html)) | `opt.UseConverter<TConverter>()` с `ITypeConverter<TSrcMember, TDestMember>` | Частично | `ConverterCacheTests.UseConverter_StatelessCachedAndStatefulNew`. Создаётся сгенерированным кодом, а не берётся из DI: converter без состояния — один экземпляр, иначе `new` на каждый вызов ([Экземпляры converter](configuration.ru.md#экземпляры-converter)) |
| `ConvertUsing<TTypeConverter>()` ([type converters](https://docs.automapper.io/en/latest/Custom-type-converters.html)) | `ConvertUsing<TConverter>()` с `ITypeConverter<TSource, TDest>` (только `Convert(source)`) | Частично | `ConverterCacheTests.ConvertUsing_StatelessCachedAndStatefulNew`. Не применяется in-place перегрузкой |
| `CreateMap<Guid, UserId>().ConvertUsing(UserId.From)` / `CreateMap<UserId, Guid>().ConvertUsing(v => v.Value)` применяются к каждому члену этой пары ([type converters](https://docs.automapper.io/en/latest/Custom-type-converters.html)) | то же (с 2.1.0) | Частично | `PairConverterTests` (генератор и runtime), `PairConverterMapperTests` (`IMapper`). Свойства, параметры конструкторов и фабрик, элементы коллекций; `Guid?` ↔ `UserId?` поднимаются той же парой; группа методов статического метода или лямбда (`ConvertUsing((src, dest, context) => ...)` не поддерживается); к значениям `IncludeMembers` не применяются. Без карты пары — `IM0002` при сборке. По соглашению ничего не ищется. Оба направления одной строкой (только в IronMapper): `.ConvertUsing(UserId.From).ReverseMap(v => v.Value)`. См. [Преобразование значений](advanced.ru.md#преобразование-значений-карты-пар) |
| — (только в IronMapper: атрибут конвертера для свойства) | `[MapConverter(typeof(TConverter))]` | Поддерживается | `DiIntegrationTests.Mapper_WithCustomConverter_ViaServiceProvider`, `ConverterCacheTests` (converter создаёт сгенерированный код, а не DI) |
| Type converter, value converter или resolver с зависимостями в конструкторе (AutoMapper создаёт его через DI-контейнер) | **Не поддерживается**: сгенерированный код создаёт converters сам, нужен конструктор без параметров (иначе `IM0015`). `AddConverter<T>()` на маппинг не влияет и с 2.1.0 помечен `[Obsolete]` | Обходной путь | `AddConverterObsoleteTests`, `DiIntegrationTests.AddConverter_IsObsolete_AndStillOnlyRegistersTheType`. Посчитайте значение до маппинга и положите его в source или задайте свойство после маппинга ([Converters из DI](configuration.ru.md#converters-из-di-не-поддерживаются-addconvertert-устарел)) |
| `opt.NullSubstitute("x")` | `opt.MapFrom(s => s.X ?? "x")` | Обходной путь | Через `MapFrom` |
| `opt.Condition(s => ...)` | **Не поддерживается** на уровне члена; `MapFrom(s => cond ? s.X : default)` или `.When(...)` для всего маппинга | Обходной путь | `.When`: `ProfileTests.WhenCondition_ConditionFalse_ReturnsDefault` |
| `opt.MapFrom<TValueResolver>()` ([resolvers](https://docs.automapper.io/en/latest/Custom-value-resolvers.html)) | **Не поддерживается** | — | Используйте лямбду в `MapFrom` |
| `opt.UseDestinationValue()` | **Не поддерживается** | — | |
| `Include<,>()` / `IncludeBase<,>()` | **Не поддерживается** | — | Объявляйте каждый маппинг отдельно |
| `ResolutionContext.Items` / `opts.Items[...]` | **Не поддерживается** | — | [#7](https://github.com/iron-mapper/IronMapper/issues/7) |
| `ProjectTo<Dest>(queryable)` | **Пока не поддерживается** | Запланировано | [#9](https://github.com/iron-mapper/IronMapper/issues/9) |
| Flattening по соглашению (`CustomerAddressCity` из `Customer.Address.City`) | **Не поддерживается** | — | Используйте `MapFrom` или `IncludeMembers`; в маппингах из профилей свойство молча остаётся незаполненным ([#25](https://github.com/iron-mapper/IronMapper/issues/25)) |
| Вложенные одиночные объекты по настроенному маппингу | так же (начиная с 2.1.0) | Поддерживается | Маппингом пары типов, без `ForMember`; `null` — по nullable-аннотациям: `null` → `null` для свойства destination, допускающего `null`, `InvalidOperationException` с именами обоих свойств для не допускающего ([подробнее](advanced.ru.md#null-во-вложенном-объекте-который-не-допускает-null); AutoMapper всегда маппит `null` в `null`). Без маппинга — `IM0002` с code fix, который его добавляет. Циклы через вложенные объекты — предупреждение `IM0004`. См. [Вложенные объекты](advanced.ru.md#вложенные-объекты); `NestedObjectTests`, `CyclicMappingTests` ([#23](https://github.com/iron-mapper/IronMapper/issues/23)). До 2.0.0 включительно: `ForMember(d => d.X, o => o.MapFrom(s => s.X.MapToXDto()))` |
| `null` во вложенном свойстве, объявленном не допускающим `null` (навигация EF без `Include`) → `null` | `InvalidOperationException` с именами обоих свойств (начиная с 2.1.0) | **Отличается** | AutoMapper молча маппит его в `null` (и в новом объекте, и in-place). IronMapper маппит `null` в `null` только для свойства destination, допускающего `null`: объявите `CustomerDto? Customer`, загрузите навигацию или настройте свойство (`MapFrom`, `Ignore`). См. [`null` во вложенном объекте](advanced.ru.md#null-во-вложенном-объекте-который-не-допускает-null); `NestedObjectTests.NullInANonNullableNestedObject_ThrowsInvalidOperationException_NamingBothProperties` |
| Маппинг через конструктор (позиционные record, параметры конструктора сопоставляются с членами source) | destination без конструктора без параметров создаётся конструктором, у которого больше всего параметров из source; параметр настраивается через `ForMember` одноимённого свойства (`MapFrom`, `UseConverter`, `Ignore` = значение по умолчанию) или через `ForCtorParam` | Частично | С 2.1.0 ([#24](https://github.com/iron-mapper/IronMapper/issues/24)): `ConstructorMappingTests` (генератор и runtime), `ConstructorMapperTests` (`IMapper`). Нет in-place-перегрузки для такого destination (`Map(source, destination)` бросает `MappingException`). Статический фабричный метод или другой конструктор — `ConstructWith` (ниже). См. [Конструкторы и позиционные record](advanced.ru.md#конструкторы-и-позиционные-record) |
| `ForCtorParam("name", o => o.MapFrom(s => ...))` | то же, с вариантами `ForMember` (`MapFrom`, `UseConverter`, `Ignore` = значение по умолчанию) | Поддерживается | С 2.1.0: `FactoryMappingTests.ForCtorParam_ParameterWithoutPropertyOfTheSameName_MapFrom_NameOfAndLiteral` (генератор), `FactoryMappingTests.LambdaChoosesTheConstructor_ForCtorParamForAParameterWithoutProperty` (runtime). Имя сравнивается без учёта регистра и должно быть константой; имя, которого нет среди параметров, — ошибка сборки `IM0019` (в AutoMapper — ошибка конфигурации во время выполнения). См. [Параметры без свойства](advanced.ru.md#параметры-без-свойства-forctorparam) |
| `ConstructUsing(s => new Dest(...))` | то же | Частично | С 2.1.0: `FactoryMappingTests.ConstructUsing_ExpressionAsWritten_SettablePropertiesAfterIt_InPlaceIgnoresIt`. Выражение выводится как написано (правила лямбд `MapFrom`); затем свойства с публичным `set` маппятся и присваиваются, `init`-свойства оставлены выражению; `Map(source, destination)` его не использует, как в AutoMapper (для destination без доступного конструктора без параметров in-place-перегрузки нет). `ConstructUsing((src, context) => ...)` не поддерживается. Статическая фабрика с сопоставлением параметров — `ConstructWith(Dest.Create)` (только в IronMapper), см. [Фабрики](advanced.ru.md#фабрики-constructwith) |
| `AssertConfigurationIsValid()` | Прямого аналога нет | — | Часть проверок выполняется при сборке, см. [диагностики](diagnostics.ru.md). Ближе всего к его проверке несопоставленных членов: `<IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>` выдаёт IM0001 для маппингов профилей (обратные маппинги не проверяются, как в AutoMapper), `dotnet_diagnostic.IM0001.severity = error` роняет сборку ([конфигурация](configuration.ru.md#несопоставленные-свойства-в-профилях)); тесты: `ProfileUnmappedPropertyTests` |

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

Интерфейс `IMapper` в IronMapper предоставляет `Map<TDest>(source)`, `Map<TSource, TDest>(source)`, `Map<TSource, TDest>(source, destination)` и `MapCollection<TSource, TDest>(source)`. Коллекции маппятся по типу приёмника (`Map<List<Dest>>(list)`, начиная с 2.0.0, [#8](https://github.com/iron-mapper/IronMapper/issues/8)). Передачи опций/контекста в вызов нет ([#7](https://github.com/iron-mapper/IronMapper/issues/7)).

### 5. Опционально — переходи на extension methods

AutoMapper требует внедрения `IMapper` везде. IronMapper генерирует строго типизированные extension methods на source-типе, поэтому там, где типы известны во время компиляции, можно убрать DI-зависимость:

При неявных using пакет сам импортирует пространство имён сгенерированных методов (`IronMapper.Generated`); без
них добавьте в файл `using IronMapper.Generated;`.

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
