# Диагностики компилятора

> 🌐 **[English version](diagnostics.md)**

IronMapper генерирует диагностики компилятора (ошибки и предупреждения), когда обнаруживает проблемы конфигурации маппинга во время сборки.

---

## IM0001 — Несопоставленное свойство destination

**Серьёзность:** Предупреждение

**Сообщение:** `Property '{PropertyName}' on '{DestType}' has no matching source property and will not be mapped.`

**Причина:** Destination-тип содержит свойство, которое не удаётся сопоставить ни с одним свойством source (по имени, через `[MapProperty]` или `ForMember`).

**Решение:**
- Добавь соответствующее свойство в source-тип.
- Используй `[MapProperty("DestPropertyName")]` на source-свойстве для переименования.
- Используй `ForMember(d => d.Prop, o => o.MapFrom(...))` в profile.
- Используй `ForMember(d => d.Prop, o => o.Ignore())`, если свойство должно быть намеренно пропущено.

---

## IM0002 — Source-тип недоступен

**Серьёзность:** Ошибка

**Сообщение:** `Source type '{TypeName}' is not accessible (must be public or internal).`

**Причина:** Source-тип с атрибутом `[MapTo]` является private или вложен так, что генератор не может получить к нему доступ.

**Решение:** Сделай source-тип `public` или `internal`.

---

## IM0003 — Destination-тип недоступен

**Серьёзность:** Ошибка

**Сообщение:** `Destination type '{TypeName}' is not accessible (must be public or internal).`

**Причина:** Destination-тип, указанный в `[MapTo]` или `[MapFrom]`, является private или недоступен по другой причине.

**Решение:** Сделай destination-тип `public` или `internal`.

---

## IM0004 — Свойство destination из [MapProperty] не найдено

**Серьёзность:** Ошибка
*(см. также IM0006)*

**Причина:** `[MapProperty("X")]` применён к source-свойству, но `X` не существует в destination-типе.

**Решение:** Исправь имя свойства в аргументе атрибута или добавь свойство в destination-тип.

---

## IM0005 — Пустой mapping profile

**Серьёзность:** Предупреждение

**Сообщение:** `MappingProfile '{ProfileName}' contains no mappings.`

**Причина:** Обнаружен подкласс `MappingProfile`, но в его конструкторе нет ни одного вызова `CreateMap` или `ForMember`.

**Решение:** Добавь хотя бы один вызов `CreateMap<TSource, TDest>()` или удали пустой класс profile.

---

## IM0006 — Destination из [MapProperty] не найден

**Серьёзность:** Ошибка

**Сообщение:** `[MapProperty] destination property '{Name}' not found on '{DestType}'.`

**Причина:** Имя, переданное в `[MapProperty]`, не совпадает ни с одним записываемым свойством destination-типа.

**Решение:** Проверь написание и регистр; совпадение регистрозависимо.

---

## IM0007 — Тип converter не реализует ITypeConverter

**Серьёзность:** Ошибка

**Сообщение:** `Type '{ConverterType}' used in [MapConverter] does not implement ITypeConverter<TSource, TDest>.`

**Причина:** Тип, переданный в `[MapConverter(typeof(X))]`, не реализует требуемый обобщённый интерфейс.

**Решение:** Реализуй `ITypeConverter<TSourceProp, TDestProp>` в классе converter.

---

## IM0008 — Destination-тип абстрактный или интерфейс

**Серьёзность:** Ошибка

**Сообщение:** `Destination type '{TypeName}' cannot be instantiated (abstract class or interface).`

**Причина:** `[MapTo]` / `[MapFrom]` / `CreateMap` указывает на тип, который нельзя создать через `new`.

**Решение:** Используй конкретный destination-тип или предоставь фабрику через `ConvertUsing`, которая выполнит создание объекта.

---

## IM0009 — Несопоставленные параметры конструктора record destination

**Серьёзность:** Предупреждение

**Сообщение:** `Record destination '{TypeName}' has constructor parameter '{ParameterName}' with no matching source property.`

**Причина:** Параметр primary constructor record destination не удаётся сопоставить ни с одним source-свойством.

**Решение:**
- Добавь соответствующее свойство в source-тип.
- Используй `ForMember` для указания значения этого параметра конструктора.
- Если это намеренно, подавь предупреждение через `#pragma warning disable IM0009`.
