# Обновление до IronMapper 2.0

English version: [upgrading-to-2.0.md](upgrading-to-2.0.md)

2.0.0 выходит после 1.1.4. Она меняет пять вещей, на которые мог опираться код, написанный для 1.1.x. В каждом разделе
ниже — что было, что стало и что сделать. Всё остальное в 2.0.0 — новые возможности и исправления
([CHANGELOG](../CHANGELOG.md), на английском).

Большинству проектов достаточно первого шага: собрать проект и, если сборка сообщает `IDE0005` на
`using IronMapper.Generated;`, убрать эти using.

| Изменение | Кого касается |
|---|---|
| [`global using IronMapper.Generated` из пакета](#1-global-using-ironmappergenerated-из-пакета) | Проекты с неявными using и строгим стилем кода в сборке (`IDE0005` как ошибка); файлы с написанным вручную методом расширения `MapTo*` с тем же именем |
| [Сгенерированный класс называется `GeneratedMappers_{ИмяСборки}`](#2-сгенерированный-класс-называется-generatedmappers_имясборки) | Код, который называет класс: `GeneratedMappers.MapToX(...)`, `using static`, reflection по имени, `RegisterIronMapperMappings()` в проектах на C# 8 |
| [`IMapper` не оборачивает исключения](#3-imapper-не-оборачивает-исключения) | Код, который ловит `TargetInvocationException` вокруг `IMapper` |
| [`AddIronMapper` не регистрирует профили](#4-addironmapper-не-регистрирует-профили-в-контейнере) | Код, который получает `MappingProfile` из DI-контейнера |
| [`MapCollection` маппит во время вызова](#5-mapcollection-маппит-во-время-вызова) | Код, который рассчитывает на ленивость `MapCollection` |

Вызовы сгенерированных методов расширения (`source.MapToX()`, `MapToXList()`, `MapToXArray()`, in-place перегрузка) и
`IMapper.Map` не меняются.

---

## 1. `global using IronMapper.Generated` из пакета

**Было.** Сгенерированные методы `MapTo*` лежат в пространстве имён `IronMapper.Generated`; каждому файлу, который их
вызывал, был нужен `using IronMapper.Generated;`, иначе `CS1061`.

**Стало.** Пакет добавляет `global using IronMapper.Generated;` в проекты C# с неявными using
(`<ImplicitUsings>enable</ImplicitUsings>`, по умолчанию в шаблонах .NET 6+), в том числе когда он приходит через
`IronMapper.Extensions.DI`. Проекты без неявных using и C# 9 и раньше это не затрагивает.

**Что можно увидеть.**

- Явный `using IronMapper.Generated;` теперь лишний. Компилятор не предупреждает, но строгий стиль кода в сборке —
  да: с `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` и `dotnet_diagnostic.IDE0005.severity = error` (или
  `warning` при `TreatWarningsAsErrors`) сборка падает:

  ```text
  Program.cs(7,1): error IDE0005: Using directive is unnecessary.
  ```

  (с русской локалью SDK — «Директива using не нужна»; в сборке `IDE0005` выдаётся только при включённом
  `GenerateDocumentationFile`, как для любого лишнего using). Этот случай собирается на упакованном пакете в
  `eng/verify-package.ps1`.
- Файл, который импортирует другой метод расширения с тем же именем и типом параметра, что у сгенерированного
  (например, написанный вручную `MapToUserDto` в переносимом коде), получает `CS0121` (неоднозначный вызов).

**Что сделать.** Одно из двух:

- Убрать явные using: «Удалить ненужные директивы using» в IDE или
  `dotnet format style --diagnostics IDE0005 --severity info`.
- Оставить их и отключить global using для проекта (или для всех проектов в `Directory.Build.props`):

  ```xml
  <PropertyGroup>
    <IronMapperImplicitUsings>false</IronMapperImplicitUsings>
  </PropertyGroup>
  ```

При `CS0121` удалите или переименуйте написанный вручную метод либо отключите global using.
См. [Global using](configuration.ru.md#global-using-для-ironmappergenerated).

## 2. Сгенерированный класс называется `GeneratedMappers_{ИмяСборки}`

**Было.** Каждый проект получал класс `IronMapper.Generated.GeneratedMappers`.

**Стало.** В имени класса есть имя сборки: `IronMapper.Generated.GeneratedMappers_{ИмяСборки}`, каждый символ, кроме
букв, цифр и `_`, заменён на `_`:

| Сборка | Класс |
|---|---|
| `Shop.Application` | `GeneratedMappers_Shop_Application` |
| `Shop.Infrastructure.EfCore` | `GeneratedMappers_Shop_Infrastructure_EfCore` |
| `My-App.Web` | `GeneratedMappers_My_App_Web` |

Имя сборки длиннее 64 символов сокращается до первых 55 символов, к ним добавляются `_` и 8 шестнадцатеричных цифр
хэша полного имени (одинаковый от сборки к сборке). Зачем: при одном имени во всех сборках инструменты, которые
различают типы по полному имени (например, ArchUnitNET), приписывали зависимости сгенерированного класса одной сборки
классу другой, и правило слоёв вида «Application не зависит от Infrastructure» падало; явное обращение к классу к тому
же давало `CS0436`, если был виден класс проекта-зависимости. Подробнее:
[Решения из нескольких проектов](configuration.ru.md#решения-из-нескольких-проектов).

**Что ломается.** Только код, который называет класс:

| 1.1.x | 2.0 |
|---|---|
| `GeneratedMappers.MapToUserDto(user)` | `user.MapToUserDto()` (или `GeneratedMappers_Shop_Application.MapToUserDto(user)`) |
| `users.Select(GeneratedMappers.MapToUserDto)` | `users.Select(u => u.MapToUserDto())` или `users.MapToUserDtoList()` |
| `using static IronMapper.Generated.GeneratedMappers;` | удалить; методам расширения нужно только пространство имён |
| `assembly.GetType("IronMapper.Generated.GeneratedMappers")` | новое имя или `MapperRegistry` (`IronMapper.Registration`) для маппинга по `Type` |
| C# 8: `GeneratedMappers.RegisterIronMapperMappings()` | `GeneratedMappers_{ИмяСборки}.RegisterIronMapperMappings()` |

Каждый из этих случаев, кроме поиска через reflection, сообщает компилятор (`CS0103` «Имя "GeneratedMappers" не
существует в текущем контексте», `CS0234` для `using static`).

**Что сделать.** Используйте синтаксис метода расширения: он не зависит от имени класса и не ломается при
переименовании сборки. Предупреждение `IM0013` (уточнённые имена методов) теперь называет и класс проекта.

## 3. `IMapper` не оборачивает исключения

**Было.** `IMapper` (`IronMapper.Extensions.DI`) вызывал сгенерированные методы через `MethodInfo.Invoke`: исключение
сгенерированного кода (`ArgumentNullException` для `null`-элемента вложенной коллекции), вашего converter или хука
`BeforeMap`/`AfterMap` приходило как `TargetInvocationException`, исходное — в `InnerException`.

**Стало.** `IMapper` вызывает сгенерированные методы через делегаты: исходное исключение доходит до вызывающего кода
как есть, со стеком сгенерированного метода.

**Что сделать.** Ловите исходный тип исключения вместо `TargetInvocationException` и уберите код, который
разворачивает `InnerException`:

```csharp
// 1.1.x
catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException inner) { ... }

// 2.0
catch (ArgumentNullException ex) { ... }
```

## 4. `AddIronMapper` не регистрирует профили в контейнере

**Было.** `AddIronMapper(assemblies)` просматривал сборки через `GetTypes()` и добавлял каждый наследник
`MappingProfile` в контейнер как singleton.

**Стало.** Профили читает генератор при сборке; `AddIronMapper` регистрирует `IMapper` и для каждой переданной сборки
(также через `AddProfile<T>()` и `AddProfilesFromAssembly`) запускает её сгенерированную регистрацию маппингов. Для
профилей в контейнер ничего не добавляется. `AddProfilesFromAssembly(null)` бросает `ArgumentNullException` сразу (раньше
падал позже).

**Что сделать.** Если ваш код получает профиль из контейнера (`GetRequiredService<OrderProfile>()`, профиль как параметр
конструктора), зарегистрируйте его сами: `services.AddSingleton<OrderProfile>();`. Обычно это не нужно: во время
выполнения профиль ничего не делает. Передайте в `AddIronMapper` все сборки с маппингами, особенно ту, которую
приложение больше никак не использует до первого маппинга (профили в `Application`, вызов из веб-проекта).

## 5. `MapCollection` маппит во время вызова

**Было.** `IMapper.MapCollection<TSource, TDest>(source)` возвращал ленивую последовательность: элементы маппились при
переборе результата, заново при каждом переборе, а `null`-источник, `null`-элемент или отсутствующий маппинг бросали
исключение только тогда.

**Стало.** Возвращает `List<TDest>` (тип результата — `IEnumerable<TDest>`, сигнатура та же), заполненный во время
вызова: источник перебирается один раз, исключения бросаются при вызове, повторный перебор результата не маппит заново,
изменения источника после вызова не видны.

**Что сделать.** Ничего, если вы не рассчитывали на ленивость (бесконечная или очень большая последовательность,
которую маппят по частям, или перебор результата после изменения источника). Ленивую последовательность запишите явно:

```csharp
IEnumerable<OrderDto> lazy = orders.Select(o => mapper.Map<Order, OrderDto>(o));
```

---

## Другие изменения, которые можно заметить

Они не требуют правок кода, но могут проявиться после обновления:

- **Покрытие кода.** Сгенерированный класс помечен `[GeneratedCode]`. Инструмент покрытия, который его исключает
  (coverlet с `ExcludeByAttribute=GeneratedCodeAttribute`), не включает сгенерированные маппинги в отчёт, и процент
  меняется. Чтобы измерять их, как раньше:
  `<IronMapperGeneratedCodeAttribute>false</IronMapperGeneratedCodeAttribute>`
  ([подробнее](configuration.ru.md#атрибуты-сгенерированного-класса)).
- **Новые ошибки вместо молчаливых пропусков или ошибок в сгенерированных файлах**: `IM0003` для `[MapTo]`/`[MapFrom]`
  с открытым generic-типом или массивом (раньше маппинг молча пропадал), `IM0015` для converter, который
  сгенерированный код не может создать (раньше — `CS7036`/`CS1729`/`CS0122` внутри файла `.g.cs`).
  См. [диагностики](diagnostics.ru.md).
- **У диагностик есть место в исходном коде**: к ним теперь применяются уровни из `.editorconfig`
  (`dotnet_diagnostic.IMxxxx.severity`), так что давно заданное правило может начать действовать.
- Converter без состояния создаётся один раз и переиспользуется (раньше — на каждый вызов); это видно только через
  состояние вне converter ([Экземпляры converter](configuration.ru.md#экземпляры-converter)).
