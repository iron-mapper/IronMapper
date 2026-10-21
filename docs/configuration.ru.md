# Справочник по конфигурации

> 🌐 **[English version](configuration.md)**

## Доступность сгенерированных методов

Относится и к маппингам `[MapTo]`/`[MapFrom]`, и к `MappingProfile`.

Каждый сгенерированный метод (`MapToX`, in-place перегрузка, `MapToXList`, `MapToXArray`) доступен ровно настолько,
насколько доступен **менее доступный** из типов source и destination. Учитывается *эффективная* доступность:
содержащие типы для вложенных типов и аргументы обобщённых типов тоже.

| Типы source / destination | Сгенерированные методы |
|---|---|
| оба `public` (включая все содержащие типы и аргументы типов) | `public` |
| хотя бы один `internal` или `protected internal` (или вложен в `internal`-тип) | `internal` |
| хотя бы один вложенный тип `private`, `protected` или `private protected` | не генерируются, ошибка [`IM0010`](diagnostics.ru.md#im0010--тип-маппинга-недоступен-из-сгенерированного-кода) |

Вспомогательные методы `BeforeMap`/`AfterMap` и `AddTransformer` всегда `private`.

Internal-маппинги работают и через `IMapper` (`IronMapper.Extensions.DI`): сгенерированная регистрация сборки
регистрирует и их. Код из других сборок может вызвать internal-маппинг напрямую только через `[InternalsVisibleTo]`.

---

## Имена методов и коллизии

Каждый маппинг генерирует `MapTo{Name}` (новый объект), in-place перегрузку `MapTo{Name}(destination)`,
`MapTo{Name}List` и `MapTo{Name}Array`, где `{Name}` — **простое имя** destination-типа. Маппинги разных source-типов
— это перегрузки, и они сохраняют эти имена даже при одинаковых простых именах (`Shop.Item → Shop.ItemDto` и
`Stock.Item → Stock.ItemDto` оба используют `MapToItemDto`).

### Одна пара типов задана дважды

Пара типов генерируется **один раз**, а предупреждение [`IM0007`](diagnostics.ru.md#im0007--повторное-определение-маппинга)
перечисляет все определения. Генерируется определение из первого подходящего пункта:

1. `CreateMap` в `MappingProfile`;
2. `ReverseMap()` в профиле;
3. `[MapTo]`;
4. `[MapFrom]`.

Профиль имеет приоритет над атрибутами, потому что профиль — это явная конфигурация (`ForMember`, хуки, `When`); так и
было записано в сообщении IM0007. Явный `CreateMap` побеждает `ReverseMap()`, который маппит только по именам. Среди
определений одного вида побеждает профиль (или тип), чьё полное имя идёт первым при порядковом сравнении, затем — первый
вызов в конструкторе. Все остальные маппинги проекта генерируются.

### Один source-тип, несколько destination-типов с одинаковым простым именем

Два метода `MapToUserDto(this User)` не могут существовать одновременно (`CS0111`), поэтому **все** маппинги этой
группы получают уточнённое имя, а предупреждение
[`IM0013`](diagnostics.ru.md#im0013--имя-метода-маппинга-уточнено-из-за-коллизии) сообщает, как вызывать метод.
Используется первое правило, при котором все имена группы уникальны (и не совпадают с другими именами методов того же
source-типа):

1. последние 1, 2, … сегмента пространства имён и содержащих типов destination, перед именем;
2. аргументы типа по простым именам, после имени;
3. и то и другое;
4. аргументы типа по полным именам.

| Маппинги из `App.User` / `App.Item` | Сгенерированные имена |
|---|---|
| `App.Admin.UserDto`, `App.Public.UserDto` | `MapToAdminUserDto`, `MapToPublicUserDto` (+ `List`, `Array`) |
| `App.Orders.Dto`, `App.Invoices.Dto` (вложенные типы) | `MapToOrdersDto`, `MapToInvoicesDto` |
| `App.Box<int>`, `App.Box<string>` | `MapToBoxOfInt32`, `MapToBoxOfString` |
| глобальное пространство имён: `Dto` и `App.Dto` | `MapToGlobalDto`, `MapToAppDto` |
| `App.SummaryDto` (коллизии нет) | `MapToSummaryDto`, без изменений |

Сгенерированный код для элементов вложенных коллекций и `IMapper` (`IronMapper.Extensions.DI`, который использует
сгенерированную регистрацию) используют уточнённые имена автоматически. Ваш код — в том числе лямбды `MapFrom` — должен вызывать
уточнённое имя. Если ни одно правило не даёт уникальных имён (все заняты другими маппингами), маппинги этой группы не
генерируются и выдаётся ошибка [`IM0014`](diagnostics.ru.md#im0014--коллизию-имён-методов-маппинга-нельзя-устранить).

Имена, которые компилировались раньше, не меняются: правила применяются только к группам, которые раньше падали с
`CS0111`. Но если позже добавить второй destination-тип с тем же простым именем, существующий маппинг этого source-типа
будет переименован (IM0013 сообщит новое имя). Чтобы сохранить `MapToUserDto`, переименуйте один из destination-типов.

### Имена вспомогательных методов и файлов

Вспомогательные методы `BeforeMap`/`AfterMap` и `AddTransformer` называются `BeforeMap_{Source}_{Dest}`,
`TransformValue_{Type}_{Source}_{Dest}` по простым именам. Если у другого маппинга те же простые имена, используются
полные (`TransformValue_String_Shop_Item_Shop_ItemDto`). Эти методы `private`. Сгенерированные файлы называются по
полным именам обоих типов (`Shop.Item_MapTo_Shop.ItemDto.g.cs`).

## Global using для `IronMapper.Generated`

Сгенерированные методы `MapTo*` лежат в пространстве имён `IronMapper.Generated`. Пакет `IronMapper` (и когда он
приходит через `IronMapper.Extensions.DI`) добавляет `global using IronMapper.Generated;` в проекты C# с неявными using
(`<ImplicitUsings>enable</ImplicitUsings>` или `true`, по умолчанию в шаблонах .NET 6+); файл пакета —
`buildTransitive/IronMapper.props`.

- **Без неявных using** ничего не добавляется: пишите `using IronMapper.Generated;` в файлах, где вызываются методы
  `MapTo*` (в C# 9 и раньше global using нет вообще).
- Явный `using IronMapper.Generated;` рядом с глобальным компилируется без предупреждений.
- Проект без маппингов получает от генератора пустое пространство имён `IronMapper.Generated`, поэтому global using
  компилируется и там.
- **Отключить** для проекта (или для всех проектов в `Directory.Build.props`):

  ```xml
  <PropertyGroup>
    <IronMapperImplicitUsings>false</IronMapperImplicitUsings>
  </PropertyGroup>
  ```

Global using действует так же, как `using IronMapper.Generated;` в каждом файле. В файле, который раньше не
импортировал это пространство имён, вызов `x.MapToY()` становится неоднозначным (`CS0121`), только если файл через
директиву `using` импортирует и другой метод расширения с тем же именем и типом параметра (например, написанный
вручную маппер из переносимого кода): удалите или переименуйте его либо отключите global using. Про одну пару типов,
смапленную в двух проектах, — см. [Решения из нескольких проектов](#решения-из-нескольких-проектов).

## Решения из нескольких проектов

Каждый проект, который ссылается на пакет `IronMapper`, получает собственный класс
`IronMapper.Generated.GeneratedMappers` с маппингами этого проекта. Имя класса одинаковое во всех сборках.

**Доступность класса.** Класс `public`, если хотя бы один сгенерированный метод проекта `public`, иначе `internal`.
Поэтому проект только с internal-маппингами ничего не публикует: проекты, которые на него ссылаются, вообще не видят
его класс (если он не открывает внутренности через `[InternalsVisibleTo]`).

**Вызов маппингов проекта-зависимости.** Используйте синтаксис метода расширения; пространство имён импортирует
[global using](#global-using-для-ironmappergenerated) или `using IronMapper.Generated;`:

```csharp
using IronMapper.Generated;               // только без неявных using

var dto = product.MapToProductDto();      // public-маппинг из проекта, на который ссылается текущий
var order = entity.MapToOrderDto();       // маппинг текущего проекта
```

Маппинги текущего проекта и public-маппинги проектов-зависимостей можно использовать вместе; это собирается с
`TreatWarningsAsErrors` (проверяется в `GeneratedClassVisibilityTests` и в проекте-потребителе пакета
`tests/IronMapper.PackageConsumer`, который ссылается на две библиотеки с IronMapper).

Уточнённые имена ([`IM0013`](#один-source-тип-несколько-destination-типов-с-одинаковым-простым-именем)) public-маппингов
проекта-зависимости вызываются так же — по имени из предупреждения в том проекте
(`member.MapToAdminMemberDto()`, `members.MapToAdminMemberDtoList()`).

Остаются два ограничения:

- **Не называйте класс явно.** `GeneratedMappers.MapToOrderDto(entity)` связывается с классом текущего проекта. Если
  класс проекта-зависимости тоже виден (в нём есть public-маппинги или он открывает `[InternalsVisibleTo]`),
  компилятор выдаёт предупреждение `CS0436` (ошибку при `TreatWarningsAsErrors`), а методы зависимости так вызвать
  нельзя. С синтаксисом метода расширения этой проблемы нет.
- **Объявляйте каждый маппинг в одном проекте.** Если проект-зависимость уже публикует маппинг для пары типов, а
  текущий проект маппит ту же пару ещё раз, текущий проект собирается, но вызов `source.MapToX()` в нём
  неоднозначен (`CS0121`). То же происходит внутри сгенерированного кода, если тип элемента вложенной коллекции
  маппится в обоих проектах. Держите маппинг в проекте, которому принадлежат типы.

`IMapper` (`IronMapper.Extensions.DI`) использует маппинги всех сборок, чья сгенерированная регистрация выполнилась,
независимо от того, `public` класс или `internal`: передайте сборки с маппингами в `AddIronMapper` (см.
[IMapper и сгенерированные extension-методы](advanced.ru.md#imapper-и-сгенерированные-extension-методы)). Самой
библиотеке с маппингами пакет DI не нужен: достаточно, чтобы на него ссылалось приложение.

## Регистрация для `IMapper` в приложениях с trimming и NativeAOT

Для `IMapper` каждая сборка с маппингами регистрирует их через сгенерированный инициализатор модуля (см.
[IMapper и сгенерированные extension-методы](advanced.ru.md#imapper-и-сгенерированные-extension-методы)). В приложении с
trimming или NativeAOT эта регистрация удерживала бы реестр и все сгенерированные методы сборки — даже в приложении,
которое вызывает только extension-методы. Поэтому она стоит за feature switch `IronMapper.Registration.IsSupported`
(`MapperRegistry.IsSupported`), который пакет `IronMapper` выставляет по MSBuild-свойству `IronMapperRegistration`
проекта приложения (`buildTransitive/IronMapper.targets`):

| Приложение | Switch | Регистрация |
|---|---|---|
| `PublishAot` или `PublishTrimmed`, ни один проект не ссылается на `IronMapper.Extensions.DI` | `false` | удаляется trimmer'ом |
| ссылается на `IronMapper.Extensions.DI` напрямую или через свою библиотеку | не задан (включён) | остаётся |
| без trimming (`dotnet run`, публикация без trimming) | не задан (включён) | остаётся |

Задайте свойство в проекте приложения, если автоматический выбор не подходит: например, приложение NativeAOT
использует `MapperRegistry` без пакета DI (`true`) или ссылается на пакет DI, но `IMapper` не использует (`false`):

```xml
<PropertyGroup>
  <IronMapperRegistration>true</IronMapperRegistration>
</PropertyGroup>
```

- При выключенном switch создание `IMapper` (`RuntimeMapper`) бросает `InvalidOperationException` с именем свойства,
  а `MappingException` из `MapperRegistry` для ненайденного маппинга говорит то же самое вместо простого «No mapping
  registered».
- Сгенерированные extension-методы от switch не зависят.
- Свойство действует только в проекте приложения (оно превращается в `RuntimeHostConfigurationOption`); в библиотеке
  оно ни на что не влияет. При ссылках на исходники IronMapper (ProjectReference) вместо пакета ничего не задаётся, и
  регистрация остаётся.

---

## Атрибуты

### `[MapTo(typeof(TDest))]`

Применяется к **source**-типу. Указывает генератору создать extension method `MapToTDest()`.

```csharp
[MapTo(typeof(ProductDto))]
public class ProductEntity { ... }
```

Поддерживается несколько destination-типов — применяй атрибут несколько раз:

```csharp
[MapTo(typeof(ProductDto))]
[MapTo(typeof(ProductSummaryDto))]
public class ProductEntity { ... }
```

---

### `[MapFrom(typeof(TSource))]`

Применяется к **destination**-типу. Функционально идентичен `[MapTo]` на source — используй тот вариант, который лучше вписывается в структуру кода.

```csharp
[MapFrom(typeof(ProductEntity))]
public class ProductDto { ... }
```

---

### `[MapProperty("DestPropertyName")]`

Применяется к **свойству source-типа**. Перенаправляет значение в свойство destination-типа с другим именем. Имя сравнивается без учёта регистра. Работает только для маппингов через атрибуты.

```csharp
[MapTo(typeof(PersonDto))]
public class Person
{
    [MapProperty("FullName")]
    public string Name { get; set; } = "";
}

public class PersonDto
{
    public string FullName { get; set; } = "";
}
```

---

### `[Ignore]`

Применяется к **свойству source-типа** или **свойству destination-типа**. Свойство исключается из обоих вариантов mapping — создающего и in-place; на свойстве destination оно также подавляет `IM0001`.

`[Ignore]` учитывается только в маппингах `[MapTo]`/`[MapFrom]`. В профилях используйте `ForMember(d => d.X, o => o.Ignore())`.

```csharp
[MapTo(typeof(OrderDto))]
public class Order
{
    public int Id { get; set; }

    [Ignore]
    public string InternalToken { get; set; } = "";
}
```

---

### `[MapConverter(typeof(TConverter))]`

Применяется к **свойству source-типа**, когда типы source и destination отличаются и нужна кастомная конвертация. Сгенерированный код создаёт converter через `new TConverter()`, поэтому нужен публичный конструктор без параметров.

```csharp
public class PriceConverter : ITypeConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("F2");
}

[MapTo(typeof(InvoiceDto))]
public class Invoice
{
    [MapConverter(typeof(PriceConverter))]
    public decimal Total { get; set; }
}

public class InvoiceDto
{
    public string Total { get; set; } = "";
}
```

---

## Fluent API (MappingProfile)

### `CreateMap<TSource, TDest>()`

Регистрирует пару маппинга внутри конструктора `MappingProfile`.

```csharp
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<OrderEntity, OrderDto>();
    }
}
```

---

### `ForMember(dest => dest.Prop, opt => opt.MapFrom(...))`

Настраивает, как заполняется конкретное свойство destination. Лямбда переписывается в сгенерированный код с полными именами; что в ней можно использовать, см. [Лямбды в профилях](#лямбды-в-профилях).

```csharp
CreateMap<Employee, EmployeeDto>()
    .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.FirstName + " " + s.LastName));
```

---

### `ForMember(dest => dest.Prop, opt => opt.Ignore())`

Исключает свойство destination из маппинга.

```csharp
CreateMap<User, UserDto>()
    .ForMember(d => d.PasswordHash, opt => opt.Ignore());
```

---

### `ForMember(dest => dest.Prop, opt => opt.UseConverter<TConverter>())`

Применяет type converter к одному свойству (создаётся через `new TConverter()`). Покрыто тестом генератора, который проверяет только сгенерированный код; runtime-теста пока нет.

```csharp
CreateMap<Product, ProductDto>()
    .ForMember(d => d.Price, opt => opt.UseConverter<DecimalToStringConverter>());
```

---

### `ConvertUsing<TConverter>()`

Заменяет весь object initializer единственным вызовом converter. Не применяется в in-place перегрузке `source.MapToX(existing)`. Покрыто только тестом сгенерированного кода.

```csharp
public class FullOrderConverter : ITypeConverter<OrderEntity, OrderDto>
{
    public OrderDto Convert(OrderEntity source) => new() { ... };
}

CreateMap<OrderEntity, OrderDto>()
    .ConvertUsing<FullOrderConverter>();
```

---

### `ConvertUsing(src => ...)`

Lambda-based конвертация всего объекта. Тело выражения генератор вставляет дословно. Не применяется в in-place перегрузке. Покрыто только тестом сгенерированного кода.

```csharp
CreateMap<Point, PointDto>()
    .ConvertUsing(src => new PointDto { X = src.X, Y = src.Y });
```

---

### `When(src => condition)`

Добавляет guard: mapping выполняется только когда предикат истинен; иначе возвращается `default` (`null` для классов). Не применяется в in-place перегрузке.

```csharp
CreateMap<UserEntity, UserDto>()
    .When(src => src.IsActive);
```

---

## Лямбды в профилях

Относится ко всем лямбдам в конструкторе `MappingProfile`: `MapFrom`, `When`, `ConvertUsing(src => ...)`,
`BeforeMap`/`AfterMap` и `AddTransformer`.

Генератор не вызывает эти лямбды во время выполнения. Он переписывает их код в сгенерированный класс
`IronMapper.Generated.GeneratedMappers` с помощью семантической модели:

- параметры лямбды переименовываются в `source` / `destination` / `value` по символу, поэтому строковые литералы и имена
  членов, совпадающие с именем параметра, не затрагиваются (`s => s.s + "s"` превращается в `source.s + "s"`);
- пространства имён, типы, статические члены, константы и значения enum получают полные имена (`global::...`), поэтому
  директивы `using` из файла профиля в сгенерированном файле не нужны;
- вызовы методов расширения превращаются в статические вызовы класса, в котором они объявлены
  (`s.Title.ToSlug()` → `global::App.Text.SlugExtensions.ToSlug(source.Title)`).

**Можно использовать:** собственные параметры лямбды; статические методы, свойства, поля и константы любого типа,
доступного сгенерированному коду, в том числе `public` и `internal` статические члены самого профиля; значения enum;
методы расширения; локальные переменные и вложенные лямбды, объявленные внутри лямбды; `nameof(...)`.

**Нельзя использовать:**

| Что | Диагностика | Почему / как исправить |
|---|---|---|
| `private` или `protected` члены и типы, например `private static` метод профиля | [`IM0011`](diagnostics.ru.md#im0011--лямбда-профиля-использует-член-недоступный-сгенерированному-коду) | Код выполняется в `GeneratedMappers` — другом классе, где private и protected члены не видны. Сделайте член `internal`: internal-члены видны всему коду той же сборки, включая сгенерированный класс. |
| Члены экземпляра профиля, `this`, `base` | [`IM0012`](diagnostics.ru.md#im0012--лямбда-профиля-захватывает-состояние) | Во время маппинга экземпляра профиля нет. Сделайте член `static` (или `const`). |
| Локальные переменные, параметры и локальные функции конструктора | [`IM0012`](diagnostics.ru.md#im0012--лямбда-профиля-захватывает-состояние) | В сгенерированном коде их нет. Используйте константу или статический член либо вычисляйте значение из source. |

Если в лямбде есть такая проблема, этот маппинг (и его `ReverseMap`) не генерируется, а ошибка называет член и профиль;
остальные маппинги генерируются. Проблема в лямбде `AddTransformer` отключает этот преобразователь и выдаёт ошибку.

Замечания:
- `MapFrom` принимает дерево выражений (`Expression<Func<...>>`), поэтому сам C# не разрешает там `?.`, блочные тела
  и некоторые другие конструкции.
- Методы расширения, вызванные после `?.` (это возможно в `When`, хуках и преобразователях), выводятся как написаны.
  Они компилируются, только если их пространство имён подключено для всего проекта, например через `global using`.
- Лямбды намеренно не вызываются как делегаты: для этого пришлось бы создавать профиль во время выполнения через
  reflection, а вызов делегата нельзя перевести в SQL, что закрыло бы путь к будущему `ProjectTo`.

---

## Кастомные Converters (`ITypeConverter<TSource, TDest>`)

Реализуй `ITypeConverter<TSource, TDest>` для переиспользуемых конвертаций:

```csharp
public class DateToStringConverter : ITypeConverter<DateTime, string>
{
    public string Convert(DateTime source) => source.ToString("yyyy-MM-dd");
}
```

`IronMapperOptions.AddConverter<T>()` регистрирует тип converter в DI-контейнере как transient-сервис.
На маппинг это **не** влияет: сгенерированный код не получает converters из DI и всегда создаёт их через
`new TConverter()`. `AddProfilesFromAssembly` и `AddProfile<T>()` регистрируют сгенерированные маппинги сборки для
`IMapper`; сами профили в контейнер не добавляются (до 1.1.4 включительно добавлялись как singleton).

```csharp
services.AddIronMapper(opt =>
{
    opt.AddProfilesFromAssembly(typeof(Program).Assembly);
    opt.AddConverter<DateToStringConverter>();
});
```
