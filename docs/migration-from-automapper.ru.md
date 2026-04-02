# Миграция с AutoMapper

> 🌐 **[English version](migration-from-automapper.md)**

IronMapper спроектирован так, чтобы миграция с AutoMapper была максимально безболезненной. Fluent API намеренно схож.

---

## Таблица соответствия API

| AutoMapper | IronMapper | Примечание |
|---|---|---|
| `[assembly: AutoMapper.AutoMapAttribute]` | `[MapTo(typeof(TDest))]` на source | Атрибут на source-типе |
| `CreateMap<Src, Dest>()` в `Profile` | `CreateMap<Src, Dest>()` в `MappingProfile` | Одинаковое имя метода |
| `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | Идентичный синтаксис |
| `ForMember(d => d.X, o => o.Ignore())` | `ForMember(d => d.X, o => o.Ignore())` | Идентичный синтаксис |
| `o.MapFrom<TConverter>()` | `opt.UseConverter<TConverter>()` | Небольшое отличие в имени |
| `ConvertUsing<TConverter>()` | `ConvertUsing<TConverter>()` | Одинаковое имя метода |
| `cfg.CreateMapper()` | `services.AddIronMapper(assembly)` | Однострочная DI-регистрация |
| `mapper.Map<Dest>(source)` | `source.MapToDest()` **или** `mapper.Map<Dest>(source)` | Extension method предпочтителен |
| `mapper.Map(source, destination)` | `source.MapToDest(destination)` **или** `mapper.Map<Src, Dest>(source, dest)` | Обновление существующего объекта |
| `mapper.Map<IEnumerable<Dest>>(list)` | `list.MapToDestList()` / `list.MapToDestArray()` | Специализированные collection helpers |
| `ReverseMap()` | Пока не поддерживается | Объявляй оба направления явно |
| `BeforeMap` / `AfterMap` | Пока не поддерживается | Используй лямбду `ConvertUsing` как обходное решение |
| `IncludeMembers` | Пока не поддерживается | Маппи включённые члены явно |
| `ValueTransformers` | Пока не поддерживается | Используй `[MapConverter]` для каждого свойства |

---

## Пошаговая миграция

### 1. Замени NuGet-пакеты

```bash
dotnet remove package AutoMapper
dotnet remove package AutoMapper.Extensions.Microsoft.DependencyInjection

dotnet add package IronMapper
dotnet add package IronMapper.Extensions.DI
```

### 2. Замени базовый класс profile

```diff
- using AutoMapper;
+ using IronMapper.Configuration;

- public class OrderProfile : Profile
+ public class OrderProfile : MappingProfile
```

### 3. Замени DI-регистрацию

```diff
- services.AddAutoMapper(typeof(Program).Assembly);
+ services.AddIronMapper(typeof(Program).Assembly);
```

### 4. Замени namespace для `IMapper`

```diff
- using AutoMapper;
+ using IronMapper.Interfaces;
```

Интерфейс `IMapper` в IronMapper предоставляет `Map<TDest>(source)`, `Map<TSource, TDest>(source)`, `Map<TSource, TDest>(source, destination)` и `MapCollection<TSource, TDest>(source)` — все часто используемые перегрузки AutoMapper.

### 5. Опционально — переходи на extension methods

AutoMapper требует внедрения `IMapper` везде. IronMapper генерирует строго типизированные extension methods на source-типе, поэтому там, где типы известны во время компиляции, можно убрать DI-зависимость:

```diff
- var dto = _mapper.Map<OrderDto>(entity);
+ var dto = entity.MapToOrderDto();
```

---

## Ключевые отличия

| | AutoMapper | IronMapper |
|---|---|---|
| Расположение кода маппинга | Runtime, на основе reflection | Compile time, сгенерированный C# |
| Ошибки конфигурации | Нет (runtime `AutoMapperConfigurationException`) | Да (ошибки компилятора / предупреждения IM00xx) |
| NativeAOT / trimming | Требует дополнительных аннотаций | Полностью совместим из коробки |
| Лицензия | MIT | MIT |
| Размер пакета | ~500 КБ | ~30 КБ |
| Производительность | ~2–5 мкс / объект (reflection) | ~0 нс накладных расходов (обычное присваивание свойств) |
