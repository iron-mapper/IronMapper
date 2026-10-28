# Сравнительные бенчмарки мапперов — спецификация

Документ для исполнителя (Claude Code). Положить в `benchmarks/BENCHMARK_SPEC.md`.
Работа идёт поэтапно; после каждого этапа — остановиться и отчитаться (что сделано, что не получилось, какие решения приняты сверх спецификации).

---

## 0. Цель

Честное, воспроизводимое сравнение IronMapper с другими мапперами .NET по скорости, памяти, времени старта, совместимости с NativeAOT, размеру и влиянию на сборку. Результаты должны выдерживать критику: любой читатель может склонировать репозиторий, запустить один скрипт и получить сопоставимые цифры.

Главный принцип: **все мапперы в замеряемом коде выполняют одинаковую работу**. Если какой-то маппер не умеет сценарий — он в этом сценарии помечается N/A в матрице возможностей, а не «подгоняется» нечестно.

---

## 1. Правила работы в репозитории

- Работать **только** внутри `benchmarks/`. Не трогать `src/`, `tests/` (включая существующий `tests/IronMapper.Benchmarks`), `docs/`, корневой `README*`, `.github/`, корневые `Directory.Build.props` и `IronMapper.slnx`.
- Соблюдать корневой `CLAUDE.md`: никаких `git commit`, `git push`, `git tag` — это делает мейнтейнер. `git add` разрешён.
- Не менять CI. Бенчмарки в CI не добавлять.
- Секреты не коммитить. Лицензионный ключ AutoMapper — только из переменной окружения `AUTOMAPPER_LICENSE_KEY`.
- Целевой фреймворк: `net10.0`. Язык: `latest`.

---

## 2. Участники

| Id в отчётах | Библиотека | Версия | Режим |
|---|---|---|---|
| `Manual` | — | — | Ручной код, **baseline** (`[Benchmark(Baseline = true)]`) |
| `IronMapper` | IronMapper | 1.1.3 (NuGet) | Сгенерированные extension-методы (`source.MapToXxx()`) |
| `IronMapper_IMapper` | IronMapper + IronMapper.Extensions.DI | 1.1.3 | Через `IMapper` из DI (внутри reflection + `MethodInfo.Invoke`) — отдельная строка, т.к. это путь, которым пойдут пользователи DI |
| `Mapperly` | Riok.Mapperly | последняя стабильная | Source generator |
| `Mapster` | Mapster | последняя стабильная 10.x | Runtime, отдельный `TypeAdapterConfig`, `Compile()` в setup |
| `AutoMapper` | AutoMapper | последняя стабильная (16.x) | Runtime, `MapperConfiguration`, ключ из env |

Опционально: `Mapster_Codegen`, если у Mapster 10 есть путь кодогенерации, который работает обычным `dotnet build` без ручных шагов. Если нет — пропустить и упомянуть в README.

Все версии пакетов зафиксировать централизованно в `benchmarks/Directory.Packages.props` (Central Package Management). Точные версии вписать в итоговый отчёт.

### Как подключается IronMapper

