# IronMapper — Plan & Claude Code Prompts
> Бесплатная альтернатива AutoMapper на .NET 10 от Iron Programmer School

---

## Концепция и название

**IronMapper** — compile-time Object-to-Object mapper для .NET 10.  
Ключевые принципы:
- Нулевая рефлексия в runtime — весь маппинг генерируется Roslyn Source Generator
- API, знакомый пользователям AutoMapper, но более простой
- Ошибки конфигурации → ошибки компиляции, а не runtime-исключения
- Полностью бесплатен (MIT License)

---

## Структура репозитория

```
IronMapper/
├── src/
│   ├── IronMapper/                    # Основная библиотека (runtime часть)
│   │   ├── Attributes/                 # [MapTo], [MapFrom], [Ignore], [MapProperty]
│   │   ├── Configuration/              # MappingProfile, MappingOptions
│   │   ├── Interfaces/                 # IMapper, ITypeConverter
│   │   └── IronMapper.csproj
│   │
│   ├── IronMapper.Generator/          # Roslyn Source Generator
│   │   ├── Analysis/                   # Анализ синтаксиса и семантики
│   │   ├── CodeGeneration/             # Генерация C# кода
│   │   ├── Diagnostics/                # Compile-time ошибки и предупреждения
│   │   └── IronMapper.Generator.csproj
│   │
│   └── IronMapper.Extensions.DI/      # Microsoft.DI интеграция (опционально)
│
├── tests/
│   ├── IronMapper.Tests/              # Unit-тесты (xUnit)
│   ├── IronMapper.Generator.Tests/    # Source Generator тесты
│   ├── IronMapper.Integration.Tests/  # Интеграционные тесты
│   └── IronMapper.Benchmarks/         # BenchmarkDotNet
│
├── docs/                               # Документация
│   ├── getting-started.md
│   ├── configuration.md
│   ├── advanced.md
│   └── migration-from-automapper.md
│
├── samples/                            # Примеры использования
│   └── IronMapper.Samples/
│
├── IronMapper.sln
├── README.md
├── CLAUDE.md                          # Инструкции для Claude Code
└── Directory.Build.props              # Общие MSBuild настройки
```

---

## Этапы реализации

### Этап 0 — Scaffold & Инфраструктура
**Цель**: Создать рабочий скелет солюшена, настроить CI, общие настройки сборки.

### Этап 1 — Атрибуты и интерфейсы (Runtime API)
**Цель**: Определить публичный API библиотеки — атрибуты, интерфейсы, базовые классы.

### Этап 2 — Roslyn Source Generator (ядро)
**Цель**: Реализовать генератор, который читает атрибуты/профили и генерирует маппер-классы.

### Этап 3 — Fluent API (MappingProfile)
**Цель**: Реализовать `CreateMap<TSource, TDest>()`, `.ForMember()`, `.Ignore()`, `.When()`.

### Этап 4 — Продвинутые сценарии
**Цель**: Вложенные объекты, коллекции, кастомные конвертеры, Nullable.

### Этап 5 — DI интеграция
**Цель**: `services.AddIronMapper()`, автосканирование профилей.

### Этап 6 — Диагностика и сообщения об ошибках
**Цель**: Понятные ошибки компиляции вместо runtime-исключений.

### Этап 7 — Тесты и бенчмарки
**Цель**: Полное покрытие, сравнение с AutoMapper и ручным маппингом.

### Этап 8 — Документация и NuGet
**Цель**: README, docs/, XML-комментарии, публикация пакета.

---

## Claude Code Prompts

---

### PROMPT 0 — Scaffold & Инфраструктура

