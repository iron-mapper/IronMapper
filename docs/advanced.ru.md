# Продвинутые возможности

> 🌐 **[English version](advanced.md)**

## Вложенные объекты

Когда тип свойства сам имеет атрибут `[MapTo]` / `[MapFrom]` или маппинг через `MappingProfile`, генератор автоматически генерирует вложенный вызов.

```csharp
[MapTo(typeof(AddressDto))]
public class Address
{
    public string Street { get; set; } = "";
    public string City   { get; set; } = "";
}

[MapTo(typeof(CustomerDto))]
public class Customer
{
    public string  Name    { get; set; } = "";
    public Address Address { get; set; } = new();
}

public class AddressDto { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
public class CustomerDto { public string Name { get; set; } = ""; public AddressDto Address { get; set; } = new(); }
```

Сгенерированный код:

```csharp
// MapToCustomerDto
return new CustomerDto
{
    Name    = source.Name,
    Address = source.Address.MapToAddressDto(),
};
```

---

## Коллекции

### Вспомогательные extension methods для коллекций

Для каждого маппинга через `[MapTo]` генератор создаёт два вспомогательных метода:

```csharp
// Возвращает List<TDest> (пустой список, если source == null)
IEnumerable<UserEntity>? entities = ...;
List<UserDto> dtos = entities.MapToUserDtoList();

// Возвращает TDest[] (Array.Empty<TDest>(), если source == null)
UserDto[] arr = entities.MapToUserDtoArray();
```

### Вложенные collection-свойства

Когда свойство source имеет тип `List<T>`, а соответствующее свойство destination — `List<TDest>` (с разными типами элементов), генератор автоматически генерирует вызов `Select + ToList`:

```csharp
[MapTo(typeof(BlogDto))]
public class BlogEntity
{
    public int Id { get; set; }
    public List<CommentEntity> Comments { get; set; } = new();
}
```

Сгенерированный код:

```csharp
Comments = source.Comments is null ? null!
    : global::System.Linq.Enumerable.ToList(
        global::System.Linq.Enumerable.Select(source.Comments, x => x.MapToCommentDto())),
```

---

## Record-типы

Record-типы с позиционными конструкторами поддерживаются как destination. Генератор выдаёт вызов конструктора, когда destination-тип имеет primary constructor:

```csharp
[MapTo(typeof(PointDto))]
public class PointEntity { public double X { get; set; } public double Y { get; set; } }

public record PointDto(double X, double Y);
```

> **Примечание:** Все параметры конструктора должны совпадать с именами свойств source (без учёта регистра). Несопоставленные параметры вызывают предупреждение `IM0009`.

---

## In-place mapping

Каждый сгенерированный маппинг включает void-перегрузку, которая обновляет существующий объект:

```csharp
var entity = new WorkerEntity { Id = 7, Name = "Alice" };
var dto    = new WorkerDto();

entity.MapToWorkerDto(dto);  // заполняет dto на месте без аллокации нового объекта

// эквивалентно через IMapper:
mapper.Map<WorkerEntity, WorkerDto>(entity, dto);
```

Свойства destination с `init`-only setter молча пропускаются in-place перегрузкой.

---

## Советы по производительности

- В hot path предпочитай сгенерированные extension methods (`source.MapToDto()`) вместо `IMapper.Map<>()` — extension methods это обычный скомпилированный C# без reflection.
- `IMapper` выполняет однократный reflection-поиск для каждой пары `(TSource, TDest)` и кэширует `MethodInfo`. Последующие вызовы платят только за lookup в `ConcurrentDictionary`.
- Для bulk collection mapping используй `MapToDestTypeList()` / `MapToDestTypeArray()` вместо ручного цикла `Select` — сгенерированный код эквивалентен, но избегает лишней аллокации замыкания.
- Поскольку весь код маппинга генерируется во время компиляции, IronMapper полностью совместим с **NativeAOT** и **IL trimming** без каких-либо дополнительных аннотаций.
