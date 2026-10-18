# Диагностики компилятора

> 🌐 **[English version](diagnostics.md)**

Source generator IronMapper выдаёт диагностики с идентификаторами `IM0001`–`IM0012`. Они объявлены в
[`DiagnosticDescriptors.cs`](../src/IronMapper.Generator/Diagnostics/DiagnosticDescriptors.cs).

**Текущее состояние** (`main`, после 1.1.1):

| ID | Уровень | Название | Выдаётся? |
|---|---|---|---|
| IM0001 | Warning | Несопоставленное свойство destination | Да — только `[MapTo]`/`[MapFrom]` |
| IM0002 | Error | Несовместимые типы свойств | **Пока не выдаётся** |
| IM0003 | Error | Не удаётся разрешить целевой тип | **Пока не выдаётся** |
| IM0004 | Error | Обнаружен циклический маппинг | **Пока не выдаётся** |
| IM0005 | Warning | Пустой MappingProfile | Да |
| IM0006 | Error | Свойство из `[MapProperty]` не найдено в destination | Да |
| IM0007 | Warning | Повторное определение маппинга | **Пока не выдаётся** |
| IM0008 | Error | Destination — абстрактный тип или интерфейс | Да — атрибуты и профили |
| IM0009 | Warning | Параметр конструктора record не сопоставлен | Да — только `[MapTo]`/`[MapFrom]` |
| IM0010 | Error | Тип маппинга недоступен из сгенерированного кода | Да — атрибуты и профили |
| IM0011 | Error | Лямбда профиля использует член, недоступный сгенерированному коду | Да — профили |
| IM0012 | Error | Лямбда профиля захватывает состояние | Да — профили |

Сейчас все диагностики выдаются **без указания места в исходном коде**: IDE показывает их в списке ошибок,
но не указывает строку в вашем коде.

Если для проблемы нет работающей диагностики (например, несовпадение типов свойств), сборка падает
с обычной ошибкой C# (`CS0029`, `CS7036`, …) внутри сгенерированного файла `*.g.cs`.

---

## IM0001 — Несопоставленное свойство destination

**Уровень:** Warning

**Сообщение:** `Property '{0}' on destination type '{1}' has no corresponding source property in '{2}' and will be left at its default value`

**Причина:** у публичного свойства destination с сеттером нет source-свойства с тем же именем
(без учёта регистра) и нет `[MapProperty]`, указывающего на него.

**Когда выдаётся:** только для маппингов `[MapTo]` / `[MapFrom]`. В маппингах `MappingProfile`
несопоставленные свойства destination молча пропускаются.

**Как исправить:**
- Добавьте соответствующее свойство в source-тип.
- Используйте `[MapProperty("DestPropertyName")]` на source-свойстве.
- Поставьте `[Ignore]` на свойство destination, чтобы подавить предупреждение.
- В `IronMapper.CodeFixes` есть code fix («Add [Ignore] to '...'»); тестами он не покрыт.

---

## IM0002 — Несовместимые типы свойств

**Уровень:** Error · **Пока не выдаётся**

**Сообщение (объявлено):** `Property '{0}': source type '{1}' cannot be assigned to destination type '{2}'. Add [MapConverter] or implement ITypeConverter<{1}, {2}>.`

**Текущее поведение:** генератор не проверяет типы свойств. Несовпадение (в том числе вложенный объект
другого типа, например `Address` → `AddressDto`) приводит к ошибке компиляции C#, например `CS0029`,
в сгенерированном файле.

**Как исправить:** используйте `[MapConverter]`, `opt.UseConverter<T>()` или `opt.MapFrom(...)` для этого свойства.

---

## IM0003 — Не удаётся разрешить целевой тип

**Уровень:** Error · **Пока не выдаётся**

**Сообщение (объявлено):** `The type '{0}' referenced in [{1}] could not be resolved in the current compilation`

**Текущее поведение:** если тип, переданный в `[MapTo]`/`[MapFrom]`, не разрешается, компилятор C#
сообщает `CS0246` в выражении `typeof(...)`, а генератор всё равно создаёт файл маппинга со ссылкой
на отсутствующий тип, что добавляет ошибки `CS0400` в сгенерированном коде.

---

## IM0004 — Обнаружен циклический маппинг

**Уровень:** Error · **Пока не выдаётся**

**Сообщение (объявлено):** `Cyclic mapping detected: '{0}' maps to '{1}' which directly or indirectly maps back to '{0}'`

**Текущее поведение:** обнаружение циклов не реализовано.

---

## IM0005 — Пустой MappingProfile

**Уровень:** Warning

**Сообщение:** `MappingProfile '{0}' has no mappings defined`

**Причина:** в конструкторе (конструкторах) класса-наследника `MappingProfile` нет ни одной цепочки
`CreateMap<TSource, TDest>()`.