```
Создай структуру .NET 10 солюшена для библиотеки IronMapper — compile-time Object Mapper, 
альтернативы AutoMapper.

ЗАДАЧА:
1. Создай IronMapper.sln
2. Создай проекты:
   - src/IronMapper/IronMapper.csproj (netstandard2.0 + net10.0, тип: library)
   - src/IronMapper.Generator/IronMapper.Generator.csproj (netstandard2.0, тип: Analyzer/Generator)
   - tests/IronMapper.Tests/IronMapper.Tests.csproj (net10.0, xUnit)
   - tests/IronMapper.Generator.Tests/IronMapper.Generator.Tests.csproj (net10.0, xUnit)
   - tests/IronMapper.Benchmarks/IronMapper.Benchmarks.csproj (net10.0, BenchmarkDotNet)

3. Создай Directory.Build.props с общими настройками:
   - Nullable=enable
   - ImplicitUsings=enable
   - TreatWarningsAsErrors=true
   - Версия: 1.0.0
   - Authors, Description, RepositoryUrl

4. Создай CLAUDE.md с описанием архитектуры проекта для Claude Code:
   - Описание каждого проекта
   - Соглашения именования
   - Как запускать тесты
   - Архитектурные решения (почему Source Generator, а не рефлексия)

5. Создай .editorconfig с настройками для C#

6. Создай GitHub Actions workflow (.github/workflows/ci.yml):
   - Сборка на ubuntu-latest, windows-latest
   - Запуск всех тестов
   - Запуск бенчмарков только на main ветке

7. Добавь .gitignore для .NET проектов

ТРЕБОВАНИЯ:
- IronMapper.Generator должен быть настроен как Roslyn Source Generator:
  <IsRoslynComponent>true</IsRoslynComponent>
  <AnalyzerLanguage>cs</AnalyzerLanguage>
- IronMapper.Tests должен референсить IronMapper и иметь настройки для тестирования генераторов:
  <ReferenceOutputAssembly>false</ReferenceOutputAssembly> для генераторного проекта

После создания структуры запусти `dotnet build` и убедись, что всё компилируется.
```

---

### PROMPT 1 — Атрибуты и Runtime API

```
В проекте src/IronMapper/ реализуй публичный API библиотеки IronMapper.

ЗАДАЧА — создай следующие типы:

1. Атрибуты (папка Attributes/):

[MapTo(Type destinationType)] — указывает целевой тип для маппинга
[MapFrom(Type sourceType)] — указывает исходный тип
[MapProperty(string destinationProperty)] — переименование поля при маппинге
[Ignore] — игнорировать поле при маппинге
[MapConverter(Type converterType)] — кастомный конвертер для поля

2. Интерфейсы (папка Interfaces/):

IMapper — основной интерфейс:
  TDest Map<TDest>(object source)
  TDest Map<TSource, TDest>(TSource source)
  void Map<TSource, TDest>(TSource source, TDest destination) // маппинг в существующий объект
  IEnumerable<TDest> MapCollection<TSource, TDest>(IEnumerable<TSource> source)

ITypeConverter<TSource, TDest> — интерфейс кастомного конвертера:
  TDest Convert(TSource source)

IMappingProfile — маркерный интерфейс для профилей

3. Базовые классы (папка Configuration/):

MappingProfile — базовый класс для профилей пользователя:
  protected IMappingExpression<TSource, TDest> CreateMap<TSource, TDest>()

IMappingExpression<TSource, TDest> — fluent builder:
  IMappingExpression<TSource, TDest> ForMember(Expression<Func<TDest, object?>> dest, Action<IMemberConfigurationExpression<TSource, TDest>> opts)
  IMappingExpression<TSource, TDest> Ignore(Expression<Func<TDest, object?>> dest)
  IMappingExpression<TSource, TDest> When(Func<TSource, bool> condition)
  IMappingExpression<TSource, TDest> ConvertUsing<TConverter>() where TConverter : ITypeConverter<TSource, TDest>
  IMappingExpression<TSource, TDest> ReverseMap()

IMemberConfigurationExpression<TSource, TDest>:
  void MapFrom<TMember>(Expression<Func<TSource, TMember>> sourceMember)
  void MapFrom(Func<TSource, object?> resolver)
  void Ignore()
  void UseConverter<TConverter>() where TConverter : ITypeConverter

4. Исключения:
MappingException — базовое исключение маппинга
MappingConfigurationException — ошибка конфигурации (обнаруженная в runtime если генератор не запустился)

ТРЕБОВАНИЯ:
- Все публичные типы должны иметь XML-документацию (///)
- Используй nullable reference types везде
- Атрибуты должны быть sealed
- Следуй принципам immutability где возможно

ТЕСТЫ (tests/IronMapper.Tests/AttributeTests.cs):
- Проверь что атрибуты можно применить к классам и свойствам
- Проверь конструкторы атрибутов
- Проверь что интерфейсы имеют правильные сигнатуры (через typeof + reflection для проверки контракта)

Запусти тесты: dotnet test tests/IronMapper.Tests/
```

---

### PROMPT 2 — Roslyn Source Generator (основа)

