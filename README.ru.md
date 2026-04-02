# IronMapper

[![NuGet](https://img.shields.io/nuget/v/IronMapper.svg)](https://www.nuget.org/packages/IronMapper)
[![Build](https://github.com/algmironov/IronMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/algmironov/IronMapper/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> 🌐 **[English documentation](README.md)**

**Zero-reflection, compile-time object mapper для .NET.**

IronMapper генерирует весь код маппинга во время сборки с помощью Roslyn Source Generator — никакой reflection во время выполнения, никакого IL emit, никаких динамических прокси. Только чистый, читаемый и отлаживаемый C#, который компилятор может оптимизировать как любой другой код.

---

## IronMapper vs AutoMapper

| Возможность | AutoMapper | IronMapper |
|---|---|---|
| Стратегия маппинга | Reflection во время выполнения | Source-generated C# во время сборки |
| Ошибки конфигурации обнаруживаются | При запуске приложения | При `dotnet build` |
| NativeAOT / IL trimming | Требует аннотаций | Полностью совместим |
| Производительность | ~2–5 мкс / объект | ~0 нс накладных расходов |
| Лицензия | MIT | MIT |
| Стиль API | Profile-based fluent API | Атрибуты **или** Profile-based fluent API |
| Размер пакета | ~500 КБ | ~30 КБ |

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

Всё. Никакого `IMapper`, никакой регистрации сервисов, никаких накладных расходов при запуске.

---

## Demo Application

Мы собрали демонстрационное консольное приложение, которое показывает все возможности IronMapper,
используя реальное Open Library API (API-ключ не нужен).

**Что демонстрирует пример:**
- Простой attribute-based mapping (`[MapTo]`)
- Fluent API с `MappingProfile`
- Collection mapping (`List<BookDoc>` → `List<BookSummary>`)
- Вложенный object mapping (Book + Author)
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
// генератор выдаёт: Comments = Enumerable.ToList(Enumerable.Select(source.Comments, x => x.MapToCommentDto()))
```

### Интеграция с ASP.NET Core

```csharp
// Program.cs
builder.Services.AddIronMapper(typeof(Program).Assembly);

// В контроллере / сервисе
public class OrdersController(IMapper mapper) : ControllerBase
{
    public OrderDto Get(int id) => mapper.Map<OrderDto>(_repo.GetById(id));
}
```

---

## Документация

- [Getting Started](docs/getting-started.md)
- [Configuration Reference](docs/configuration.md)
- [Advanced Topics](docs/advanced.md)
- [Migrating from AutoMapper](docs/migration-from-automapper.md)
- [Compiler Diagnostics](docs/diagnostics.md)

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

[MIT](LICENSE) © Iron Programmer School
