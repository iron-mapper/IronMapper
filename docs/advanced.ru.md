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

Параметр — `IEnumerable<T>`, но сгенерированный код сначала проверяет тип во время выполнения: массив или `List<T>`
читается циклом `for`, и результат создаётся сразу нужного размера; другой `ICollection<T>` даёт размер через `Count`
и читается `foreach`; любая другая последовательность читается `foreach` в растущий список. Без LINQ и делегатов:
каждый элемент маппится прямым вызовом `MapToUserDto`.

Начиная с 1.2.0 эти циклы написаны один раз на проект, в двух обобщённых методах `GeneratedMappers`
(`MapElementsToList`, `MapElementsToArray`); `MapToUserDtoList` — однострочный вызов с маленькой закрытой структурой
маппинга, чей `Map` вызывает `MapToUserDto`. Структура передаётся аргументом типа, поэтому среда выполнения компилирует
отдельную копию цикла для каждого маппинга, где вызов прямой и может быть встроен: метод коллекции занимает несколько
строк на маппинг вместо нескольких десятков, косвенного вызова на элемент нет. Проверки, ёмкость и результаты те же.

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

Сгенерированный код (закрытый вспомогательный метод на каждое свойство-коллекцию):

```csharp
Comments = MapCollection_Comments_BlogEntity_BlogDto(source.Comments),
// ...
private static List<CommentDto> MapCollection_Comments_BlogEntity_BlogDto(List<CommentEntity>? source)
{
    if (source is null) return null!;
    var result = new List<CommentDto>(source.Count);
    for (var i = 0; i < source.Count; i++) result.Add(source[i].MapToCommentDto());
    return result;
}
```

Как читается источник, зависит от его объявленного типа: `T[]`, `List<T>`, `IList<T>` и `IReadOnlyList<T>` — циклом
`for` по индексатору; `ICollection<T>`, `IReadOnlyCollection<T>` и классы с открытым `Count` (например,
`HashSet<T>`) — `foreach` по объявленному типу, результат сразу нужного размера; остальное — `foreach`. Приёмником
может быть `List<T>`, `T[]`, `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`
(присваивается `List<T>`) или `IEnumerable<T>`.

Замечания:
- Должен существовать маппинг для типов элементов (`CommentEntity → CommentDto`), иначе сгенерированный вызов не скомпилируется.
- Если source-коллекция равна `null`, результат тоже `null`, а не пустая коллекция.
- Приёмник `IEnumerable<T>` — **ленивая** последовательность (как в 1.1): элементы маппятся при перечислении, при
  каждом перечислении заново, и изменения исходной коллекции после маппинга видны.
- Все сочетания перечисленных видов источника и приёмника покрыты runtime-тестами (`CollectionShapeTests`).
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
- `IMapper` из `IronMapper.Extensions.DI` (`RuntimeMapper`) вызывает те же сгенерированные методы через делегаты,
  без reflection (начиная с 1.2.0). Для каждой сборки с маппингами генератор выпускает регистрацию
  (`GeneratedMappers.Registration.g.cs`): инициализатор модуля (`[ModuleInitializer]`) передаёт её в
  `IronMapper.Registration.MapperRegistry`, а тот регистрирует маппинги при создании первого `IMapper` (для сборки, к
  которой обратились позже, — при следующем маппинге). Инициализатор модуля только кладёт в список один делегат на
  сборку, поэтому приложение без `IMapper` ничего не регистрирует; сборку IronMapper оно всё же загружает: инициализатор
  проверяет там `MapperRegistry.IsSupported` (см. ниже). Это работает с
  trimming и NativeAOT, а исключения сгенерированного кода, ваших конвертеров и хуков приходят к вызывающему как есть.
  До 1.1.4 включительно `RuntimeMapper` искал `GeneratedMappers` в загруженных сборках, вызывал методы через
  `MethodInfo.Invoke` (исключения оборачивались в `TargetInvocationException`) и не работал в NativeAOT.