```
В проекте src/IronMapper.Generator/ реализуй базовый Roslyn Source Generator.

КОНТЕКСТ: 
Source Generator читает атрибуты [MapTo]/[MapFrom] на классах DTO и генерирует 
статические extension методы для маппинга без рефлексии.

ЗАДАЧА:

1. Создай IronMapperGenerator.cs (главный класс):
   [Generator]
   public class IronMapperGenerator : IIncrementalGenerator
   
   Используй IIncrementalGenerator (не устаревший ISourceGenerator).
   
2. Реализуй pipeline в Initialize():
   - Находи все классы с атрибутом [MapTo] или [MapFrom]
   - Собирай MappingDescriptor (модель данных для генерации)
   - Генерируй код через RegisterSourceOutput

3. Создай модели (папка Analysis/Models/):
   MappingDescriptor — описание одного маппинга:
     - SourceType (имя, namespace, свойства)
     - DestinationType (имя, namespace, свойства)  
     - PropertyMappings (список PropertyMappingDescriptor)
     - HasCustomConverter
   
   PropertyMappingDescriptor:
     - SourcePropertyName
     - DestPropertyName
     - IsIgnored
     - ConverterType (nullable)
     - NeedsNullCheck

4. Создай MappingAnalyzer.cs (папка Analysis/):
   Метод анализа: ExtractMappings(GeneratorSyntaxContext context)
   - Извлекает атрибуты с классов
   - Разрешает типы через SemanticModel
   - Строит PropertyMappingDescriptor для каждой пары свойств
   - Логика: сначала атрибут [MapProperty], потом совпадение по имени (case-insensitive), потом игнор

5. Создай MapperCodeEmitter.cs (папка CodeGeneration/):
   Генерирует для каждого MappingDescriptor:
   
   ```csharp
   // <auto-generated/>
   #nullable enable
   namespace IronMapper.Generated
   {
       public static partial class GeneratedMappers
       {
           /// <summary>Maps <see cref="SourceType"/> to <see cref="DestType"/>.</summary>
           public static DestType MapToDestType(this SourceType source)
           {
               if (source is null) throw new ArgumentNullException(nameof(source));
               return new DestType
               {
                   Prop1 = source.Prop1,
                   Prop2 = source.Prop2,
                   // ...
               };
           }
       }
   }
   ```

6. Создай DiagnosticDescriptors.cs (папка Diagnostics/):
   SM001 — свойство в Dest не имеет соответствия в Source (Warning)
   SM002 — несовместимые типы свойств без конвертера (Error)
   SM003 — [MapTo] указывает на несуществующий тип (Error)
   SM004 — циклический маппинг обнаружен (Error)

ТРЕБОВАНИЯ:
- Используй только netstandard2.0 API в генераторе
- НЕ используй рефлексию — только Roslyn Syntax/Semantic API
- Кэшируй результаты через ForAttributeWithMetadataName для performance
- Весь генерированный код должен начинаться с // <auto-generated/>

ТЕСТЫ (tests/IronMapper.Generator.Tests/):
Используй Microsoft.CodeAnalysis.CSharp.Testing и xUnit.

Создай BasicGeneratorTests.cs:
- TestSimpleMapping: класс с [MapTo] → проверь что генерируется MapToXxx()
- TestPropertyNameMapping: [MapProperty] → проверь переименование
- TestIgnoreAttribute: [Ignore] → свойство не попадает в маппинг
- TestDiagnosticMissingProperty: несоответствие свойств → SM001 warning

Шаблон теста генератора:
```csharp
[Fact]
public async Task TestSimpleMapping()
{
    var source = """
        using IronMapper;
        [MapTo(typeof(DestClass))]
        public class SourceClass { public string Name { get; set; } }
        public class DestClass { public string Name { get; set; } }
        """;
    
    await new CSharpSourceGeneratorTest<IronMapperGenerator, DefaultVerifier>
    {
        TestCode = source,
        ExpectedDiagnostics = { },
    }.RunAsync();
}
```

Запусти: dotnet test tests/IronMapper.Generator.Tests/
```

---

### PROMPT 3 — Fluent API (MappingProfile + генератор для профилей)

```
Реализуй поддержку Fluent API через MappingProfile в IronMapper.

КОНТЕКСТ:
Пользователь создаёт класс-наследник MappingProfile и вызывает CreateMap<Src, Dest>().
Source Generator должен обнаружить этот профиль в compile time и сгенерировать маппинг.

ЗАДАЧА:

1. В src/IronMapper/ доделай MappingProfile и IMappingExpression<TSource, TDest>:

```csharp
public abstract class MappingProfile : IMappingProfile
{
    private readonly List<MappingRegistration> _registrations = new();
    
