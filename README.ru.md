# IronMapper

[![NuGet](https://img.shields.io/nuget/v/IronMapper.svg)](https://www.nuget.org/packages/IronMapper)
[![Build](https://github.com/iron-mapper/IronMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/iron-mapper/IronMapper/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> 🌐 **[English documentation](README.md)**

**Compile-time object mapper для .NET.**

IronMapper с помощью Roslyn source generator генерирует код маппинга во время сборки. Сгенерированные extension-методы `MapTo*` — это обычные присваивания свойств на C#: без reflection, без IL emit, без динамических прокси, их можно пройти отладчиком.

Необязательный пакет `IronMapper.Extensions.DI` добавляет сервис `IMapper`. Он **не** свободен от reflection: находит сгенерированные методы через reflection и вызывает их через `MethodInfo.Invoke` (см. [Известные ограничения](#известные-ограничения)).

---

## IronMapper vs AutoMapper

| | AutoMapper | IronMapper |
|---|---|---|
| Стратегия маппинга | План маппинга строится во время выполнения и компилируется из expression tree ([docs](https://docs.automapper.io/en/latest/Configuration.html)) | C#-код, сгенерированный при компиляции |
| Ошибки конфигурации | Сообщаются при вызове `AssertConfigurationIsValid()`, обычно в unit-тесте ([docs](https://docs.automapper.io/en/latest/Configuration-validation.html)) | Часть — при сборке (диагностики IM00xx или ошибки C# в сгенерированном коде); см. [Известные ограничения](#известные-ограничения) |
| Лицензия | 14.x и ранее: MIT. 15.0+: RPL-1.5 или коммерческая лицензия, есть бесплатная Community-лицензия для подходящих организаций ([automapper.io](https://automapper.io/)) | MIT |
| Стиль API | Fluent API на профилях, атрибут `[AutoMap]` | Атрибуты **или** fluent API на профилях |
| `ProjectTo` для `IQueryable` | Есть | Пока не поддерживается (запланировано, [#9](https://github.com/iron-mapper/IronMapper/issues/9)) |

Сравнительные бенчмарки с ручным кодом, Mapperly, Mapster и AutoMapper — в [`benchmarks/`](benchmarks/README.md); результаты версии 1.1.3 (.NET 10, один компьютер) — в [`benchmarks/results/RESULTS.md`](benchmarks/results/RESULTS.md).

---

## Quick Start (5 минут)

### 1. Установка

```bash
dotnet add package IronMapper
```

### 2. Аннотируй source-тип

```csharp
using IronMapper.Attributes;

[MapTo(typeof(UserDto))]
public class UserEntity
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class UserDto
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

### 3. Маппинг

```csharp
var entity = new UserEntity { Id = 1, Name = "Alice", Email = "alice@example.com" };
UserDto dto = entity.MapToUserDto();  // сгенерированный extension method
```

Всё. Ни `IMapper`, ни регистрация сервисов не нужны.

---

## Demo Application

Демонстрационное консольное приложение показывает основные возможности IronMapper,
используя реальное Open Library API (API-ключ не нужен).

**Что демонстрирует пример:**
- Простой attribute-based mapping (`[MapTo]`)
- Fluent API с `MappingProfile`
- Collection mapping (`List<BookDoc>` → `List<BookSummary>`)
- Вложенные объекты (книга и автор маппятся отдельно, затем объединяются в коде)
- Кастомные type converters (очистка HTML из биографии)
- Условный mapping (только живые авторы)

**Запуск:**
```bash
git clone https://github.com/iron-mapper/IronMapper
cd IronMapper
dotnet run --project samples/IronMapper.Demo
```

---

## Ключевые возможности

### Attribute-based mapping

```csharp
[MapTo(typeof(ProductDto))]      // source → dest
[MapFrom(typeof(ProductEntity))] // dest ← source (эквивалентно)
```

### Переименование свойств

```csharp
[MapProperty("FullName")]
public string Name { get; set; } = "";  // маппится в FullName на destination
```

### Игнорирование свойств

```csharp
[Ignore]
public string InternalSecret { get; set; } = "";
```

### Кастомные type converters

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
```

### Profile-based fluent API

```csharp
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<OrderEntity, OrderDto>()
            .ForMember(d => d.FullAddress, o => o.MapFrom(s => s.Street + ", " + s.City))
            .ForMember(d => d.InternalId,  o => o.Ignore());
    }
}
```

### Вспомогательные методы для коллекций

```csharp
List<UserEntity> entities = ...;
List<UserDto>    dtos = entities.MapToUserDtoList();
UserDto[]        arr  = entities.MapToUserDtoArray();
```

### In-place (void) mapping

```csharp
entity.MapToUserDto(existingDto);  // обновляет существующий объект без создания нового
```

### Вложенные коллекции

```csharp
[MapTo(typeof(BlogDto))]
public class BlogEntity
{
    public List<CommentEntity> Comments { get; set; } = new();
}
// генератор выдаёт: Comments = source.Comments is null ? null! : Enumerable.ToList(Enumerable.Select(source.Comments, x => x.MapToCommentDto()))
```

Нужен маппинг `CommentEntity → CommentDto`. Тестами покрыт только случай `List<T> → List<TDto>`. Вложенные *одиночные* объекты автоматически не маппятся, см. [Известные ограничения](#известные-ограничения).

### Интеграция с ASP.NET Core

```csharp
// Program.cs — регистрирует IMapper и типы MappingProfile из сборки
builder.Services.AddIronMapper(typeof(Program).Assembly);

// В контроллере / сервисе
public class OrdersController(IMapper mapper) : ControllerBase
{
    public OrderDto Get(int id) => mapper.Map<OrderDto>(_repo.GetById(id));
}
```

---

## Известные ограничения

Текущие пробелы, подтверждённые чтением кода. Каждый из них запланирован или отслеживается в issue.

- **Вложенные одиночные объекты не маппятся автоматически.** Для `Customer.Address → CustomerDto.Address` с разными типами генератор выдаёт `Address = source.Address`, и сборка падает с `CS0029` в сгенерированном файле. Обходной путь: `ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()))`. Вложенные *коллекции* поддерживаются (см. выше).
- **Private и protected вложенные типы маппить нельзя.** Сгенерированные методы находятся в классе верхнего уровня `IronMapper.Generated.GeneratedMappers`, которому не видны вложенные типы с доступом `private`, `protected` и `private protected`. Такой маппинг сообщается как `IM0010` и пропускается; остальные маппинги генерируются. Internal-типы поддерживаются: сгенерированные методы тогда `internal` (см. [Доступность сгенерированных методов](docs/configuration.ru.md#доступность-сгенерированных-методов)).
- **Решения из нескольких проектов:** каждый проект объявляет свой `IronMapper.Generated.GeneratedMappers` (`internal`, если все его маппинги internal). Маппинги других проектов вызывайте как методы расширения (`source.MapToX()`); явное имя класса даёт `CS0436`, если виден класс проекта-зависимости, а маппинг одной пары типов в двух проектах делает `source.MapToX()` неоднозначным (`CS0121`). См. [Решения из нескольких проектов](docs/configuration.ru.md#решения-из-нескольких-проектов).
- **Позиционные record как destination не компилируются** (`CS7036`): генератор всегда использует object initializer. Record и классы со свойствами `init` работают.
- **Несовместимые типы свойств не диагностируются.** `IM0002`, `IM0003` и `IM0004` объявлены, но нигде не выдаются; сборка падает позже с ошибкой C# в сгенерированном файле. См. [диагностики](docs/diagnostics.ru.md).
- **Destination-типы с одинаковым именем получают уточнённые имена методов.** Маппинг одного source-типа на два destination-типа с одинаковым простым именем (например, `Admin.UserDto` и `Public.UserDto`) генерирует `MapToAdminUserDto`/`MapToPublicUserDto` вместо `MapToUserDto` (`IM0013`); если такой второй тип добавить позже, существующий метод переименовывается. Пара типов, заданная дважды, генерируется один раз (`IM0007`, побеждает профиль). См. [Имена методов и коллизии](docs/configuration.ru.md#имена-методов-и-коллизии).
- **Для профилей диагностик меньше.** `IM0001` и `IM0009` выдаются только для `[MapTo]`/`[MapFrom]`; несопоставленные свойства destination в профилях молча пропускаются. Все диагностики выдаются без указания места в исходном коде.
- **In-place перегрузка** `source.MapToX(existing)` игнорирует `When`, `ConvertUsing`, `BeforeMap`/`AfterMap` и пропускает свойства `init`.
- **`ReverseMap()`** сопоставляет только по именам свойств; `ForMember`, `When`, хуки и `IncludeMembers` в обратную сторону не переносятся.
- **Лямбды из профилей** переписываются в сгенерированный класс с полными именами (методы расширения — в статические вызовы). В них нельзя использовать члены экземпляра профиля, `this` и локальные переменные конструктора (`IM0012`), а также `private`/`protected` члены — сделайте их `internal` (`IM0011`). Методы расширения после `?.` выводятся без изменений. См. [Лямбды в профилях](docs/configuration.ru.md#лямбды-в-профилях).
- **Конвертеры** создаются через `new TConverter()` (нужен публичный конструктор без параметров). `IronMapperOptions.AddConverter<T>()` только регистрирует тип в DI; сгенерированный код его не использует.
- **`IMapper` (IronMapper.Extensions.DI) использует reflection**: сканирует загруженные сборки, ищет метод по имени destination-типа и точному runtime-типу source и вызывает его через `MethodInfo.Invoke`. `Map<List<TDto>>(list)` бросает `MappingException` ([#8](https://github.com/iron-mapper/IronMapper/issues/8)); передачи контекста в вызов нет ([#7](https://github.com/iron-mapper/IronMapper/issues/7)).
- **`ProjectTo` / проекция `IQueryable`** пока не поддерживается (запланировано, [#9](https://github.com/iron-mapper/IronMapper/issues/9)).
- **Совместимость с NativeAOT / trimming не проверялась.** Сгенерированные extension-методы не используют reflection; `IMapper` использует reflection, его следует считать несовместимым.
- **Платформы.** Пакеты собираются под `netstandard2.0` + `net10.0` (IronMapper) и `netstandard2.1` + `net10.0` (IronMapper.Extensions.DI). Тесты запускаются только на .NET 10 (Ubuntu и Windows в CI); другие среды выполнения не тестируются. Генератор собран против Microsoft.CodeAnalysis 4.11.
- **Code fix для IM0001/IM0006 не покрыты тестами.**

---

## Документация

- [Getting Started](docs/getting-started.ru.md)
- [Configuration Reference](docs/configuration.ru.md)
- [Advanced Topics](docs/advanced.ru.md)
- [Migrating from AutoMapper](docs/migration-from-automapper.ru.md)
- [Compiler Diagnostics](docs/diagnostics.ru.md)

---

## Contributing

Мы рады участию! Пожалуйста:

1. Сделай fork репозитория и создай feature-ветку (`feature/my-feature`).
2. Внеси изменения с тестами.
3. Запусти `dotnet build` и `dotnet test` — оба должны завершиться без ошибок.
4. Открой pull request в `main`.

Придерживайся существующего стиля кода (см. [.editorconfig](.editorconfig)).

---

## Лицензия

[MIT](LICENSE) © 2026 Aleksey Mironov
