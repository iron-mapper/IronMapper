# HANDOFF — сравнительные бенчмарки мапперов

Передача работы новой сессии (без контекста предыдущего разговора). Спецификация: [BENCHMARK_SPEC.md](BENCHMARK_SPEC.md),
правила репозитория: корневой [CLAUDE.md](../CLAUDE.md) (работать только в `benchmarks/`; `git add` можно,
`git commit` / `push` / `tag` — нельзя, это делает мейнтейнер). После каждого этапа — остановиться и дать отчёт.

Дата передачи: 2026-10-18.

---

## 1. Где мы

| Этап (§13) | Статус |
|---|---|
| 1. Каркас, props, проекты, модели, генератор данных | принят |
| 2. Мапперы + тесты эквивалентности и семантики | принят |
| 3. Runtime-бенчмарки (§7) | принят, плюс две доработки (см. ниже) |
| **4. Холодный старт (§8)** | **в работе: JIT готов, NativeAOT не выполнен** |
| 5. NativeAOT, footprint, build time (§9–11) | не начат |
| 6. Отчёт, README, полный прогон (§12) | не начат (есть черновик README) |

### Этап 4: что сделано

- `probes/StartupProbe.{Baseline,Manual,IronMapper,IronMapper_IMapper,Mapperly,Mapster,AutoMapper}`: каждая проба
  ссылается **только на свою библиотеку** + `Comparison.Models`; конфигурации подключены ссылками на исходники
  из `src/Comparison.Mappers` (`<Compile Include=... Link=...>`, общие настройки — `probes/StartupProbe.props`).
  Программа: создать заказ → настроить маппер → смаппить → напечатать `OrderChecksum` → выйти.
  `Baseline` маппер не использует и печатает контрольную сумму **исходника**; формула одна для Entity и DTO, поэтому
  все пробы обязаны печатать одно и то же число (на этой машине `12742788875462939321`).
- `src/Comparison.Benchmarks/StartupBenchmark.cs` — §8.1, `RunStrategy.ColdStart`, 20 запусков.
- `scripts/run-startup.ps1` (PowerShell 7, Windows/Linux/macOS): публикация JIT/AOT, 3 прогревочных + 30 запусков
  с чередованием проб, проверка exit code и контрольной суммы, медиана/p95/min/mean, Δ к Baseline.
  Каждый режим пишет свой CSV (`results/raw/startup-jit.csv`, `startup-aot.csv`); `results/startup.md` собирается из
  всех имеющихся CSV + отчёта BDN (`-ReportOnly` — только пересборка отчёта). AOT публикуется строго последовательно,
  после каждой пробы `dotnet build-server shutdown`; перед AOT проверяется свободная память (порог `-MinFreeMemoryGB 4`,
  в неинтерактивном запуске без `-Force` скрипт завершается); `-LowMemory` → `-p:IlcSingleThreaded=true`
  (проверено по `Microsoft.NETCore.Native.targets` ILCompiler 10.0.13: даёт `ilc --parallelism:1`).
- JIT-режим и BDN ColdStart прогнаны, `results/startup.md` сформирован (только JIT). Итог JIT: IronMapper/Mapperly
  ≈ Manual (+3 мс к старту процесса), `IronMapper_IMapper` +27 мс, AutoMapper +90 мс, Mapster +122 мс.

### Этап 4: что не сделано и почему

- **NativeAOT-режим не выполнен.** Первая попытка была остановлена Claude Code из-за нехватки памяти во время
  AOT-публикации `IronMapper_IMapper` (ILC требует много памяти; на ноутбуке 15.5 ГБ, свободно было ~3 ГБ).
  Вторая попытка остановлена защитой скрипта (свободно 3.0 ГБ < 4 ГБ). Успели опубликоваться в AOT без
  IL-предупреждений: Baseline, Manual, IronMapper; `IronMapper_IMapper` выдал
  `IL2104: Assembly 'IronMapper.Extensions.DI' produced trim warnings` (факт для §9).