    protected IMappingExpression<TSource, TDest> CreateMap<TSource, TDest>()
    {
        var expression = new MappingExpression<TSource, TDest>();
        _registrations.Add(new MappingRegistration(typeof(TSource), typeof(TDest), expression));
        return expression;
    }
    
    // Internal: генератор читает это через Roslyn
    internal IReadOnlyList<MappingRegistration> GetRegistrations() => _registrations;
}
```

2. Реализуй MappingExpression<TSource, TDest>:
   - ForMember(dest => dest.PropName, opt => opt.MapFrom(src => src.OtherProp))
   - ForMember(dest => dest.PropName, opt => opt.Ignore())
   - ForMember(dest => dest.PropName, opt => opt.MapFrom(src => src.X + src.Y)) // лямбда
   - When(src => src.IsActive) // условный маппинг всего объекта
   - ReverseMap() // автоматический обратный маппинг

3. В src/IronMapper.Generator/ добавь ProfileAnalyzer.cs:
   - Находи классы наследующие MappingProfile через Roslyn
   - Анализируй вызовы CreateMap<,>() внутри конструктора профиля
   - Анализируй цепочки ForMember(), Ignore(), When()
   - Строй MappingDescriptor из профиля (аналогично атрибутному подходу)

4. Обнови MapperCodeEmitter.cs:
   - Поддержи лямбда-выражения из ForMember().MapFrom()
   - Встраивай лямбду напрямую в сгенерированный код
   - Поддержи When() — оберни маппинг в if (condition(source))

5. Создай Mapper.cs в src/IronMapper/ — runtime реализация IMapper:
   Mapper использует сгенерированные extension методы через reflection на старте
   (или регистрацию через DI). Для pure compile-time использования: 
   пользователь вызывает source.MapToDestType() напрямую.

ПРИМЕР использования (должен компилироваться и работать):
```csharp
public class UserProfile : MappingProfile
{
    public UserProfile()
    {
        CreateMap<UserEntity, UserDto>()
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => src.FirstName + " " + src.LastName))
            .ForMember(dest => dest.Email, opt => opt.Ignore())
            .When(src => src.IsActive);
            
        CreateMap<UserDto, UserEntity>(); // простой маппинг
    }
}

// Использование:
var dto = userEntity.MapToUserDto();
```

ТЕСТЫ (tests/IronMapper.Tests/ProfileTests.cs):
- TestFluentMapping: CreateMap + ForMember.MapFrom → правильный маппинг
- TestForMemberIgnore: ForMember.Ignore() → свойство null/default
- TestWhenCondition: When(false) → возвращает default(TDest) или null
- TestLambdaExpression: MapFrom(src => src.X + src.Y) → вычисляется правильно
- TestReverseMap: ReverseMap() → генерируется обратный маппер

ТЕСТЫ ГЕНЕРАТОРА (tests/IronMapper.Generator.Tests/ProfileGeneratorTests.cs):
- TestProfileDetection: класс : MappingProfile → генерируется маппер
- TestForMemberInCode: ForMember генерирует правильный C# код

Запусти: dotnet test
```

---

### PROMPT 4 — Продвинутые сценарии

```
Реализуй продвинутые сценарии маппинга в IronMapper.

ЗАДАЧА:

1. ВЛОЖЕННЫЕ ОБЪЕКТЫ:
Если свойство Source является сложным типом и для него есть зарегистрированный маппинг,
генератор должен вставить рекурсивный маппинг:

```csharp
// Должно генерироваться:
return new OrderDto
{
    Id = source.Id,
    Customer = source.Customer.MapToCustomerDto(), // вложенный маппер
    Items = source.Items.Select(x => x.MapToOrderItemDto()).ToList()
};
```

Реализуй в генераторе:
- NestedMappingResolver: определяет нужен ли вложенный маппинг для типа свойства
- Обнаруживает зарегистрированные маппинги и встраивает их

2. КОЛЛЕКЦИИ:
Поддержи автоматическое маппирование коллекций:
- IEnumerable<T> → List<TDest> 
- T[] → TDest[]
- ICollection<T> → List<TDest>
- IReadOnlyList<T> → IReadOnlyList<TDest>

Генерируй extension методы для коллекций:
```csharp
public static List<DestType> MapToDestTypeList(this IEnumerable<SourceType> source)
    => source?.Select(x => x.MapToDestType()).ToList() ?? new List<DestType>();
