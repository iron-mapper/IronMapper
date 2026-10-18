# Продвинутые возможности

> 🌐 **[English version](advanced.md)**

## Вложенные объекты

Вложенные **одиночные** объекты **пока не маппятся автоматически**. Если у source и destination есть
свойство с одинаковым именем, но разными типами, генератор выдаёт простое присваивание:

```csharp
// сгенерировано для Customer → CustomerDto
Address = source.Address,   // error CS0029: нельзя преобразовать Address в AddressDto
```

Сборка падает внутри сгенерированного файла (диагностика IronMapper при этом не выдаётся, см.
[IM0002](diagnostics.ru.md#im0002--несовместимые-типы-свойств)).

**Обходной путь (профиль):** замапьте вложенное свойство явно через `MapFrom`. Лямбда выводится в
сгенерированный класс, где доступны остальные сгенерированные методы `MapTo*`:

```csharp
public class CustomerProfile : MappingProfile
{
    public CustomerProfile()
    {
        CreateMap<Address, AddressDto>();
        CreateMap<Customer, CustomerDto>()
            .ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()));
    }
}
```

Для маппингов через атрибуты поставьте `[Ignore]` на свойство destination и присвойте его после маппинга.
Так сделано в демо (`samples/IronMapper.Demo/Demos/Demo4_NestedObjects.cs`).

---

## Коллекции

### Вспомогательные extension methods для коллекций

Для каждого маппинга (через атрибуты или профиль) генератор создаёт два метода для коллекций:

```csharp
// Возвращает List<TDest> (пустой список, если source равен null)
IEnumerable<UserEntity>? entities = ...;
List<UserDto> dtos = entities.MapToUserDtoList();

// Возвращает TDest[] (Array.Empty<TDest>(), если source равен null)
UserDto[] arr = entities.MapToUserDtoArray();
```

### Вложенные collection-свойства

Если source-свойство — коллекция, а destination-свойство — коллекция с **другим** типом элементов,
генератор вызывает `MapTo{DestElementType}()` для каждого элемента:

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

Замечания:
- Должен существовать маппинг для типов элементов (`CommentEntity → CommentDto`), иначе сгенерированный вызов не скомпилируется.
- Если source-коллекция равна `null`, результат тоже `null`, а не пустая коллекция.
- Тестами покрыт только случай `List<T> → List<TDto>`. В генераторе есть ветки для массивов и других типов коллекций, но они не протестированы.
- Коллекции с одинаковым типом элементов присваиваются как есть (копируется ссылка).

---

## Record-типы

Генератор всегда создаёт destination через **object initializer** (`new TDest { ... }`) и никогда не
вызывает конструктор с аргументами.

- **Работает:** record (и классы) с конструктором без параметров и свойствами `init` или `set`:

  ```csharp
  public record PointDto
  {
      public double X { get; init; }
      public double Y { get; init; }
  }
  ```

- **Не компилируется:** позиционные record без конструктора без параметров, например
  `public record PointDto(double X, double Y);` — сгенерированный `new PointDto { ... }` падает с `CS7036`.

Для маппингов через атрибуты `IM0009` предупреждает, если параметру конструктора record не соответствует
ни одно source-свойство (см. [диагностики](diagnostics.ru.md#im0009--параметр-конструктора-record-не-сопоставлен)).

---

## In-place mapping

Каждый сгенерированный маппинг включает void-перегрузку, которая обновляет существующий объект:

```csharp
var entity = new WorkerEntity { Id = 7, Name = "Alice" };
var dto    = new WorkerDto();

entity.MapToWorkerDto(dto);  // заполняет dto на месте

// эквивалентно через IMapper:
mapper.Map<WorkerEntity, WorkerDto>(entity, dto);
```

In-place перегрузка только присваивает замапленные свойства. Она:
- пропускает свойства destination с `init`;
- **не** применяет `When`, `ConvertUsing`, `BeforeMap` и `AfterMap`.

---

## IMapper и сгенерированные extension-методы

- Сгенерированные extension-методы (`source.MapToDto()`) — обычный скомпилированный C# без reflection.
- `IMapper` из `IronMapper.Extensions.DI` (`RuntimeMapper`) использует reflection. При первом вызове для пары
  `(TSource, TDest)` он сканирует загруженные сборки (пропуская имена, начинающиеся с `System.` или
  `Microsoft.`) в поисках `IronMapper.Generated.GeneratedMappers` (public или internal) и ищет метод `MapTo{TDest.Name}` с точным
  типом source; найденный `MethodInfo` кэшируется. **Каждый вызов** затем выполняется через `MethodInfo.Invoke`.
- `IMapper.Map<TDest>(object)` использует runtime-тип source; производный тип source не совпадёт с маппингом,
  объявленным для базового типа.
- `IMapper.Map<List<TDto>>(list)` не поддерживается и бросает `MappingException`
  ([#8](https://github.com/iron-mapper/IronMapper/issues/8)); используйте `MapCollection<TSource, TDest>` или `list.MapToDtoList()`.

### Производительность

Результаты бенчмарков пока не опубликованы. В `tests/IronMapper.Benchmarks` есть бенчмарки BenchmarkDotNet
(ручной маппинг против сгенерированного, один объект и 1000 элементов); они запускаются в CI на `main`,
результаты будут добавлены в документацию после того, как будут закоммичены.

### NativeAOT и trimming

**Не проверялось.** Сгенерированные extension-методы не используют reflection, но публикация с NativeAOT или
trimming не тестируется. `IMapper` / `AddIronMapper` используют reflection (сканирование сборок `AppDomain`,
`Assembly.GetTypes()`, `MethodInfo.Invoke`) без аннотаций для trimming, их следует считать несовместимыми.
