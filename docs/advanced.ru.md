# Продвинутые возможности

> 🌐 **[English version](advanced.md)**

## Вложенные объекты

Начиная с 2.1.0 вложенный объект маппится маппингом своей пары типов. Если у source и destination есть свойство с
одинаковым именем и разными типами (`Customer.Address` → `CustomerDto.Address` типа `AddressDto`) и в проекте есть
маппинг `Address` → `AddressDto` (атрибут или профиль, в этом проекте или в проекте, на который он ссылается),
сгенерированный код вызывает его. `ForMember` не нужен:

```csharp
public class CustomerProfile : MappingProfile
{
    public CustomerProfile()
    {
        CreateMap<Address, AddressDto>();
        CreateMap<Customer, CustomerDto>();   // Address и Billing маппятся маппингом выше
    }
}

// Сгенерировано (сокращённо):
private static CustomerDto MapToCustomerDtoCore(Customer source)
{
    return new CustomerDto
    {
        Address = MapToAddressDtoCore(source.Address ?? NestedObjectIsNull<Address>("…")), // Address  → AddressDto
        Billing = source.Billing is null ? null : MapToAddressDtoCore(source.Billing),     // Address? → AddressDto?
    };
}
```

У каждого маппинга есть публичный `MapToX(source)`, который, как и раньше, проверяет `source` на `null`, и закрытый
`MapToXCore` с тем же телом без проверки. Вложенный объект вызывает `MapToXCore` напрямую, после своей проверки: одно
сравнение, во вложенном маппинге нет второй проверки и броска исключения, и JIT может его встроить.

Обработка `null` следует nullable-аннотациям (без `null!` для destination, который не допускает `null`):

