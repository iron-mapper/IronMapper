# Переход на IronMapper 2.1

English version: [upgrading-to-2.1.md](upgrading-to-2.1.md)

2.1.0 выходит после 2.0.0. Исправления настройки, которую профили молча игнорировали (`ConvertUsing`, `MapFrom`, `When`,
`BeforeMap` и `AfterMap` с группой методов, `[Ignore]` на свойстве), могут изменить то, что делает маппинг, который собирался с 2.0.0,
или то, собирается ли он; предупреждения nullable переходят из сгенерированного файла в ваш код;
in-place перегрузки, которые ничего не делают, помечены `[Obsolete]`; in-place перегрузка применяет `When` и хуки;
`init`-свойства маппингов с хуками маппятся; `IronMapperRegistration=false` теперь действует и на проект, в котором
задано; `AddConverter<T>()` помечен `[Obsolete]`. Всё остальное в 2.1.0 — новые возможности или исправления, которые затрагивают только код,
не собиравшийся с 2.0.0 ([CHANGELOG](../CHANGELOG.md)).

| Изменение | Кого касается |
|---|---|
| [`ConvertUsing` с группой методов статического метода вызывается](#1-convertusing-с-группой-методов-статического-метода-вызывается) | Профили с `ConvertUsing(SomeType.Method)` |
| [`ConvertUsing`, который нельзя вызвать, — `IM0022`](#2-convertusing-который-нельзя-вызвать--im0022) | Профили с `ConvertUsing(экземпляр.Метод)`, локальной функцией, приватным методом или делегатом в переменной |
| [Предупреждения nullable переходят из сгенерированного файла в `IM0023`](#3-предупреждения-nullable-переходят-из-сгенерированного-файла-в-im0023) | Проекты с nullable-аннотациями, у карт которых были `CS8601`/`CS8604` в файле `*.g.cs` |
| [In-place перегрузки, которые ничего не делают, — `[Obsolete]`](#4-in-place-перегрузки-которые-ничего-не-делают--obsolete) | Вызовы `source.MapToX(existing)` для приёмника-struct, маппинга с `ConvertUsing` или приёмника только с `init`-свойствами и `IMapper.Map(source, destination)` для такой пары |
| [`IronMapperRegistration=false` не генерирует регистрацию](#5-ironmapperregistrationfalse-не-генерирует-регистрацию) | Проекты с `<IronMapperRegistration>false</IronMapperRegistration>` |
| [`MapFrom`, `When` и хуки с группой методов статического метода вызываются](#6-mapfrom-when-и-хуки-с-группой-методов-статического-метода-вызываются) | Профили с `MapFrom(SomeType.Method)` в `ForMember`/`ForCtorParam`, `.When(SomeType.Method)`, `.BeforeMap(SomeType.Method)` или `.AfterMap(SomeType.Method)` |
| [`[Ignore]` учитывается в профилях](#7-ignore-учитывается-в-профилях) | Маппинги профилей между типами с `[Ignore]` на свойстве, которое есть и у другого типа |
| [`null` на входе может возвращать `null` (по выбору)](#8-null-на-входе-может-возвращать-null-по-выбору) | Проекты с `<IronMapperNullSourceReturnsNull>true</IronMapperNullSourceReturnsNull>` (без него ничего не меняется) |
| [`init`-свойства in-place: вложенные объекты обновляются, предупреждение `IM0025`](#9-init-свойства-in-place-вложенные-объекты-обновляются-предупреждение-im0025) | Маппинги с in-place перегрузками в типы с `init`-свойствами (предупреждение `IM0025`; с `TreatWarningsAsErrors` — ошибка сборки) |
| [Хуки и `When` в in-place перегрузке](#10-хуки-и-when-в-in-place-перегрузке) | In-place вызовы (`source.MapToX(existing)`, `IMapper.Map(source, existing)`) маппингов профилей с `When`, `BeforeMap` или `AfterMap` |
| [`required` и `init`-свойства с `BeforeMap`/`AfterMap`](#11-required-и-init-свойства-с-beforemapaftermap) | Маппинги профилей с хуками в типы с `init`-свойствами; `required`-свойства без значения (`IM0026` вместо `CS9035`) |
| [`AddConverter<T>()` помечен `[Obsolete]`](#12-addconvertert-помечен-obsolete) | Вызовы `opt.AddConverter<T>()` в `AddIronMapper(...)` (предупреждение `CS0618`; с `TreatWarningsAsErrors` — ошибка сборки) |

Чтобы найти первые два, поищите в профилях `ConvertUsing(`, аргумент которого не лямбда (`s => …`) и не аргумент типа
(`ConvertUsing<TConverter>()`); как найти случаи разделов 6 и 7 — в них. Все сразу — сравнением сгенерированного кода
2.0.0 и 2.1.0: соберите с `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` на каждой версии и сравните
`obj/<configuration>/<tfm>/generated/IronMapper.Generator/` (у маппинга, который меняют эти исправления, другое
присваивание этого свойства или его нет).

---

## 1. `ConvertUsing` с группой методов статического метода вызывается

**Было.** До 2.0.0 включительно генератор читал в `ConvertUsing(...)` только лямбду. Группа методов молча
игнорировалась: маппинг генерировался по свойствам, как будто `ConvertUsing` нет.

```csharp
CreateMap<Guid, UserId>().ConvertUsing(UserId.From);
// 2.0.0: return new UserId { };   (UserId.From не вызывается; struct получает значение по умолчанию)
// 2.1.0: return UserId.From(source);
```

**Стало.** Метод вызывается с source.

**Что сделать.** **Результат во время выполнения изменится.** Проверьте каждую такую карту: метод теперь выполняется
(и его исключения доходят до вызывающего кода), а свойства, которые задавал маппинг по свойствам, им больше не задаются.
Если вы полагались на прежний результат, уберите вызов `ConvertUsing`.

---

## 2. `ConvertUsing`, который нельзя вызвать, — `IM0022`

**Было.** Аргумент, который сгенерированный класс не может вызвать, — метод экземпляра (`ConvertUsing(converter.Convert)`),
локальная функция, приватный метод, делегат в переменной или поле — молча игнорировался, как группа методов: маппинг
генерировался по свойствам, без converter.

**Стало.** Сборка падает с ошибкой
[`IM0022`](diagnostics.ru.md#im0022--converter-convertusing-нельзя-использовать) на аргументе, маппинг не генерируется.

**Что сделать.** Передайте public или internal статический метод или лямбду, которая не использует локальные
переменные и экземпляр профиля:

```csharp
CreateMap<Guid, UserId>().ConvertUsing(v => UserIds.Create(v));   // или ConvertUsing(UserIds.Create), если он статический
```

---

## 3. Предупреждения nullable переходят из сгенерированного файла в `IM0023`

**Было.** Nullable-значение source для ненулевого destination (`string?` → `string`, nullable-элементы коллекции,
converter или трансформер, принимающий ненулевое значение) компилировалось с `CS8601`/`CS8604` внутри сгенерированного
файла `*.g.cs`, где его нельзя было исправить или подавить для одной карты.

**Стало.** О том же месте сообщается один раз — предупреждением
[`IM0023`](diagnostics.ru.md#im0023--nullable-значение-для-ненулевого-члена) в вашем маппинге, а в сгенерированном файле
предупреждений nullable нет ([#54](https://github.com/iron-mapper/IronMapper/issues/54)). Результат маппинга не меняется.

**Что вы можете увидеть.** Чистая сборка остаётся чистой, а сборка, которая падала на этих предупреждениях, падает и
теперь — с `IM0023`; проверено на опубликованном пакете 2.0.0:

| Проект | 2.0.0 | 2.1.0 |
|---|---|---|
| `<Nullable>enable</Nullable>` | предупреждения `CS8601`, `CS8604` | предупреждение `IM0023` |
| … с `TreatWarningsAsErrors` | ошибки `CS8601`, `CS8604` | ошибка `IM0023` |
| `<Nullable>disable</Nullable>` (с `TreatWarningsAsErrors` и без) | предупреждений нет | предупреждений нет |
| `<NoWarn>CS8601;CS8604</NoWarn>` или `.globalconfig` с `severity = none`, с `TreatWarningsAsErrors` | предупреждений нет | предупреждений нет |
| `<WarningsAsErrors>nullable</WarningsAsErrors>` (или `CS8601;CS8604`) | ошибки `CS8601`, `CS8604` | ошибка `IM0023` |
| `<WarningsNotAsErrors>CS8601;CS8604</WarningsNotAsErrors>` с `TreatWarningsAsErrors` | предупреждения | **ошибка `IM0023`** |

**Что сделать.** Исправьте маппинг, как подсказывает сообщение (`MapFrom(s => s.Email!)`, `?? ""`, nullable destination,
`Ignore()`), или задайте уровень `IM0023` так же, как задавали его для предупреждений компилятора (IM0023 берёт настройку заменяемого предупреждения компилятора, если она задана для всего проекта: подавлено — не выдаётся, ошибка — ошибка) (в последнем случае
таблицы добавьте `IM0023` в `WarningsNotAsErrors`). Коллекция, элементы которой отличаются только `?` (`List<string?>` →
`List<string>`), теперь присваивается как есть с `IM0023`; до 2.1 она не компилировалась.

---

## 4. In-place перегрузки, которые ничего не делают, — `[Obsolete]`

**Было.** У каждого маппинга была in-place перегрузка `source.MapToX(existing)`, в том числе там, где она ничего не может
изменить: приёмник — value-тип (`struct`, value object `readonly record struct`, `Guid`; метод получает копию), маппинг с
`ConvertUsing` (in-place перегрузка конвертер не применяет) и приёмник, у которого все сопоставленные свойства `init`
(in-place перегрузка их пропускает). Вызов компилировался и молча ничего не делал, как и `IMapper.Map(source, destination)`
для такой пары.

**Стало.** Удаление публичного метода сломало бы код, который его вызывает, поэтому в 2.x эти перегрузки остаются и
помечены `[Obsolete]` с причиной и тем, что делать; удаляются в 3.0.0. `IMapper.Map(source, destination)` для такой пары
бросает `MappingException` с той же причиной вместо бездействия:

```csharp
CreateMap<Guid, UserId>().ConvertUsing(UserId.From);

guid.MapToUserId(existing);
// warning CS0618: 'MapToUserId(Guid, UserId)' is obsolete: 'MapToUserId(source, destination) does nothing: UserId is a
// value type, so the destination is passed by copy and the caller's value does not change. Map to a new object instead
// (destination = source.MapToUserId()), or turn this overload off (...). It will be removed in IronMapper 3.0.0.'

mapper.Map(guid, existing);
// MappingException: No in-place mapping from Guid to UserId: UserId is a value type, ...
```

Результат остальных вызовов не меняется. Вложенный объект, маппинг которого — в проекте-ссылке, собранном с 2.1.0, и
имеет такую перегрузку, при in-place обновлении родителя получает новый экземпляр — как уже получал вложенный объект
такого маппинга своего проекта (раньше вызывалась перегрузка ссылки, и оставались старые значения).

**Что можно увидеть.** `CS0618` у каждого вызова такой перегрузки; с `TreatWarningsAsErrors` — ошибка.

**Что делать.** Маппить в новый объект (`existing = guid.MapToUserId();`) или выключить перегрузку для маппинга
(`GenerateInPlace = false`, `.GenerateMethods(inPlace: false)`) или проекта (`IronMapperGenerateInPlace=false`), если её
никто не вызывает. Чтобы пока оставить вызов, подавите `CS0618` вокруг него (`#pragma warning disable CS0618`).

---

## 5. `IronMapperRegistration=false` не генерирует регистрацию

**Было.** `IronMapperRegistration` только выставлял feature switch `IronMapper.Registration.IsSupported` приложения;
регистрация для `IMapper` (`GeneratedMappers.Registration.g.cs`, инициализатор модуля, закрытые in-place методы, которые она
вызывает) генерировалась всегда, а убирал её только триммер. В библиотеке свойство ни на что не влияло.

**Стало.** `<IronMapperRegistration>false</IronMapperRegistration>`, заданное в проекте (приложении или библиотеке), ещё и
отключает генерацию регистрации для маппингов этого проекта ([Регистрация для `IMapper`](configuration.ru.md#регистрация-для-imapper-в-приложениях-с-trimming-и-nativeaot)).
Значение, которое пакет выбирает сам для публикации с триммингом или NativeAOT без DI-пакета, как и раньше, попадает
только в feature switch и ничего не меняет в сгенерированном коде.

**Что можно увидеть.** `IMapper` не находит маппингов такого проекта: `MappingException` упоминает это свойство.

**Что делать.** Уберите свойство из проекта, маппинги которого нужны `IMapper`; оставьте там, где вызываются только
сгенерированные методы `MapTo…`.

---

## 6. `MapFrom`, `When` и хуки с группой методов статического метода вызываются

**Было.** У `MapFrom` есть перегрузка с `Func<TSource, object?>`, поэтому группа методов компилируется. Генератор читал
там только лямбду; группа методов молча игнорировалась, и член маппился так, будто `ForMember` нет
([#59](https://github.com/iron-mapper/IronMapper/issues/59)):

```csharp
public static class Helper { public static string Compute(Row r) => r.First + "!"; }

CreateMap<Row, Dto>()
    .ForMember(d => d.Title, o => o.MapFrom(Helper.Compute))   // 2.0.0: Title = source.Title (Compute не вызывается)
    .ForMember(d => d.Label, o => o.MapFrom(Helper.Compute));  // 2.0.0: у Row нет Label — CS1061 в сгенерированном файле
// 2.1.0: Title = Helper.Compute(source), Label = Helper.Compute(source), и in-place тоже
```

То же с `When`, `BeforeMap` и `AfterMap` (`Func<TSource, bool>`, `Action<TSource, TDest>`):

```csharp
CreateMap<Row, Dto>().When(Rules.IsActive).AfterMap(Audit.Stamp);
// 2.0.0: условия нет (маппится каждая строка), Audit.Stamp не вызывается
// 2.1.0: if (!Rules.IsActive(source)) return default!; … Audit.Stamp(source, destination);
```

**Стало.** Статический метод (public или internal) вызывается с source (хук — с source и destination). `MapFrom` — в новом
объекте и in-place (и в `ForCtorParam`); `When` и хуки — только в новом объекте, как и лямбда (in-place перегрузка не
применяет ни то, ни другое). Всё, что сгенерированный класс вызвать не может, — метод экземпляра (`MapFrom(formats.Title)`),
локальная функция, приватный метод, делегат в переменной или поле, анонимный метод — ошибка
[`IM0024`](diagnostics.ru.md#im0024--метод-настройки-профиля-нельзя-использовать) на аргументе, маппинг не генерируется.

**Что делать.** **Изменится результат во время выполнения.** `MapFrom`: там, где у source есть свойство с именем члена,
теперь выполняется метод (и его исключения доходят до вызывающего), а не копируется это свойство. `When`: для source, на
котором метод возвращает `false`, теперь возвращается `null` (`default`), а не смапленный объект. Хуки: метод теперь
выполняется. Такие маппинги ищутся по этим вызовам, аргумент которых не лямбда, — например регулярным выражением
`(MapFrom|When|BeforeMap|AfterMap)\(\s*[A-Za-z_][\w.<>]*\s*\)`. Если вы рассчитывали на прежний результат, уберите
вызов. Для `IM0024` передайте статический метод или лямбду, которая не использует локальные переменные и экземпляр
профиля.

---

## 7. `[Ignore]` учитывается в профилях

**Было.** `[Ignore]` на свойстве работал в маппингах `[MapTo]`/`[MapFrom]`. Маппинг профиля его не учитывал: на свойстве
destination только не выдавал `IM0001` и всё равно копировал одноимённое свойство source
([#61](https://github.com/iron-mapper/IronMapper/issues/61)); свойство source с `[Ignore]` всё равно читалось:

```csharp
public class Dto { public string Title { get; set; } = ""; [Ignore] public string Secret { get; set; } = ""; }

public class Row { public string Title { get; set; } = ""; public string Secret { get; set; } = ""; [Ignore] public string Draft { get; set; } = ""; }

CreateMap<Row, Dto>();   // у Dto тоже есть Draft
// 2.0.0: Secret = source.Secret, Draft = source.Draft (и in-place)
// 2.1.0: не заполняется ни одно
```

**Стало.** Маппинг профиля пропускает такое свойство, как маппинг атрибутами: на destination оно не заполняется, на source
не читается (и свойство члена `IncludeMembers`). В новом объекте остаётся значение по умолчанию, in-place перегрузка (и
`IMapper.Map(source, destination)`) оставляет текущее значение, а параметр конструктора с тем же именем получает значение
по умолчанию (без значения по умолчанию — `IM0009`). `ForMember` свойства destination (`MapFrom`, `UseConverter`) важнее
`[Ignore]`. С `ReverseMap()` свойство с `[Ignore]` на любом из двух типов пропускается в обоих направлениях.

**Что делать.** **Изменится результат во время выполнения:** свойство с `[Ignore]`, которое до сих пор копировалось,
сохраняет значение по умолчанию (in-place — текущее). Как найти: типы с `[Ignore]` на свойстве, которые стоят в
`CreateMap<TSource, TDest>()` (любым из двух, и с `.ReverseMap()` или `IncludeMembers`), если у другого типа есть свойство
с тем же именем. Если вы рассчитывали на копирование, уберите `[Ignore]` или замапьте свойство через
`ForMember(d => d.Secret, o => o.MapFrom(s => s.Secret))`.

---

## 8. `null` на входе может возвращать `null` (по выбору)

**Было.** `MapToX(source)` бросал `ArgumentNullException` для `null` на входе
([#27](https://github.com/iron-mapper/IronMapper/issues/27)); обходной путь — `source?.MapToX()` или проверка перед вызовом.

**Стало.** По умолчанию ничего не меняется. С `<IronMapperNullSourceReturnsNull>true</IronMapperNullSourceReturnsNull>`
метод маппинга класса в класс становится `MapToX(this S? source)` → `D?` с `[return: NotNullIfNotNull("source")]` и
возвращает `null` для `null` ([`null` на входе](configuration.ru.md#null-на-входе-ironmappernullsourcereturnsnull)). В
3.0.0 планируется поведением по умолчанию.

**Что можно увидеть со свойством.** Код, который маппит ненулевой источник, не получает новых предупреждений nullable:
результат там не `null`. Источники и приёмники-struct, in-place перегрузка, `null`-элементы коллекций и вложенные объекты
ведут себя как без свойства, а `IMapper.Map<TDest>(null)` по-прежнему бросает `ArgumentNullException`. В netstandard2.0 с
PolySharp задайте `<IronMapperEmitPolyfills>false</IronMapperEmitPolyfills>` (оба генератора объявили бы
`NotNullIfNotNullAttribute`, ошибка `CS0101`). `catch (ArgumentNullException)` вокруг `MapToX(source)` верхнего уровня больше не
срабатывает: вместо исключения результат — `null`.

**Что делать.** Ничего, если `null` → `null` не нужен; иначе задайте свойство и (по желанию) уберите проверки `?.`/`null`
вокруг вызовов — или подготовьтесь к 3.0.0.

---

## 9. `init`-свойства in-place: вложенные объекты обновляются, предупреждение `IM0025`

**Было.** In-place перегрузка `source.MapToX(destination)` (и `IMapper.Map(source, destination)`) молча пропускала все
`init`-свойства, в том числе вложенный объект в `init`-свойстве
([#58](https://github.com/iron-mapper/IronMapper/issues/58)). Destination только с `init`-свойствами описан в разделе 4.

```csharp
public class OrderDto
{
    public string Name { get; set; } = "";
    public string Code { get; init; } = "";
    public CustomerDto? Customer { get; init; }
}

order.MapToOrderDto(existing);
// 2.0.0: Name обновлён; Code и Customer без изменений, предупреждения нет
// 2.1.0: Name обновлён; Code без изменений; существующий existing.Customer обновлён на месте; предупреждение IM0025 у маппинга
```

**Стало.**

- Существующий объект во вложенном `init`-свойстве обновляется на месте через getter (тот же объект, новые значения),
  как и вложенное свойство с `set`. `null` там остаётся `null`, и `null` из source не присваивается.
- Предупреждение [`IM0025`](diagnostics.ru.md#im0025--in-place-перегрузка-не-обновляет-init-свойства) у маппинга
  перечисляет сопоставленные `init`-свойства (и `required init`), которые in-place перегрузка не обновляет, и вложенные,
  которые она обновляет, только если они есть. Не выдаётся для проигнорированных свойств, выключенной in-place перегрузки
  и перегрузки с `[Obsolete]` (раздел 4).
- Тип только с `init`-свойствами, среди которых есть вложенный объект, который можно обновить, больше не `[Obsolete]`:
  его in-place перегрузка обновляет этот объект.

**Что можно увидеть.** **Изменится результат во время выполнения** только для вложенных объектов в `init`-свойствах:
существующий теперь получает значения source (до 2.0.0 включительно сохранял старые). Остальные `init`-свойства
пропускаются, как и раньше. С `TreatWarningsAsErrors` новое предупреждение — ошибка сборки.

**Что делать.** Проверьте маппинги, на которые указывает `IM0025`. Дальше одно из:

- свойства маппить не нужно — проигнорируйте их (`[Ignore]`, `ForMember(d => d.Code, o => o.Ignore())`; тогда их не
  получит и новый объект);
- in-place обновление не нужно — выключите его (`GenerateInPlace = false`, `.GenerateMethods(inPlace: false)`,
  `IronMapperGenerateInPlace=false`). Предупреждение тогда уходит, но `IMapper.Map(source, destination)` всё равно
  обновляет такой тип частично, без предупреждения (у регистрации остаётся закрытый in-place метод с тем же телом): если
  вы вызываете его для этой пары, учитывайте это;
- свойства должны обновляться — сделайте их изменяемыми (`set`);
- оставить как есть — `#pragma warning disable IM0025` вокруг атрибута или строки `CreateMap`, или
  `dotnet_diagnostic.IM0025.severity = none` в `.editorconfig` (`<NoWarn>IM0025</NoWarn>` для всего проекта).


---

## 10. Хуки и `When` в in-place перегрузке

**Было.** In-place перегрузка `source.MapToX(destination)` (и `IMapper.Map(source, destination)`) только присваивала
сопоставленные свойства: не проверяла `When` и не вызывала `BeforeMap`/`AfterMap`
([#62](https://github.com/iron-mapper/IronMapper/issues/62)). `Map(source, destination)` в AutoMapper их применяет,
поэтому перенесённый вызов молча пропускал эту логику.

```csharp
CreateMap<Row, Dto>().When(s => s.Active).AfterMap((s, d) => d.Version++);

row.MapToDto(existing);
// 2.0.0: свойства присвоены при любом Active; Version без изменений
// 2.1.0: Active == false — existing без изменений; иначе BeforeMap, свойства, AfterMap (Version + 1)
```

**Стало.** In-place тело выполняется так же, как для нового объекта: сначала `When` (`false` — destination не
меняется, хуки не вызываются), затем `BeforeMap`, свойства и `AfterMap`. Так же и для существующего вложенного объекта,
который обновляется на месте: к нему применяются `When` и хуки маппинга его пары. `ConvertUsing` в in-place не
применяется (такая перегрузка — `[Obsolete]`, раздел 4). Destination только с `init`-свойствами и хуком больше не
`[Obsolete]`: хук может его изменить (`init`-свойства перечислены в `IM0025`, раздел 9). Маппинги без хуков и `When`
получают тот же код, что и раньше.

**Что можно увидеть.** **Изменится результат во время выполнения** для in-place вызовов маппингов с `When`, `BeforeMap`
или `AfterMap`: destination, для которого `When` ложно, больше не обновляется, а хуки вызываются (один раз за вызов). С
`TreatWarningsAsErrors` маппинг в тип только с `init`-свойствами и хуком получает `IM0025` вместо `CS0618` в местах
вызова.

**Что делать.** Найдите in-place вызовы (`source.MapToX(existing)`, `mapper.Map(source, existing)`) маппингов с `When`,
`BeforeMap` или `AfterMap` и проверьте, что хуки можно выполнять на существующем объекте (например, счётчик или отметка
времени, которые ставит хук). Если нужно старое поведение, обновляйте объект без логики маппинга своим кодом или
объявите для in-place обновления второй маппинг без хуков.

---

## 11. `required` и `init`-свойства с `BeforeMap`/`AfterMap`

**Было.** Маппинг с `BeforeMap` или `AfterMap` создавал destination через `new D()` и присваивал свойства по одному
([#60](https://github.com/iron-mapper/IronMapper/issues/60)): `required`-свойство давало ошибку сборки внутри
сгенерированного файла (`CS9035`), а `init`-свойство молча оставалось со значением по умолчанию. `required`-свойство, которому
маппинг не даёт значения (проигнорировано, ничто его не сопоставляет), тоже давало там `CS9035`, и без хуков.

```csharp
public class Dto { public required string Code { get; set; } public string Name { get; init; } = ""; public int Id { get; set; } }

CreateMap<Row, Dto>().AfterMap((s, d) => d.Id++);
// 2.0.0: CS9035 в сгенерированном файле (Code); без required — Name не маппится
// 2.1.0: new Dto { Code = …, Name = … }, затем BeforeMap, Id, AfterMap
```

**Стало.** Инициализатор объекта задаёт сопоставленные `required`- и `init`-свойства, затем вызывается `BeforeMap`,
присваиваются остальные свойства и вызывается `AfterMap`; с конструктором инициализатор идёт после его аргументов. После
фабрики (`ConstructWith`, `ConstructUsing`) свойства присваиваются, как и раньше (`init`-свойство там — `IM0021`).
`required`-свойство без значения — новая ошибка
[`IM0026`](diagnostics.ru.md#im0026--обязательное-свойство-не-сопоставлено) у маппинга, и маппинг не генерируется, вместо
`CS9035` внутри сгенерированного файла (не для конструктора с `[SetsRequiredMembers]`).

**Что можно увидеть.** **Изменится результат во время выполнения** для `init`-свойств маппингов с хуками: теперь они
получают значения source (до 2.0.0 включительно оставались по умолчанию). `BeforeMap` видит `required`- и `init`-свойства
уже заданными (остальные — ещё нет). `IM0026` появляется только там, где сборка падала с `CS9035`.

**Что делать.** Проверьте хуки `BeforeMap` таких маппингов, которые читают или задают `required`- или `init`-свойство:
инициализатор задал его до вызова хука, а хук, который его задаёт, перезаписывает сопоставленное значение. Для `IM0026` —
сопоставьте свойство, уберите `required` или создавайте destination конструктором с `[SetsRequiredMembers]` либо
фабрикой.

## 12. `AddConverter<T>()` помечен `[Obsolete]`

**Было.** `opt.AddConverter<T>()` в `AddIronMapper(...)` регистрировал converter в DI-контейнере как transient-сервис, и
только. Сгенерированный код создаёт converters сам и никогда не берёт их из контейнера, поэтому вызов на маппинг не
влиял, а converter с зависимостями в конструкторе не собирался (`IM0015`).

**Стало.** Метод помечен `[Obsolete]` и будет удалён в 3.0.0. Тип он по-прежнему регистрирует.

**Что вы можете увидеть.** Предупреждение `CS0618` в месте вызова, с `TreatWarningsAsErrors` — ошибка сборки. Во время
выполнения ничего не меняется.

**Что делать.** Удалите вызов. Если ваш собственный код получает converter из контейнера, зарегистрируйте его сами
(`services.AddTransient<MyConverter>()`). Converter, которому нужен сервис, не поддерживается: посчитайте значение до
маппинга или задайте его после ([Converters из DI не поддерживаются](configuration.ru.md#converters-из-di-не-поддерживаются-addconvertert-устарел)).