- **Какие сборки регистрируются.** Инициализатор модуля выполняется при первом использовании кода сборки.
  `AddIronMapper(assembly)` (а также `AddProfile<T>()` и `AddProfilesFromAssembly`) запускает его сам
  (`MapperRegistry.EnsureModuleInitialized`), поэтому
  передайте все сборки с маппингами, особенно ту, которую приложение больше никак не использует до первого маппинга
  (профили в `Application`, сущности в `Domain`, вызов из веб-проекта). Если маппинг не найден, `IMapper` запускает
  инициализаторы модулей всех загруженных сборок (кроме имён, начинающихся с `System.` и `Microsoft.`) и ищет снова.
  Профили не добавляются в DI-контейнер: до 1.1.4 включительно `AddIronMapper` сканировал сборки через `GetTypes()` и
  добавлял каждый `MappingProfile` как singleton.
- **Приложения с trimming и NativeAOT без пакета DI.** Регистрация стоит за feature switch
  `IronMapper.Registration.IsSupported`: при публикации с trimming или NativeAOT, если ни один проект не ссылается на
  `IronMapper.Extensions.DI`, пакет выключает его, и trimmer удаляет регистрацию, поэтому приложение, которое вызывает
  только extension-методы, её не несёт. Правила и свойство `IronMapperRegistration` — в разделе
  [Регистрация для IMapper](configuration.ru.md#регистрация-для-imapper-в-приложениях-с-trimming-и-nativeaot).
- **C# 8** (`LangVersion` ниже 9) не поддерживает инициализаторы модулей: регистрация генерируется без
  `[ModuleInitializer]`, и `IMapper` не найдёт маппинги этой сборки, пока вы не вызовете
  `IronMapper.Generated.GeneratedMappers.RegisterIronMapperMappings()` один раз при старте (метод `internal`, поэтому
  вызывайте его из этой сборки). Для целей без `ModuleInitializerAttribute` (`netstandard`, .NET Framework) в сборку
  генерируется internal-копия атрибута.
- `Map<TSource, TDest>(source)` использует маппинг точной пары: чтение статического поля и вызов делегата. Если его
  нет, а также для `Map<TDest>(object)` маппинг ищется по типу: runtime-тип источника, затем его базовые типы
  (`DerivedEntity` без своего маппинга использует маппинг `Entity`). Интерфейсы источника и базовые типы приёмника не
  учитываются. Если одну пару типов маппят две сборки, действует первая регистрация.
- **Коллекции как тип приёмника** ([#8](https://github.com/iron-mapper/IronMapper/issues/8)): `Map<List<UserDto>>(users)`, `Map<UserDto[]>`,
  `Map<IEnumerable<UserDto>>`, а также `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`
  работают для любого источника `IEnumerable<User>` (`List`, массив, `HashSet`, …), если есть маппинг
  `User → UserDto`. Они вызывают сгенерированные `MapToUserDtoList`/`MapToUserDtoArray`, поэтому результат —
  `List<UserDto>` (для `T[]` — массив).
- `RuntimeMapper.Map(object source, Type sourceType, Type destinationType)` и `MapperRegistry.Map` маппят по `Type`.
  В интерфейсе `IMapper` этого метода нет: новый член интерфейса сломал бы другие его реализации в 1.x.
- `IMapper.MapCollection<TSource, TDest>` маппит весь источник во время вызова и возвращает `List<TDest>` (с типом
  `IEnumerable<TDest>`); повторное перечисление результата не маппит заново. До 1.1.4 включительно метод возвращал
  ленивую последовательность, которая маппила при каждом перечислении и бросала исключение для `null`-источника
  только при перечислении.

### Производительность

IronMapper 1.1.3, .NET 10.0.12, Intel Core i7-12650H; среднее время и отношение к ручному коду. Полный отчёт,
методика и другие библиотеки: [`benchmarks/results/RESULTS.md`](../benchmarks/results/RESULTS.md) (на английском).

| Сценарий | Extension-метод | `IMapper` |
|---|---:|---:|
| Flat (15 свойств) | 11,4 нс (×0,98), 168 Б | 31,0 нс (×2,66), 200 Б |
| Nested (заказ, покупатель, адрес, 5–20 строк) | 209,8 нс (×1,26) | 227,6 нс (×1,37) |
| Коллекция, 1 000 заказов | 258,1 мкс (×1,01) | 282,4 мкс (×1,11) |
| Холодный старт, Δ мс к консольному приложению без маппера | 2,2 | 23,1 |

- В 1.1.3 `IMapper` добавляет вызов через reflection (`MethodInfo.Invoke`) на каждый вызов и сканирование сборок при
  первом вызове для пары типов. Начиная с 1.2.0 он вызывает сгенерированные методы через делегаты и сам ничего не
  выделяет; таблицу обновит полный прогон к релизу.
- В сценарии Nested IronMapper маппит вложенные объекты через `ForMember(... MapFrom(...))` (см.
  [Вложенные объекты](#вложенные-объекты)); сгенерированный код коллекций 1.1.3 использует `Enumerable.Select` +
  `ToList`. У Mapperly там ×1,00.
- Не выпущено (1.2.0): в сгенерированном коде нет LINQ и делегатов (циклы с известным размером, см.
  [Вложенные collection-свойства](#вложенные-collection-свойства)). Предварительный прогон локальных исходников на той же
  машине (не полный; таблицу выше обновит полный прогон к релизу): Nested ×1,09, коллекция из 10 заказов ×0,97
  (Mapperly ×1,18), обновление на месте ×0,90. Оставшийся разрыв в Nested — проверки на null в сгенерированных
  методах: из-за них JIT не встраивает `MapToAddressDto` в `MapToCustomerDto`; без них тот же прогон даёт ×1,02.
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
  255,5 КБ больше приложения без маппера (у Mapperly +23,6 КБ) в 1.1.3, в основном потому, что сгенерированный код
  коллекций (`Enumerable.Select` + `ToList`) подтягивает загрузчик типов NativeAOT. Не выпущено (1.2.0, без LINQ в
  сгенерированном коде): +2,6 КБ, предварительно, замерено на локальных исходниках. Регистрации для `IMapper` в таком
  приложении нет: без `IronMapper.Extensions.DI` trimmer её удаляет (см.
  [Регистрация для IMapper](configuration.ru.md#регистрация-для-imapper-в-приложениях-с-trimming-и-nativeaot)); CI
  проверяет, что в `tests/IronMapper.AotProbe` её нет, а с `IronMapperRegistration=true` она есть.
- **`IMapper` / `AddIronMapper` (IronMapper.Extensions.DI): совместимы начиная с 1.2.0** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)). Они используют
  сгенерированный реестр (см. [IMapper и сгенерированные extension-методы](#imapper-и-сгенерированные-extension-методы)),
  а не reflection по сгенерированному коду. CI публикует `tests/IronMapper.AotProbe.DI` для .NET 8 и .NET 10, не
  допуская ни одного предупреждения trimming/AOT, и запускает его: `AddIronMapper`, `Map` по объявленному и
  runtime-типу, обновление на месте, `MapCollection`, `Map<List<T>>`/`Map<T[]>`/`Map<IEnumerable<T>>`, маппинг из
  профиля с конвертером, исходное `ArgumentNullException` для `null`-элемента. Ещё CI публикует
  `tests/IronMapper.AotProbe.Transitive` (пакет DI только через библиотеку, маппинги в библиотеке без него), проверяет,
  что с `IronMapperRegistration=false` приложение падает с сообщением, где названо свойство, и публикует пробу
  extension-методов и пробу `IMapper` с trimming без NativeAOT (`PublishTrimmed`, self-contained; ILLink): без
  предупреждений, они запускаются, из первой регистрация удалена, во второй осталась. До 1.1.4 включительно `RuntimeMapper`
  находил сгенерированные методы по имени через reflection, чего trimmer не видит: публикация выдавала IL2026, IL2062,
  IL2072 и IL2075 внутри сборки IronMapper (по умолчанию одним общим IL2104), а исполняемый файл падал при первом
  вызове с `MappingException: No mapping registered`.
- **`MappingValidator`** читает свойства и конструкторы переданных типов через reflection и тоже помечен
  `[RequiresUnreferencedCode]`: вызов в приложении с trimming или NativeAOT даёт IL2026. Генератор сообщает об этих
  проблемах при компиляции.
