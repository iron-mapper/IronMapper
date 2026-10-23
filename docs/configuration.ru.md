# Справочник по конфигурации

> 🌐 **[English version](configuration.md)**

## Доступность сгенерированных методов

Относится и к маппингам `[MapTo]`/`[MapFrom]`, и к `MappingProfile`.

Каждый сгенерированный метод (`MapToX`, in-place перегрузка, `MapToXList`, `MapToXArray`) доступен ровно настолько,
насколько доступен **менее доступный** из типов source и destination. Учитывается *эффективная* доступность:
содержащие типы для вложенных типов и аргументы обобщённых типов тоже.

| Типы source / destination | Сгенерированные методы |
|---|---|
| оба `public` (включая все содержащие типы и аргументы типов) | `public` |
| хотя бы один `internal` или `protected internal` (или вложен в `internal`-тип) | `internal` |
| хотя бы один вложенный тип `private`, `protected` или `private protected` | не генерируются, ошибка [`IM0010`](diagnostics.ru.md#im0010--тип-маппинга-недоступен-из-сгенерированного-кода) |

Вспомогательные методы `BeforeMap`/`AfterMap` и `AddTransformer` всегда `private`.

Internal-маппинги работают и через `IMapper` (`IronMapper.Extensions.DI`): сгенерированная регистрация сборки
регистрирует и их. Код из других сборок может вызвать internal-маппинг напрямую только через `[InternalsVisibleTo]`.

---

## Имена методов и коллизии

Каждый маппинг генерирует `MapTo{Name}` (новый объект), in-place перегрузку `MapTo{Name}(destination)`,
`MapTo{Name}List` и `MapTo{Name}Array`, где `{Name}` — **простое имя** destination-типа. Маппинги разных source-типов
— это перегрузки, и они сохраняют эти имена даже при одинаковых простых именах (`Shop.Item → Shop.ItemDto` и
`Stock.Item → Stock.ItemDto` оба используют `MapToItemDto`).

### Одна пара типов задана дважды

Пара типов генерируется **один раз**, а предупреждение [`IM0007`](diagnostics.ru.md#im0007--повторное-определение-маппинга)
перечисляет все определения. Генерируется определение из первого подходящего пункта:

1. `CreateMap` в `MappingProfile`;
2. `ReverseMap()` в профиле;
3. `[MapTo]`;
4. `[MapFrom]`.

Профиль имеет приоритет над атрибутами, потому что профиль — это явная конфигурация (`ForMember`, хуки, `When`); так и
было записано в сообщении IM0007. Явный `CreateMap` побеждает `ReverseMap()`, который маппит только по именам. Среди
определений одного вида побеждает профиль (или тип), чьё полное имя идёт первым при порядковом сравнении, затем — первый
вызов в конструкторе. Все остальные маппинги проекта генерируются.

### Один source-тип, несколько destination-типов с одинаковым простым именем

Два метода `MapToUserDto(this User)` не могут существовать одновременно (`CS0111`), поэтому **все** маппинги этой
группы получают уточнённое имя, а предупреждение
[`IM0013`](diagnostics.ru.md#im0013--имя-метода-маппинга-уточнено-из-за-коллизии) сообщает, как вызывать метод.
Используется первое правило, при котором все имена группы уникальны (и не совпадают с другими именами методов того же
source-типа):

1. последние 1, 2, … сегмента пространства имён и содержащих типов destination, перед именем;
2. аргументы типа по простым именам, после имени;
3. и то и другое;
4. аргументы типа по полным именам.

| Маппинги из `App.User` / `App.Item` | Сгенерированные имена |
|---|---|
| `App.Admin.UserDto`, `App.Public.UserDto` | `MapToAdminUserDto`, `MapToPublicUserDto` (+ `List`, `Array`) |
| `App.Orders.Dto`, `App.Invoices.Dto` (вложенные типы) | `MapToOrdersDto`, `MapToInvoicesDto` |
| `App.Box<int>`, `App.Box<string>` | `MapToBoxOfInt32`, `MapToBoxOfString` |
| глобальное пространство имён: `Dto` и `App.Dto` | `MapToGlobalDto`, `MapToAppDto` |
| `App.SummaryDto` (коллизии нет) | `MapToSummaryDto`, без изменений |

Сгенерированный код для элементов вложенных коллекций и `IMapper` (`IronMapper.Extensions.DI`, который использует
сгенерированную регистрацию) используют уточнённые имена автоматически. Ваш код — в том числе лямбды `MapFrom` — должен вызывать
уточнённое имя. Если ни одно правило не даёт уникальных имён (все заняты другими маппингами), маппинги этой группы не
генерируются и выдаётся ошибка [`IM0014`](diagnostics.ru.md#im0014--коллизию-имён-методов-маппинга-нельзя-устранить).

Имена, которые компилировались раньше, не меняются: правила применяются только к группам, которые раньше падали с
`CS0111`. Но если позже добавить второй destination-тип с тем же простым именем, существующий маппинг этого source-типа
будет переименован (IM0013 сообщит новое имя). Чтобы сохранить `MapToUserDto`, переименуйте один из destination-типов.

### Имена вспомогательных методов и файлов

Вспомогательные методы `BeforeMap`/`AfterMap` и `AddTransformer` называются `BeforeMap_{Source}_{Dest}`,
`TransformValue_{Type}_{Source}_{Dest}` по простым именам. Если у другого маппинга те же простые имена, используются
полные (`TransformValue_String_Shop_Item_Shop_ItemDto`). Эти методы `private`. Сгенерированные файлы называются по
полным именам обоих типов (`Shop.Item_MapTo_Shop.ItemDto.g.cs`).

## Global using для `IronMapper.Generated`

Сгенерированные методы `MapTo*` лежат в пространстве имён `IronMapper.Generated`. Пакет `IronMapper` (и когда он
приходит через `IronMapper.Extensions.DI`) добавляет `global using IronMapper.Generated;` в проекты C# с неявными using
(`<ImplicitUsings>enable</ImplicitUsings>` или `true`, по умолчанию в шаблонах .NET 6+); файл пакета —
`buildTransitive/IronMapper.props`.

- **Без неявных using** ничего не добавляется: пишите `using IronMapper.Generated;` в файлах, где вызываются методы
  `MapTo*` (в C# 9 и раньше global using нет вообще).
- Явный `using IronMapper.Generated;` рядом с глобальным компилируется без предупреждений компилятора. При строгом
  стиле кода в сборке (`<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` и
  `dotnet_diagnostic.IDE0005.severity = error` или `warning` с `TreatWarningsAsErrors`) он считается лишним:
  `Program.cs(7,1): error IDE0005: Using directive is unnecessary.` Уберите явные using или отключите global using
  (ниже). Проверяется в `eng/verify-package.ps1` на упакованном пакете.
- Проект без маппингов получает от генератора пустое пространство имён `IronMapper.Generated`, поэтому global using
  компилируется и там.
- **Отключить** для проекта (или для всех проектов в `Directory.Build.props`):

  ```xml
  <PropertyGroup>
    <IronMapperImplicitUsings>false</IronMapperImplicitUsings>
  </PropertyGroup>
  ```

Global using действует так же, как `using IronMapper.Generated;` в каждом файле. В файле, который раньше не
импортировал это пространство имён, вызов `x.MapToY()` становится неоднозначным (`CS0121`), только если файл через
директиву `using` импортирует и другой метод расширения с тем же именем и типом параметра (например, написанный
вручную маппер из переносимого кода): удалите или переименуйте его либо отключите global using. Про одну пару типов,
смапленную в двух проектах, — см. [Решения из нескольких проектов](#решения-из-нескольких-проектов).

## Решения из нескольких проектов

Каждый проект, который ссылается на пакет `IronMapper`, получает собственный класс с маппингами этого проекта в
пространстве имён `IronMapper.Generated`. Начиная с 2.0.0 в имени класса есть имя сборки: `GeneratedMappers_{ИмяСборки}`,
где каждый символ имени сборки, кроме букв, цифр и `_`, заменён на `_` (`Shop.Application` →
`GeneratedMappers_Shop_Application`, `My-App.Web` → `GeneratedMappers_My_App_Web`). Имя сборки длиннее 64 символов
сокращается до первых 55 символов, к ним добавляются `_` и 8 шестнадцатеричных цифр хэша полного имени (FNV-1a;
одинаковый в каждой сборке проекта). До 1.1.4 включительно класс назывался `GeneratedMappers` во всех сборках, и явное
обращение к нему давало `CS0436`, если был виден класс проекта-зависимости. С регистрацией для `IMapper` у каждой сборки
появились бы ещё и члены и вложенные типы с одинаковыми полными именами (`RegisterIronMapperMappingsCore`, его лямбды в
`<>c`, `ConverterCache<T>`, …): инструменты, которые различают типы по полному имени, например ArchUnitNET, приписывали
зависимости сгенерированного класса одной сборки классу другой, и правило слоёв вида «Application не зависит от
Infrastructure» падало (`GeneratedTypeNamesArchitectureTests`). Две сборки, имена которых отличаются только заменёнными
символами (`My.App` и `My_App`), получают одинаковое имя класса; компилятору это не мешает (классы в разных сборках),
мешало бы только таким инструментам. Имена сгенерированных файлов не меняются (`GeneratedMappers.g.cs`,
`GeneratedMappers.Registration.g.cs`).

**Доступность класса.** Класс `public`, если хотя бы один сгенерированный метод проекта `public`, иначе `internal`.
Поэтому проект только с internal-маппингами ничего не публикует: проекты, которые на него ссылаются, вообще не видят
его класс (если он не открывает внутренности через `[InternalsVisibleTo]`).

**Вызов маппингов проекта-зависимости.** Используйте синтаксис метода расширения; пространство имён импортирует
[global using](#global-using-для-ironmappergenerated) или `using IronMapper.Generated;`:

```csharp
using IronMapper.Generated;               // только без неявных using

var dto = product.MapToProductDto();      // public-маппинг из проекта, на который ссылается текущий
var order = entity.MapToOrderDto();       // маппинг текущего проекта
```

Маппинги текущего проекта и public-маппинги проектов-зависимостей можно использовать вместе; это собирается с
`TreatWarningsAsErrors` (проверяется в `GeneratedClassVisibilityTests` и в проекте-потребителе пакета
`tests/IronMapper.PackageConsumer`, который ссылается на две библиотеки с IronMapper).

Уточнённые имена ([`IM0013`](#один-source-тип-несколько-destination-типов-с-одинаковым-простым-именем)) public-маппингов
проекта-зависимости вызываются так же — по имени из предупреждения в том проекте
(`member.MapToAdminMemberDto()`, `members.MapToAdminMemberDtoList()`).

**Явное обращение к классу** работает по полному имени класса каждого проекта, например
`GeneratedMappers_Shop_Application.MapToOrderDto(entity)` и `GeneratedMappers_Shop_Catalog.MapToProductDto(product)` в
одном файле (`GeneratedClassVisibilityTests`). Синтаксис метода расширения предпочтительнее: он не меняется при
переименовании сборки.

Остаётся одно ограничение:

- **Объявляйте каждый маппинг в одном проекте.** Если проект-зависимость уже публикует маппинг для пары типов, а
  текущий проект маппит ту же пару ещё раз, текущий проект собирается, но вызов `source.MapToX()` в нём
  неоднозначен (`CS0121`). То же происходит внутри сгенерированного кода, если тип элемента вложенной коллекции
  маппится в обоих проектах. Держите маппинг в проекте, которому принадлежат типы.

`IMapper` (`IronMapper.Extensions.DI`) использует маппинги всех сборок, чья сгенерированная регистрация выполнилась,
независимо от того, `public` класс или `internal`: передайте сборки с маппингами в `AddIronMapper` (см.
[IMapper и сгенерированные extension-методы](advanced.ru.md#imapper-и-сгенерированные-extension-методы)). Самой
библиотеке с маппингами пакет DI не нужен: достаточно, чтобы на него ссылалось приложение.

## Регистрация для `IMapper` в приложениях с trimming и NativeAOT

Для `IMapper` каждая сборка с маппингами регистрирует их через сгенерированный инициализатор модуля (см.
[IMapper и сгенерированные extension-методы](advanced.ru.md#imapper-и-сгенерированные-extension-методы)). В приложении с
trimming или NativeAOT эта регистрация удерживала бы реестр и все сгенерированные методы сборки — даже в приложении,
которое вызывает только extension-методы. Поэтому она стоит за feature switch `IronMapper.Registration.IsSupported`
(`MapperRegistry.IsSupported`), который пакет `IronMapper` выставляет по MSBuild-свойству `IronMapperRegistration`
проекта приложения (`buildTransitive/IronMapper.targets`):

| Приложение | Switch | Регистрация |
|---|---|---|
| `PublishAot` или `PublishTrimmed`, ни один проект не ссылается на `IronMapper.Extensions.DI` | `false` | удаляется trimmer'ом |
| ссылается на `IronMapper.Extensions.DI` напрямую или через свою библиотеку | не задан (включён) | остаётся |
| без trimming (`dotnet run`, публикация без trimming) | не задан (включён) | остаётся |

Задайте свойство в проекте приложения, если автоматический выбор не подходит: например, приложение NativeAOT
использует `MapperRegistry` без пакета DI (`true`) или ссылается на пакет DI, но `IMapper` не использует (`false`):

```xml
<PropertyGroup>
  <IronMapperRegistration>true</IronMapperRegistration>
</PropertyGroup>
```

- При выключенном switch создание `IMapper` (`RuntimeMapper`) бросает `InvalidOperationException` с именем свойства,
  а `MappingException` из `MapperRegistry` для ненайденного маппинга говорит то же самое вместо простого «No mapping
  registered».
- Сгенерированные extension-методы от switch не зависят.
- Свойство действует только в проекте приложения (оно превращается в `RuntimeHostConfigurationOption`); в библиотеке
  оно ни на что не влияет. При ссылках на исходники IronMapper (ProjectReference) вместо пакета ничего не задаётся, и
  регистрация остаётся.

---

## Генерируемые перегрузки и покрытие кода

Для каждого маппинга генератор создаёт `MapToX(source)` и по умолчанию ещё три перегрузки: in-place
`MapToX(source, destination)`, `MapToXList(...)` и `MapToXArray(...)`. Необязательные можно отключить (начиная с 2.0.0),
если проект их не использует: меньше сгенерированного кода, меньше методов в IntelliSense и нет непокрытых методов в
отчётах о покрытии. По умолчанию ничего не меняется.

**Для всего проекта** (MSBuild-свойства, по умолчанию `true`; отключает только `false` без учёта регистра):

```xml
<PropertyGroup>
  <IronMapperGenerateInPlace>false</IronMapperGenerateInPlace>   <!-- MapToX(source, destination) -->
  <IronMapperGenerateList>false</IronMapperGenerateList>         <!-- MapToXList -->
  <IronMapperGenerateArray>false</IronMapperGenerateArray>       <!-- MapToXArray -->
</PropertyGroup>
```

**Для одного маппинга** (переопределяет значение проекта в обе стороны):

```csharp
[MapTo(typeof(UserDto), GenerateList = false, GenerateArray = false)]
public class User { ... }

[MapFrom(typeof(Order), GenerateInPlace = true)]   // снова включено, хотя IronMapperGenerateInPlace = false
public class OrderDto { ... }

public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<Order, OrderSummaryDto>().GenerateMethods(inPlace: false, list: false);
    }
}
```

- Не заданное свойство атрибута берёт значение проекта. Геттер `GenerateInPlace`/`GenerateList`/`GenerateArray`
  возвращает `false`, если свойство не задано (это и показывает IntelliSense): это ограничение атрибутов C#, а не
  поведение; генератор смотрит, задано ли свойство.
- `GenerateMethods(inPlace, list, array)` — extension-метод в `IronMapper.Configuration`; `null` или пропущенный аргумент
  берёт значение проекта. Аргументы должны быть константами (`true`, `false`, `null`, `const`): генератор читает их при
  компиляции; на другое выражение — предупреждение
  [`IM0016`](diagnostics.ru.md#im0016--аргумент-generatemethods-не-константа), и действует значение проекта. Действует на
  этот маппинг, а не на обратный маппинг `ReverseMap()`.
- `MapToX(source)` генерируется всегда: вложенные свойства-коллекции вызывают его для каждого элемента, поэтому они
  работают и без `MapToXList`/`MapToXArray` у маппинга элементов.
- Отключение перегрузок не меняет имена оставшихся методов и правила коллизий
  ([Имена методов и коллизии](#имена-методов-и-коллизии)).
- **`IMapper`** по-прежнему маппит всё: `Map<UserDto>(user)`, `Map<List<UserDto>>(users)`, `Map<UserDto[]>(users)`,
  `MapCollection` и `Map(source, destination)` работают для маппинга без публичных перегрузок и дают те же результаты.
  Без `MapToXList`/`MapToXArray` реестр сам заполняет список или массив, вызывая `MapToX` для каждого элемента через
  делегат (`IMapper` и так вызывает делегаты; extension-методы это не затрагивает).
- **In-place:** отключение убирает только публичный метод. Его тело остаётся закрытым методом, который `IMapper` вызывает
  для `Map(source, destination)`, поэтому на объём кода и покрытие это почти не влияет. Для покрытия используйте
  `IronMapperExcludeFromCodeCoverage` (ниже).

### Атрибуты сгенерированного класса

Сгенерированный класс помечен `[GeneratedCode("IronMapper", "<версия пакета>")]`, поэтому анализаторы и инструменты
считают его сгенерированным. С

```xml
<IronMapperExcludeFromCodeCoverage>true</IronMapperExcludeFromCodeCoverage>
```

он помечается ещё и `[ExcludeFromCodeCoverage]` (coverlet, покрытие Visual Studio и другие его пропускают), по умолчанию
`false`. Оба атрибута объявлены один раз на классе и действуют на все его части: методы маппинга, регистрацию для
`IMapper` и кэш конвертеров. Они выпускаются, только если в целевой платформе они есть (оба есть в .NET Standard 2.0 и
новее). Код маппинга по-прежнему проверяется вашими тестами: атрибут только убирает его из процента покрытия.

**Инструменты покрытия, которые исключают `[GeneratedCode]`.** coverlet с `ExcludeByAttribute=GeneratedCodeAttribute`
(в `coverlet.runsettings`, `<ExcludeByAttribute>` у `coverlet.msbuild` или `--exclude-by-attribute`) и другие инструменты
с такой же настройкой начиная с 2.0.0 не включают сгенерированные маппинги в отчёт — даже без
`IronMapperExcludeFromCodeCoverage`. Чтобы измерять их, отключите `[GeneratedCode]`:

```xml
<IronMapperGeneratedCodeAttribute>false</IronMapperGeneratedCodeAttribute>
```

По умолчанию `true`; отключает только `false` (без учёта регистра). На `IronMapperExcludeFromCodeCoverage` не влияет.

---

## Несопоставленные свойства в профилях

Для `[MapTo]`/`[MapFrom]` несопоставленное свойство destination даёт предупреждение
[`IM0001`](diagnostics.ru.md#im0001--несопоставленное-свойство-destination). Для маппингов профилей (`CreateMap`)
оно по умолчанию выключено, как в 1.1 (несопоставленное свойство остаётся со значением по умолчанию без предупреждения;
issue [#22](https://github.com/iron-mapper/IronMapper/issues/22)), и включается MSBuild-свойством (начиная с 2.0.0):

```xml
<PropertyGroup>
  <IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>
</PropertyGroup>
```

Свойство задаётся в проекте или в `Directory.Build.props`. Пакет передаёт его генератору (`CompilerVisibleProperty` в
`buildTransitive/IronMapper.props`); любое значение, кроме `true` (без учёта регистра), оставляет проверку выключенной.
В следующей мажорной версии её планируется включить по умолчанию.

Предупреждение указывает на вызов `CreateMap`. Свойство destination считается сопоставленным, если:

- его настраивает `ForMember`: `opt.MapFrom(...)`, `opt.Ignore()`, `opt.UseConverter<T>()` (и отдельный
  `.Ignore(d => d.X)`);
- его даёт `IncludeMembers` или source-свойство с тем же именем (без учёта регистра);
- на нём стоит `[Ignore]` (как в маппингах атрибутами).

Маппинг с `ConvertUsing<T>()` или `ConvertUsing(src => ...)` IM0001 не получает: converter создаёт весь объект
destination. Обратный маппинг `ReverseMap()` не проверяется: он сопоставляет только по именам, и `ForMember` к нему не
применяется (AutoMapper обратные маппинги тоже не проверяет — у них `MemberList.None`). Чтобы он проверялся, объявите
его отдельным `CreateMap`. Code fix «Ignore '...' in this mapping (ForMember)» добавляет
`.ForMember(dest => dest.X, opt => opt.Ignore())` после `CreateMap`.

### Уровень

Отдельного свойства IronMapper для уровня нет: используйте стандартные настройки любой диагностики. В `.editorconfig`
(действует на файлы, которые он покрывает) или `.globalconfig`:

```ini
[*.cs]
dotnet_diagnostic.IM0001.severity = error
```

`<WarningsAsErrors>$(WarningsAsErrors);IM0001</WarningsAsErrors>` и `<NoWarn>` в файле проекта тоже работают. Уровень
действует и на маппинги атрибутами, и на профили (это один идентификатор диагностики).

---

## Атрибуты

### `[MapTo(typeof(TDest))]`

Применяется к **source**-типу. Указывает генератору создать extension method `MapToTDest()`.

```csharp
[MapTo(typeof(ProductDto))]
public class ProductEntity { ... }
```

Тип в `typeof(...)` должен быть классом, структурой или record; generic-тип — закрытым (`typeof(Box<int>)`). Для
открытого generic-типа (`typeof(Box<>)`) или массива маппинг не генерируется и выдаётся
[`IM0003`](diagnostics.ru.md#im0003--тип-в-maptomapfrom-нельзя-смапить). Тип, который не разрешается
(`typeof(Missing)`), компилятор сообщает сам (`CS0246`) на атрибуте, а этот маппинг пропускается, поэтому
сгенерированный код не добавляет своих ошибок; то же для `[MapFrom]`, `CreateMap<,>` и типов converter.

Поддерживается несколько destination-типов — применяй атрибут несколько раз:

```csharp
[MapTo(typeof(ProductDto))]
[MapTo(typeof(ProductSummaryDto))]
public class ProductEntity { ... }
```

---

### `[MapFrom(typeof(TSource))]`

Применяется к **destination**-типу. Функционально идентичен `[MapTo]` на source — используй тот вариант, который лучше вписывается в структуру кода.

```csharp
[MapFrom(typeof(ProductEntity))]
public class ProductDto { ... }
```

---

### `[MapProperty("DestPropertyName")]`

Применяется к **свойству source-типа**. Перенаправляет значение в свойство destination-типа с другим именем. Имя сравнивается без учёта регистра. Работает только для маппингов через атрибуты.

```csharp
[MapTo(typeof(PersonDto))]
public class Person
{
    [MapProperty("FullName")]
    public string Name { get; set; } = "";
}

public class PersonDto
{
    public string FullName { get; set; } = "";
}
```

---

### `[Ignore]`

Применяется к **свойству source-типа** или **свойству destination-типа**. Свойство исключается из обоих вариантов mapping — создающего и in-place; на свойстве destination оно также подавляет `IM0001`.

`[Ignore]` учитывается только в маппингах `[MapTo]`/`[MapFrom]`. В профилях используйте `ForMember(d => d.X, o => o.Ignore())`.

```csharp
[MapTo(typeof(OrderDto))]
public class Order
{
    public int Id { get; set; }

    [Ignore]
    public string InternalToken { get; set; } = "";
}
```

---

### `[MapConverter(typeof(TConverter))]`

Применяется к **свойству source-типа**, когда типы source и destination отличаются и нужна кастомная конвертация. Converter создаёт сам сгенерированный код, поэтому нужен публичный или internal конструктор без параметров (иначе [`IM0015`](diagnostics.ru.md#im0015--converter-нельзя-создать-в-сгенерированном-коде)); converter без состояния создаётся один раз и переиспользуется, см. [Экземпляры converter](#экземпляры-converter).

```csharp
public class PriceConverter : ITypeConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("F2");
}

[MapTo(typeof(InvoiceDto))]
public class Invoice
{
    [MapConverter(typeof(PriceConverter))]
    public decimal Total { get; set; }
}

public class InvoiceDto
{
    public string Total { get; set; } = "";
}
```

---

## Fluent API (MappingProfile)

### `CreateMap<TSource, TDest>()`

Регистрирует пару маппинга внутри конструктора `MappingProfile`.

```csharp
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<OrderEntity, OrderDto>();
    }
}
```

---

### `ForMember(dest => dest.Prop, opt => opt.MapFrom(...))`

Настраивает, как заполняется конкретное свойство destination. Лямбда переписывается в сгенерированный код с полными именами; что в ней можно использовать, см. [Лямбды в профилях](#лямбды-в-профилях).

```csharp
CreateMap<Employee, EmployeeDto>()
    .ForMember(d => d.FullName, opt => opt.MapFrom(s => s.FirstName + " " + s.LastName));
```

---

### `ForMember(dest => dest.Prop, opt => opt.Ignore())`

Исключает свойство destination из маппинга.

```csharp
CreateMap<User, UserDto>()
    .ForMember(d => d.PasswordHash, opt => opt.Ignore());
```

---

### `ForMember(dest => dest.Prop, opt => opt.UseConverter<TConverter>())`

Применяет type converter к одному свойству. Converter создаёт сгенерированный код, как для `[MapConverter]` (см. [Экземпляры converter](#экземпляры-converter)). Runtime-тест: `ConverterCacheTests.UseConverter_StatelessCachedAndStatefulNew`.

```csharp
CreateMap<Product, ProductDto>()
    .ForMember(d => d.Price, opt => opt.UseConverter<DecimalToStringConverter>());
```

---

### `ConvertUsing<TConverter>()`

Заменяет весь object initializer единственным вызовом converter. Не применяется в in-place перегрузке `source.MapToX(existing)`. Converter создаёт сгенерированный код, как для `[MapConverter]` (см. [Экземпляры converter](#экземпляры-converter)). Runtime-тест: `ConverterCacheTests.ConvertUsing_StatelessCachedAndStatefulNew`.

```csharp
public class FullOrderConverter : ITypeConverter<OrderEntity, OrderDto>
{
    public OrderDto Convert(OrderEntity source) => new() { ... };
}

CreateMap<OrderEntity, OrderDto>()
    .ConvertUsing<FullOrderConverter>();
```

---

### `ConvertUsing(src => ...)`

Lambda-based конвертация всего объекта. Тело выражения генератор вставляет дословно. Не применяется в in-place перегрузке. Покрыто только тестом сгенерированного кода.

```csharp
CreateMap<Point, PointDto>()
    .ConvertUsing(src => new PointDto { X = src.X, Y = src.Y });
```

---

### `When(src => condition)`

Добавляет guard: mapping выполняется только когда предикат истинен; иначе возвращается `default` (`null` для классов). Не применяется в in-place перегрузке.

```csharp
CreateMap<UserEntity, UserDto>()
    .When(src => src.IsActive);
```

---

## Лямбды в профилях

Относится ко всем лямбдам в конструкторе `MappingProfile`: `MapFrom`, `When`, `ConvertUsing(src => ...)`,
`BeforeMap`/`AfterMap` и `AddTransformer`.

Генератор не вызывает эти лямбды во время выполнения. Он переписывает их код в сгенерированный класс
`IronMapper.Generated.GeneratedMappers_{ИмяСборки}` с помощью семантической модели:

- параметры лямбды переименовываются в `source` / `destination` / `value` по символу, поэтому строковые литералы и имена
  членов, совпадающие с именем параметра, не затрагиваются (`s => s.s + "s"` превращается в `source.s + "s"`);
- пространства имён, типы, статические члены, константы и значения enum получают полные имена (`global::...`), поэтому
  директивы `using` из файла профиля в сгенерированном файле не нужны;
- вызовы методов расширения превращаются в статические вызовы класса, в котором они объявлены
  (`s.Title.ToSlug()` → `global::App.Text.SlugExtensions.ToSlug(source.Title)`).

**Можно использовать:** собственные параметры лямбды; статические методы, свойства, поля и константы любого типа,
доступного сгенерированному коду, в том числе `public` и `internal` статические члены самого профиля; значения enum;
методы расширения; локальные переменные и вложенные лямбды, объявленные внутри лямбды; `nameof(...)`.

**Нельзя использовать:**

| Что | Диагностика | Почему / как исправить |
|---|---|---|
| `private` или `protected` члены и типы, например `private static` метод профиля | [`IM0011`](diagnostics.ru.md#im0011--лямбда-профиля-использует-член-недоступный-сгенерированному-коду) | Код выполняется в сгенерированном классе — другом классе, где private и protected члены не видны. Сделайте член `internal`: internal-члены видны всему коду той же сборки, включая сгенерированный класс. |
| Члены экземпляра профиля, `this`, `base` | [`IM0012`](diagnostics.ru.md#im0012--лямбда-профиля-захватывает-состояние) | Во время маппинга экземпляра профиля нет. Сделайте член `static` (или `const`). |
| Локальные переменные, параметры и локальные функции конструктора | [`IM0012`](diagnostics.ru.md#im0012--лямбда-профиля-захватывает-состояние) | В сгенерированном коде их нет. Используйте константу или статический член либо вычисляйте значение из source. |

Если в лямбде есть такая проблема, этот маппинг (и его `ReverseMap`) не генерируется, а ошибка называет член и профиль;
остальные маппинги генерируются. Проблема в лямбде `AddTransformer` отключает этот преобразователь и выдаёт ошибку.

Замечания:
- `MapFrom` принимает дерево выражений (`Expression<Func<...>>`), поэтому сам C# не разрешает там `?.`, блочные тела
  и некоторые другие конструкции.
- Методы расширения, вызванные после `?.` (это возможно в `When`, хуках и преобразователях), выводятся как написаны.
  Они компилируются, только если их пространство имён подключено для всего проекта, например через `global using`.
- Лямбды намеренно не вызываются как делегаты: для этого пришлось бы создавать профиль во время выполнения через
  reflection, а вызов делегата нельзя перевести в SQL, что закрыло бы путь к будущему `ProjectTo`.

---

## Кастомные Converters (`ITypeConverter<TSource, TDest>`)

Реализуй `ITypeConverter<TSource, TDest>` для переиспользуемых конвертаций:

```csharp
public class DateToStringConverter : ITypeConverter<DateTime, string>
{
    public string Convert(DateTime source) => source.ToString("yyyy-MM-dd");
}
```

### Экземпляры converter

Сгенерированный код создаёт converters сам и никогда не получает их из DI. Как именно — зависит от converter:

- **Без состояния — один экземпляр на все вызовы.** Класс с публичным конструктором без параметров, без полей экземпляра
  во всей иерархии классов, кроме `object` (backing-поля автосвойств и field-like события считаются полями), без
  финализатора, с конструкторами без кода (неявный или с пустым телом, вызов `base()`/`this()` без аргументов).
  Экземпляр создаётся при первом использовании, один на тип converter в сборке (`ConverterCache<T>` в
  `GeneratedMappers.g.cs`); среда выполнения выполняет эту инициализацию один раз, даже если маппинг идёт из нескольких
  потоков одновременно. Статические поля состоянием не считаются.
- **Всё остальное — новый экземпляр на каждый вызов**, как до 1.1.4 включительно: converter с полями экземпляра (в самом классе или в
  базовом), конструктор с кодом или с аргументами в вызове `base(...)`, primary-конструктор с параметрами, структура,
  непубличный конструктор без параметров (`internal`, или есть только конструктор с необязательными параметрами) и любой
  класс, в иерархии которого есть тип из другой сборки (генератор не видит его закрытых полей и тел конструкторов).
- **Создать нельзя — маппинг не генерируется**, выдаётся
  [`IM0015`](diagnostics.ru.md#im0015--converter-нельзя-создать-в-сгенерированном-коде): нет конструктора без
  параметров (только конструкторы с параметрами, например с зависимостями), он `private`/`protected`, абстрактный класс,
  интерфейс, статический класс, открытый generic-тип или тип converter, недоступный сгенерированному классу. Остальные
  маппинги проекта генерируются.

Кэшированный converter не должен рассчитывать на то, что он новый: это один и тот же объект для всех маппингов и потоков.
Полей у него нет, поэтому это важно, только если он хранит состояние в другом месте, например в статическом поле. Тесты:
`tests/IronMapper.Tests/ConverterCacheTests.cs` (один экземпляр; новый экземпляр на вызов при поле в классе, в базовом
классе или автосвойстве; конструктор с кодом; первое использование из 16 потоков).

### `AddConverter<T>()` не влияет на маппинг

`IronMapperOptions.AddConverter<T>()` регистрирует тип converter в DI-контейнере как transient-сервис, и больше ничего не
делает: сгенерированный код не получает converters из DI (см. [Экземпляры converter](#экземпляры-converter)), поэтому
зарегистрированный так converter с его зависимостями **не** используется при маппинге. Converter, у которого все
конструкторы принимают параметры, получает `IM0015` независимо от регистрации. `AddProfilesFromAssembly` и
`AddProfile<T>()` регистрируют сгенерированные маппинги сборки для `IMapper`; сами профили в контейнер не добавляются
(до 1.1.4 включительно добавлялись как singleton).

```csharp
services.AddIronMapper(opt =>
{
    opt.AddProfilesFromAssembly(typeof(Program).Assembly);
    opt.AddConverter<DateToStringConverter>();
});
```
