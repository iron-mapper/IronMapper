# Диагностики компилятора

> 🌐 **[English version](diagnostics.md)**

Source generator IronMapper выдаёт диагностики с идентификаторами `IM0001`–`IM0016`. Они объявлены в
[`DiagnosticDescriptors.cs`](../src/IronMapper.Generator/Diagnostics/DiagnosticDescriptors.cs).

**Текущее состояние** (2.0.0):

| ID | Уровень | Название | Выдаётся? |
|---|---|---|---|
| IM0001 | Warning | Несопоставленное свойство destination | Да — `[MapTo]`/`[MapFrom]`; профили с `IronMapperReportUnmappedInProfiles` |
| IM0002 | Error | Несовместимые типы свойств | **Пока не выдаётся** |
| IM0003 | Error | Тип в `[MapTo]`/`[MapFrom]` нельзя смапить | Да — открытые generic-типы и массивы; неразрешённый тип сообщает только компилятор |
| IM0004 | Error | Обнаружен циклический маппинг | **Пока не выдаётся** |
| IM0005 | Warning | Пустой MappingProfile | Да |
| IM0006 | Error | Свойство из `[MapProperty]` не найдено в destination | Да |
| IM0007 | Warning | Повторное определение маппинга | Да — атрибуты и профили |
| IM0008 | Error | Destination — абстрактный тип или интерфейс | Да — атрибуты и профили |
| IM0009 | Warning | Параметр конструктора record не сопоставлен | Да — только `[MapTo]`/`[MapFrom]` |
| IM0010 | Error | Тип маппинга недоступен из сгенерированного кода | Да — атрибуты и профили |
| IM0011 | Error | Лямбда профиля использует член, недоступный сгенерированному коду | Да — профили |
| IM0012 | Error | Лямбда профиля захватывает состояние | Да — профили |
| IM0013 | Warning | Имя метода маппинга уточнено из-за коллизии | Да — атрибуты и профили |
| IM0014 | Error | Коллизию имён методов маппинга нельзя устранить | Да — атрибуты и профили |
| IM0015 | Error | Converter нельзя создать в сгенерированном коде | Да — `[MapConverter]`, `UseConverter`, `ConvertUsing<T>` |
| IM0016 | Warning | Аргумент `GenerateMethods` не константа | Да — профили |

Каждая диагностика указывает на место в вашем коде (**Куда указывает** ниже): IDE подчёркивает его, в выводе сборки
видны файл и строка. Уровень задаётся стандартно, в `.editorconfig` (или `.globalconfig`):

```ini
[*.cs]
dotnet_diagnostic.IM0001.severity = error   # или warning, suggestion, none
```

`<NoWarn>` и `<WarningsAsErrors>` в файле проекта тоже работают. До 1.1.4 включительно все диагностики выдавались без
места в коде (только в списке ошибок), поэтому ни уровни `.editorconfig` для файлов, ни code fix'ы к ним не применялись.

Если для проблемы нет работающей диагностики (например, несовпадение типов свойств), сборка падает
с обычной ошибкой C# (`CS0029`, `CS7036`, …) внутри сгенерированного файла `*.g.cs`.

Тип, который не разрешается (`typeof(Missing)` в `[MapTo]`/`[MapFrom]`, аргумент типа `CreateMap`, тип converter),
сообщает компилятор в вашем коде (`CS0246`, `CS0234`, `CS0122`, …). Генератор пропускает этот маппинг, поэтому второй
ошибки внутри сгенерированного файла нет; остальные маппинги генерируются.

---

## IM0001 — Несопоставленное свойство destination

**Уровень:** Warning

**Куда указывает:** свойство destination (его имя), если оно объявлено в вашем проекте: code fix ставит туда `[Ignore]`. Если тип destination из другой сборки — атрибут `[MapTo]`/`[MapFrom]`. В маппинге профиля — вызов `CreateMap` (code fix добавляет `.ForMember(dest => dest.X, opt => opt.Ignore())`).

**Сообщение:** `Property '{0}' on destination type '{1}' has no corresponding source property in '{2}' and will be left at its default value`