**Как исправить:** добавьте хотя бы один вызов `CreateMap<TSource, TDest>()` или удалите пустой профиль.

---

## IM0006 — Свойство из `[MapProperty]` не найдено в destination

**Уровень:** Error

**Сообщение:** `[MapProperty] destination '{0}' does not exist in type '{1}'`

**Причина:** имя, переданное в `[MapProperty]`, не совпадает ни с одним публичным свойством destination
с сеттером. Сравнение **без учёта регистра**.

**Как исправить:** исправьте имя или добавьте свойство в destination-тип. В `IronMapper.CodeFixes` есть
code fix («Did you mean '...'?», расстояние Левенштейна ≤ 2); тестами он не покрыт.

---

## IM0007 — Повторное определение маппинга

**Уровень:** Warning · **Пока не выдаётся**

**Сообщение (объявлено):** `Both an attribute and a MappingProfile define a mapping from '{0}' to '{1}'. The profile-based mapping takes precedence.`

**Текущее поведение:** обнаружение дубликатов не реализовано, правило «профиль имеет приоритет» тоже.
Если одна и та же пара source/destination определена дважды (например, через `[MapTo]` и через `CreateMap`
в профиле), генератор падает с предупреждением `CS8785` («HintName ... must be unique») и **не генерирует
никакого кода маппинга** для проекта. Определяйте каждую пару только один раз.

---

## IM0008 — Destination — абстрактный тип или интерфейс

**Уровень:** Error

**Сообщение:** `Type '{0}' is abstract or an interface and cannot be instantiated as a mapping destination`

**Причина:** destination-тип в `[MapTo]`, `[MapFrom]` или `CreateMap` абстрактный или является интерфейсом.

**Как исправить:** используйте конкретный тип. Для профилей `ConvertUsing` не помогает обойти ошибку:
генератор выдаёт IM0008 до разбора остальной конфигурации маппинга.

---

## IM0009 — Параметр конструктора record не сопоставлен

**Уровень:** Warning

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

**Сообщение:** `Mapping from '{0}' to '{1}' was not generated: type '{2}' is {3}, so the generated class IronMapper.Generated.GeneratedMappers cannot access it. Make the type and all types that contain it public or internal.`

**Причина:** тип source или destination, один из содержащих его типов или один из аргументов обобщённого типа —
вложенный тип с доступом `private`, `protected` или `private protected`. Сгенерированные методы находятся в классе
верхнего уровня `IronMapper.Generated.GeneratedMappers` и не могут ссылаться на такие типы.

**Когда выдаётся:** для маппингов `[MapTo]` / `[MapFrom]` и `MappingProfile`. Пропускается только этот маппинг;
остальные маппинги проекта генерируются.

**Как исправить:** сделайте тип (и все типы, которые его содержат) `public` или `internal`. Internal-типы
поддерживаются; сгенерированные методы тогда `internal` (см. [Доступность сгенерированных методов](configuration.ru.md#доступность-сгенерированных-методов)).

---

## IM0011 — Лямбда профиля использует член, недоступный сгенерированному коду

**Уровень:** Error

**Сообщение:** `A lambda in MappingProfile '{0}' uses '{1}', but '{2}' is {3}. Profile lambdas are copied into the generated class IronMapper.Generated.GeneratedMappers, which cannot access it. Make '{2}' internal (or public). The mapping was not generated.`

**Причина:** лямбда в `MapFrom`, `When`, `ConvertUsing`, `BeforeMap`/`AfterMap` или `AddTransformer` ссылается на
член или тип с доступом `private`, `protected` или `private protected` (или на член такого типа) — обычно это
`private static` метод или константа профиля. Лямбды переписываются в сгенерированный класс, а не вызываются у профиля,
поэтому такие члены там не видны.

**Как исправить:** сделайте член (или содержащий его тип) `internal`. См. [Лямбды в профилях](configuration.ru.md#лямбды-в-профилях).

Этот маппинг не генерируется (для `AddTransformer` отключается преобразователь); остальные генерируются.

---

## IM0012 — Лямбда профиля захватывает состояние

**Уровень:** Error

**Сообщение:** `A lambda in MappingProfile '{0}' uses {1} '{2}'. Profile lambdas are copied into generated code that runs without a profile instance, so they can only use their own parameters, static members and constants. Make it static or const, or compute the value from the source object. The mapping was not generated.`

**Причина:** лямбда использует поле, свойство или метод экземпляра профиля, `this`/`base`, либо локальную переменную,
параметр или локальную функцию конструктора. Ничего из этого нет, когда выполняется сгенерированный код.

**Как исправить:** сделайте член `static` (или `const`) либо вычисляйте значение из source.
См. [Лямбды в профилях](configuration.ru.md#лямбды-в-профилях).

Этот маппинг не генерируется (для `AddTransformer` отключается преобразователь); остальные генерируются.
