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

Internal-маппинги работают и через `IMapper` (`IronMapper.Extensions.DI`): он ищет в том числе непубличные
сгенерированные методы. Код из других сборок может использовать internal-маппинг только через `[InternalsVisibleTo]`.

---

## Решения из нескольких проектов

Каждый проект, который ссылается на пакет `IronMapper`, получает собственный класс
`IronMapper.Generated.GeneratedMappers` с маппингами этого проекта. Имя класса одинаковое во всех сборках.

**Доступность класса.** Класс `public`, если хотя бы один сгенерированный метод проекта `public`, иначе `internal`.
Поэтому проект только с internal-маппингами ничего не публикует: проекты, которые на него ссылаются, вообще не видят
его класс (если он не открывает внутренности через `[InternalsVisibleTo]`).

**Вызов маппингов проекта-зависимости.** Используйте синтаксис метода расширения с `using IronMapper.Generated;`:

```csharp
using IronMapper.Generated;

var dto = product.MapToProductDto();      // public-маппинг из проекта, на который ссылается текущий
var order = entity.MapToOrderDto();       // маппинг текущего проекта
```

Маппинги текущего проекта и public-маппинги проектов-зависимостей можно использовать вместе; это собирается с
`TreatWarningsAsErrors` (проверяется в `GeneratedClassVisibilityTests` и в проекте-потребителе пакета
`tests/IronMapper.PackageConsumer`, который ссылается на две библиотеки с IronMapper).

Остаются два ограничения:

- **Не называйте класс явно.** `GeneratedMappers.MapToOrderDto(entity)` связывается с классом текущего проекта. Если
  класс проекта-зависимости тоже виден (в нём есть public-маппинги или он открывает `[InternalsVisibleTo]`),
  компилятор выдаёт предупреждение `CS0436` (ошибку при `TreatWarningsAsErrors`), а методы зависимости так вызвать
  нельзя. С синтаксисом метода расширения этой проблемы нет.
- **Объявляйте каждый маппинг в одном проекте.** Если проект-зависимость уже публикует маппинг для пары типов, а
  текущий проект маппит ту же пару ещё раз, текущий проект собирается, но вызов `source.MapToX()` в нём
  неоднозначен (`CS0121`). То же происходит внутри сгенерированного кода, если тип элемента вложенной коллекции
  маппится в обоих проектах. Держите маппинг в проекте, которому принадлежат типы.

`IMapper` (`IronMapper.Extensions.DI`) находит сгенерированные методы во всех загруженных сборках, независимо от
того, `public` класс или `internal`.

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
`new TConverter()`.

```csharp
services.AddIronMapper(opt =>
{
    opt.AddProfilesFromAssembly(typeof(Program).Assembly);
    opt.AddConverter<DateToStringConverter>();
});
```