```

3. КАСТОМНЫЕ КОНВЕРТЕРЫ:
ITypeConverter<TSource, TDest> — пользователь создаёт класс:
```csharp
public class DateTimeToStringConverter : ITypeConverter<DateTime, string>
{
    public string Convert(DateTime source) => source.ToString("yyyy-MM-dd");
}
```

В генераторе: встраивай `new DateTimeToStringConverter().Convert(source.Date)` 
или `converterInstance.Convert(source.Date)` если зарегистрирован в DI.

4. NULL SAFETY:
- Для nullable reference types генерируй null-checks
- source?.Property ?? default паттерн
- Nullable value types: source.Value?.MapToDto()

5. RECORD ПОДДЕРЖКА:
- Обнаруживай record types (primary constructor)
- Генерируй маппинг через конструктор: new DestRecord(source.Prop1, source.Prop2)
- Если порядок параметров не совпадает — используй named arguments

6. INIT-ONLY СВОЙСТВА:
- Поддержи свойства с `init;` accessor
- Генерируй object initializer (уже работает) — они совместимы

ТЕСТЫ (tests/IronMapper.Tests/AdvancedMappingTests.cs):
- TestNestedObjectMapping: Order → OrderDto с вложенным Customer → CustomerDto
- TestCollectionMapping: List<Entity> → List<Dto>
- TestArrayMapping: Entity[] → Dto[]
- TestCustomConverter: DateTime → string через кастомный конвертер
- TestNullableProperty: nullable свойство не вызывает NullReferenceException
- TestRecordMapping: record Source → record Dest
- TestInitOnlyProperties: init; свойства корректно маппируются
- TestNullSourceCollection: null коллекция → пустой список (не исключение)

Запусти: dotnet test
```

---

### PROMPT 5 — DI интеграция

```
Создай пакет IronMapper.Extensions.DI для интеграции с Microsoft.Extensions.DependencyInjection.

ЗАДАЧА:

1. Создай проект src/IronMapper.Extensions.DI/IronMapper.Extensions.DI.csproj:
   - net10.0, netstandard2.1
   - Зависимость от Microsoft.Extensions.DependencyInjection.Abstractions
   - Зависимость от IronMapper (основной пакет)

2. Реализуй IronMapperServiceCollectionExtensions.cs:

```csharp
public static class IronMapperServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует IronMapper с автосканированием профилей из указанных сборок.
    /// </summary>
    public static IServiceCollection AddIronMapper(
        this IServiceCollection services,
        params Assembly[] assemblies)
    
    /// <summary>
    /// Регистрирует IronMapper с конфигурацией через Action.
    /// </summary>
    public static IServiceCollection AddIronMapper(
        this IServiceCollection services,
        Action<IronMapperOptions> configure)
}
```

3. Реализуй IronMapperOptions:
```csharp
public class IronMapperOptions
{
    public IronMapperOptions AddProfile<TProfile>() where TProfile : MappingProfile, new()
    public IronMapperOptions AddProfilesFromAssembly(Assembly assembly)
    public IronMapperOptions AddConverter<TConverter>() where TConverter : class, ITypeConverter
}
```

4. Реализуй RuntimeMapper.cs — IMapper реализация для DI:
   - В конструкторе принимает IServiceProvider для разрешения конвертеров
   - Map<TDest>(object source) — через сгенерированные методы через словарь делегатов
   - Кеширует делегаты маппинга в ConcurrentDictionary<(Type, Type), Delegate>

5. При старте (через IHostedService или lazy init):
   - Сканирует сборки на классы : MappingProfile
   - Регистрирует профили
   - Валидирует конфигурацию (все маппинги работают)

ПРИМЕР использования (ASP.NET Core):
```csharp
// Program.cs
builder.Services.AddIronMapper(options =>
{
    options.AddProfilesFromAssembly(typeof(Program).Assembly);
    options.AddConverter<DateTimeToStringConverter>();
});

// Controller:
public class UsersController(IMapper mapper) : ControllerBase
{
    public IActionResult Get() {
        var entity = _repo.GetUser(1);
        return Ok(mapper.Map<UserDto>(entity));
    }
}
```

ТЕСТЫ (tests/IronMapper.Integration.Tests/DiIntegrationTests.cs):
- TestAddIronMapper_RegistersIMapper
- TestAddIronMapper_AutoScansProfiles
- TestMapper_MapsCorrectly_ViaIMapper
- TestMapper_WithCustomConverter_ViaServiceProvider
- TestMapper_ThrowsForUnregisteredMapping

Запусти: dotnet test tests/IronMapper.Integration.Tests/
```

