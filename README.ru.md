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
- **Плоские объекты — со скоростью ручного кода:** 11,4 нс против 11,6 нс на объект. Вложенные объекты медленнее
  (×1,26), см. [Производительность](https://github.com/iron-mapper/IronMapper/blob/main/README.ru.md#производительность).
- **Работа в NativeAOT проверяется в CI** (.NET 8 и .NET 10): сгенерированные методы и, начиная с 1.2.0,
  необязательный сервис `IMapper` публикуются без предупреждений trimming/AOT и работают корректно. До 1.1.4
  включительно `IMapper` использовал reflection и в NativeAOT не работал ([#26](https://github.com/iron-mapper/IronMapper/issues/26)).
- **Холодный старт — около 2 мс** (запуск процесса и первый маппинг сверх консольного приложения без маппера), тот же
  порядок, что у ручного кода. AutoMapper и Mapster тратят на этом десятки миллисекунд — строят конфигурацию маппинга
  ([результаты](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/results/RESULTS.md#cold-start)).
- **Нет зависимостей во время выполнения.** У пакета `IronMapper` нет NuGet-зависимостей, в приложение добавляется
  одна сборка (+28,2 КБ к framework-dependent publish). Необязательный пакет `IronMapper.Extensions.DI` зависит от
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

IronMapper 1.1.3 (1.1.4 генерирует тот же код) против ручного кода (`Manual`), Mapperly 4.3.1, Mapster 10.0.13 и AutoMapper 16.2.0.
BenchmarkDotNet 0.15.8, .NET 10.0.12, Windows 11, Intel Core i7-12650H. Полный отчёт, методика и исходные данные:
[benchmarks/results/RESULTS.md](https://github.com/iron-mapper/IronMapper/blob/main/benchmarks/results/RESULTS.md)
(на английском).

| Библиотека | Flat | Nested | Коллекция, 1 000 | Холодный старт, Δ мс | NativeAOT | Δ publish (JIT) | Δ AOT-файла |
|---|---:|---:|---:|---:|---|---:|---:|
| Manual | 11,6 нс | 165,9 нс | 255,0 мкс | 1,1 | работает | +4,6 КБ | +2,0 КБ |
| IronMapper | 11,4 нс (×0,98) | 209,8 нс (×1,26) | 258,1 мкс (×1,01) | 2,2 | работает | +28,2 КБ | +255,5 КБ |
| IronMapper `IMapper` | 31,0 нс (×2,66) | 227,6 нс (×1,37) | 282,4 мкс (×1,11) | 23,1 | падает | +205,8 КБ | +1,47 МБ |
| Mapperly | 11,6 нс (×0,99) | 165,5 нс (×1,00) | 267,3 мкс (×1,05) | −1,1 | работает | +38,5 КБ | +23,6 КБ |
| Mapster | 15,6 нс (×1,34) | 192,3 нс (×1,16) | 263,5 мкс (×1,03) | 99,1 | падает | +244,8 КБ | +3,99 МБ |
| AutoMapper | 34,6 нс (×2,97) | 270,7 нс (×1,63) | 344,6 мкс (×1,35) | 73,5 | падает | +1,17 МБ | +5,16 МБ |

Как читать таблицу:

- Flat — один объект с 15 свойствами. Nested — заказ с покупателем, адресом и 5–20 строками. Коллекция —
  `List<Order>` → `List<OrderDto>` из 1 000 таких заказов. Время — среднее; ×N — отношение к `Manual`.
- **Вложенные объекты в IronMapper настроены вручную** (`ForMember(... MapFrom(s => s.Customer.MapToCustomerDto()))`),
  потому что вложенные одиночные объекты он автоматически не маппит (см.
  [Известные ограничения](https://github.com/iron-mapper/IronMapper/blob/main/README.ru.md#известные-ограничения)).
  Остальные библиотеки маппят их автоматически.
- Холодный старт — медиана времени от запуска процесса до выхода с одним маппингом, минус то же консольное
  приложение без маппера (30 запусков). Метрика чувствительна к шуму, смотрите на порядок величины.
- NativeAOT: «работает» — опубликованный файл запустился и вывел правильный результат. Файл с `IMapper` из IronMapper
  1.1.3 падает с `MappingException` (начиная с 1.2.0 работает, проверяется в CI). Mapster и AutoMapper строят мапперы
  во время выполнения и тоже падают.
- Размеры — прирост к тому же консольному приложению без маппера.
- Разница в коллекциях в несколько процентов (здесь ×1,01–×1,05; 100 000 элементов — в полном отчёте) в пределах
  разброса между запусками и не означает, что одна библиотека быстрее.
- Замеры только на .NET 10 и на одном компьютере. Mapster.Tool (генератор кода Mapster) не измерялся.

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
`Map<TDest>(object)` — ещё поиск по типу); если типы известны при компиляции, вызывайте extension-методы напрямую. До 1.1.4 включительно `IMapper` использовал reflection, был в 2,7 раза медленнее на Flat и
не работал в NativeAOT.

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
protected-вложенные типы маппить нельзя (`IM0010`); у каждого проекта решения свой класс `GeneratedMappers`.
Подробности: [Продвинутые возможности](https://github.com/iron-mapper/IronMapper/blob/main/docs/advanced.ru.md),
[Конфигурация](https://github.com/iron-mapper/IronMapper/blob/main/docs/configuration.ru.md),
[Диагностики](https://github.com/iron-mapper/IronMapper/blob/main/docs/diagnostics.ru.md).

### Платформы

- Пакеты: `IronMapper` — `netstandard2.0` и `net10.0`; `IronMapper.Extensions.DI` — `netstandard2.1` и `net10.0`.
  Генератору нужен .NET SDK 8.0.400+.
- Тесты библиотеки запускаются на .NET 10 (Ubuntu и Windows в CI). Тесты эквивалентности набора бенчмарков также
  запускаются на .NET 8. .NET 9 в CI не проверяется ([#10](https://github.com/iron-mapper/IronMapper/issues/10)).
  Быстрый старт выше компилируется и запускается на упакованном пакете в CI (.NET 10); 1.1.4 проверялась вручную
  на .NET 8, 9 и 10.

---

## Планы

Открытые issues:

- `ProjectTo<TDestination>()` для `IQueryable` — [#9](https://github.com/iron-mapper/IronMapper/issues/9)
- Передача контекста в вызов `Map` (аналог `ResolutionContext.Items`) — [#7](https://github.com/iron-mapper/IronMapper/issues/7)
- Целевой фреймворк `net9.0` — [#10](https://github.com/iron-mapper/IronMapper/issues/10)

Следующий релиз, 1.2.0. Сделано (ещё не выпущено): сгенерированный код коллекций без LINQ, `IMapper` без reflection,
который работает в NativeAOT ([#26](https://github.com/iron-mapper/IronMapper/issues/26)) и маппит коллекции через `Map<List<T>>`
([#8](https://github.com/iron-mapper/IronMapper/issues/8)), converters без состояния создаются один раз, диагностики с местом в исходном коде и
работающие code fix'ы, `IM0001` для профилей с включением по флагу ([#22](https://github.com/iron-mapper/IronMapper/issues/22)). Рабочий план — [IRONMAPPER_PLAN.md](https://github.com/iron-mapper/IronMapper/blob/main/IRONMAPPER_PLAN.md);
он может меняться.

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
