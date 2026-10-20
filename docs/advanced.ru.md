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
генератор вызывает `MapTo{DestElementType}()` (или его уточнённое имя, см.
[Имена методов и коллизии](configuration.ru.md#имена-методов-и-коллизии)) для каждого элемента:

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
  `Microsoft.`) в поисках `IronMapper.Generated.GeneratedMappers` (public или internal) и ищет метод `MapTo*` по
  сигнатуре (точный тип source в параметре, `TDest` в возвращаемом типе), поэтому находит и уточнённые имена
  (`IM0013`); найденный `MethodInfo` кэшируется. **Каждый вызов** затем выполняется через `MethodInfo.Invoke`.
- `IMapper.Map<TDest>(object)` использует runtime-тип source; производный тип source не совпадёт с маппингом,
  объявленным для базового типа.
- `IMapper.Map<List<TDto>>(list)` не поддерживается и бросает `MappingException`
  ([#8](https://github.com/iron-mapper/IronMapper/issues/8)); используйте `MapCollection<TSource, TDest>` или `list.MapToDtoList()`.

### Производительность

IronMapper 1.1.3, .NET 10.0.12, Intel Core i7-12650H; среднее время и отношение к ручному коду. Полный отчёт,
методика и другие библиотеки: [`benchmarks/results/RESULTS.md`](../benchmarks/results/RESULTS.md) (на английском).

| Сценарий | Extension-метод | `IMapper` |
|---|---:|---:|
| Flat (15 свойств) | 11,4 нс (×0,98), 168 Б | 31,0 нс (×2,66), 200 Б |
| Nested (заказ, покупатель, адрес, 5–20 строк) | 209,8 нс (×1,26) | 227,6 нс (×1,37) |
| Коллекция, 1 000 заказов | 258,1 мкс (×1,01) | 282,4 мкс (×1,11) |
| Холодный старт, Δ мс к консольному приложению без маппера | 2,2 | 23,1 |

- `IMapper` добавляет вызов через reflection (`MethodInfo.Invoke`) на каждый вызов и сканирование сборок при первом
  вызове для пары типов.
- В сценарии Nested IronMapper маппит вложенные объекты через `ForMember(... MapFrom(...))` (см.
  [Вложенные объекты](#вложенные-объекты)); сгенерированный код коллекций использует `Enumerable.Select` + `ToList`.
  У Mapperly там ×1,00.
- Холодный старт чувствителен к шуму, смотрите на порядок величины (AutoMapper +73,5 мс, Mapster +99,1 мс).
- Измерялся только .NET 10. CI лишь проверяет, что набор бенчмарков собирается и каждый сценарий проходит одну
  итерацию на .NET 10 и .NET 8; цифры он не публикует. Как запустить набор:
  [`benchmarks/README.md`](../benchmarks/README.md) (на английском).

### NativeAOT и trimming

Сгенерированные маппинги проверяются в NativeAOT в CI при каждом изменении: `eng/verify-aot.ps1` публикует
консольное приложение на упакованном пакете с `PublishAot=true` для .NET 8 и .NET 10 (linux-x64) и запускает его
(`tests/IronMapper.AotProbe`: маппинги из атрибутов и профиля, коллекции, обновление на месте, конвертер).

- **Сгенерированные extension-методы: совместимы.** Предупреждений trimming/AOT нет (проверка в CI падает на любом);
  исполняемый файл запускается и выдаёт правильный результат. В наборе бенчмарков (win-x64, .NET 10,
  [`benchmarks/results/aot.md`](../benchmarks/results/aot.md)) время старта такое же, как без маппера, а файл на
  255,5 КБ больше приложения без маппера (у Mapperly +23,6 КБ), в основном потому, что сгенерированный код коллекций
  (`Enumerable.Select` + `ToList`) подтягивает загрузчик типов NativeAOT.
- **`IMapper` / `AddIronMapper` (IronMapper.Extensions.DI): пока несовместимы** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)).
  `RuntimeMapper` находит сгенерированные методы по имени через reflection (сканирование сборок `AppDomain`,
  `Assembly.GetType`, `GetMethods`), чего trimmer не видит, их метаданные не сохраняются, и исполняемый файл падает
  при первом вызове с `MappingException: No mapping registered`. `AddIronMapper` и `RuntimeMapper` помечены
  `[RequiresUnreferencedCode]`, поэтому публикация с trimming или NativeAOT сообщает об этом на вашем вызове:

  ```text
  Program.cs(8,1): warning IL2026: Using member 'IronMapper.Extensions.DI.IronMapperServiceCollectionExtensions.AddIronMapper(IServiceCollection, params Assembly[])' which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming application code. IMapper finds and calls the generated mapping methods through reflection; trimming and NativeAOT remove them, and mapping fails at runtime with MappingException. Call the generated MapTo* extension methods (using IronMapper.Generated;) instead. https://github.com/iron-mapper/IronMapper/issues/26
  ```

  CI проверяет, что это предупреждение есть, а других предупреждений из сборок IronMapper нет
  (`tests/IronMapper.AotProbe.DI`). До 1.1.4 включительно предупреждения (IL2026, IL2062, IL2072, IL2075) указывали
  внутрь сборки IronMapper, по умолчанию одним общим IL2104, а не на ваш вызов. В приложениях с NativeAOT и trimming
  используйте extension-методы.
- **`MappingValidator`** читает свойства и конструкторы переданных типов через reflection и тоже помечен
  `[RequiresUnreferencedCode]`: вызов в приложении с trimming или NativeAOT даёт IL2026. Генератор сообщает об этих
  проблемах при компиляции.