---

### PROMPT 6 — Диагностика и сообщения об ошибках

```
Улучши диагностику IronMapper — пользователь должен получать понятные ошибки в IDE.

ЗАДАЧА:

1. Расширь DiagnosticDescriptors.cs полным набором диагностик:

| ID    | Severity | Сообщение |
|-------|----------|-----------|
| SM001 | Warning  | Property '{0}' in '{1}' has no matching property in '{2}'. Use [Ignore] to suppress. |
| SM002 | Error    | Cannot map property '{0}' (type '{1}') to '{2}' (type '{3}'). Add ITypeConverter. |
| SM003 | Error    | [MapTo] references type '{0}' which could not be found. |
| SM004 | Error    | Circular mapping detected: {0} → {1} → ... → {0}. |
| SM005 | Warning  | MappingProfile '{0}' has no mappings defined. |
| SM006 | Error    | [MapProperty] destination '{0}' does not exist in type '{1}'. |
| SM007 | Warning  | Both [MapTo] and MappingProfile define mapping {0}→{1}. Profile takes precedence. |
| SM008 | Error    | Type '{0}' is abstract/interface and cannot be instantiated as mapping destination. |
| SM009 | Warning  | Constructor parameter '{0}' in record '{1}' has no matching source property. |

2. Добавь Code Fix провайдеры (папка CodeFixes/ в Generator проекте):

SM001CodeFixProvider — предлагает:
  - "Add [Ignore] to {PropertyName}"
  - "Add ForMember(...).Ignore() to profile"

SM006CodeFixProvider — предлагает:
  - "Did you mean '{SimilarPropertyName}'?" (Levenshtein distance <= 2)

3. Добавь analyzer hints в IDE:
   - При наведении на [MapTo] показывай список сгенерированных методов
   - При наведении на CreateMap<,>() показывай preview сгенерированного кода

4. Добавь MappingValidator.cs для runtime-валидации (когда генератор не применён):
   - Запускается при первом вызове Map<>()
   - Проверяет совместимость типов
   - Выводит детальное исключение с советами

ТЕСТЫ (tests/IronMapper.Generator.Tests/DiagnosticsTests.cs):
- TestSM001_UnmappedProperty: генерирует SM001 warning
- TestSM002_IncompatibleTypes: генерирует SM002 error  
- TestSM003_UnknownType: генерирует SM003 error
- TestSM004_CircularMapping: генерирует SM004 error
- TestSM006_WrongPropertyName: SM006 + code fix предлагает правильное имя
- TestNoWarnings_WhenIgnored: [Ignore] подавляет SM001

Используй Microsoft.CodeAnalysis.CSharp.Testing для тестирования диагностик и code fixes.

Запусти: dotnet test tests/IronMapper.Generator.Tests/
```

---

### PROMPT 7 — Тесты и бенчмарки

```
Создай полный набор тестов и бенчмарков для IronMapper.

ЗАДАЧА:

1. UNIT ТЕСТЫ — расширь существующие тесты до полного покрытия:

tests/IronMapper.Tests/:

a) SimpleTypesTests.cs — маппинг примитивных типов:
   - string, int, bool, decimal, DateTime, Guid, Enum

b) EdgeCaseTests.cs:
   - Пустые классы
   - Классы только с readonly свойствами
   - Наследование (Source наследует Base, Dest тоже наследует Base)
   - Sealed классы
   - Generic типы Source<T> → Dest<T>
   
c) CollectionEdgeCaseTests.cs:
   - null коллекция
   - Пустая коллекция
   - Коллекция из 10000 элементов
   - Nested коллекции: List<List<T>>

d) ErrorHandlingTests.cs:
   - ArgumentNullException при null source
   - MappingException при несовместимых типах (runtime fallback)

2. ИНТЕГРАЦИОННЫЕ ТЕСТЫ:

tests/IronMapper.Integration.Tests/RealWorldTests.cs:
Смоделируй реальные сценарии:

Сценарий 1: E-commerce (Order → OrderDto с вложенными Customer, Items, Address)
Сценарий 2: API Response (UserEntity → UserResponseDto с форматированием дат)
Сценарий 3: Конфигурация (AppSettings → AppConfig с преобразованием типов)

3. БЕНЧМАРКИ:

tests/IronMapper.Benchmarks/MappingBenchmarks.cs:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class MappingBenchmarks
{
    [Benchmark(Baseline = true)]
    public UserDto ManualMapping() { ... } // ручной new UserDto { ... }
    
    [Benchmark]
    public UserDto IronMapperMapping() { ... } // через сгенерированный метод
    
    [Benchmark]
    public UserDto AutoMapperMapping() { ... } // для сравнения (если нужна лицензия — закомментировать)
    
    [Benchmark]
    public List<UserDto> IronMapper_Collection_1000() { ... }
    
    [Benchmark]
    public List<UserDto> Manual_Collection_1000() { ... }
}
```