| Свойство source | Свойство destination | Сгенерированный код |
| --- | --- | --- |
| `Address`, `Address?` или без аннотаций | `AddressDto?` или без аннотаций | `null` → `null` |
| `Address` или без аннотаций (`#nullable disable`) | `AddressDto` | `null` даёт `InvalidOperationException`, в сообщении оба свойства ([ниже](#null-во-вложенном-объекте-который-не-допускает-null)) |
| `Address?` | `AddressDto` | ошибка `IM0002`: объявите destination допускающим `null` или source — не допускающим, либо настройте свойство (`MapFrom`, `Ignore`) |
| struct | любое | `MapToAddressDtoCore(source.Address)` без проверки |

Какой маппинг вызывается: маппинг точной пары, иначе маппинг базового типа или интерфейса типа source (как для
элементов коллекций); уточнённое имя метода (`IM0013`) учитывается. Маппинг из сборки-зависимости вызывается как
`source.Address.MapToAddressDto()`.

**Нет маппинга для пары:** ошибка [IM0002](diagnostics.ru.md#im0002--несовместимые-типы-свойств-или-нет-маппинга) на
свойстве destination (или на `CreateMap` в профиле) называет пару и что добавить; свойство не генерируется, остальной
маппинг — да. Code fix добавляет маппинг: `[MapTo(typeof(AddressDto))]` на `Address` для маппинга через атрибуты,
`CreateMap<Address, AddressDto>();` после вызова `CreateMap` в профиле. Тот же fix предлагается для коллекции, у
элементов которой нет маппинга.

**Явная настройка важнее:** `ForMember` с `MapFrom`, `UseConverter` или `Ignore`, `[MapConverter]` и `[Ignore]`
(маппинги через атрибуты) — такое свойство автоматически не маппится. `IncludeMembers` не маппит вложенные объекты
включённых членов (`IM0002`).

**Коллекции:** элементы вложенной коллекции маппятся публичным `MapToX` (`null`-элемент даёт `ArgumentNullException`,
как с 2.0.0); вложенные объекты каждого элемента — как описано выше.

### `null` во вложенном объекте, который не допускает `null`

Свойство, объявленное не допускающим `null`, во время выполнения всё равно может содержать `null`. Типичный случай —
навигация EF Core:

```csharp
public class OrderEntity
{
    public int Id { get; set; }
    public CustomerEntity Customer { get; set; } = null!;   // заполняет EF, только если загружена
}

var order = await db.Orders.SingleAsync(o => o.Id == id);   // без .Include(o => o.Customer)
var dto = order.MapToOrderDto();                             // order.Customer == null
```

Сгенерированный код проверяет значение перед вложенным маппингом и бросает `InvalidOperationException` с сообщением,
в котором названы оба свойства и обычная причина:

```text
OrderEntity.Customer is null, but OrderDto.Customer is not nullable. If it is an EF navigation, check Include;
otherwise declare the destination property nullable (null is then mapped to null) or configure the property explicitly.
```

Та же проверка — в in-place перегрузке и для source без nullable-аннотаций. Что сделать:

- загрузить навигацию (`Include` или проекция, которая её читает);
- объявить свойство destination допускающим `null` (`CustomerDto? Customer`): тогда `null` → `null`;
- настроить свойство: `ForMember(d => d.Customer, o => o.MapFrom(...))`, `Ignore()` или `[Ignore]` в маппинге через
  атрибуты.

Исключение — `InvalidOperationException`, а не `ArgumentNullException`: аргумент `MapToOrderDto` не `null`, а объект,
на который он ссылается, в состоянии, с которым маппинг не справится. `IMapper` передаёт его без изменений. Бросок
вынесен в один вспомогательный метод сгенерированного класса (`NestedObjectIsNull`, не встраивается), поэтому маппинг с
проверкой остаётся маленьким. AutoMapper маппит такой `null` в `null` без ошибки; Mapperly проверку не генерирует, и
маппинг падает с `NullReferenceException`.

### In-place обновление вложенных объектов

`source.MapToOrderEntity(entity)` (и `IMapper.Map(source, destination)`) обновляет вложенные объекты, которые уже есть в
destination, а не заменяет их. Это важно для EF Core: отслеживаемая навигация, заменённая новым экземпляром, вставляется
новой строкой или падает с конфликтом отслеживания; owned-тип удаляется и вставляется заново.

```csharp
var order = await db.Orders.Include(o => o.Customer).ThenInclude(c => c.Address).SingleAsync(o => o.Id == id);
dto.MapToOrderEntity(order);     // order.Customer и order.Customer.Address — те же экземпляры, с новыми значениями
await db.SaveChangesAsync();     // UPDATE, без INSERT
```

Для каждого вложенного объекта маппинга (`Customer`, затем `Address` внутри него, любая глубина):

| `source.Customer` | `destination.Customer` | Результат |
| --- | --- | --- |
| объект | объект | существующий объект обновляется in-place маппингом пары (этого проекта или публичным in-place `MapToX(source, destination)` проекта-зависимости); `ReferenceEquals` сохраняется |
| объект | `null` | создаётся и присваивается новый объект |
| `null` | любое | `null` для destination, допускающего `null`, иначе `InvalidOperationException` ([выше](#null-во-вложенном-объекте-который-не-допускает-null)) |

Новый объект присваивается (как до 2.1.0), если существующий обновить нельзя. Все случаи и что сделать, если нужно
обновление на месте:

| Случай | Почему | Чтобы обновлялось на месте |
| --- | --- | --- |
| Свойство destination — struct | getter возвращает копию, изменение копии ничего не меняет | объявить тип destination классом (struct — значение: присваивание и есть обновление) |
| У типа destination пары есть сопоставленное `init`-свойство (record с `init`-свойствами) | in-place перегрузка пропускает `init`-свойства, и в обновлённом объекте остались бы старые значения | сделать эти свойства изменяемыми (`set`) или `Ignore` их в маппинге пары |
| Тип destination пары создаётся конструктором (позиционный record, нет конструктора без параметров) или фабрикой (`ConstructWith`, `ConstructUsing`; с 2.1.0) | у него нет in-place-маппинга ([Конструкторы и позиционные record](#конструкторы-и-позиционные-record)) | дать типу конструктор без параметров и изменяемые свойства |
| Маппинг пары использует `ConvertUsing` | объект создаёт конвертер | убрать `ConvertUsing` и маппить свойства (`ForMember`) или обновлять объект своим кодом после in-place вызова (in-place перегрузка хуки не вызывает) |
| Маппинг пары — в проекте-зависимости, и in-place там выключен (`IronMapperGenerateInPlace=false`, `GenerateInPlace = false`, `GenerateMethods(inPlace: false)`) | нет публичного in-place `MapToX(source, destination)`, который можно вызвать | включить in-place перегрузку для этой пары в проекте-зависимости |
| У свойства destination нет getter, который может вызвать сгенерированный код (только `set` или непубличный getter) | существующий объект нельзя прочитать | добавить публичный (или internal) getter |

In-place перегрузка проекта-зависимости вызывается как есть: если у того маппинга `ConvertUsing` или `init`-свойства,
его in-place перегрузка игнорирует конвертер и пропускает их (как при вызове верхнего уровня). Само вложенное
`init`-свойство in-place перегрузка пропускает, как любое `init`-свойство (решается вместе со всеми `init` в in-place,
п.2.15 плана). Свойства вложенного объекта,
которые маппинг не задаёт (`Id` с `[Ignore]` или `Ignore()`), сохраняют значения. Как и сама in-place перегрузка,
обновление вложенного объекта игнорирует `When`, `ConvertUsing` и хуки маппинга пары.

Обновляет закрытый in-place `MapToXCore(source, destination)` маппинга пары. Он генерируется для каждого маппинга, в
том числе с выключенной in-place перегрузкой (`IronMapperGenerateInPlace=false`, `GenerateInPlace = false`,
`GenerateMethods(inPlace: false)`): тогда публичного in-place `MapToX` нет, а `IMapper.Map(source, destination)`
по-прежнему обновляет вложенные объекты. Коллекции не сливаются: вложенная коллекция, как и раньше, маппится в новый
список или массив.

### Циклы (IM0004)

Цикл маппингов через автоматически смапленный вложенный объект даёт предупреждение
[IM0004](diagnostics.ru.md#im0004--цикл-маппингов-через-вложенный-объект): `Employee.Manager` (типа `Employee`),
`Order.Customer.Orders`. Цикл типов приводит к бесконечной рекурсии, только если цикл есть в данных (заказ, у покупателя
которого в списке этот же заказ; тогда маппинг падает с `StackOverflowException`); цепочка руководителей заканчивается
на `null` и маппится. Сгенерированный код не отслеживает уже смапленные объекты. Разорвите цикл через `Ignore` или
`MapFrom` на свойстве цепочки или подавите предупреждение для моделей, которые всегда конечны:

```csharp
public class EmployeeDto
{
#pragma warning disable IM0004 // цепочка руководителей всегда заканчивается
    public EmployeeDto? Manager { get; set; }
#pragma warning restore IM0004
}
```

(или `dotnet_diagnostic.IM0004.severity = none` в `.editorconfig`). Циклы только через коллекции (дерево вроде
`Category.Children`) не сообщаются: они работали уже в 2.0.0.

**До 2.0.0 включительно** вложенный объект не маппился: генератор выдавал `Address = source.Address`, и сборка падала с
`CS0029` внутри сгенерированного файла. Обходной путь этих версий,
`ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()))`, по-прежнему работает. Демо
(`samples/IronMapper.Demo/Demos/Demo4_NestedObjects.cs`) собирает книгу из двух ответов API, поэтому маппит автора
отдельно и присоединяет его.

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

Начиная с 2.0.0 эти циклы написаны один раз на проект, в двух обобщённых методах сгенерированного класса
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

## Конструкторы и позиционные record

Как создаётся destination (с 2.1.0, [#24](https://github.com/iron-mapper/IronMapper/issues/24)):

- **Если есть доступный конструктор без параметров** (public или internal) или destination — структура: через
  **object initializer** (`new TDest { ... }`), как раньше. Для record и классов со свойствами `init` или `set`
  сгенерированный код тот же.
- **Иначе — через конструктор:** позиционный record или класс, у которого все конструкторы с параметрами, создаётся
  вызовом конструктора с именованными аргументами; свойства destination, которые конструктор не задаёт, заполняются
  object initializer после него.

```csharp
[MapTo(typeof(PendingChangeState))]
public record PendingChange(string NewAddress, DateTimeOffset ExpiresAt);
public record PendingChangeState(string? NewAddress, DateTimeOffset? ExpiresAt);

// сгенерировано:
// return new PendingChangeState(
//     NewAddress: source.NewAddress,
//     ExpiresAt: source.ExpiresAt);
```

**Параметры** получают значения так же, как свойства:

- из свойства source с тем же именем **без учёта регистра** (`id` ↔ `Id`); в маппингах через атрибуты — ещё из
  `[MapProperty("имяПараметра")]` на свойстве source и через `[MapConverter]` на нём;
- с теми же преобразованиями, что у свойства: неявные преобразования (`string` → `string?`, `DateTimeOffset` →
  `DateTimeOffset?`, `int` → `long`), коллекции (`T[]` передаётся в `IReadOnlyList<T>` как есть, другой тип элементов
  маппится поэлементно), вложенные объекты — маппингом своей пары с теми же проверками на `null`
  ([Вложенные объекты](#вложенные-объекты));
- параметр **со значением по умолчанию** без source получает значение по умолчанию (`string Theme = "system"`);
- в профиле `ForMember` для свойства destination **с тем же именем** (без учёта регистра) настраивает параметр:
  `MapFrom`, `UseConverter` и `Ignore()`, который для параметра означает «взять значение по умолчанию»:

  ```csharp
  public record Account(Guid Id, string Email, bool Blocked, string Theme = "system");

  CreateMap<UserRecord, Account>()
      .ForMember(d => d.Blocked, o => o.MapFrom(s => s.BlockedAt != null)); // вычисляемый параметр
  ```

- в профиле `ForCtorParam("имяПараметра", o => ...)` настраивает параметр, у которого нет одноимённого свойства
  destination, с теми же вариантами (см. [Параметры без свойства](#параметры-без-свойства-forctorparam));
- `[Ignore]` на одноимённом свойстве destination или на свойстве source тоже означает значение по умолчанию.

Свойство destination с именем параметра считается заданным конструктором: initializer не записывает его второй раз,
и `IM0001` для него нет (`required`-свойство всё же задаётся в initializer, если у конструктора нет
`[SetsRequiredMembers]`). Лишние свойства source игнорируются, как и для initializer.

**Какой конструктор.** Из доступных конструкторов (без параметров `ref`/`out`, не копирующий конструктор record), у
которых каждый параметр получает значение, — тот, где больше всего параметров берётся из source, затем тот, где больше
параметров. Статические фабричные методы не используются, если профиль не назвал фабрику через
[`ConstructWith`](#фабрики-constructwith): тип с публичным конструктором и методом `Create` создаётся конструктором. В
самом типе destination ничего не выбирает конструктор: атрибуты IronMapper в доменных типах не нужны; профиль выбирает
конструктор лямбдой, `ConstructWith((int from, int to) => new Range(from, to))`. Диагностики — все ошибки, маппинг не
генерируется (остальные маппинги генерируются):

- параметр без значения (нет свойства source, значение нельзя преобразовать, `Ignore` без значения по умолчанию) —
  [`IM0009`](diagnostics.ru.md#im0009--параметр-конструктора-не-сопоставлен);
- два конструктора подходят одинаково — [`IM0017`](diagnostics.ru.md#im0017--конструктор-destination-неоднозначен);
- нет доступного конструктора (только приватный, например у доменного типа, который создаётся фабричным методом) —
  [`IM0018`](diagnostics.ru.md#im0018--у-destination-нет-доступного-конструктора): назовите фабрику через
  [`ConstructWith`](#фабрики-constructwith) в профиле.

**Не генерируется для такого destination: in-place-перегрузка.** `source.MapToX(destination)` нет для destination,
который создаётся конструктором с параметрами или фабрикой (`ConstructWith`). Решение окончательное: если обновлять
только свойства вне конструктора, всё, что принимает только конструктор или фабрика, молча осталось бы со старыми
значениями. `IMapper.Map(source, destination)` бросает `MappingException` с этим объяснением. При in-place-обновлении
родительского объекта вложенный объект такого типа заменяется новым экземпляром (как record со свойствами `init`, см.
[In-place-обновление вложенных объектов](#in-place-обновление-вложенных-объектов)).

Прочее: исключение из конструктора доходит до вызывающего кода как есть; `ConvertUsing` создаёт объект сам, конструктор
не нужен; с `BeforeMap`/`AfterMap` сначала вызывается конструктор, затем `BeforeMap`, присваивания свойств с `set` и
`AfterMap`. Как и для свойства, nullable source для ненулевого ссылочного параметра (`string?` → `string`) компилируется с
предупреждением nullable в сгенерированном файле (`CS8604`; у свойства `CS8601`) и передаёт `null` дальше.

### Фабрики (`ConstructWith`)

Доменный тип часто создаётся статическим фабричным методом, а конструктор у него приватный. В профиле `ConstructWith`
называет, чем создаётся destination (с 2.1.0):

```csharp
public sealed class Team
{
    private Team(MemberId owner, string name, IReadOnlyList<Guid> memberIds) { ... }

    public MemberId Owner { get; }
    public string Name { get; }
    public IReadOnlyList<Guid> MemberIds { get; }
    public string? Motto { get; set; }

    public static Team Restore(MemberId owner, string name, IReadOnlyList<Guid> memberIds) => new(owner, name, memberIds);
}

CreateMap<TeamRow, Team>()
    .ConstructWith(Team.Restore)                                          // группа методов: статический метод
    .ForMember(d => d.Owner, o => o.MapFrom(s => MemberId.From(s.Owner))); // value object из Guid

// сгенерировано:
// var destination = Team.Restore(
//     owner: MemberId.From(source.Owner),
//     name: source.Name,
//     memberIds: source.MemberIds);    // Guid[] → IReadOnlyList<Guid> как есть
// destination.Motto = source.Motto;    // свойство с публичным set — после вызова
// return destination;
```

- **Группа методов статического метода** (`ConstructWith(Team.Restore)`): метод любого типа, который возвращает тип
  destination или производный от него; конструктор типа может быть приватным. Метод должен быть public или internal —
  его вызывает сгенерированный класс.
- **Лямбда с типизированными параметрами** (`ConstructWith((string name, int age) => new Person(name, age))`): так же
  выбирает один из нескольких конструкторов или готовит значение. Её тело выводится в закрытый вспомогательный метод
  (выражение или блок, значения параметров по умолчанию сохраняются); как в `MapFrom`, в ней нельзя использовать
  локальные переменные и экземпляр профиля (`IM0012`). Типы параметров обязательны: имена параметров сопоставляются с
  source.
- Параметры метода или лямбды получают значения **так же, как параметры конструктора** (см. выше): одноимённое
  свойство source без учёта регистра, `ForCtorParam`, `ForMember` одноимённого свойства destination, вложенные объекты
  (в том числе созданные фабрикой: `CreateMap<SettingsRow, Settings>().ConstructWith(Settings.Create)`), коллекции,
  значение по умолчанию необязательного параметра. Параметр без значения —
  [`IM0009`](diagnostics.ru.md#im0009--параметр-конструктора-не-сопоставлен).
- **После вызова** присваиваются свойства destination с публичным `set`, которые не задал ни один параметр.
  `init`-свойство после вызова задать нельзя: если у source есть для него значение, генератор выдаёт
  [`IM0021`](diagnostics.ru.md#im0021--init-свойство-нельзя-задать-после-фабрики) (предупреждение) и не маппит его.
- Исключение фабрики (например, value object не принимает `Guid.Empty`) доходит до вызывающего кода как есть, и через
  `IMapper` тоже.
- In-place-перегрузки нет (см. выше); `ReverseMap()` маппит обратное направление по именам, без фабрики.
- У группы методов перегруженного метода в C# нет типа делегата; выберите перегрузку лямбдой, которая её вызывает:
  `ConstructWith((Guid id, string name) => Team.Restore(id, name))`. Короткие формы требуют C# 10; в более старых
  версиях языка пишите `ConstructWith(new Func<Guid, string, Team>(Team.Restore))`.
- Аргумент, который сгенерированный код не может вызвать (метод экземпляра, приватный метод, другой или nullable тип
  результата, параметр `ref`, делегат в переменной), —
  [`IM0020`](diagnostics.ru.md#im0020--фабрику-constructwith-нельзя-использовать), а не ошибка в сгенерированном файле.

### Параметры без свойства (`ForCtorParam`)

`ForMember` выбирает свойство destination, поэтому не может настроить параметр, имени которого нет ни у одного
свойства. `ForCtorParam` принимает имя параметра и те же варианты, что `ForMember` (`MapFrom`, `UseConverter`, `Ignore()` —
значение по умолчанию), для конструктора, который выбирает генератор, и для фабрики `ConstructWith`:

```csharp
public class Author
{
    public Author(string display, string code) { Text = display; Id = code; }
    public string Text { get; }
    public string Id { get; }
}

CreateMap<AuthorRow, Author>()
    .ForCtorParam("display", o => o.MapFrom(s => s.First + " " + s.Last))
    .ForCtorParam(nameof(AuthorRow.Code), o => o.MapFrom(s => s.Code.ToUpperInvariant()));
```

Имя сравнивается без учёта регистра и должно быть константой: литерал или `nameof` чего-то с тем же именем (`nameof`
параметра вне его метода в C# нет). Имя, которого нет среди параметров того, что создаёт destination, —
[`IM0019`](diagnostics.ru.md#im0019--forctorparam-не-называет-параметр) (ошибка), а не молча пропущенная настройка.

### `ConstructUsing` (как в AutoMapper)

`ConstructUsing(s => new OrderDto(s.Id))` создаёт destination выражением как написано, без сопоставления параметров:
правила лямбды те же, что у `MapFrom` (дерево выражений, без локальных переменных профиля). Затем свойства destination с
публичным `set` маппятся как обычно и присваиваются после него; `init`-свойства оставлены выражению. In-place-перегрузка
его не использует, как в AutoMapper, и обновляет существующий объект; для destination без доступного конструктора без
параметров in-place-перегрузки нет. Для нового кода лучше `ConstructWith`: он сопоставляет параметры и сообщает о
параметре без значения при сборке.

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
- не генерируется для destination, который создаётся конструктором с параметрами или фабрикой (`ConstructWith`)
  ([Конструкторы и позиционные record](#конструкторы-и-позиционные-record));
- не использует `ConstructUsing` (обновляется существующий объект);
- пропускает свойства destination с `init`;
- **не** применяет `When`, `ConvertUsing`, `BeforeMap` и `AfterMap`.

---

## IMapper и сгенерированные extension-методы

- Сгенерированные extension-методы (`source.MapToDto()`) — обычный скомпилированный C# без reflection.
- `IMapper` из `IronMapper.Extensions.DI` (`RuntimeMapper`) вызывает те же сгенерированные методы через делегаты,
  без reflection (начиная с 2.0.0). Для каждой сборки с маппингами генератор выпускает регистрацию
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
  `RegisterIronMapperMappings()` сгенерированного класса сборки один раз при старте, например
  `IronMapper.Generated.GeneratedMappers_MyCompany_Mappings.RegisterIronMapperMappings()` для сборки `MyCompany.Mappings`
  (метод `internal`, поэтому вызывайте его из этой сборки; до 1.1.4 включительно класс назывался `GeneratedMappers`, см.
  [Решения из нескольких проектов](configuration.ru.md#решения-из-нескольких-проектов)). Для целей без `ModuleInitializerAttribute` (`netstandard`, .NET Framework) в сборку
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
  В интерфейсе `IMapper` этого метода нет: новый член интерфейса сломал бы другие его реализации.
- `IMapper.MapCollection<TSource, TDest>` маппит весь источник во время вызова и возвращает `List<TDest>` (с типом
  `IEnumerable<TDest>`); повторное перечисление результата не маппит заново. До 1.1.4 включительно метод возвращал
  ленивую последовательность, которая маппила при каждом перечислении и бросала исключение для `null`-источника
  только при перечислении.

### Производительность

IronMapper 2.0.0, .NET 10.0.12 и .NET 8, Intel Core i7-12650H; среднее время и отношение к ручному коду. Полный
отчёт, методика, другие библиотеки и сравнение с 1.1.3, измеренной в той же сессии:
[`benchmarks/results/RESULTS.md`](../benchmarks/results/RESULTS.md) (на английском).

| Сценарий | Extension-метод, .NET 10 | `IMapper`, .NET 10 | Extension-метод, .NET 8 | `IMapper`, .NET 8 |
|---|---:|---:|---:|---:|
| Flat (15 свойств) | 11,4 нс (×0,99), 168 Б | 16,3 нс (×1,41), 168 Б | 15,4 нс (×1,01) | 20,9 нс (×1,37) |
| Nested (заказ, покупатель, адрес, 5–20 строк) | 177,8 нс (×1,07) | 185,5 нс (×1,11) | 208,1 нс (×1,09) | 215,6 нс (×1,13) |
| Коллекция, 1 000 заказов | 271,4 мкс (×1,05) | 253,0 мкс (×0,98) | 274,9 мкс (×1,02) | 284,3 мкс (×1,05) |
| Обновление на месте | 8,0 нс (×0,91), 0 Б | 14,0 нс (×1,59), 0 Б | 8,3 нс (×0,93) | 14,9 нс (×1,68) |
| Холодный старт, Δ мс к консольному приложению без маппера | 1,3 | 19,9 | – | – |

- `IMapper` вызывает сгенерированные методы через делегаты сгенерированного реестра и сам ничего не выделяет (столько же
  байт, сколько extension-метод). Остаток сверх extension-метода (+5 нс на Flat) — вызов обобщённого метода интерфейса
  `IMapper.Map<TSource, TDest>`. В бенчмарке DI overhead (оба из одного контейнера, плоский объект) `IMapper` IronMapper —
  15,4 нс, AutoMapper — 34,5 нс. В 1.1.3 вызов шёл через reflection: 30,2 нс на Flat и +32 Б на вызов.
- Холодный старт `IMapper`: сам DI-контейнер (тот же ручной маппинг, полученный из него, `Manual_DI`) — +15,3 мс;
  IronMapper добавляет к нему 4,5 мс (регистрация маппингов при создании первого `IMapper` и первый вызов), больше — при
  большем числе маппингов. В 1.1.3 — 6,4 мс.
- В сценарии Nested этих цифр 2.0.0 IronMapper маппит вложенные объекты через `ForMember(... MapFrom(...))` (2.0.0 не
  маппит их автоматически); Mapperly маппит их автоматически, у него там ×0,98. В сгенерированном коде нет LINQ и
  делегатов (циклы с известным размером, см.
  [Вложенные collection-свойства](#вложенные-collection-свойства)); 1.1.3 использовала `Enumerable.Select` + `ToList` и
  давала ×1,25. Оставшийся разрыв в Nested у 2.0.0 — проверки на null в сгенерированных методах: из-за них JIT не
  встраивает `MapToAddressDto` в `MapToCustomerDto` (проверено `DisassemblyDiagnoser`). Начиная с 2.1.0 набор маппит
  вложенные объекты автоматически, а вложенный объект вызывает внутренний `MapToXCore` без проверки (см.
  [Вложенные объекты](#вложенные-объекты)); это пока не измерено.
- Разница в коллекциях в несколько процентов — в пределах разброса между запусками: на 1 000 заказов в этом прогоне
  `IMapper` быстрее extension-методов, которые он вызывает, а отдельный прогон только IronMapper дал ×1,01.
- Холодный старт чувствителен к шуму, смотрите на порядок величины (AutoMapper +72,1 мс, Mapster +98,2 мс). Пробы
  (холодный старт, NativeAOT, размер, время сборки) — только на .NET 10.
- Один компьютер. CI лишь проверяет, что набор бенчмарков собирается и каждый сценарий проходит одну итерацию на
  .NET 10 и .NET 8; цифры он не публикует. Как запустить набор:
  [`benchmarks/README.md`](../benchmarks/README.md) (на английском).

### NativeAOT и trimming

Сгенерированные маппинги проверяются в NativeAOT в CI при каждом изменении: `eng/verify-aot.ps1` публикует
консольное приложение на упакованном пакете с `PublishAot=true` для .NET 8 и .NET 10 (linux-x64) и запускает его
(`tests/IronMapper.AotProbe`: маппинги из атрибутов и профиля, коллекции, обновление на месте, конвертер).

- **Сгенерированные extension-методы: совместимы.** Предупреждений trimming/AOT нет (проверка в CI падает на любом);
  исполняемый файл запускается и выдаёт правильный результат. В наборе бенчмарков (win-x64, .NET 10,
  [`benchmarks/results/aot.md`](../benchmarks/results/aot.md)) время старта такое же, как без маппера, а файл на
  2,6 КБ больше приложения без маппера (у Mapperly +23,6 КБ). В 1.1.3 было +255,5 КБ, в основном потому, что
  сгенерированный код коллекций (`Enumerable.Select` + `ToList`) подтягивал загрузчик типов NativeAOT. Проба с
  `IMapper` добавляет 1,50 МБ, почти всё — Microsoft.Extensions.DependencyInjection (тот же ручной маппинг за тем же
  контейнером: +1,05 МБ). Регистрации для `IMapper` в таком
  приложении нет: без `IronMapper.Extensions.DI` trimmer её удаляет (см.
  [Регистрация для IMapper](configuration.ru.md#регистрация-для-imapper-в-приложениях-с-trimming-и-nativeaot)); CI
  проверяет, что в `tests/IronMapper.AotProbe` её нет, а с `IronMapperRegistration=true` она есть.
- **`IMapper` / `AddIronMapper` (IronMapper.Extensions.DI): совместимы начиная с 2.0.0** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)). Они используют
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
