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

Настраивает, как заполняется конкретное свойство destination. Тело лямбды копируется в сгенерированный код как текст (параметр переименовывается в `source`), поэтому в нём можно использовать параметр лямбды и доступные типы и члены, но не локальные переменные или поля профиля.

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
