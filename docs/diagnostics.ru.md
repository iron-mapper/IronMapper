# Диагностики компилятора

> 🌐 **[English version](diagnostics.md)**

Source generator IronMapper выдаёт диагностики с идентификаторами `IM0001`–`IM0021`. Они объявлены в
[`DiagnosticDescriptors.cs`](../src/IronMapper.Generator/Diagnostics/DiagnosticDescriptors.cs).

**Текущее состояние** (`main`, не выпущенная 2.1.0):

| ID | Уровень | Название | Выдаётся? |
|---|---|---|---|
| IM0001 | Warning | Несопоставленное свойство destination | Да — `[MapTo]`/`[MapFrom]`; профили с `IronMapperReportUnmappedInProfiles` |
| IM0002 | Error | Несовместимые типы свойств или нет маппинга | Да — атрибуты и профили (начиная с 2.1.0) |
| IM0003 | Error | Тип в `[MapTo]`/`[MapFrom]` нельзя смапить | Да — открытые generic-типы и массивы; неразрешённый тип сообщает только компилятор |
| IM0004 | Warning | Цикл маппингов через вложенный объект | Да — циклы через автоматически смапленный вложенный объект (начиная с 2.1.0) |
| IM0005 | Warning | Пустой MappingProfile | Да |
| IM0006 | Error | Свойство из `[MapProperty]` не найдено в destination | Да |
| IM0007 | Warning | Повторное определение маппинга | Да — атрибуты и профили |
| IM0008 | Error | Destination — абстрактный тип или интерфейс | Да — атрибуты и профили |
| IM0009 | Error | Параметр конструктора не сопоставлен | Да — атрибуты и профили (ошибка с 2.1.0; до 2.0.0 — предупреждение для record в `[MapTo]`/`[MapFrom]`) |
| IM0010 | Error | Тип маппинга недоступен из сгенерированного кода | Да — атрибуты и профили |
| IM0011 | Error | Лямбда профиля использует член, недоступный сгенерированному коду | Да — профили |
| IM0012 | Error | Лямбда профиля захватывает состояние | Да — профили |
| IM0013 | Warning | Имя метода маппинга уточнено из-за коллизии | Да — атрибуты и профили |
| IM0014 | Error | Коллизию имён методов маппинга нельзя устранить | Да — атрибуты и профили |
| IM0015 | Error | Converter нельзя создать в сгенерированном коде | Да — `[MapConverter]`, `UseConverter`, `ConvertUsing<T>` |
| IM0016 | Warning | Аргумент `GenerateMethods` не константа | Да — профили |
| IM0017 | Error | Конструктор destination неоднозначен | Да — атрибуты и профили (с 2.1.0) |
| IM0018 | Error | У destination нет доступного конструктора | Да — атрибуты и профили (с 2.1.0) |
| IM0019 | Error | `ForCtorParam` не называет параметр | Да — профили (с 2.1.0) |
| IM0020 | Error | Фабрику `ConstructWith` нельзя использовать | Да — профили (с 2.1.0) |
| IM0021 | Warning | init-свойство нельзя задать после фабрики | Да — профили (с 2.1.0) |

Каждая диагностика указывает на место в вашем коде (**Куда указывает** ниже): IDE подчёркивает его, в выводе сборки
видны файл и строка. Уровень задаётся стандартно, в `.editorconfig` (или `.globalconfig`):

```ini
[*.cs]
dotnet_diagnostic.IM0001.severity = error   # или warning, suggestion, none
```

`<NoWarn>` и `<WarningsAsErrors>` в файле проекта тоже работают. До 1.1.4 включительно все диагностики выдавались без
места в коде (только в списке ошибок), поэтому ни уровни `.editorconfig` для файлов, ни code fix'ы к ним не применялись.