**Причина:** у публичного свойства destination с сеттером нет source-свойства с тем же именем
(без учёта регистра) и нет `[MapProperty]`, указывающего на него.

**Когда выдаётся:** для маппингов `[MapTo]` / `[MapFrom]`. Для маппингов `MappingProfile` — только с
`<IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>` (по умолчанию выключено, в следующей мажорной
версии планируется включить; см. [Несопоставленные свойства в профилях](configuration.ru.md#несопоставленные-свойства-в-профилях)).
В профиле свойство destination считается сопоставленным, если его настраивает `ForMember` (`MapFrom`, `Ignore`,
`UseConverter`), если его даёт `IncludeMembers` или source-свойство с тем же именем, или если на нём `[Ignore]`. Маппинг с
`ConvertUsing` IM0001 не получает (converter создаёт весь объект). Обратный маппинг `ReverseMap()` не проверяется: он
сопоставляет только по именам, и `ForMember` к нему не применяется (AutoMapper обратные маппинги тоже не проверяет — у них
`MemberList.None`); чтобы он проверялся, объявите его через `CreateMap`.

**Как исправить:**
- Добавьте соответствующее свойство в source-тип.
- Используйте `[MapProperty("DestPropertyName")]` на source-свойстве.
- Поставьте `[Ignore]` на свойство destination, чтобы подавить предупреждение (маппинги атрибутами).
- В профиле: `.ForMember(dest => dest.X, opt => opt.Ignore())` или `opt.MapFrom(...)`.
- Code fix'ы: «Add [Ignore] to '...'» (маппинги атрибутами; при необходимости добавляет `using IronMapper.Attributes;`,
  а для параметра позиционного record — `[property: Ignore]`) и «Ignore '...' in this mapping (ForMember)» (профили).
  Оба поддерживают Fix All; покрыты `tests/IronMapper.CodeFixes.Tests`.

---

## IM0002 — Несовместимые типы свойств

**Уровень:** Error · **Пока не выдаётся**

**Сообщение (объявлено):** `Property '{0}': source type '{1}' cannot be assigned to destination type '{2}'. Add [MapConverter] or implement ITypeConverter<{1}, {2}>.`

**Текущее поведение:** генератор не проверяет типы свойств. Несовпадение (в том числе вложенный объект
другого типа, например `Address` → `AddressDto`) приводит к ошибке компиляции C#, например `CS0029`,
в сгенерированном файле.

**Как исправить:** используйте `[MapConverter]`, `opt.UseConverter<T>()` или `opt.MapFrom(...)` для этого свойства.

---

## IM0003 — Тип в [MapTo]/[MapFrom] нельзя смапить

**Уровень:** Error

**Куда указывает:** атрибут `[MapTo]`/`[MapFrom]`.

**Сообщение:** `Mapping for [{0}(typeof({1}))] on '{2}' was not generated: {3}`

**Причина:** тип в `typeof(...)` компилируется, но смапить его нельзя: открытый generic-тип (`typeof(Box<>)`) или массив
(`typeof(Item[])`). Компилятор принимает оба; до этой диагностики маппинг молча пропадал, и первой ошибкой была `CS1061`
на вызове несуществующего метода `MapTo...`.

**Как исправить:** используйте класс, структуру или record; закройте generic-тип (`typeof(Box<int>)`). Для коллекций
маппьте тип элемента: `MapTo{Name}List`/`MapTo{Name}Array` генерируются для каждого маппинга. Остальные маппинги
генерируются.

**Не выдаётся для типа, который не разрешается** (`typeof(Missing)`, недоступный или неоднозначный тип). Его сообщает
компилятор на `typeof` (`CS0246`, `CS0234`, `CS0122`, `CS0104`), а генератор пропускает этот маппинг, так что это
единственная ошибка, которую вы видите. IM0003 только повторила бы её (и, пока у диагностик нет места в коде, без
указания строки). До 1.1.4 включительно генератор всё равно создавал маппинг, что добавляло ошибки `CS0400`/`CS0246`
внутри сгенерированного файла.

---

## IM0004 — Обнаружен циклический маппинг

**Уровень:** Error · **Пока не выдаётся**

**Сообщение (объявлено):** `Cyclic mapping detected: '{0}' maps to '{1}' which directly or indirectly maps back to '{0}'`

**Текущее поведение:** обнаружение циклов не реализовано.

---

## IM0005 — Пустой MappingProfile

**Уровень:** Warning

**Куда указывает:** имя класса профиля.

**Сообщение:** `MappingProfile '{0}' has no mappings defined`

**Причина:** в конструкторе (конструкторах) класса-наследника `MappingProfile` нет ни одной цепочки
`CreateMap<TSource, TDest>()`.

**Как исправить:** добавьте хотя бы один вызов `CreateMap<TSource, TDest>()` или удалите пустой профиль.

---

## IM0006 — Свойство из `[MapProperty]` не найдено в destination

**Уровень:** Error

**Куда указывает:** атрибут `[MapProperty]` (code fix заменяет имя на ближайшее свойство destination).

**Сообщение:** `[MapProperty] destination '{0}' does not exist in type '{1}'`

**Причина:** имя, переданное в `[MapProperty]`, не совпадает ни с одним публичным свойством destination
с сеттером. Сравнение **без учёта регистра**.

**Как исправить:** исправьте имя или добавьте свойство в destination-тип. Code fix «Did you mean '...'?» предлагает
свойства destination смапленного типа на расстоянии Левенштейна не больше 2 (без учёта регистра); покрыт
`tests/IronMapper.CodeFixes.Tests`.

---

## IM0007 — Повторное определение маппинга

**Уровень:** Warning

**Куда указывает:** определение, которое не генерируется (его и нужно удалить); остальные определения — дополнительные места.

**Сообщение:** `Mapping from '{0}' to '{1}' is defined more than once: {2}. Only the definition from {3} is generated; remove the others.`

**Причина:** одна и та же пара source/destination задана несколько раз: `[MapTo]` и `CreateMap`, `[MapTo]` и
`[MapFrom]`, два профиля или `CreateMap` и `ReverseMap()` другого маппинга.

**Поведение:** пара генерируется один раз. Приоритет: `CreateMap` в профиле, затем `ReverseMap()`, затем `[MapTo]`,
затем `[MapFrom]`; среди определений одного вида — профиль (или тип), чьё полное имя идёт первым, затем первый вызов
в конструкторе. Все остальные маппинги проекта генерируются. См.
[Имена методов и коллизии](configuration.ru.md#одна-пара-типов-задана-дважды).

До исправления повторная пара приводила к сбою генератора с `CS8785` («HintName ... must be unique»), и код маппинга
для проекта не генерировался вообще.

**Как исправить:** оставьте одно определение.

---

## IM0008 — Destination — абстрактный тип или интерфейс

**Уровень:** Error

**Куда указывает:** атрибут `[MapTo]`/`[MapFrom]` или вызов `CreateMap`.

**Сообщение:** `Type '{0}' is abstract or an interface and cannot be instantiated as a mapping destination`

**Причина:** destination-тип в `[MapTo]`, `[MapFrom]` или `CreateMap` абстрактный или является интерфейсом.

**Как исправить:** используйте конкретный тип. Для профилей `ConvertUsing` не помогает обойти ошибку:
генератор выдаёт IM0008 до разбора остальной конфигурации маппинга.

---

## IM0009 — Параметр конструктора record не сопоставлен

**Уровень:** Warning

**Куда указывает:** параметр конструктора (его имя), если record объявлен в вашем проекте; иначе атрибут.

**Сообщение:** `Constructor parameter '{0}' in record '{1}' has no matching source property`

**Причина:** у record-типа destination есть явно объявленный конструктор (включая primary constructor),
параметру которого не соответствует source-свойство с тем же именем (без учёта регистра).

**Когда выдаётся:** только для маппингов `[MapTo]` / `[MapFrom]`.

**Важно:** генератор создаёт destination через object initializer, а не через вызов конструктора.
У позиционного record вроде `record PointDto(double X, double Y)` нет конструктора без параметров, поэтому
сгенерированный код не компилируется (`CS7036`), даже если это предупреждение не выдано. Используйте
в качестве destination record со свойствами `init`.

---

## IM0010 — Тип маппинга недоступен из сгенерированного кода

**Уровень:** Error

**Куда указывает:** атрибут `[MapTo]`/`[MapFrom]` или вызов `CreateMap`.

**Сообщение:** `Mapping from '{0}' to '{1}' was not generated: type '{2}' is {3}, so the generated class in namespace IronMapper.Generated cannot access it. Make the type and all types that contain it public or internal.`

**Причина:** тип source или destination, один из содержащих его типов или один из аргументов обобщённого типа —
вложенный тип с доступом `private`, `protected` или `private protected`. Сгенерированные методы находятся в классе
верхнего уровня `IronMapper.Generated.GeneratedMappers_{ИмяСборки}` и не могут ссылаться на такие типы.

**Когда выдаётся:** для маппингов `[MapTo]` / `[MapFrom]` и `MappingProfile`. Пропускается только этот маппинг;
остальные маппинги проекта генерируются.

**Как исправить:** сделайте тип (и все типы, которые его содержат) `public` или `internal`. Internal-типы
поддерживаются; сгенерированные методы тогда `internal` (см. [Доступность сгенерированных методов](configuration.ru.md#доступность-сгенерированных-методов)).

---

## IM0011 — Лямбда профиля использует член, недоступный сгенерированному коду

**Уровень:** Error

**Куда указывает:** член или тип в лямбде.

**Сообщение:** `A lambda in MappingProfile '{0}' uses '{1}', but '{2}' is {3}. Profile lambdas are copied into the generated class in namespace IronMapper.Generated, which cannot access it. Make '{2}' internal (or public). The mapping was not generated.`

**Причина:** лямбда в `MapFrom`, `When`, `ConvertUsing`, `BeforeMap`/`AfterMap` или `AddTransformer` ссылается на
член или тип с доступом `private`, `protected` или `private protected` (или на член такого типа) — обычно это
`private static` метод или константа профиля. Лямбды переписываются в сгенерированный класс, а не вызываются у профиля,
поэтому такие члены там не видны.

**Как исправить:** сделайте член (или содержащий его тип) `internal`. См. [Лямбды в профилях](configuration.ru.md#лямбды-в-профилях).

Этот маппинг не генерируется (для `AddTransformer` отключается преобразователь); остальные генерируются.

---

## IM0012 — Лямбда профиля захватывает состояние

**Уровень:** Error

**Куда указывает:** член, переменная или `this`/`base` в лямбде.

**Сообщение:** `A lambda in MappingProfile '{0}' uses {1} '{2}'. Profile lambdas are copied into generated code that runs without a profile instance, so they can only use their own parameters, static members and constants. Make it static or const, or compute the value from the source object. The mapping was not generated.`

**Причина:** лямбда использует поле, свойство или метод экземпляра профиля, `this`/`base`, либо локальную переменную,
параметр или локальную функцию конструктора. Ничего из этого нет, когда выполняется сгенерированный код.

**Как исправить:** сделайте член `static` (или `const`) либо вычисляйте значение из source.
См. [Лямбды в профилях](configuration.ru.md#лямбды-в-профилях).

Этот маппинг не генерируется (для `AddTransformer` отключается преобразователь); остальные генерируются.

---

## IM0013 — Имя метода маппинга уточнено из-за коллизии

**Уровень:** Warning

**Куда указывает:** определение маппинга, получившего уточнённое имя (атрибут, вызов `CreateMap` или `ReverseMap()`).

**Сообщение:** `Several mappings from '{0}' have a destination type named '{1}', so the mapping to '{2}' is generated as '{3}' instead of '{4}'. Call it as source.{3}() or IronMapper.Generated.{5}.{3}(source); the collection methods are {3}List and {3}Array.`

**Причина:** один source-тип маппится на несколько destination-типов с одинаковым простым именем (разные пространства
имён, вложенность в разные типы или разные аргументы типа). `MapTo{Name}` был бы объявлен дважды (`CS0111`), поэтому
такие маппинги получают уточнённые имена, например `MapToAdminUserDto` и `MapToPublicUserDto` или `MapToBoxOfInt32`.
Правило описано в [Имена методов и коллизии](configuration.ru.md#один-source-тип-несколько-destination-типов-с-одинаковым-простым-именем).

**Как исправить:** вызывайте метод по имени из сообщения — как метод расширения или на сгенерированном классе проекта
(`{5}` в сообщении, `GeneratedMappers_{ИмяСборки}`), — или переименуйте один из destination-типов, чтобы вернуть
`MapTo{Name}`. Если уточнённые имена вас устраивают, отключите предупреждение: `<NoWarn>$(NoWarn);IM0013</NoWarn>`.

---

## IM0014 — Коллизию имён методов маппинга нельзя устранить

**Уровень:** Error

**Куда указывает:** определение маппинга, который не сгенерирован.

**Сообщение:** `Mapping from '{0}' to '{1}' was not generated: another mapping from '{0}' has a destination type named '{2}', and every qualified method name (for example '{3}') is already used by another mapping. Rename one of the destination types.`

**Причина:** все уточнённые имена из IM0013 заняты другими маппингами того же source-типа (например, `N1.Dto` и
`N2.Dto` стали бы `MapToN1Dto`/`MapToN2Dto`, но типы `N1Dto` и `N2Dto` тоже маппятся).

**Как исправить:** переименуйте один из destination-типов. Пропускаются только маппинги конфликтующей группы,
остальные генерируются.

---

## IM0015 — Converter нельзя создать в сгенерированном коде

**Уровень:** Error

**Куда указывает:** тип converter: тип в `typeof(...)` у `[MapConverter]` или аргумент типа `UseConverter<T>()`/`ConvertUsing<T>()`.

**Сообщение:** `Mapping from '{0}' to '{1}' was not generated: converter '{2}' ({3}) cannot be created with 'new {2}()' because {4}`

**Причина:** сгенерированный код создаёт converters сам (из DI их не получает), а converter из `[MapConverter]`,
`UseConverter<T>()` или `ConvertUsing<T>()` так создать нельзя:

- у него нет конструктора без параметров (например, только конструктор с зависимостями или primary-конструктор
  с параметрами);
- его конструктор без параметров `private` или `protected`;
- это абстрактный класс, интерфейс, статический класс или открытый generic-тип (`typeof(Converter<>)`);
- сгенерированный класс `IronMapper.Generated.GeneratedMappers_{ИмяСборки}` не имеет к нему доступа (вложенный `private`-тип).

До этой диагностики сборка падала с `CS7036`, `CS1729`, `CS0122` или `CS0144` внутри сгенерированного файла.

**Как исправить:** дайте converter публичный или internal конструктор без параметров; это должен быть неабстрактный
класс или структура, доступные сгенерированному классу. Converter, которому нужны сервисы из DI, пока использовать
нельзя: `AddConverter<T>()` не меняет способ создания converters (см.
[Экземпляры converter](configuration.ru.md#экземпляры-converter)). Пропускается только этот маппинг, остальные
генерируются. Свойство с `[Ignore]` не использует свой converter, поэтому такой converter не проверяется.

---

## IM0016 — Аргумент GenerateMethods не константа

**Уровень:** Warning

**Куда указывает:** аргумент.

**Сообщение:** `Argument '{0}' of GenerateMethods in MappingProfile '{1}' is not a constant ('{2}'); the generator reads it at compile time, so the value for the assembly applies. Use true, false or null.`

**Причина:** аргумент `GenerateMethods(inPlace, list, array)` в профиле — переменная, вызов метода или другое выражение,
которое не является константой времени компиляции. Профили читает генератор, а не выполняет программа, поэтому при
генерации кода значение неизвестно.

**Поведение:** для этой перегрузки действует значение проекта (`IronMapperGenerateInPlace`/`List`/`Array`); маппинг и
остальные аргументы генерируются как написано. См. [Генерируемые перегрузки](configuration.ru.md#генерируемые-перегрузки-и-покрытие-кода).

**Как исправить:** передайте `true`, `false` или `null` (или `const`).
