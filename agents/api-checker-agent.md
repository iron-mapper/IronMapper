# IronMapper — API Checker Agent

Ты агент проверки совместимости API библиотеки IronMapper с AutoMapper.
Работаешь в корне репозитория IronMapper.
Запускается перед этапом 8 (документация и NuGet публикация).

## Твоя задача

Проверить что IronMapper покрывает все распространённые паттерны AutoMapper,
задокументировать отличия и убедиться что migration guide актуален.

---

## Шаг 1 — Сканирование публичного API IronMapper

Прочитай все публичные типы из:
- `src/IronMapper/Attributes/`
- `src/IronMapper/Interfaces/`
- `src/IronMapper/Configuration/`

```bash
grep -rn "public " src/IronMapper/ --include="*.cs" | \
  grep -v "//\|test\|Test" | \
  grep "class\|interface\|record\|void\|Task\|bool\|string\|int"
```

Составь список: все публичные методы IronMapper с сигнатурами.

---

## Шаг 2 — Проверка по чеклисту AutoMapper паттернов

Для каждого паттерна определи статус: ✅ Реализовано / ⚠️ Частично / ❌ Отсутствует

### Базовый маппинг
| AutoMapper | IronMapper эквивалент | Статус |
|---|---|---|
| `new MapperConfiguration(cfg => cfg.CreateMap<Src,Dest>())` | `class Profile : MappingProfile` + `CreateMap<Src,Dest>()` | ? |
| `mapper.Map<Dest>(source)` | `source.MapToDest()` или `mapper.Map<Dest>(source)` | ? |
| `mapper.Map(source, destination)` | `mapper.Map(source, destination)` — маппинг в существующий объект | ? |
| `mapper.Map<Src,Dest>(source)` | `mapper.Map<Src,Dest>(source)` | ? |

### ForMember конфигурация
| AutoMapper | IronMapper эквивалент | Статус |
|---|---|---|
| `.ForMember(d=>d.X, o=>o.MapFrom(s=>s.Y))` | `.ForMember(d=>d.X, o=>o.MapFrom(s=>s.Y))` | ? |
| `.ForMember(d=>d.X, o=>o.Ignore())` | `.ForMember(d=>d.X, o=>o.Ignore())` | ? |
| `.ForMember(d=>d.X, o=>o.MapFrom(s=>s.A + s.B))` | лямбда в MapFrom | ? |
| `.ForMember(d=>d.X, o=>o.NullSubstitute("default"))` | аналог? | ? |
| `.ForMember(d=>d.X, o=>o.UseDestinationValue())` | аналог? | ? |
| `.ForMember(d=>d.X, o=>o.Condition(s=>s.IsActive))` | `.When()` или per-member условие | ? |

### Конвертеры
| AutoMapper | IronMapper эквивалент | Статус |
|---|---|---|
| `cfg.CreateMap<Src,Dest>().ConvertUsing<Converter>()` | `.ConvertUsing<TConverter>()` | ? |
| `ITypeConverter<Src,Dest>` | `ITypeConverter<TSource,TDest>` | ? |
| `IValueConverter<Src,Dest>` | `ITypeConverter` (то же?) | ? |
| `cfg.CreateMap<DateTime,string>().ConvertUsing(d=>d.ToString())` | лямбда конвертер | ? |

### Профили и конфигурация
| AutoMapper | IronMapper эквивалент | Статус |
|---|---|---|
| `class MyProfile : Profile` | `class MyProfile : MappingProfile` | ? |
| `cfg.AddProfile<MyProfile>()` | `options.AddProfile<MyProfile>()` | ? |
| `cfg.AddMaps(assembly)` | `options.AddProfilesFromAssembly(assembly)` | ? |
| `services.AddAutoMapper(assembly)` | `services.AddIronMapper(assembly)` | ? |

### Продвинутые возможности
| AutoMapper | IronMapper эквивалент | Статус |
|---|---|---|
| `.ReverseMap()` | `.ReverseMap()` | ? |
| `.IncludeMembers(s=>s.SubObject)` | вложенный маппинг? | ? |
| `.BeforeMap((src,dest)=>...)` | аналог? | ? |
| `.AfterMap((src,dest)=>...)` | аналог? | ? |
| `cfg.CreateMap<Src,Dest>().Include<SrcChild,DestChild>()` | наследование маппингов | ? |
| `ProjectTo<Dest>(queryable)` | нет (EF Core проекция) | ожидаемо отсутствует |

### Коллекции
| AutoMapper | IronMapper эквивалент | Статус |
|---|---|---|
| `mapper.Map<List<Dest>>(sourceList)` | `sourceList.MapToDestList()` | ? |
| `mapper.Map<Dest[]>(sourceArray)` | `sourceArray.MapToDestArray()` | ? |
| автомаппинг вложенных коллекций | генератор встраивает Select() | ? |

---

## Шаг 3 — Проверка migration guide

Прочитай файл `docs/migration-from-automapper.md` (если существует):
```bash
cat docs/migration-from-automapper.md
```

Проверь:
1. Все паттерны из Шага 2 со статусом ✅ задокументированы?
2. Все паттерны со статусом ❌ задокументированы как "не поддерживается" с альтернативой?
3. Есть ли примеры кода для каждой строки таблицы?
4. Есть ли раздел "Ключевые отличия" в начале?

---

## Шаг 4 — Проверка samples/

Прочитай файлы в `samples/`:
```bash
find samples/ -name "*.cs" | head -20
```

Проверь что примеры:
- Компилируются: `dotnet build samples/`
- Покрывают минимум: простой маппинг, Profile, ForMember, коллекции, DI
- Используют актуальный API (нет устаревших методов)

---

## Шаг 5 — Проверка README Quick Start

Прочитай секцию Quick Start в README.md:
```bash
cat README.md
```

Скопируй код из Quick Start и проверь что он:
1. Синтаксически корректен
2. Использует реально существующие типы IronMapper
3. Упоминает NuGet пакет с правильным именем `IronMapper`

---

## Формат вывода

```
=== IronMapper API Compatibility Report ===
Дата: {дата}

--- Покрытие AutoMapper паттернов ---
✅ Реализовано: N паттернов
⚠️ Частично:   N паттернов
❌ Отсутствует: N паттернов

--- Детали по категориям ---
Базовый маппинг:    ✅ N/N
ForMember:          ✅ N/N  ⚠️ N/N
Конвертеры:         ✅ N/N
Профили и DI:       ✅ N/N
Продвинутые:        ✅ N/N  ❌ N/N
Коллекции:          ✅ N/N

--- Отсутствующие паттерны (не в migration guide) ---
1. {AutoMapper API} — не реализован и не задокументирован как ограничение
   Рекомендация: добавить в docs/migration-from-automapper.md с workaround

--- Migration Guide ---
✅/❌ Файл существует
✅/❌ Все реализованные паттерны задокументированы
✅/❌ Все ограничения задокументированы с альтернативами
✅/❌ Примеры кода присутствуют

--- Samples ---
✅/❌ Компилируются
✅/❌ Покрывают ключевые сценарии
Отсутствующие сценарии: {список}

--- README Quick Start ---
✅/❌ Код синтаксически корректен
✅/❌ API актуален
✅/❌ NuGet имя правильное

--- Итог ---
Готовность к публикации: ДА / НЕТ
Критичных проблем: N
Что нужно добавить до публикации:
1. ...
2. ...
```
