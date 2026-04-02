# Getting Started with IronMapper

## Installation

```bash
dotnet add package IronMapper
```

For ASP.NET Core DI integration, also add:

```bash
dotnet add package IronMapper.Extensions.DI
```

---

## Your first mapping in 3 steps (attribute approach)

### Step 1 — Annotate the source type

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

### Step 2 — Build

```bash
dotnet build
```

The source generator emits `MapToUserDto()` extension methods into your assembly at compile time.

### Step 3 — Call the generated extension

```csharp
var entity = new UserEntity { Id = 1, Name = "Alice", Email = "alice@example.com" };
UserDto dto = entity.MapToUserDto();
```

---

## Your first mapping via Profile

Profiles are useful when you can't annotate the source type (e.g., it lives in an external library) or when you want to keep mapping config in one place.

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

The generator discovers all `MappingProfile` subclasses at compile time and emits the same extension methods. No additional wiring needed.

---

## Integration with ASP.NET Core

Install the DI extension package, then register IronMapper in `Program.cs`:

```csharp
using IronMapper.Extensions.DI;

var builder = WebApplication.CreateBuilder(args);

// Scans the calling assembly for MappingProfile subclasses automatically.
builder.Services.AddIronMapper(typeof(Program).Assembly);
```

Inject `IMapper` wherever you need runtime mapping (e.g., in controllers):

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

> **Tip:** The generated extension methods (`entity.MapToUserDto()`) work without DI and are always available.
> `IMapper` is optional — use it when the destination type is only known at runtime.