- **JIT-результаты нужно переснять на новой машине**: CSV лежат в `results/raw/` (в `.gitignore`), и смешивать JIT
  с одной машины и AOT с другой нельзя. Поэтому продолжать — полным прогоном `-Mode Both` (см. §5 этого файла).

### Доработки этапа 3 (сделаны, но не все проверены прогоном)

- `CollectionBenchmark`: столбцы `ns/item` и `B/item` (`src/Comparison.Benchmarks/PerItemColumns.cs`,
  `[Config(typeof(PerItemConfig))]`). Собирается, **в прогоне BDN ещё не проверялись**.
- Manual сравнен со сгенерированным кодом IronMapper/Mapperly (включая JIT-дизассемблер) — лишней работы нет,
  Manual не менялся. Расхождение «Mapperly на 20 % быстрее Manual» в CustomConfig: один неудачный процесс
  (82 нс против ~55 нс) + ~6 % от Dynamic PGO (с `DOTNET_TieredPGO=0` разница исчезает). Отсюда решение
  `launchCount = 3` для итоговых замеров (записано в §7 спецификации и в README).

---

## 2. Решения сверх спецификации

| Раздел | Решение |
|---|---|
| §2, §5 | IronMapper настраивается **только через `MappingProfile`** (`src/Comparison.Mappers/IronMapper/ComparisonProfile.cs`); модели нейтральны, без атрибутов. Для Flat профиль генерирует байт-в-байт тот же код, что `[MapTo]` → строки `IronMapper_Profile` нет. Следствие: IM0001 для профилей не выдаётся (несопоставленные свойства пропускаются молча). |
| §2 | `UseLocalIronMapper` реализован в `Directory.Build.targets` (флаги проектов `UsesIronMapper` / `UsesIronMapperDI`); target `_KeepConfigurationForLocalIronMapper` сохраняет `Configuration` для ссылок на `../src`, иначе `-c Release` собирал их в Debug. |
| §3 | Доп. проект `src/Comparison.Mappers.IronMapperStaticFormat` (extern alias `StaticFormat`): строка `IronMapper_StaticFormat` в CustomConfig — `MapFrom(s => SalaryFormat.Format(s.Salary))` вместо `UseConverter<T>()`; подписывать как **обходной путь**, показывающий стоимость создания конвертера (§7 обновлён). Отдельная сборка — потому что IronMapper генерирует одну пару типов на проект (IM0007). |
| §5 | Mapster вызывается `Adapt<TSource, TDest>(config)` (§5 обновлён). Конвертер IronMapper — `UseConverter<T>()` (то, что пишет пользователь). |
| §5 | Ключ AutoMapper: `AUTOMAPPER_LICENSE_KEY`, иначе `~/.automapper/license.txt` (`AutoMapperSetup.ResolveLicenseKey()`); ключ нигде не логируется. |
| §5 | `IronMapper_IMapper`, коллекции: ленивый `MapCollection` + `ToList()` (`Map<List<T>>` не поддерживается, issue #8). |
| §6 | Тесты на `xunit.v3.mtp-off` 4.x (VSTest), т.к. `dotnet test` на SDK .NET 10 принимает MTP-проекты только с opt-in в `global.json`. |
| §6.4 | Матрица пишется тестом (`FeatureMatrixTests`) в `results/feature-matrix.md`, по-английски; null-случаи — в двух вариантах (non-nullable / nullable-аннотации, `NullableOrderEntity`), т.к. Mapperly генерирует код по аннотациям. Позиционный record в IronMapper не объявлен (CS7036 ломает сборку) → N/A. |
| §7 | `DiOverheadBenchmark`: Manual как baseline в каждой категории, методы с префиксами `Flat_` / `Nested_`; AutoMapper `IMapper` из `AddAutoMapper`, Mapster `IMapper` = `ServiceMapper` из scope. Артефакты BDN → `results/raw/bdn`. Итоговые замеры — **`launchCount = 3`** (принято; в коде ещё не зашито — передать `--launchCount 3` в `run-all.ps1` на этапе 6). |
| §8 | Пробы подключают конфигурации ссылками на исходники; DI-части настроек вынесены в `*.DependencyInjection.cs` / `IronMapperDiSetup.cs`, чтобы пробы не тянули лишние пакеты. Время — wall-clock от `Process.Start` до выхода (накладные расходы скрипта одинаковы → смотреть Δ). |

Заметки для итогового README собраны в черновике [README.md](README.md).

---

## 3. Известные особенности окружения (старый ноутбук)

- Windows 11, Intel Core Ultra 7 155H — **гибридный CPU**: P-ядра = логические 0, 1, 10–19 (маска `0xFFC03`),
  E/LP-E = 2–9, 20, 21. Привязка к P-ядрам на результат почти не влияла. На новой машине топология другая.
- 15.5 ГБ RAM; NativeAOT-публикация упиралась в память. При нехватке: `-LowMemory`. Claude Code останавливает фоновые
  команды при нехватке памяти (отключается переменной `CLAUDE_CODE_DISABLE_BG_SHELL_PRESSURE_REAP=1` при запуске
  Claude Code).
- .NET SDK 10.0.402 (есть и 10.0.113), runtime 10.0.13, `global.json` нет. PowerShell 7 (`pwsh`).
  NativeAOT на Windows требует MSVC C++ build tools — на старой машине были.
- Переменная `AUTOMAPPER_LICENSE_KEY` не была видна процессу Claude Code; использовался файл
  `~/.automapper/license.txt` (Community, до 2027-10-18). Без ключа AutoMapper работает, но пишет `Warning` в лог
  (асинхронно, ~через 1 с). **На новой машине ключ нужно положить заново** (файл или переменная) — он вне репозитория.
- В Git Bash длинные heredoc с C#-кодом ломались при разборе — файлы надёжнее писать инструментом записи файлов.

---

## 4. `.gitignore` и чистая машина

Проверено: ни корневой `.gitignore`, ни `benchmarks/.gitignore` не исключают ничего, что нужно для сборки
(игнорируются только `bin/`, `obj/`, `results/raw/`, `BenchmarkDotNet.Artifacts/`, `probes/BuildProbe.*/`, служебные
файлы IDE). Чего на новой машине **не будет** и это нормально:

- `results/raw/**` — все сырые результаты (CSV старта, отчёты BDN, опубликованные пробы, логи) → переснять;
- ключ AutoMapper (вне репозитория);
- `.claude/` (память сессий) — контекст передаётся этим файлом.

Перед переносом мейнтейнеру: убедиться, что всё в `benchmarks/` закоммичено (`git status benchmarks/`) —
в этой сессии коммитов не делалось.

---

## 5. Как продолжить

Требования: .NET SDK 10.0.4xx, PowerShell 7, toolchain NativeAOT (Windows: MSVC build tools; Linux: clang + zlib;
macOS: Xcode CLT), ключ AutoMapper в `~/.automapper/license.txt` или `AUTOMAPPER_LICENSE_KEY`.

```bash
cd benchmarks
dotnet build IronMapper.Comparison.slnx -c Release   # 0 ошибок, 0 предупреждений
dotnet test IronMapper.Comparison.slnx -c Release    # ожидается 128/128
pwsh ./scripts/run-startup.ps1 -Mode Both            # JIT + NativeAOT + BDN ColdStart → results/startup.md
```

Если не хватает памяти для AOT: `pwsh ./scripts/run-startup.ps1 -Mode Aot -LowMemory` (после `-Mode Jit`;
отчёт соберётся из обоих CSV). Затем — отчёт по этапу 4 и стоп; дальше этап 5 (§9–11).