Цель по performance:
- IronMapper должен быть не медленнее ручного маппинга (±5%)
- По памяти: нулевые аллокации помимо создания объектов

4. ПОКРЫТИЕ КОДА:
Настрой coverlet в тестовых проектах:
```xml
<PackageReference Include="coverlet.collector" Version="*"/>
```

Добавь в CI шаг:
```
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

Целевое покрытие: >= 90% для IronMapper, >= 80% для Generator

ЗАПУСК:
dotnet test --verbosity normal
dotnet run --project tests/IronMapper.Benchmarks -c Release
```

---

### PROMPT 8 — Документация и NuGet пакет

```
Создай полную документацию и настрой публикацию NuGet пакета для IronMapper.

ЗАДАЧА:

1. README.md (корень репозитория):

Структура:
- Badges (NuGet version, build status, coverage)
- Краткое описание: "Zero-reflection, compile-time object mapper for .NET"
- Сравнение с AutoMapper (таблица: скорость, рефлексия, лицензия, API совместимость)
- Quick Start (5 минут до работающего кода)
- Основные возможности с примерами кода
- Ссылки на документацию
- Migration guide from AutoMapper (ссылка)
- Contributing

2. docs/ папка:

docs/getting-started.md:
- Установка NuGet
- Первый маппинг за 3 шага (атрибуты)
- Первый маппинг через Profile
- Интеграция с ASP.NET Core

docs/configuration.md:
- Все атрибуты с примерами
- Fluent API полный справочник
- MappingProfile
- Кастомные конвертеры
- Условный маппинг

docs/advanced.md:
- Вложенные объекты
- Коллекции
- Record типы
- Generic маппинг
- Performance советы

docs/migration-from-automapper.md:
Таблица соответствия:
| AutoMapper | IronMapper |
|------------|-------------|
| CreateMap<Src,Dest>() | CreateMap<Src,Dest>() (в Profile) или [MapTo] |
| ForMember(d=>d.X, o=>o.MapFrom(s=>s.Y)) | ForMember(d=>d.X, o=>o.MapFrom(s=>s.Y)) |
| cfg.CreateMapper() | services.AddIronMapper() |
| mapper.Map<Dest>(source) | source.MapToDest() или mapper.Map<Dest>(source) |

docs/diagnostics.md:
- Все коды SM001-SM009 с объяснением и решением

3. XML ДОКУМЕНТАЦИЯ:
Убедись что все публичные типы имеют:
- <summary>
- <typeparam> для generic параметров
- <param> для параметров методов
- <returns>
- <exception> для выбрасываемых исключений
- <example> для ключевых методов

4. NUGET КОНФИГУРАЦИЯ в Directory.Build.props:
```xml
<PropertyGroup>
  <PackageId>IronMapper</PackageId>
  <PackageVersion>1.0.0</PackageVersion>
  <PackageDescription>Zero-reflection, compile-time object mapper for .NET. Free alternative to AutoMapper. By Iron Programmer School.</PackageDescription>
  <PackageTags>mapper;automapper;dto;object-mapping;source-generator;compile-time</PackageTags>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/iron-programmer/IronMapper</PackageProjectUrl>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageIcon>icon.png</PackageIcon>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
```

5. GitHub Actions — публикация:
.github/workflows/publish.yml:
- Триггер: push tag v*
- dotnet pack
- dotnet nuget push

Запусти финальную проверку:
dotnet build -c Release
dotnet test
dotnet pack -c Release
```

---

## Про субагентов в Claude Code

### Рекомендация: ДА, стоит использовать субагентов

Claude Code поддерживает запуск задач параллельно через `claude --task` (в режиме multi-agent).
Вот для чего это имеет смысл в нашем проекте:

