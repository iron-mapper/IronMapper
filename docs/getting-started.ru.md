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
var entity = new UserEntity { Id = 1, Name = "Alice", Email = "alice@example.com" };
UserDto dto = entity.MapToUserDto();
```

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

// Автоматически сканирует сборку на наличие подклассов MappingProfile.
builder.Services.AddIronMapper(typeof(Program).Assembly);
```

Внедри `IMapper` туда, где нужен runtime mapping (например, в контроллеры):

```csharp
using IronMapper.Interfaces;

public class UsersController : ControllerBase
{
    private readonly IMapper _mapper;

    public UsersController(IMapper mapper) => _mapper = mapper;

    [HttpGet("{id}")]
    public UserDto Get(int id)
    {
        var entity = _repository.GetById(id);
        return _mapper.Map<UserDto>(entity);
    }
}
```

> **Совет:** Сгенерированные extension methods (`entity.MapToUserDto()`) работают без DI и доступны всегда.
> `IMapper` опционален — используй его, когда destination-тип известен только во время выполнения.
