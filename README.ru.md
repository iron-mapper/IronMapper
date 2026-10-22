# IronMapper

Маппер объектов для .NET, работающий во время компиляции: генератор исходного кода (Roslyn source generator) пишет
код маппинга на обычном C# при сборке проекта.

English version: [README.md](https://github.com/iron-mapper/IronMapper/blob/main/README.md)

[![NuGet](https://img.shields.io/nuget/v/IronMapper.svg)](https://www.nuget.org/packages/IronMapper)
[![Downloads](https://img.shields.io/nuget/dt/IronMapper.svg)](https://www.nuget.org/packages/IronMapper)
[![CI](https://github.com/iron-mapper/IronMapper/actions/workflows/ci.yml/badge.svg)](https://github.com/iron-mapper/IronMapper/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/iron-mapper/IronMapper/blob/main/LICENSE)
[![Targets](https://img.shields.io/badge/targets-netstandard2.0%20%7C%20net10.0-512BD4.svg)](https://www.nuget.org/packages/IronMapper)

---

## Почему IronMapper

- **Код маппинга можно прочитать и отладить.** Для каждой пары типов генератор создаёт extension-методы `MapTo*`
  с явными присваиваниями свойств; их можно открыть и пройти отладчиком в IDE. Reflection в сгенерированном коде
  нет.
- **Ошибки конфигурации — при сборке, но не все.** Для `[MapTo]`/`[MapFrom]` несопоставленное свойство приёмника
  даёт предупреждение `IM0001`, неверное имя в `[MapProperty]` — ошибку `IM0006`, недоступный тип — `IM0010`
  ([диагностики](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.ru.md)). Для профилей проверок
  меньше (`IM0001` для них включается флагом, начиная с 1.2.0, [#22](https://github.com/iron-mapper/IronMapper/issues/22)), а несовместимые типы свойств дают ошибку C# в сгенерированном файле.
- **Плоские объекты — со скоростью ручного кода:** 11,4 нс против 11,5 нс на объект (.NET 10). Вложенные объекты
  немного медленнее (×1,07 на .NET 10, ×1,09 на .NET 8; в 1.1.3 — ×1,25), см.
  [Производительность](https://github.com/iron-mapper/IronMapper/blob/main/README.ru.md#производительность).
- **Работа в NativeAOT проверяется в CI** (.NET 8 и .NET 10): сгенерированные методы и, начиная с 1.2.0,
  необязательный сервис `IMapper` публикуются без предупреждений trimming/AOT и работают корректно. До 1.1.4
  включительно `IMapper` использовал reflection и в NativeAOT не работал ([#26](https://github.com/iron-mapper/IronMapper/issues/26)).
- **Холодный старт — около 1 мс** (запуск процесса и первый маппинг сверх консольного приложения без маппера: +1,3 мс;
  ручной код и Mapperly — в пределах 0,2 мс от приложения без маппера, IronMapper загружает свою сборку ради
  сгенерированного инициализатора модуля). AutoMapper и Mapster тратят на этом 70–100 мс — строят конфигурацию маппинга
  ([результаты](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/results/RESULTS.md#cold-start)).
- **Нет зависимостей во время выполнения.** У пакета `IronMapper` нет NuGet-зависимостей, в приложение добавляется
  одна сборка (+47,7 КБ к framework-dependent publish консольного приложения из бенчмарков, +2,6 КБ к его
  NativeAOT-файлу). Необязательный пакет `IronMapper.Extensions.DI` зависит от
  `Microsoft.Extensions.DependencyInjection.Abstractions`.

---

## Быстрый старт

```bash
dotnet add package IronMapper
```

`Program.cs` консольного приложения (`dotnet new console`):

<!-- quickstart:begin -->

```csharp
using IronMapper.Attributes;

var entity = new UserEntity { Id = 1, Name = "Alice", Email = "alice@example.com" };
UserDto dto = entity.MapToUserDto();   // создан при сборке
Console.WriteLine(dto.Name);           // Alice

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

<!-- quickstart:end -->

Ни `IMapper`, ни регистрация сервисов не нужны.

Сгенерированные методы `MapTo*` лежат в пространстве имён `IronMapper.Generated`. **При неявных using**
(`<ImplicitUsings>enable</ImplicitUsings>`, по умолчанию в шаблонах .NET 6+) пакет сам добавляет
`global using IronMapper.Generated;`. **Без неявных using добавьте `using IronMapper.Generated;`** в каждый файл, где
вызываются методы `MapTo*`, иначе вызов падает с `CS1061` («не содержит определения "MapToUserDto"»). Отключить
global using: `<IronMapperImplicitUsings>false</IronMapperImplicitUsings>`
([Конфигурация](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.ru.md#global-using-для-ironmappergenerated)).

**Требования.** Генератору нужен компилятор C# из **.NET SDK 8.0.400 или новее** (Roslyn 4.11; Visual Studio 2022
17.11+). На более старом SDK компилятор выдаёт предупреждение `CS9057`, генератор не запускается, и каждый вызов
`MapTo*` тоже падает с `CS1061`. Пакет `IronMapper` собран под `netstandard2.0` и `net10.0`, см.
[Платформы](https://github.com/iron-mapper/IronMapper/blob/main/README.ru.md#платформы).

---

## Производительность

IronMapper 1.2.0 против ручного кода (`Manual`), Mapperly 4.3.1, Mapster 10.0.13 и AutoMapper 16.2.0.
BenchmarkDotNet 0.15.8, .NET 10.0.12, Windows 11, Intel Core i7-12650H. Полный отчёт (также .NET 8), методика и
сравнение с 1.1.3, измеренной в той же сессии:
[benchmarks/results/RESULTS.md](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/results/RESULTS.md)
(на английском).

| Библиотека | Flat | Nested | Коллекция, 1 000 | Холодный старт, Δ мс | NativeAOT | Δ publish (JIT) | Δ AOT-файла |
|---|---:|---:|---:|---:|---|---:|---:|
| Manual | 11,5 нс | 166,5 нс | 257,5 мкс | −0,2 | работает | +4,6 КБ | +2,0 КБ |
| IronMapper | 11,4 нс (×0,99) | 177,8 нс (×1,07) | 271,4 мкс (×1,05) | 1,3 | работает | +47,7 КБ | +2,6 КБ |
| IronMapper `IMapper` | 16,3 нс (×1,41) | 185,5 нс (×1,11) | 253,0 мкс (×0,98) | 19,9 | работает | +223,4 КБ | +1,50 МБ |
| Mapperly | 11,6 нс (×1,01) | 163,1 нс (×0,98) | 276,9 мкс (×1,08) | 0,1 | работает | +38,5 КБ | +23,6 КБ |
| Mapster | 15,2 нс (×1,32) | 185,8 нс (×1,12) | 287,0 мкс (×1,11) | 98,2 | падает | +244,8 КБ | +3,99 МБ |
| AutoMapper | 34,0 нс (×2,95) | 264,0 нс (×1,59) | 344,6 мкс (×1,34) | 72,1 | падает | +1,17 МБ | +5,16 МБ |

По сравнению с 1.1.3 (та же сессия, тот же компьютер, отношение к `Manual`, .NET 10): Nested ×1,25 → ×1,07,
коллекция из 10 заказов ×1,15 → ×0,98 (Mapperly ×1,17), обновление in-place ×1,10 → ×0,91, коллекции выделяют столько
же памяти, сколько `Manual` (1.1.3: +4 %); `IMapper` на Flat 30,2 → 16,3 нс без собственных аллокаций, в NativeAOT
«падает» → «работает»; NativeAOT-файл +255,5 КБ → +2,6 КБ.

Как читать таблицу:

- Flat — один объект с 15 свойствами. Nested — заказ с покупателем, адресом и 5–20 строками. Коллекция —
  `List<Order>` → `List<OrderDto>` из 1 000 таких заказов. Время — среднее; ×N — отношение к `Manual`.
- **Вложенные объекты в IronMapper настроены вручную** (`ForMember(... MapFrom(s => s.Customer.MapToCustomerDto()))`),
  потому что вложенные одиночные объекты он автоматически не маппит (см.
  [Известные ограничения](https://github.com/iron-mapper/IronMapper/blob/main/README.ru.md#известные-ограничения)).
  Остальные библиотеки маппят их автоматически.
- Холодный старт — медиана времени от запуска процесса до выхода с одним маппингом, минус то же консольное
  приложение без маппера (30 запусков). Метрика чувствительна к шуму, смотрите на порядок величины. Большая часть
  холодного старта `IMapper` — сам DI-контейнер: тот же ручной маппинг, полученный из того же контейнера (`Manual_DI`),
  даёт +15,3 мс, то есть собственный вклад IronMapper — 4,5 мс (в 1.1.3 — 6,4 мс); он растёт с числом маппингов
  (регистрация).
- NativeAOT: «работает» — опубликованный файл запустился и вывел правильный результат; начиная с 1.2.0 это относится и
  к `IMapper` (проверяется в CI; файл 1.1.3 падал с `MappingException`). Mapster и AutoMapper строят мапперы во время
  выполнения и падают.
- Размеры — прирост к тому же консольному приложению без маппера.
- Разница в коллекциях в несколько процентов — в пределах разброса между запусками и не означает, что одна библиотека
  быстрее: на 1 000 заказов в этом прогоне `IMapper` (×0,98) быстрее extension-методов (×1,05), которые он вызывает, а
  Mapster — ×1,11 против ×1,03 в прогоне 1.1.3 той же сессии; отдельный прогон только IronMapper дал ×1,01. На 100 000
  заказов время определяет сборщик мусора (полный отчёт).
- .NET 8 (тот же прогон): Flat ×1,01, Nested ×1,09, коллекция 1 000 ×1,02, in-place ×0,93; `IMapper` на Flat ×1,37.
- Один компьютер. Mapster.Tool (генератор кода Mapster) не измерялся.

---

## Ключевые возможности

Во всех примерах подразумевается `using IronMapper.Attributes;` (и `using IronMapper.Generated;` без неявных
using). Полный справочник:
[Конфигурация](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.ru.md).

**Атрибуты на типе-источнике или типе-приёмнике**

```csharp
[MapTo(typeof(ProductDto))]        // на источнике
public class Product { /* ... */ }

[MapFrom(typeof(Order))]           // на приёмнике, результат тот же
public class OrderDto { /* ... */ }
```

**Переименование, пропуск и преобразование свойства**

```csharp
[MapTo(typeof(InvoiceDto))]
public class Invoice
{
    [MapProperty("CustomerName")]              // попадёт в InvoiceDto.CustomerName
    public string Customer { get; set; } = "";

    [Ignore]                                   // не маппится
    public string InternalNote { get; set; } = "";

    [MapConverter(typeof(PriceConverter))]     // decimal → string через конвертер
    public decimal Total { get; set; }
}

public class PriceConverter : ITypeConverter<decimal, string>   // IronMapper.Interfaces
{
    public string Convert(decimal source) => source.ToString("F2");
}
```

**Профили (fluent API, похожий на AutoMapper)** — для типов, которые нельзя пометить атрибутами. Генератор читает
профиль при сборке; для маппинга профиль не выполняется.

```csharp
public class OrderProfile : MappingProfile   // IronMapper.Configuration
{
    public OrderProfile()
    {
        CreateMap<Order, OrderSummary>()
            .ForMember(d => d.Address, o => o.MapFrom(s => s.Street + ", " + s.City))
            .ForMember(d => d.InternalId, o => o.Ignore());
    }
}
```

В профилях также есть `ReverseMap`, `BeforeMap`/`AfterMap`, `When`, `ConvertUsing`, `IncludeMembers` и
`AddTransformer`; см. [Миграция с AutoMapper](https://github.com/iron-mapper/IronMapper/blob/main/docs/migration-from-automapper.ru.md).

**Коллекции и обновление существующего объекта**

```csharp
List<UserDto> list  = users.MapToUserDtoList();    // из любого IEnumerable<UserEntity>
UserDto[]     array = users.MapToUserDtoArray();
entity.MapToUserDto(existingDto);                  // обновляет существующий объект
```

Свойства `List<T>` (`List<Comment>` → `List<CommentDto>`) маппятся автоматически, если есть маппинг для типов
элементов.

**Покрытие кода и неиспользуемые перегрузки.** Начиная с 1.2.0 сгенерированный класс помечен `[GeneratedCode]`, а
`<IronMapperExcludeFromCodeCoverage>true</IronMapperExcludeFromCodeCoverage>` добавляет `[ExcludeFromCodeCoverage]`,
поэтому неиспользуемые сгенерированные методы (например, `MapToXList`, который никто не вызывает) больше не снижают
покрытие вашего проекта. Неиспользуемые перегрузки можно отключить для проекта (`IronMapperGenerateInPlace`,
`IronMapperGenerateList`, `IronMapperGenerateArray`) или для одного маппинга (`GenerateList = false` у `[MapTo]`,
`.GenerateMethods(list: false)` в профиле); `IMapper` по-прежнему маппит списки, массивы и in-place
([подробнее](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.ru.md#генерируемые-перегрузки-и-покрытие-кода)).

**Внедрение зависимостей (необязательно)**

```bash
dotnet add package IronMapper.Extensions.DI
```

```csharp
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;

builder.Services.AddIronMapper(typeof(Program).Assembly);   // регистрирует IMapper и маппинги этой сборки

app.MapGet("/users/{id}", (int id, IMapper mapper) => mapper.Map<UserDto>(LoadUser(id)));
app.MapGet("/users", (IMapper mapper) => mapper.Map<List<UserDto>>(LoadUsers()));
```

Начиная с 1.2.0 `IMapper` вызывает сгенерированные методы через делегаты, которые генератор регистрирует для каждого
маппинга (без reflection): он работает с trimming и NativeAOT, а исключения ваших конвертеров и хуков приходят как
есть. `Map<List<UserDto>>`, `Map<UserDto[]>` и `Map<IEnumerable<UserDto>>` маппят коллекции. Передайте в
`AddIronMapper` все сборки, где объявлены маппинги; маппинги регистрируются при создании первого `IMapper`.
Приложение, которое не использует `IMapper`, только загружает сборку IronMapper для сгенерированного инициализатора
модуля, а в приложении с trimming или NativeAOT без `IronMapper.Extensions.DI` регистрации нет вовсе
([подробнее](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.ru.md#регистрация-для-imapper-в-приложениях-с-trimming-и-nativeaot)).
Вызов через `IMapper` всё равно дороже extension-метода (вызов обобщённого метода интерфейса и делегата, а для
`Map<TDest>(object)` — ещё поиск по типу); если типы известны при компиляции, вызывайте extension-методы напрямую
(бенчмарк DI overhead, плоский объект: 15,4 нс через `IMapper` против 11,1 нс у extension-метода; `IMapper` AutoMapper
из того же контейнера — 34,5 нс). До 1.1.4 включительно `IMapper`
использовал reflection, был в 2,7 раза медленнее на Flat и не работал в NativeAOT.

---

## Известные ограничения

- **Вложенные одиночные объекты не маппятся автоматически** ([#23](https://github.com/iron-mapper/IronMapper/issues/23)). Для `Customer.Address` → `CustomerDto.Address`
  генератор пишет `Address = source.Address`, и сборка падает с `CS0029` в сгенерированном файле. Обходной путь:
  `ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()))`. Вложенные коллекции маппятся.
- **Позиционные record в качестве приёмника не компилируются** (`CS7036`, [#24](https://github.com/iron-mapper/IronMapper/issues/24)): генератор всегда использует
  инициализатор объекта. Record и классы со свойствами `init` работают.
- **Нет flattening по соглашению** ([#25](https://github.com/iron-mapper/IronMapper/issues/25)): `CustomerAddressCity` не заполняется из `Customer.Address.City`.
  Используйте `MapFrom`.
- **`IM0001` для маппингов из профилей по умолчанию выключена** ([#22](https://github.com/iron-mapper/IronMapper/issues/22)): несопоставленное свойство приёмника в `CreateMap`
  остаётся со значением по умолчанию без предупреждения. Начиная с 1.2.0 включается свойством
  `<IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>` (в 2.0 планируется включить по
  умолчанию); уровень задаётся в `.editorconfig` (`dotnet_diagnostic.IM0001.severity = error`).
- **`null` на входе бросает `ArgumentNullException`**, даже если источник объявлен nullable (`UserEntity?`) ([#27](https://github.com/iron-mapper/IronMapper/issues/27)).
  Mapperly, Mapster и AutoMapper в этом случае возвращают `null`.
- **Преобразования типов свойств** работают только там, где C# преобразует неявно (`int` → `long`, `int` → `int?`, …).
  `int?` → `int`, `long` → `int`, enum ↔ enum/`string`, `Guid` ↔ `string` дают ошибку C# в сгенерированном файле;
  используйте `MapFrom` или конвертер. Объявленные диагностики `IM0002` и `IM0004` пока не выдаются.
- **`ProjectTo` / проекция `IQueryable`** не поддерживается ([#9](https://github.com/iron-mapper/IronMapper/issues/9)).

Кроме того: до 1.1.4 включительно у диагностик нет места в исходном коде (начиная с 1.2.0 они указывают на строку); в `IMapper` нельзя передать контекст в вызов
([#7](https://github.com/iron-mapper/IronMapper/issues/7)); in-place перегрузка игнорирует `When`, `ConvertUsing` и хуки;
`ReverseMap()` сопоставляет только по именам; конвертеры создаёт сгенерированный код, а не DI, и им нужен публичный или
internal конструктор без параметров (`IM0015`); private- и
protected-вложенные типы маппить нельзя (`IM0010`); у каждого проекта решения свой класс `GeneratedMappers`; первый
`IMapper` регистрирует все маппинги сборки, что добавляет к холодному старту 4,5 мс сверх DI-контейнера в бенчмарке
(больше — при большем числе маппингов).
Подробности: [Продвинутые возможности](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.ru.md),
[Конфигурация](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.ru.md),
[Диагностики](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.ru.md).

### Платформы

- Пакеты: `IronMapper` — `netstandard2.0` и `net10.0`; `IronMapper.Extensions.DI` — `netstandard2.1` и `net10.0`.
  Генератору нужен .NET SDK 8.0.400+.
- Тесты библиотеки запускаются на .NET 10 (Ubuntu и Windows в CI). Тесты эквивалентности набора бенчмарков также
  запускаются на .NET 8, runtime-бенчмарки 1.2.0 измерены на .NET 8 и .NET 10. .NET 9 в CI не проверяется ([#10](https://github.com/iron-mapper/IronMapper/issues/10)).
  Быстрый старт выше компилируется и запускается на упакованном пакете в CI (.NET 10); 1.1.4 проверялась вручную
  на .NET 8, 9 и 10.

---

## Планы

Открытые issues:

- `ProjectTo<TDestination>()` для `IQueryable` — [#9](https://github.com/iron-mapper/IronMapper/issues/9)
- Передача контекста в вызов `Map` (аналог `ResolutionContext.Items`) — [#7](https://github.com/iron-mapper/IronMapper/issues/7)
- Целевой фреймворк `net9.0` — [#10](https://github.com/iron-mapper/IronMapper/issues/10)

1.2.0 (этот релиз): сгенерированный код коллекций без LINQ, `IMapper` без reflection, который работает в NativeAOT
([#26](https://github.com/iron-mapper/IronMapper/issues/26)) и маппит коллекции через `Map<List<T>>`
([#8](https://github.com/iron-mapper/IronMapper/issues/8)), конвертеры без состояния создаются один раз, диагностики с
местом в исходном коде и работающие code fix'ы, `IM0001` для профилей с включением по флагу
([#22](https://github.com/iron-mapper/IronMapper/issues/22)), отключаемые необязательные перегрузки.

Дальше, 1.3.0: автоматический маппинг вложенных одиночных объектов с проверками на `null` по nullable-аннотациям
([#23](https://github.com/iron-mapper/IronMapper/issues/23); на нём же — оставшийся разрыв в Nested), конструкторы и
позиционные records ([#24](https://github.com/iron-mapper/IronMapper/issues/24)), диагностики `IM0002` (несовместимые
типы) и `IM0004` (циклы). 2.0: `null` → `null` для nullable-источника
([#27](https://github.com/iron-mapper/IronMapper/issues/27)), `IM0001` для профилей по умолчанию, режим «класс-маппер».
Рабочий план — [IRONMAPPER_PLAN.md](https://github.com/iron-mapper/IronMapper/blob/main/IRONMAPPER_PLAN.md); он может
меняться.

---

## Документация

- [Начало работы](https://github.com/iron-mapper/IronMapper/blob/main/docs/getting-started.ru.md)
- [Справочник по конфигурации](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.ru.md)
- [Продвинутые возможности](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.ru.md)
- [Миграция с AutoMapper](https://github.com/iron-mapper/IronMapper/blob/main/docs/migration-from-automapper.ru.md)
- [Диагностики компилятора](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.ru.md)
- [Бенчмарки](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/README.md) (на английском)
- [Список изменений](https://github.com/iron-mapper/IronMapper/blob/main/CHANGELOG.md) (на английском)

**Демо.** [samples/IronMapper.Demo](https://github.com/iron-mapper/IronMapper/tree/main/samples/IronMapper.Demo) —
консольное приложение с восемью сценариями, большинство — на данных Open Library API (ключ не нужен): атрибуты,
профили, коллекции, вложенные объекты, смапленные по отдельности, свой конвертер, условный маппинг (`When`), хуки с
`ReverseMap` и `IncludeMembers`.

```bash
git clone https://github.com/iron-mapper/IronMapper
cd IronMapper
dotnet run --project samples/IronMapper.Demo
```

---

## Участие в разработке

1. Сделайте fork репозитория и создайте ветку от `main`.
2. Внесите изменение вместе с тестами.
3. `dotnet build IronMapper.slnx` и `dotnet test IronMapper.slnx` должны проходить без предупреждений.
4. Откройте pull request в `main`; добавьте строку в `CHANGELOG.md` в раздел `[Unreleased]`.

Стиль кода: [.editorconfig](https://github.com/iron-mapper/IronMapper/blob/main/.editorconfig).

---

## Лицензия

[MIT](https://github.com/iron-mapper/IronMapper/blob/main/LICENSE) © 2026 Aleksey Mironov