Если для проблемы нет работающей диагностики, сборка падает с обычной ошибкой C# внутри сгенерированного файла
`*.g.cs`. До 2.0.0 так было с позиционным record в качестве destination (`CS7036`); с 2.1.0 он создаётся своим
конструктором ([Конструкторы и позиционные record](advanced.ru.md#конструкторы-и-позиционные-record)), а если
конструктор вызвать нельзя — `IM0009`, `IM0017` или `IM0018`; фабрика `ConstructWith`, которую сгенерированный код не
может вызвать, — `IM0020`.

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

## IM0002 — Несовместимые типы свойств или нет маппинга

**Уровень:** Error (начиная с 2.1.0; до 2.0.0 включительно была объявлена, но не выдавалась)

**Куда указывает:** на свойство destination (`[MapTo]`/`[MapFrom]`; на атрибут, если тип destination в другой сборке),
на вызов `CreateMap` (профиль, сопоставление по имени или `IncludeMembers`), на вызов `ReverseMap()` или на выражение
`MapFrom`.

**Сообщение:** `Property '{0}' of '{1}' is not mapped: {2}` — в `{2}` названы типы source и destination и что добавить,
например:

- `'Order.Count' is 'int?', which cannot be assigned to 'int'. Add [MapConverter(typeof(...))] (an ITypeConverter<int?, int>) to the source property, map it in a profile with ForMember(..., o => o.MapFrom(...)), or mark the destination property [Ignore].`
- `the elements of 'Order.Lines' are 'App.Line' and the elements of the destination are 'App.LineDto', and there is no mapping from 'App.Line' to 'App.LineDto'. Add [MapTo(typeof(App.LineDto))] to 'App.Line', [MapFrom(typeof(App.Line))] to 'App.LineDto', or CreateMap<App.Line, App.LineDto>() in a profile.`
- `'Customer.Address' is 'App.Address' and the destination is 'App.AddressDto', and there is no mapping from 'App.Address' to 'App.AddressDto'. Add [MapTo(typeof(App.AddressDto))] to 'App.Address', [MapFrom(typeof(App.Address))] to 'App.AddressDto', or CreateMap<App.Address, App.AddressDto>() in a profile.`
- `'Customer.Address' is 'App.Address?', which can be null, and the destination is 'App.AddressDto', which cannot. Declare the destination property nullable or the source property non-nullable, or mark the destination property [Ignore].`
- `the MapFrom expression is 'int?', which cannot be assigned to 'int'. …`

**Причина:** сгенерированное присваивание `destination.X = source.X` не скомпилировалось бы:

- у типов нет неявного преобразования (`int?` → `int`, `long` → `int`, `string` → `Guid`, enum → другой enum,
  `List<string>` → `string[]`); проверка такая же, как у компилятора (`Compilation.ClassifyConversion`), поэтому всё,
  что преобразуется неявно, принимается (`int` → `long`, `int` → `int?`, производный тип → базовый, пользовательские
  неявные операторы, `dynamic`);
- вложенный объект другого типа (`Address` → `AddressDto`), для пары типов которого нет маппинга: ни в проекте (в том
  числе через базовый тип или интерфейс типа source), ни доступного метода расширения `MapTo…` проекта-зависимости или
  написанного вручную. Если маппинг есть, вложенный объект маппится им (начиная с 2.1.0,
  [Вложенные объекты](advanced.ru.md#вложенные-объекты));
- вложенный объект, объявленный в source допускающим `null` (`Address?`), а в destination — нет (`AddressDto`):
  сгенерированный код не превращает `null` в `null!`;
- коллекция, у которой типы элементов разные и их не маппит ни один маппинг: ни в проекте (в том числе через базовый
  тип или интерфейс элемента источника), ни доступный метод расширения `MapTo…` проекта-зависимости или написанный
  вручную, к которому привязывается сгенерированный вызов;
- выражение `MapFrom` другого типа, чем свойство destination.

До 2.0.0 включительно это давало ошибку внутри сгенерированного файла (`CS0029`, `CS0266`, `CS1061`). Теперь свойство не
генерируется (у destination остаётся значение по умолчанию), остальной маппинг генерируется, а сборка падает с `IM0002`
в вашем коде. Маппинг с `ConvertUsing` тоже проверяется: `ConvertUsing` заменяет `MapToX(source)`, но in-place
перегрузка по-прежнему присваивает свойства.

**Как исправить:** преобразуйте значение (`[MapConverter]`, `opt.UseConverter<T>()`, `MapFrom` с преобразованием),
добавьте недостающий маппинг или проигнорируйте свойство (`[Ignore]`, `opt.Ignore()`). Для `ReverseMap()` объявите
обратный маппинг через `CreateMap` и настройте свойство там.

**Code fix** (вложенный объект или элемент коллекции без маппинга): добавляет маппинг пары —
`[MapTo(typeof(AddressDto))]` на тип source маппинга через атрибуты (`[MapFrom(typeof(Address))]` на тип destination,
если типа source нет в вашем коде), `CreateMap<Address, AddressDto>();` после оператора с вызовом
`CreateMap`/`ReverseMap()` в профиле. Для остальных случаев code fix нет.

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

## IM0004 — Цикл маппингов через вложенный объект

**Уровень:** Warning (начиная с 2.1.0; до 2.0.0 включительно была объявлена как ошибка, но не выдавалась)

**Куда указывает:** на свойство destination вложенного объекта (`[MapTo]`/`[MapFrom]`) или на вызов
`CreateMap`/`ReverseMap()` (профиль). Выдаётся на каждом автоматически смапленном вложенном объекте цикла.

**Сообщение:** `Property '{0}' of '{1}' is mapped automatically and closes a cycle of mappings: {2}. The mapping recurses without end only if the objects form a cycle at run time; break the cycle with Ignore or MapFrom on a property of the chain, or suppress IM0004 for models that are always finite (#pragma warning disable IM0004, or dotnet_diagnostic.IM0004.severity = none in .editorconfig).`
— `{2}` — цепочка маппингов, например
`App.Order → App.OrderDto, Customer: App.Customer → App.CustomerDto, Orders (elements): App.Order → App.OrderDto`.

**Причина:** автоматически смапленный вложенный объект ([Вложенные объекты](advanced.ru.md#вложенные-объекты))
приводит обратно к своему маппингу — через другие вложенные объекты или элементы коллекций: `Employee.Manager` типа
`Employee`, `Order.Customer.Orders`. Цикл типов сам по себе не бесконечная рекурсия: цепочка руководителей заканчивается
на `null` и маппится. Рекурсия бесконечна (и падает с `StackOverflowException`), только если цикл есть в данных (заказ, у
покупателя которого в списке этот же заказ). Сгенерированный код не отслеживает уже смапленные объекты.

**Не выдаётся:** для циклов только через коллекции (дерево вроде `Category.Children`, которое работает с 2.0.0) и для
циклов, разорванных явно настроенным свойством (`MapFrom`, `Ignore`, converter) или маппингом с `ConvertUsing` (он
вызывает converter, а не маппинги своих свойств). Ни одна сборка, проходящая на 2.0.0, IM0004 не получает: вложенный
объект другого типа там не компилировался.

**Как исправить:** разорвите цикл через `Ignore` или `MapFrom` на свойстве цепочки
(`ForMember(d => d.Customer, o => o.Ignore())`, `[Ignore]`) или подавите предупреждение там, где данные всегда конечны:
`#pragma warning disable IM0004` вокруг свойства (или вызова `CreateMap`), `dotnet_diagnostic.IM0004.severity = none` в
`.editorconfig` или `<NoWarn>`.

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

## IM0009 — Параметр конструктора не сопоставлен

**Уровень:** Error (с 2.1.0; до 2.0.0 — Warning, см. ниже)

**Куда указывает:** в маппингах через атрибуты — на параметр конструктора (его имя), если тип destination объявлен в
вашем проекте, иначе на атрибут; в профилях — на вызов `CreateMap` или `ReverseMap()`.

**Сообщение:** `Mapping to '{1}' was not generated: parameter '{0}' of {3} gets no value: {2}` (`{3}` — `the constructor`,
`factory 'Team.Restore'` или `the lambda of ConstructWith`)

**Причина:** у destination нет доступного конструктора без параметров, поэтому он создаётся конструктором
([Конструкторы и позиционные record](advanced.ru.md#конструкторы-и-позиционные-record)), и ни один доступный
конструктор нельзя вызвать: параметр ближайшего (больше всего параметров из source) не получает значения. Или такой
параметр у фабрики `ConstructWith` ([Фабрики](advanced.ru.md#фабрики-constructwith)). Последняя часть сообщения
объясняет почему:

- в source нет свойства с именем параметра (без учёта регистра), и у параметра нет значения по умолчанию;
- значение source нельзя передать (причины [`IM0002`](#im0002--несовместимые-типы-свойств-или-нет-маппинга):
  нет неявного преобразования, nullable вложенный объект для ненулевого параметра); значение по умолчанию тогда не помогает;
- параметр проигнорирован (`Ignore()` одноимённого свойства, `[Ignore]`), и у него нет значения по умолчанию.

**Поведение:** маппинг не генерируется; остальные маппинги проекта генерируются.

**Как исправить:** добавьте свойство в source или сопоставьте с параметром другое (`[MapProperty("имяПараметра")]` на
свойстве source; в профиле — `ForMember(d => d.Свойство, o => o.MapFrom(...))` для одноимённого свойства или
`ForCtorParam("имяПараметра", o => o.MapFrom(...))` для параметра без него), преобразуйте значение (converter,
`MapFrom`), дайте параметру значение по умолчанию или выберите другой конструктор или фабрику через `ConstructWith`.

**До 2.0.0** `IM0009` было предупреждением для record в маппингах через атрибуты и выдавалось для каждого объявленного
конструктора, хотя сгенерированный object initializer всё равно не компилировался, если у record не было конструктора
без параметров (`CS7036`). Record с конструктором без параметров создаётся через object initializer, и параметры его
других конструкторов больше не проверяются.

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

---

## IM0017 — Конструктор destination неоднозначен

**Уровень:** Error (с 2.1.0)

**Куда указывает:** на атрибут `[MapTo]`/`[MapFrom]` или вызов `CreateMap`.

**Сообщение:** `Mapping from '{0}' to '{1}' was not generated: the constructors {2} fit equally well (each maps {3} parameter(s) from the source). Choose one in a profile with a lambda that calls it, such as ConstructWith({4}), or remove or change one of the constructors.`
(`{4}` — лямбда с параметрами первого из них: `(int from, int to) => new Range(from, to)`)

**Причина:** destination создаётся конструктором, и два или больше его конструкторов можно вызвать с одинаковым числом
параметров из source и одинаковым числом параметров, например `Range(int, int)` и `Range(long, long)` для source со
свойствами `int`. Порядок объявления не решает: перестановка кода молча меняла бы маппинг.

**Поведение:** маппинг не генерируется; остальные маппинги проекта генерируются.

**Как исправить:** выберите конструктор в профиле,
`CreateMap<RangeRow, Range>().ConstructWith((int from, int to) => new Range(from, to))`
([Фабрики](advanced.ru.md#фабрики-constructwith)), либо удалите или измените один из конструкторов. Атрибут в типе
destination намеренно не предлагается: доменные типы не должны зависеть от IronMapper.

---

## IM0018 — У destination нет доступного конструктора

**Уровень:** Error (с 2.1.0)

**Куда указывает:** на атрибут `[MapTo]`/`[MapFrom]` или вызов `CreateMap`.

**Сообщение:** `Mapping from '{0}' to '{1}' was not generated: '{1}' has no public or internal constructor the generated code can call. Create it with its static factory method in a profile: CreateMap<{2}, {3}>().ConstructWith({3}.Create), where Create is the factory method.`

**Причина:** все конструкторы destination `private` или `protected` (или с параметрами `ref`/`out`), как у доменного
типа, который создаётся статическим фабричным методом (`Project.Restore(...)`). До 2.0.0 сборка падала внутри
сгенерированного файла (`CS0122`, `CS7036`).

**Поведение:** маппинг не генерируется; остальные маппинги проекта генерируются.

**Как исправить:** назовите фабрику в профиле, `CreateMap<ProjectRow, Project>().ConstructWith(Project.Restore)`: её
параметры сопоставляются с source так же, как параметры конструктора ([Фабрики](advanced.ru.md#фабрики-constructwith)).
Или сделайте конструктор public или internal.

---

## IM0019 — ForCtorParam не называет параметр

**Уровень:** Error (с 2.1.0)

**Куда указывает:** на имя в `ForCtorParam`.

**Сообщение:** `Mapping from '{0}' to '{1}' was not generated: ForCtorParam({2}) {3}`, например
`ForCtorParam("nmae") names no parameter of the constructor of 'Author' that the generator chose. Its parameters: name.`

**Причина:** `ForCtorParam` настраивает параметр конструктора, который выбирает генератор, или фабрики `ConstructWith`
([Параметры без свойства](advanced.ru.md#параметры-без-свойства-forctorparam)); имена сравниваются без учёта регистра.
Такого параметра нет (опечатка, параметр переименован), destination создаётся через object initializer (есть доступный
конструктор без параметров) или через `ConvertUsing`/`ConstructUsing`, которые параметры не сопоставляют, либо имя — не
константа.

**Поведение:** маппинг не генерируется (настройка иначе молча пропала бы); остальные маппинги проекта генерируются.

**Как исправить:** укажите одно из имён параметров из сообщения (литерал или `nameof` чего-то с тем же именем); для
свойства object initializer используйте `ForMember`.

---

## IM0020 — Фабрику ConstructWith нельзя использовать

**Уровень:** Error (с 2.1.0)

**Куда указывает:** на аргумент `ConstructWith` (или на конфликтующий вызов, или на аргумент `ConstructUsing`).

**Сообщение:** `Mapping from '{0}' to '{1}' was not generated: {2}`; `{2}` объясняет, что не так и что написать.

**Причина:** сгенерированный код не может вызвать то, что передано в `ConstructWith`
([Фабрики](advanced.ru.md#фабрики-constructwith)):

- метод экземпляра или локальная функция (у сгенерированного класса нет экземпляра для вызова);
- приватный или защищённый метод либо метод приватного типа;
- метод возвращает не тип destination и не производный от него, либо nullable (`Team?`);
- параметр `ref`/`out`;
- группа методов перегруженного метода или лямбда без типов параметров: тип делегата не выводится (компилятор в том же
  месте сообщает `CS8917`);
- не группа методов и не лямбда, написанная в вызове (делегат в переменной или поле при сборке не прочитать);
- `ConstructWith` вместе с `ConstructUsing` или `ConvertUsing` либо дважды.

**Поведение:** маппинг не генерируется; остальные маппинги проекта генерируются.

**Как исправить:** передайте public или internal статический метод либо лямбду с типизированными параметрами, которая
вызывает фабрику или конструктор: `ConstructWith((Guid id, string name) => Team.Restore(id, name))`.

---

## IM0021 — init-свойство нельзя задать после фабрики

**Уровень:** Warning (с 2.1.0)

**Куда указывает:** на вызов `CreateMap`.

**Сообщение:** `Property '{1}.{0}' is not mapped: it is init-only, and the destination is created by {2}, so it cannot be set after the call. Pass the value to the factory, or ignore the property with ForMember(d => d.{0}, o => o.Ignore()).`

**Причина:** destination создаётся фабрикой `ConstructWith`, и у source есть значение для `init`-свойства, которое не
задаёт ни один параметр фабрики. `init`-свойство можно задать только в object initializer при `new`; после вызова
сгенерированный код присваивает свойства с публичным `set`.

**Поведение:** маппинг генерируется без этого свойства.

**Как исправить:** добавьте в фабрику параметр для него (или задайте его в лямбде `ConstructWith`) либо проигнорируйте
его через `ForMember(d => d.Свойство, o => o.Ignore())`.