По умолчанию — `PackageReference` на опубликованный пакет 1.1.3 (мерим то, что получает пользователь, и не зависим от текущей разработки).
Переключатель для мейнтейнера: `dotnet run -c Release -p:UseLocalIronMapper=true` → `ProjectReference` на `../../src/IronMapper` и на генератор (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`). Реализовать условием в `.csproj` / `Directory.Build.props`.

---

## 3. Структура

```
benchmarks/
  BENCHMARK_SPEC.md
  README.md                          методология, как запускать, ссылки на результаты
  Directory.Build.props              СВОЙ, НЕ импортирует корневой (см. ниже)
  Directory.Packages.props
  IronMapper.Comparison.slnx         отдельное решение, в корневой .slnx не добавлять
  src/
    Comparison.Models/               entity + DTO, генератор тестовых данных
    Comparison.Mappers/              по папке на библиотеку: конфиги/профили/[Mapper]-классы
                                     + единый фасад (см. п.5)
    Comparison.Benchmarks/           BenchmarkDotNet, Exe
  tests/
    Comparison.Tests/                эквивалентность результатов (xUnit)
  probes/
    StartupProbe.<Lib>/              мини-консоли для замера старта и NativeAOT
    BuildProbe.<Lib>/                проекты для замера времени сборки (генерируемые)
  scripts/
    run-all.ps1                      полный прогон всего (+ run-all.sh)
    run-startup.ps1
    run-aot.ps1
    run-build-time.ps1
    generate-report.ps1              сборка итогового RESULTS.md
  results/                           выход прогонов (артефакты BDN в .gitignore, итоговый RESULTS.md — коммитится)
```

**Важно про `Directory.Build.props`.** Корневой файл включает `TreatWarningsAsErrors`, `GenerateDocumentationFile`, `Version` и свойства упаковки NuGet. Бенчмарки не должны их наследовать: MSBuild берёт ближайший `Directory.Build.props`, поэтому свой файл в `benchmarks/` без `<Import>` корневого изолирует решение. В своём файле: `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `IsPackable=false`, `TreatWarningsAsErrors=false`, `ManagePackageVersionsCentrally=true`.

---

## 4. Модели и данные

Модели общие для всех мапперов. Обычные классы с `{ get; set; }` и конструктором без параметров (позиционные records IronMapper не поддерживает — см. матрицу возможностей).

1. **Flat** — `UserEntity` → `UserDto`, ~15 свойств: `int`, `long`, `string` ×4, `decimal`, `double`, `bool`, `DateTime`, `DateTimeOffset`, `Guid`, `enum` (тот же тип enum в обоих), `int?`, `decimal?`.
2. **Nested** — `OrderEntity` → `OrderDto`:
   - `Customer` (`CustomerEntity` → `CustomerDto`), внутри `Address` (`AddressEntity` → `AddressDto`) — вложенные **одиночные** объекты, глубина 3;
   - `List<OrderLineEntity>` → `List<OrderLineDto>` (5–20 элементов, детерминированно), у строки — вложенный `ProductEntity` → `ProductDto`.
3. **CustomConfig** — `EmployeeEntity` → `EmployeeDto`: одно переименование свойства, одно игнорируемое, один конвертер (`decimal` → `string` формата `"F2"`, InvariantCulture), одно вычисляемое (`FullName = FirstName + " " + LastName`).

Правила для данных в замеряемом коде:
- Нет `null` в ссылочных свойствах и коллекциях (поведение с `null` у библиотек разное — это проверяется отдельно, см. п.6).
- Нет коллекций с одинаковым типом элемента (например `List<string>` → `List<string>`): IronMapper копирует ссылку, остальные могут создавать новый список — это разная работа. Поведение описывается в матрице, но в тайминги не попадает.
- Генерация детерминированная: `new Random(42)` (или Bogus с фиксированным seed). Данные создаются в `[GlobalSetup]` и не входят в замер.

---

## 5. Мапперы

Каждая библиотека реализует общий интерфейс в `Comparison.Mappers`, чтобы тесты и бенчмарки были одинаковыми:

```csharp
public interface IComparisonMapper
{
    string Name { get; }
    UserDto MapFlat(UserEntity src);
    OrderDto MapNested(OrderEntity src);
    List<OrderDto> MapCollection(List<OrderEntity> src);
    EmployeeDto MapCustom(EmployeeEntity src);
    void MapInPlace(UserEntity src, UserDto dest);   // может бросать NotSupportedException
}
```

Интерфейс используется **в тестах**. В бенчмарках вызывать библиотеки **напрямую** (без интерфейсного/виртуального вызова), чтобы не добавлять одинаковый, но лишний overhead и не мешать инлайнингу у source-генераторов.

Особенности, которые надо учесть:

- **IronMapper — вложенные одиночные объекты не маппятся автоматически** (docs/advanced.md, раздел «Nested objects»). Использовать документированный обходной путь через профиль:
  `CreateMap<CustomerEntity, CustomerDto>().ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()));`
  Это честно (так и будет писать пользователь), но отметить в матрице возможностей как «требует ручной настройки».
- **IronMapper — Flat**: мерить через атрибуты `[MapTo]`. Если профиль генерирует идентичный код — отдельная строка не нужна; если нет — добавить `IronMapper_Profile`.
- **IronMapper — коллекция**: `list.MapToOrderDtoList()`.
- **IronMapper_IMapper**: собрать `ServiceProvider` с `AddIronMapper(...)` в `[GlobalSetup]`, резолвить `IMapper` один раз, в замере — только `mapper.Map<...>(...)`. Коллекции через `IMapper` — `MapCollection<TSource, TDest>` (`Map<List<T>>` не поддерживается, issue #8).
- **Mapperly**: `[Mapper] partial class`; in-place — метод с `[MappingTarget]`; кастомная конфигурация — `[MapProperty]`, `[MapperIgnoreTarget]`/`[MapperIgnoreSource]`, user-implemented метод для конвертера.
- **Mapster**: собственный `TypeAdapterConfig` (не `GlobalSettings`), конфигурация + `config.Compile()` в setup, вызов `src.Adapt<TSource, TDest>(config)` — типизированная перегрузка, как `Map<TSource, TDest>` у AutoMapper (`Adapt<TDest>(config)` принимает `object` и определяет тип источника во время выполнения). Проверить, что вложенные объекты создаются заново, а не копируются по ссылке.
- **AutoMapper**: `MapperConfiguration` с `cfg.LicenseKey = Environment.GetEnvironmentVariable("AUTOMAPPER_LICENSE_KEY")` (или переменная окружения, если версия читает её сама), `ILoggerFactory` — `NullLoggerFactory`, `CreateMapper()` в setup. В тестах — `AssertConfigurationIsValid()`.
- **Manual**: простой ручной код, без LINQ там, где он не нужен; коллекции — `new List<T>(capacity)` + цикл. (Отметить в README: если IronMapper генерирует `Enumerable.ToList(Enumerable.Select(...))`, разница с Manual в коллекциях частично объясняется этим — это полезная обратная связь для генератора.)

---

## 6. Тесты корректности (`Comparison.Tests`) — обязательный шлюз

Бенчмарки не запускаются, пока тесты не зелёные.

1. **Эквивалентность**: для каждого маппера и каждого сценария результат структурно равен результату `Manual` (`Assert.Equivalent(expected, actual, strict: true)` из xUnit или собственный компаратор). FluentAssertions не использовать (коммерческая лицензия с v8).
2. **Глубокое копирование**: для всех типов, которые маппятся в другой тип, ни один объект/коллекция результата не является ссылкой на объект источника (`Assert.NotSame`), и вложенные DTO — новые экземпляры.
3. **In-place**: объект назначения тот же экземпляр, свойства обновлены.
4. **Семантика граничных случаев — не шлюз, а сбор фактов** для матрицы возможностей: `null` во вложенном объекте, `null`-коллекция, коллекция с одинаковым типом элемента (копия или ссылка), позиционный record как назначение, flattening (`Customer.Address.City` → `CustomerAddressCity`). Тест фиксирует фактическое поведение каждой библиотеки (через `Theory` + запись результата), падения ожидаемы и оформляются как «не поддерживается», а не как красные тесты. Результат — таблица в `results/feature-matrix.md`.

---

## 7. Runtime-бенчмарки (`Comparison.Benchmarks`)

Общее: `[MemoryDiagnoser]`, `[CategoriesColumn]`, `[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]`, `RatioColumn` относительно `Manual`, Release, `Program` через `BenchmarkSwitcher` (запуск всех или по фильтру). Каждый бенчмарк возвращает результат (защита от dead code elimination). Экспорт: Markdown (GitHub), CSV, HTML, JSON.

Итоговые замеры — `launchCount = 3` (каждый бенчмарк в трёх отдельных процессах). Причина: результат одного процесса зависит от решений Dynamic PGO и может заметно отличаться от другого процесса с тем же кодом (на этапе 3 один запуск дал `Manual` 82 нс вместо ~55 нс). Прогоны `--job short` / `dry` — только проверка работоспособности.

| Класс | Что мерим | Параметры |
|---|---|---|
| `FlatBenchmark` | Один плоский объект | — |
| `NestedBenchmark` | Один Order с вложенными объектами и строками | — |
| `CollectionBenchmark` | `List<OrderEntity>` → `List<OrderDto>` | `[Params(10, 1_000, 100_000)]` |
| `CustomConfigBenchmark` | Переименование/игнор/конвертер/вычисляемое. Дополнительная строка `IronMapper_StaticFormat`: `MapFrom(s => SalaryFormat.Format(s.Salary))` вместо `UseConverter<T>()` — обходной путь, показывает стоимость создания конвертера (в README и отчёте подписать именно так) | — |
| `InPlaceBenchmark` | Обновление существующего объекта | только поддерживающие |
| `DiOverheadBenchmark` | IronMapper ext. method vs `IronMapper_IMapper` vs AutoMapper `IMapper` vs Mapster `IMapper` (если есть) — Flat и Nested | — |

Для 100 000 элементов — убедиться, что BDN не урезает итерации до статистически бессмысленных (при необходимости `[SimpleJob(iterationCount: ...)]` только для этого класса). Gen0/Gen1/Gen2 должны попасть в отчёт: на больших коллекциях это важная часть картины.

Многопоточный throughput не мерим: все мапперы в замеряемом пути stateless, отдельный сценарий ничего нового не покажет. Упомянуть в README в разделе «Чего здесь нет и почему».

---

## 8. Холодный старт

Две независимые методики:

**8.1. In-process (BDN):** `StartupBenchmark` с `[SimpleJob(RunStrategy.ColdStart, launchCount: 20, warmupCount: 0, iterationCount: 1)]`. Каждый бенчмарк: создать конфигурацию с нуля (для source-генераторов — ничего) + первый `MapNested`. Это показывает стоимость компиляции expression trees и JIT первого вызова.

**8.2. Process start (`probes/StartupProbe.<Lib>`):** отдельная консоль на каждую библиотеку + пустая консоль-baseline. Программа: настроить маппер, смаппить один Order, вывести контрольную сумму, выйти. `scripts/run-startup.ps1` запускает каждую N=30 раз (после 3 прогревочных запусков для дискового кэша) и считает медиану/p95 wall-clock времени от старта процесса до выхода. Два варианта публикации: обычный framework-dependent (JIT) и NativeAOT (см. п.9). Результат — `results/startup.md`.

---

## 9. NativeAOT и trimming

Для каждого `StartupProbe.<Lib>`:
1. `dotnet publish -c Release -r <текущий RID> -p:PublishAot=true`.
2. Собрать все предупреждения IL2xxx/IL3xxx (анализаторы trimming/AOT), сгруппировать по библиотеке.
3. Запустить бинарник и проверить, что контрольная сумма совпадает с JIT-версией (то есть маппинг реально работает, а не падает в рантайме).
4. Записать размер исполняемого файла и разницу с пустым baseline-приложением.

Отдельно проверить `IronMapper_IMapper` (DI-путь использует reflection; по документации считается несовместимым — подтвердить или опровергнуть фактом).

`scripts/run-aot.ps1` → `results/aot.md`: таблица «Библиотека | Предупреждения | Работает в AOT | Размер бинарника | Δ к baseline | Время старта AOT».

Для IronMapper это первая реальная проверка AOT (в docs/advanced.md помечено как «Not verified») — результат важен вне зависимости от того, какой он.

---

## 10. Размер и зависимости

`results/footprint.md`:
- размер `.nupkg` каждой библиотеки и список транзитивных зависимостей (`dotnet list package --include-transitive`);
- Δ размера publish-папки framework-dependent `StartupProbe.<Lib>` относительно baseline;
- Δ размера AOT-бинарника (из п.9).

---

## 11. Влияние на время сборки

Source-генераторы работают при компиляции, runtime-мапперы — при старте. Честно показать обе стороны.

- `scripts/run-build-time.ps1` генерирует для каждой библиотеки проект `probes/BuildProbe.<Lib>` с **N = 50 и N = 200** пар entity/DTO (по ~10 свойств, часть с вложенной коллекцией), плюс baseline-проект с теми же моделями без маппера.
- Замер: `dotnet build --no-incremental -c Release` × 10 запусков после прогрева (`dotnet build-server shutdown` между сериями, а не между запусками), медиана и разброс. Если в системе есть `hyperfine` — использовать его.
- Дополнительно, если возможно: время работы генератора из `-p:ReportAnalyzer=true` / binlog для IronMapper и Mapperly.
- Сгенерированные проекты не коммитить (генерируются скриптом), в `.gitignore`.

Результат — `results/build-time.md`.

---

## 12. Отчёт

`scripts/generate-report.ps1` собирает `results/RESULTS.md`:
1. Окружение: CPU, ОС, версия .NET SDK/runtime, версии всех пакетов, дата, коммит.
2. Сводная таблица «на одной странице»: по строке на библиотеку, столбцы — Flat, Nested, Collection(100k), Allocated(100k), Cold start, AOT работает, Δ размера, Δ сборки.
3. Полные таблицы BDN по каждому сценарию.
4. Матрица возможностей (`feature-matrix.md`).
5. Startup, AOT, footprint, build time.
6. Графики: Mermaid `xychart-beta` (рендерится на GitHub) для Collection по размерам и для cold start.
7. Рядом — `results/summary.json`: те же числа сводной таблицы (и все размеры Collection, .NET 8, in-process cold start) без форматирования, с окружением, коммитом и версиями пакетов; `schemaVersion` меняется при переименовании или удалении поля. Для сайта проекта и других потребителей, которым нельзя разбирать Markdown.

`benchmarks/README.md`: что и зачем мерим, методика и правила честности, как запустить (одной командой и по частям), как подключить ключ AutoMapper, как переключиться на локальный IronMapper, раздел «Чего здесь нет и почему», ссылка на `results/RESULTS.md`. Тон нейтральный: таблицы и факты, без маркетинговых формулировок.

---

## 13. Этапы и критерии готовности

| Этап | Содержание | Готово, когда |
|---|---|---|
| 1 | Каркас: решение, props, проекты, модели, генератор данных | `dotnet build benchmarks/IronMapper.Comparison.slnx -c Release` без ошибок; корневое решение собирается как раньше |
| 2 | Мапперы всех библиотек + тесты эквивалентности и семантики | `dotnet test` зелёный (кроме сбора фактов п.6.4), `feature-matrix.md` черновик |
| 3 | Runtime-бенчмарки (п.7) | Прогон `--job short` по всем классам проходит; показан пример вывода |
| 4 | Холодный старт (п.8) | Оба метода работают, `startup.md` сформирован |
| 5 | NativeAOT, footprint, build time (п.9–11) | Скрипты работают на текущей ОС, отчёты сформированы |
| 6 | Отчёт и README (п.12), полный прогон | `run-all.ps1` выполняет всё; `RESULTS.md` заполнен реальными цифрами |

На каждом этапе: если что-то в спецификации не работает как описано (API библиотеки отличается, сценарий невозможен) — не обходить молча, а описать в отчёте этапа и предложить решение.
