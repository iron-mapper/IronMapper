# Начало работы с IronMapper

> 🌐 **[English version](getting-started.md)**

## Установка

```bash
dotnet add package IronMapper
```

Для интеграции с DI в ASP.NET Core установите также:

```bash
dotnet add package IronMapper.Extensions.DI
```

---

## Первый mapping за 3 шага (через атрибуты)

### Шаг 1 — Аннотируй source-тип

```csharp
using IronMapper.Attributes;

[MapTo(typeof(UserDto))]
public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

### Шаг 2 — Собери проект

```bash
dotnet build
```

Source generator генерирует extension method `MapToUserDto()` прямо в твою сборку во время компиляции.

### Шаг 3 — Вызови сгенерированный extension

```csharp
using IronMapper.Generated;   // сгенерированные методы MapTo* лежат в этом пространстве имён

var entity = new UserEntity { Id = 1, Name = "Alice", Email = "alice@example.com" };
UserDto dto = entity.MapToUserDto();
```

**`using IronMapper.Generated;` обязателен** в каждом файле, где вызываются методы `MapTo*`. Без него вызов падает
с `CS1061` («не содержит определения "MapToUserDto"»). Та же ошибка появляется, если генератор вообще не запустился,
например на .NET SDK старше 8.0.400 (тогда сборка ещё выдаёт предупреждение `CS9057`).

---

## Первый mapping через Profile

Profile удобен, когда нельзя аннотировать source-тип (например, он находится во внешней библиотеке), или когда хочется держать конфигурацию маппинга в одном месте.

```csharp
using IronMapper.Configuration;

public class UserProfile : MappingProfile
{
    public UserProfile()
    {
        CreateMap<UserEntity, UserDto>();
    }
}
```

Генератор обнаруживает все подклассы `MappingProfile` во время компиляции и генерирует те же extension methods. Никакой дополнительной настройки не нужно.

---

## Интеграция с ASP.NET Core

Установи пакет DI extension, затем зарегистрируй IronMapper в `Program.cs`:

```csharp
using IronMapper.Extensions.DI;

var builder = WebApplication.CreateBuilder(args);

// Регистрирует IMapper и подклассы MappingProfile из этой сборки (как singleton).
// Сами маппинги генерируются при компиляции; регистрация их не меняет.
builder.Services.AddIronMapper(typeof(Program).Assembly);
```

Внедри `IMapper` туда, где удобнее сервис, а не extension-методы (например, в контроллеры):

```csharp
using IronMapper.Interfaces;

public class UsersController : ControllerBase
{
    private readonly IMapper _mapper;
    private readonly IUserRepository _repository;   // ваш доступ к данным

    public UsersController(IMapper mapper, IUserRepository repository)
    {
        _mapper = mapper;
        _repository = repository;
    }

    [HttpGet("{id}")]
    public UserDto Get(int id)
    {
        var entity = _repository.GetById(id);
        return _mapper.Map<UserDto>(entity);
    }
}
```

> **Совет:** Сгенерированные extension methods (`entity.MapToUserDto()`) работают без DI и доступны всегда.
> `IMapper` опционален. Он находит и вызывает сгенерированные методы через reflection, поэтому там, где типы известны
> при компиляции, лучше использовать extension-методы. См. [IMapper и сгенерированные extension-методы](advanced.ru.md#imapper-и-сгенерированные-extension-методы).