### Субагент 1 — Code Reviewer
После каждого этапа запускай субагента для проверки:

```bash
# В CLAUDE.md добавь задачу:
claude task review --prompt "
Проверь последние изменения в IronMapper:
1. Все публичные методы имеют XML-документацию?
2. Nullable Reference Types используются везде?
3. Нет утечек Roslyn API в IronMapper.dll (только в Generator)?
4. Тесты покрывают happy path и edge cases?
5. Нет магических строк — только константы?
Выведи список нарушений или 'OK'.
"
```

### Субагент 2 — Test Runner + Analyzer
```bash
claude task test-analyze --prompt "
1. Запусти dotnet test --collect:'XPlat Code Coverage'
2. Проанализируй отчёт покрытия
3. Найди методы с покрытием < 80%
4. Предложи конкретные тест-кейсы для непокрытых ветвей
"
```

### Субагент 3 — API Consistency Checker
```bash
claude task api-check --prompt "
Сравни публичный API IronMapper с AutoMapper.
Для каждого распространённого паттерна AutoMapper проверь:
- Есть ли эквивалент в IronMapper?
- Работает ли он так же?
- Задокументировано ли отличие в migration guide?
Список паттернов: [CreateMap, ForMember, MapFrom, Ignore, ReverseMap, 
ConvertUsing, BeforeMap, AfterMap, IncludeMembers]
"
```

### Когда запускать субагентов:
- После каждого PROMPT (этапа) — Review агент
- После PROMPT 7 — Test analyzer агент  
- Перед PROMPT 8 — API consistency агент

---

## CLAUDE.md — содержимое для репозитория

```markdown
# IronMapper — Instructions for Claude Code

## Architecture Overview

IronMapper consists of two projects:

1. **IronMapper** (src/IronMapper/) — Runtime library
   - Public API: attributes, interfaces, base classes
   - NO reflection in hot path
   - Depends only on: System.*

2. **IronMapper.Generator** (src/IronMapper.Generator/) — Roslyn Source Generator
   - Reads attributes/profiles at COMPILE TIME
   - Generates C# extension methods
   - Must target netstandard2.0 only
   - NEVER reference IronMapper.dll from here (circular dep)

## Key Decisions

- **IIncrementalGenerator** (not ISourceGenerator) — better performance
- **ForAttributeWithMetadataName** — efficient attribute scanning
- **Extension methods** pattern — no runtime dictionary lookup
- **Partial classes** — allows user to add custom methods alongside generated ones

## Running Tests

dotnet test                    # all tests
dotnet test tests/IronMapper.Tests/          # unit tests only
dotnet test tests/IronMapper.Generator.Tests/ # generator tests only
dotnet run -c Release --project tests/IronMapper.Benchmarks  # benchmarks

## Naming Conventions

- Generated mappers: public static DestType MapToDestType(this SourceType source)
- Generated collection mappers: public static List<DestType> MapToDestTypeList(...)
- Diagnostic IDs: SM001-SM099
- Test classes: {Feature}Tests.cs, methods: Test{Scenario}_{Condition}_{ExpectedResult}

## DO NOT

- Add reflection in IronMapper.dll runtime hot path
- Reference Microsoft.CodeAnalysis from IronMapper.dll
- Use async in Source Generator (Roslyn doesn't support it)
- Generate code that allocates unnecessarily
```

---

## Итоговый план этапов

| # | Этап | Промпт | Тесты | Приоритет |
|---|------|--------|-------|-----------|
| 0 | Scaffold & CI | PROMPT 0 | — | Критично |
| 1 | Атрибуты & API | PROMPT 1 | AttributeTests | Критично |
| 2 | Source Generator | PROMPT 2 | GeneratorTests | Критично |
| 3 | Fluent API | PROMPT 3 | ProfileTests | Высокий |
| 4 | Продвинутые сценарии | PROMPT 4 | AdvancedTests | Высокий |
| 5 | DI интеграция | PROMPT 5 | IntegrationTests | Средний |
| 6 | Диагностика | PROMPT 6 | DiagnosticsTests | Средний |
| 7 | Тесты & Бенчмарки | PROMPT 7 | Все + Benchmarks | Высокий |
| 8 | Документация & NuGet | PROMPT 8 | — | Средний |

**Минимальный рабочий продукт**: этапы 0–4 (примерно 2–3 дня работы с Claude Code).
