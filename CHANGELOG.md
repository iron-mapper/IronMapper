# Changelog

## [Unreleased]

### Fixed

- Documentation site preparation excludes repository instruction files (`AGENTS.md` and `AGENTS.override.md`)
  from documentation and hand-written pages, while retaining EN/RU pairing checks for user documentation.

### Added

- **Constructors and positional records** ([#24](https://github.com/iron-mapper/IronMapper/issues/24)): a destination
  without an accessible parameterless constructor (a positional record such as `record StateDto(string? Address,
  DateTimeOffset? ExpiresAt)`, a class whose constructors all take parameters) is created by its constructor, with named
  arguments, in attribute mappings, profiles and `ReverseMap()`. A parameter takes the source property of the same name
  (ignoring case; `[MapProperty("param")]` and `[MapConverter]` on it in attribute mappings) with the conversions of a
  property: implicit conversions (widening to nullable), collections (`T[]` → `IReadOnlyList<T>`, elements mapped one
  by one), nested objects through the mapping of their pair with the same `null` checks; a parameter with a default
  value and no source keeps the default value. In a profile, `ForMember` of the property with the same name configures
  the parameter: `MapFrom` (a computed parameter), `UseConverter`, `Ignore()` (the default value). The destination
  properties that the constructor does not set are filled by an object initializer after it. Of several constructors,
  the one with the most parameters mapped from the source among those that can be called, then the one with the most
  parameters; nothing in the destination type selects it (no IronMapper attribute in domain types). An exception thrown
  by the constructor reaches the caller as it is. A destination with an accessible parameterless constructor, or a
  struct, is created with an object initializer as before (the generated code of such mappings does not change). Up to
  2.0.0 such a destination failed with `CS7036` inside the generated file, so no build that compiles with 2.0.0 changes.
  **No in-place overload** is generated for a destination created by a constructor (updating only the other properties
  would silently keep the old values of what only the constructor takes): `IMapper.Map(source, destination)` throws
  `MappingException` that says so, and the in-place update of a parent object assigns a new instance of such a nested object. See
  [Constructors and positional records](docs/advanced.md#constructors-and-positional-records).
- **Diagnostics `IM0017` and `IM0018` (errors):** two constructors of the destination fit equally well (`IM0017`; the
  order of declaration does not decide; choose one with `ConstructWith`), or the destination has no public or internal
  constructor, such as a domain type created by a static factory method (`IM0018`; name the factory with
  `ConstructWith`). The mapping is not generated; up to 2.0.0 the build failed inside the generated file (`CS7036`,
  `CS0122`). See
  [IM0017](docs/diagnostics.md#im0017--constructor-of-the-destination-is-ambiguous) and
  [IM0018](docs/diagnostics.md#im0018--destination-has-no-accessible-constructor).
- **Factories: `ConstructWith`, `ForCtorParam`, `ConstructUsing`** (profiles). `CreateMap<S, D>().ConstructWith(D.Restore)`
  creates the destination with a static factory method, also when its constructor is private;
  `ConstructWith((string name, int age) => new D(name, age))` with a lambda with typed parameters, which also chooses one
  of several constructors. The parameters of the factory are matched to the source as those of a constructor (ignoring
  case, default values, `ForMember` of the property of the same name, nested objects, also created by a factory, and
  collections); the properties with a public `set` are assigned after the call; an exception of the factory reaches the
  caller as it is. `ForCtorParam("name", o => o.MapFrom(...))` configures a parameter of the constructor or the factory
  that has no property of the same name. `ConstructUsing(s => new D(...))` creates the destination with the expression
  as written, as in AutoMapper (no parameters matched; the in-place overload ignores it). New diagnostics: `IM0019`
  (error, `ForCtorParam` names no parameter), `IM0020` (error, a `ConstructWith` argument that the generated code cannot
  call: an instance or private method, a wrong or nullable return type, an overloaded method group, a lambda without
  parameter types), `IM0021` (warning, an `init` property cannot be set after the factory). No in-place overload for a
  destination created by a constructor with parameters or a factory: this is final (updating only the other properties
  would silently keep the old values of what only the constructor takes). See
  [Factories](docs/advanced.md#factories-constructwith).
- **Value conversions through the mappings of type pairs** (profiles and attribute mappings): with
  `CreateMap<Guid, UserId>().ConvertUsing(UserId.From)` and `CreateMap<UserId, Guid>().ConvertUsing(v => v.Value)`, every
  property, constructor or factory parameter and collection element whose types are that pair is converted by its
  mapping (the private `MapToUserIdCore`, as for a nested object), also primitives and structs such as a
  `readonly record struct` value object — no more `MapFrom(s => s.OwnerId.Value)` or `UserId.From` in each mapping.
  `Nullable<T>` is lifted from the same pair (`Guid?` → `UserId?`, `null` → `null`; `Guid` → `UserId?`); an exact
  mapping of the nullable pair wins; narrowing (`UserId?` → `Guid`) stays `IM0002`. The pair mapping may be in another
  profile or in a referenced project; `MapFrom`, `UseConverter`, `Ignore` and `[MapConverter]` still take precedence;
  nothing is found by convention. Without a pair mapping the property gets `IM0002` with the message of 2.0.0, so no
  build that compiles with 2.0.0 changes. A constructor that needs such a conversion is chosen only when no other one
  can be called. An exception of the converter reaches the caller as it is. See
  [Value conversions](docs/advanced.md#value-conversions-pair-mappings).
- **Diagnostic `IM0023` (warning): a nullable value for a non-nullable member**
  ([#54](https://github.com/iron-mapper/IronMapper/issues/54)). `string?` → `string` (and any `T?` → `T` of a reference
  type) for a property, a constructor or factory parameter, the expression of `MapFrom`, collection elements assigned as
  is, a converter or a transformer that takes a non-nullable value, and the elements of a collection mapped one by one
  (`List<Line?>` → `List<LineDto>`/`List<LineDto?>`: a `null` element throws `ArgumentNullException`, as in 2.0.0) is
  reported at your mapping, naming the member, the types and the fix (`MapFrom(s => s.X!)`, `ForCtorParam` for a
  parameter without a property). Up to 2.0.0 the compiler reported these places inside the generated file (`CS8601`,
  `CS8604`); the generated code now marks exactly that value with `!`, only together with `IM0023`, so the generated file
  has no nullable warning. The mapped values do not change. Builds keep their result (checked against the 2.0.0
  package): `TreatWarningsAsErrors` fails as before (now with `IM0023`), `<Nullable>disable</Nullable>` gets no warning,
  and `IM0023` takes the project-wide configuration of the compiler warning it replaces: suppressed (`NoWarn`, a
  `.globalconfig`), it is not reported; an error (`<WarningsAsErrors>nullable</WarningsAsErrors>`), it is an error. The one exception is `WarningsNotAsErrors=CS8601;CS8604` with `TreatWarningsAsErrors` (add `IM0023` there), see
  [Upgrading to 2.1](docs/upgrading-to-2.1.md#3-nullable-warnings-move-from-the-generated-file-to-im0023). A collection
  whose elements differ only by `?` (`List<string?>` → `List<string>`) is assigned as is (it was `IM0002`). See
  [IM0023](docs/diagnostics.md#im0023--nullable-value-mapped-to-a-non-nullable-member).
- **`ReverseMap(converter)`** declares both directions of a value pair in one line:
  `CreateMap<Guid, UserId>().ConvertUsing(UserId.From).ReverseMap(v => v.Value)` is the same as a second
  `CreateMap<UserId, Guid>().ConvertUsing(v => v.Value)` (a lambda or a static method group; `Nullable<T>` is lifted in
  the reverse direction too). `ReverseMap()` without an argument is unchanged.

- **Nested objects are mapped automatically** ([#23](https://github.com/iron-mapper/IronMapper/issues/23)): a
  name-matched property whose types differ (`Customer.Address` → `CustomerDto.Address` of type `AddressDto`) is mapped by
  the mapping of its type pair — of this project (also of a base type or an interface of the source type) or of a
  referenced assembly — in attribute mappings, profiles and `ReverseMap()`, without `ForMember`. `null` follows the
  nullable annotations: `null` → `null` for a nullable destination property; for a non-nullable one a `null` at run time
  (an EF navigation declared `= null!` and not loaded) throws `InvalidOperationException` whose message names both
  properties and suggests `Include` (not a `NullReferenceException` inside the mapping); a nullable source for a
  non-nullable destination is `IM0002` (no `null!`). `MapFrom`, `UseConverter`, `Ignore`, `[MapConverter]` and `[Ignore]`
  still take precedence. Up to 2.0.0 such a property failed with `CS0029` inside the generated file, so no build that
  compiles with 2.0.0 changes. See [Nested objects](docs/advanced.md#nested-objects) and
  [Null in a non-nullable nested object](docs/advanced.md#null-in-a-non-nullable-nested-object).
- **The in-place overload updates existing nested objects** (`source.MapToX(destination)`, `IMapper.Map(source,
  destination)`): when `destination.Customer` is not `null`, its properties are mapped into it (any depth), so a tracked
  EF navigation keeps its instance instead of being replaced (a duplicate insert, a tracking conflict, a delete and insert
  of an owned type); a `null` destination value gets a new object, as AutoMapper does. A new object is still assigned
  for a struct, a destination type with a mapped `init` property (a record with `init` properties), a `ConvertUsing`
  mapping, and a mapping of a referenced project whose in-place overload is turned off; a referenced project's public
  in-place `MapToX(source, destination)` is called for the existing object (maps in one project, the update in another).
  Works also with the in-place overload turned off in this project: the private in-place
  `MapToXCore(source, destination)` is generated for every mapping. See
  [In-place update of nested objects](docs/advanced.md#in-place-update-of-nested-objects).
- **Diagnostic `IM0004` (warning) is reported** for a cycle of mappings through at least one automatically mapped
  nested object (`Employee.Manager`, `Order.Customer.Orders`), with the chain of mappings in the message. Cycles only
  through collections (trees) are not reported, and a property of the chain configured with `MapFrom` or `Ignore`
  breaks the cycle. A cycle of types recurses forever only when the data has a cycle; suppress the warning for finite
  models with `#pragma warning disable IM0004` or `dotnet_diagnostic.IM0004.severity = none`. Up to 2.0.0 `IM0004` was
  declared as an error and never reported. See [IM0004](docs/diagnostics.md#im0004--nested-object-mapping-closes-a-cycle).
- **Code fix "add the mapping for the pair"** for `IM0002` of a nested object or a collection element without a
  mapping: `[MapTo(typeof(D))]` on the source type of an attribute mapping (`[MapFrom(typeof(S))]` on the destination
  type when the source type is not in source code), `CreateMap<S, D>();` after the statement of the `CreateMap`/
  `ReverseMap()` call in a profile. It reads the pair from the diagnostic properties.
- **Diagnostic `IM0002` (error) is reported** for a destination property that the generated assignment cannot fill:
  the types have no implicit conversion (`int?` → `int`, `long` → `int`, `string` → `Guid`, an enum → another enum,
  `List<string>` → `string[]`), a nested object or a collection whose type pair has no mapping (the message names the
  pair and what to add), a nullable nested object for a non-nullable destination, or a `MapFrom` expression of another
  type. It points to the
  destination property (attribute mappings), the `CreateMap` or `ReverseMap()` call, or the `MapFrom` expression, and
  says what to add (a converter, `MapFrom`, the missing mapping, `[Ignore]`/`Ignore()`). The property is not generated;
  the rest of the mapping is. Up to 2.0.0 the build failed inside the generated file (`CS0029`, `CS0266`, `CS1061`), and
  `IM0002` was declared but never reported. Mappings that compiled with 2.0.0 get no `IM0002`: the check accepts every
  implicit conversion (also user-defined operators and `dynamic`), and an element mapping is found also through a base
  type or interface of the source element, in a referenced assembly, or as a hand-written extension method that the
  generated call binds to. A mapping with `ConvertUsing` is checked too, because its in-place overload assigns the
  properties. See [IM0002](docs/diagnostics.md#im0002--property-types-are-incompatible-or-have-no-mapping).
- **`null` source → `null`, opt-in** (`<IronMapperNullSourceReturnsNull>true</IronMapperNullSourceReturnsNull>`, part of
  [#27](https://github.com/iron-mapper/IronMapper/issues/27)): `MapToX(this S? source)` returns `D?` with
  `[return: NotNullIfNotNull("source")]` and `null` for a `null` source, instead of `ArgumentNullException`. A non-null
  source still gives a non-null result for the compiler, so code written for the default gets no new nullable warning
  (checked with `TreatWarningsAsErrors`, also on netstandard2.0, where the generator declares an internal
  `NotNullIfNotNullAttribute`). Without the property nothing changes. A struct source or destination, the in-place
  overload, a `null` collection element, nested objects and `IMapper` behave as without it. Planned to be the default in
  3.0.0. See [`null` source](docs/configuration.md#null-source-ironmappernullsourcereturnsnull).
- **`IronMapperEmitPolyfills`** (MSBuild, default `true`): `false` stops declaring the internal copies of
  `ModuleInitializerAttribute` (the registration for `IMapper`) and `NotNullIfNotNullAttribute` (`IronMapperNullSourceReturnsNull`)
  on targets without them (netstandard2.0, .NET Framework). For projects where PolySharp or another generator declares
  them: two copies are error `CS0101`, and one generator cannot see the other's output. Without the property nothing
  changes.
- **Help links of the diagnostics:** every `IMxxxx` diagnostic links to its page on the project site,
  `https://iron-mapper.ru/diagnostics/im0001/` (the code in lowercase; the same text as
  [Diagnostics](docs/diagnostics.md)): the code in the error list of the IDE opens it (`helpLinkUri` of the
  diagnostic).

### Changed

- **`IM0009` is an error and means "a constructor parameter gets no value"** (attribute mappings and profiles): for a
  destination created by its constructor (or by the factory of `ConstructWith`), a parameter of the constructor that comes closest has no source property of
  the same name and no default value, a source value that cannot be converted, or `Ignore` without a default value; the
  mapping is not generated. Up to 2.0.0 `IM0009` was a warning for records in attribute mappings only, while the
  generated object initializer did not compile anyway without a parameterless constructor (`CS7036`); a record with a
  parameterless constructor no longer gets `IM0009` for the parameters of its other constructors. A parameter whose
  value a mapping of the pair could convert (`Guid` → `UserId`) is `IM0002` without that mapping, as a property (the
  argument is `default!`, so the generated file compiles). See
  [IM0009](docs/diagnostics.md#im0009--constructor-parameter-not-mapped).

- **Generated code:** the `MapToX(source)` of a pair used as a nested object, a converted value or a constructor or
  factory argument is split into the public method, which checks `source` for `null` as before, and a private
  `MapToXCore(source)` with the body (`{MethodName}Core{HelperScope}` when a mapping of the same source type is already
  named `{MethodName}Core`). Nested objects call `MapToXCore` directly. Elements of nested collections still call the
  public `MapToX`: a `null` element throws `ArgumentNullException`, as documented in 2.0.0. The in-place overload of a
  pair whose existing nested objects another in-place update updates is split the same way: the public method (or the
  private `MapInto…` of a turned-off overload) checks both arguments and calls the private
  `MapToXCore(source, destination)`. **A mapping that nothing uses so keeps the code of 2.0.0** — one method with the
  check and the body, without `AggressiveInlining` — byte for byte, except the version in `[GeneratedCode]` (checked
  against the 2.0.0 package; snapshot `NotNestedMapping_SameCodeAs200`). On a real project the 2.1.0 alpha, which split
  every mapping, made each generated file 730–840 bytes (≈30 %) longer with the same mapping logic. A mapping that
  checks a nested object uses one throw helper of the generated class, `NestedObjectIsNull<T>` (not inlined). The
  behavior of existing mappings does not change.
- **In-place overloads that do nothing are `[Obsolete]`** (removing them would break the code that calls them; they are
  removed in 3.0.0): a value-type destination (the method gets a copy: a struct, a `readonly record struct` value object,
  `Guid`), a mapping with `ConvertUsing` (the in-place overload does not apply it) and a destination whose mapped
  properties are all `init` (they are skipped). Each call gets `CS0618` with the reason and the fix (map to a new
  object, or turn the overload off) — an error with `TreatWarningsAsErrors`. **`IMapper.Map(source, destination)` for
  such a pair throws `MappingException`** with the same reason instead of silently doing nothing (the registration calls
  a private `MapInto…` that throws). The in-place update of a parent no longer calls such an overload of a referenced
  project: the nested object gets a new instance, as for such a mapping of the same project. See
  [Upgrading to 2.1](docs/upgrading-to-2.1.md#4-in-place-overloads-that-do-nothing-are-obsolete).
- **`<IronMapperRegistration>false</IronMapperRegistration>` set in a project generates no registration for
  `IMapper`** (application or library): no `GeneratedMappers.Registration.g.cs`, no module initializer, no private
  `MapInto…`, also no throwing `MapInto…` of a destination created by a constructor or a factory. Up to 2.0.0 the property
  only set the feature switch of an application, and the code was removed only by the trimmer. The `MappingException`
  of `IMapper` for a missing mapping mentions the property (it does not name the assembly: a marker read at run time
  would add 7–15 KB to every NativeAOT app with `IMapper`). The value that the package sets automatically for a trimmed or
  NativeAOT publish without the DI package goes only into the feature switch, as before (`IronMapperRegistration`
  itself keeps the value of the project). See
  [Registration for `IMapper`](docs/configuration.md#registration-for-imapper-in-trimmed-and-nativeaot-apps).

### Deprecated

- **`IronMapperOptions.AddConverter<T>()`** is `[Obsolete]` (warning `CS0618`) and will be removed in 3.0.0. It only
  registered the converter in the DI container: the generated code creates converters itself (`new T()` or one cached
  instance) and never takes them from there, so the call did nothing for mapping. Remove it. Converters with
  dependencies from DI are not supported; compute the value before mapping or set it after. Nothing changes at run time.
  See [Upgrading to 2.1](docs/upgrading-to-2.1.md#12-addconvertert-is-obsolete) and
  [Converters from DI are not supported](docs/configuration.md#converters-from-di-are-not-supported-addconvertert-is-obsolete).

### Fixed

- **`ConvertUsing` with a static method group** (`ConvertUsing(UserId.From)`) was silently ignored up to 2.0.0: the
  mapping was generated property by property, without the converter. Now the method is called. **The result at run time
  changes:** check such mappings when you upgrade. See [Upgrading to 2.1](docs/upgrading-to-2.1.md).
- **`ConvertUsing` with an instance method, a local function, a private method or a delegate in a variable** is now the
  build error `IM0022` at the argument, instead of a silently wrong mapping (the converter could not be called anyway).
  Fix: pass a static method or a lambda. See
  [IM0022](docs/diagnostics.md#im0022--converter-of-convertusing-cannot-be-used).
- **`MapFrom` with a static method group** (`ForMember(d => d.Title, o => o.MapFrom(Helper.Compute))`, also in
  `ForCtorParam`) was silently ignored up to 2.0.0: the member was mapped by name, or the generated file did not compile
  (`CS1061`) when the source had no property of that name ([#59](https://github.com/iron-mapper/IronMapper/issues/59)).
  Now the method is called with the source. **The result at run time changes** where the source has a property of that
  name: check such mappings when you upgrade. An instance method, a local function, a private method or a delegate in a
  variable is the new build error `IM0024` at the argument. See
  [Upgrading to 2.1](docs/upgrading-to-2.1.md#6-mapfrom-when-and-hooks-with-a-static-method-group-are-called) and
  [IM0024](docs/diagnostics.md#im0024--method-of-a-profile-setting-cannot-be-used).
- **`When`, `BeforeMap` and `AfterMap` with a static method group** (`.When(Rules.IsActive)`, `.AfterMap(Audit.Stamp)`)
  were silently ignored up to 2.0.0: the mapping had no condition, the hook was never called. Now the method is called
  (a hook with the source and the destination). **The result at run time changes:** a source for which the condition is
  `false` now maps to `null`, and the hook runs; check such mappings when you upgrade. An argument the generated code
  cannot call is `IM0024`, as for `MapFrom`. Same section of
  [Upgrading to 2.1](docs/upgrading-to-2.1.md#6-mapfrom-when-and-hooks-with-a-static-method-group-are-called).
- **`[Ignore]` on a destination property in profile mappings** only suppressed `IM0001` up to 2.0.0; the source property
  of the same name was still copied, also in place ([#61](https://github.com/iron-mapper/IronMapper/issues/61)). Now a
  profile mapping leaves it out, as an attribute mapping does (a constructor parameter of the same name gets its default
  value); `ForMember` of the property still wins. **The result at run time changes:** such a property keeps its default
  value (in place: its current value); check such mappings when you upgrade. See
  [Upgrading to 2.1](docs/upgrading-to-2.1.md#7-ignore-applies-in-profiles).
- **`[Ignore]` on a source property in profile mappings** was not read up to 2.0.0: the property was still copied to the
  destination property of the same name (also in place, with `ReverseMap()` and `IncludeMembers`). Now it is not read,
  as in attribute mappings (a constructor parameter of the same name gets its default value); `ForMember` of the
  destination property still wins. **The result at run time changes:** the destination property keeps its default value
  (in place: its current value); check such mappings when you upgrade. Same section of
  [Upgrading to 2.1](docs/upgrading-to-2.1.md#7-ignore-applies-in-profiles).
- **`init`-only properties in the in-place overload** ([#58](https://github.com/iron-mapper/IronMapper/issues/58)):
  `source.MapToX(destination)` and `IMapper.Map(source, destination)` skipped them silently up to 2.0.0. An existing
  object in an `init`-only nested property is now updated in place through its getter, as a settable one (a `null` one
  stays `null`). **The result at run time changes** for such nested objects: they get the source's values instead of
  keeping the old ones; check such mappings when you upgrade. The other `init` properties (also `required init`) are
  still skipped, now with the new warning `IM0025` at the mapping, which lists them (not for ignored properties, an
  in-place overload that is turned off, or one that is `[Obsolete]`); with `TreatWarningsAsErrors` it is a build error.
  See [Upgrading to 2.1](docs/upgrading-to-2.1.md#9-init-only-properties-in-place-nested-objects-updated-warning-im0025)
  and [IM0025](docs/diagnostics.md#im0025--in-place-overload-does-not-update-init-only-properties).
- **Hooks and `When` in the in-place overload** ([#62](https://github.com/iron-mapper/IronMapper/issues/62)):
  `source.MapToX(destination)` and `IMapper.Map(source, destination)` did not evaluate `When` and did not call
  `BeforeMap`/`AfterMap` up to 2.0.0. They now do, as a new object and AutoMapper's `Map(source, destination)` do:
  `When` first (`false` leaves the destination as it is), then `BeforeMap`, the properties and `AfterMap`; also for an
  existing nested object updated in place. **The result at run time changes** for in-place calls of such mappings; check
  them when you upgrade. `ConvertUsing` stays unapplied in place (`[Obsolete]`); a destination with only `init`
  properties and a hook is no longer `[Obsolete]`. Mappings without hooks and `When` get the same code. See
  [Upgrading to 2.1](docs/upgrading-to-2.1.md#10-hooks-and-when-in-the-in-place-overload).
- **`required` and `init` properties with `BeforeMap`/`AfterMap`** ([#60](https://github.com/iron-mapper/IronMapper/issues/60)):
  a mapping with a hook created the destination with `new D()` and assigned the properties one by one, so a `required`
  property failed the build inside the generated file (`CS9035`) and an `init` property was silently not mapped. The
  object initializer now sets them, then `BeforeMap`, the other properties and `AfterMap` run (also after the arguments
  of a constructor). **The result at run time changes** for `init` properties of such mappings: they get the source's
  values; `BeforeMap` sees the `required` and `init` properties already set. A `required` property that the mapping gives
  no value (ignored, nothing maps it), with or without hooks, is the new error `IM0026` at the mapping (the mapping is not
  generated) instead of `CS9035` in the generated file. See
  [Upgrading to 2.1](docs/upgrading-to-2.1.md#11-required-and-init-properties-with-beforemapaftermap) and
  [IM0026](docs/diagnostics.md#im0026--required-property-is-not-mapped).

### Performance

- Not measured (no benchmark run for 2.1.0): a nested object calls `MapToXCore` after one comparison with `null` (the
  throw is in a helper that is not inlined), without a second check and without a throw inside, so the JIT can inline the
  nested mapping (aimed at the Nested gap of 2.0.0, ×1.07 on .NET 10). A top-level mapping that is not nested anywhere
  (the Flat and InPlace scenarios) has the machine code of 2.0.0: one call of the mapping method (checked with
  `DisassemblyDiagnoser` on .NET 8 and .NET 10).
  The Nested scenario of the benchmark suite maps the nested objects automatically (local sources or a package 2.1 or
  later; `ForMember(... MapFrom(...))` before). The published numbers stay those of 2.0.0.
- Quick A/B only (`run-quick.ps1` against 2.0.0, one session; no benchmark run for 2.1.0, the published numbers stay
  those of 2.0.0): the public `MapToX(source)` and `MapToX(source, destination)`, and the private `MapInto…`
  of a turned-off in-place overload, are marked `[MethodImpl(MethodImplOptions.AggressiveInlining)]` when they only check
  the arguments and call `…Core`, so the checks move to the call site and one call remains. The .NET 10 JIT already
  inlined them; the .NET 8 JIT did not (a call and a jump to `…Core` on the hot path of a top-level mapping; seen with
  `DisassemblyDiagnoser` in the InPlace, Flat and Nested scenarios). `…Core` and a method with the body (a mapping not
  used as a nested object, as in 2.0.0) are not marked. On .NET 8 the extra call is gone: InPlace ×0.92 of `Manual`
  (×1.01 before this change, 2.0.0 ×0.93); Flat, Nested and Collection are not slower. On .NET 10 the machine code is the
  same and the differences are within the noise of the session.

### Build

- Tests of the volume of the generated code (`GeneratedCodeSizeTests`): `…Core` only for a pair used as a nested object
  (also of itself, `Employee.Manager`), the in-place `…Core` of a nested pair only when an in-place body of the parent is
  generated, `[Obsolete]` on the in-place overloads that do nothing (`CS0618` at each call and none in the generated
  files; the messages), `MappingException` of the registry for such a pair (also `NoOpInPlaceMapperTests` through
  `IMapper`), a referenced project's `[Obsolete]` in-place overload not called, `IronMapperRegistration` values (only
  `false` turns the registration off), the `MappingException` that mentions the property, a changed
  property in the IDE; snapshots `NotNestedMapping_SameCodeAs200` (identical to the output of the 2.0.0 package) and
  `RegistrationTurnedOff`; the snapshots of the scenarios of 2.0.0 are identical to 2.0.0 again, except the `[Obsolete]`
  in-place overload of a `ConvertUsing` mapping. `eng/verify-package.ps1`: a variant with
  `IronMapperRegistration=false` (no registration method, no `MapInto…`, the switch off);
  `eng/verify-aot.ps1` prints the size of every case.
- Tests: `NestedObjectTests` (generator: null handling by annotations, structs and records, base types and interfaces,
  referenced assemblies, qualified names and a `…Core` name collision, hooks, `IncludeMembers`, collections of objects
  with nested objects — arrays, `IEnumerable<T>`, `IReadOnlyList<T>` —, a mapping added or removed in another file in the
  IDE; runtime: `tests/IronMapper.Tests/NestedObjectTests.cs`, which also checks `#pragma warning disable IM0004` in a
  build with `TreatWarningsAsErrors`; `IMapper`: `NestedObjectMapperTests`), `CyclicMappingTests` (a tree through a
  collection, `Employee.Manager`, `Ignore`/`MapFrom` on the path, `Order.Customer.Orders` with the full chain, two nested
  objects on a cycle, `ConvertUsing`, severity options, models that compile with 2.0.0 get no new warnings),
  `IM0002CodeFixTests`; snapshot scenario `Nested`. In-place: the existing nested objects are kept (`ReferenceEquals`) and
  updated over two levels, a `null` destination value gets a new object, a `null` source sets `null`, a struct, a record
  with `init` properties and a `ConvertUsing` pair get a new object, the in-place overload turned off (generated code and
  `IMapper`), a pair of a referenced project with its in-place overload (the object is kept) and without it (a new one); `null` in a non-nullable nested object: `InvalidOperationException` with the message, for a new object and
  in place.
- Benchmark suite: `FeatureMatrixTests.Write_feature_matrix` writes `results/feature-matrix.md` only with
  `IRONMAPPER_WRITE_FEATURE_MATRIX=true` (`run-all.ps1` sets it for its test step); a plain `dotnet test` no longer
  changes the committed file.
- Benchmark suite: `scripts/run-quick.ps1` — A/B of a published release (2.0.0 by default) and the local sources,
  same job as the full run, results in `artifacts/quick/`, with checks that the published package comes from nuget.org
  and that BenchmarkDotNet's child build uses the intended IronMapper. The documented
  `dotnet run -p:UseLocalIronMapper=true -- --filter ...` measured the published package (BenchmarkDotNet rebuilds the
  project without the parent's `-p:` properties); the docs now use the environment variable `UseLocalIronMapper=true`.
- Tests: `IncompatiblePropertyTypesTests` (attributes, profiles, `IncludeMembers`, `ReverseMap()`, `MapFrom`, collection
  elements, implicit conversions and element mappings that must not be reported, an element mapping added or removed
  in another file in the IDE); snapshot scenario `IncompatibleProperties`. `eng/verify-package.ps1`: an incompatible
  property fails the build with `IM0002` at the destination property and with no error inside a generated file. The
  check itself does not change the generated code of mappings without `IM0002`.

## [2.0.0] - 2026-10-23

### Breaking changes

2.0.0 follows 1.1.4 (1.2.0 was prepared but not published; its changes are in this release). Each change below with
what to do: [Upgrading to 2.0](docs/upgrading-to-2.0.md) ([in Russian](docs/upgrading-to-2.0.ru.md)).

- **`global using IronMapper.Generated;` from the package** (see Added): with implicit usings an explicit
  `using IronMapper.Generated;` is redundant. The compiler does not warn, but a strict code style in the build
  (`EnforceCodeStyleInBuild=true` and `dotnet_diagnostic.IDE0005.severity = error`, or `warning` with
  `TreatWarningsAsErrors`) fails with `error IDE0005: Using directive is unnecessary.` at that line (checked against the
  packed package in `eng/verify-package.ps1`). Remove the explicit usings, or set
  `<IronMapperImplicitUsings>false</IronMapperImplicitUsings>`. A file that imports another extension method
  with the same name and parameter type as a generated one (e.g. a hand-written `MapToUserDto`) now gets `CS0121` at
  that call; remove or rename the method, or turn the global using off.
- **The generated class is `IronMapper.Generated.GeneratedMappers_{AssemblyName}`** instead of `GeneratedMappers` in
  every assembly: every character of the assembly name that is not a letter, a digit or `_` becomes `_`
  (`Shop.Application` → `GeneratedMappers_Shop_Application`); a name longer than 64 characters keeps its first 55 and
  gets `_` and 8 hex digits of a stable hash (FNV-1a) of the full name. Calls `source.MapToX()` (and `MapToXList`,
  `MapToXArray`, in-place) and `IMapper` do not change. Code that names the class breaks: `GeneratedMappers.MapToX(...)`,
  a method group `Select(GeneratedMappers.MapToX)`, `using static IronMapper.Generated.GeneratedMappers;`, a lookup by
  reflection, and in C# 8 projects the call `GeneratedMappers.RegisterIronMapperMappings()`; use the extension method
  syntax or the new name. Why: with the registration for `IMapper` every assembly had members and nested types with the
  same full names (`RegisterIronMapperMappingsCore`, its lambdas in `<>c`, `<>O`, `ConverterCache<T>`,
  `IElementMapper<TSource, TDest>`); tools that identify types by their full name attributed the dependencies of one
  assembly's generated class to the other's: an ArchUnitNET rule "Application does not depend on EfCore" over two
  assemblies with mappings passed with 1.1.3 and failed before this change (`GeneratedTypeNamesArchitectureTests`).
  Naming the class of a referenced project now works (up to 1.1.4 it reported `CS0436`). `IM0013` names the class:
  `Call it as source.MapToX() or IronMapper.Generated.GeneratedMappers_App.MapToX(source)`. The diagnostics that
  mentioned `IronMapper.Generated.GeneratedMappers` (`IM0010`, `IM0011`, `IM0015`) say "the generated class in
  namespace IronMapper.Generated". The generated file names do not change.
- **Exceptions thrown through `IMapper` are no longer wrapped:** an `ArgumentNullException` of the generated code (a
  `null` nested element), an exception of your converter or hook reach the caller as they are, with the stack of the
  generated method. Up to 1.1.4 they came as `TargetInvocationException` with the original as `InnerException`; code
  that catches `TargetInvocationException` around `IMapper` must catch the original exception instead.
- **`AddIronMapper` does not scan assemblies and does not add profiles to the container.** For every assembly passed
  (also `AddProfile<T>()` and `AddProfilesFromAssembly`) it runs the assembly's module initializer
  (`RuntimeHelpers.RunModuleConstructor`), so the mappings of an assembly that nothing has used yet are found; they
  are registered when the first `IMapper` is created. Profiles are read by the source generator; up to 1.1.4 `AddIronMapper` called `GetTypes()` and added
  every `MappingProfile` as a singleton, so code that resolved a profile from the container gets nothing now.
  `AddProfilesFromAssembly(null)` throws `ArgumentNullException` (it used to fail later in `AddIronMapper`). When a
  mapping is not found, `IMapper` runs the module initializers of all loaded assemblies (names not starting with
  `System.` or `Microsoft.`) and looks again, as the old assembly scan did.
- **`IMapper.MapCollection` returns a materialized `List<TDest>`** (typed as `IEnumerable<TDest>`, the signature is
  the same). The elements are mapped during the call: a `null` source throws `ArgumentNullException` and a `null`
  element or a missing mapping throws at the call, not when the result is enumerated; enumerating the result again
  does not map again. Up to 1.1.4 it was a lazy sequence that mapped on every enumeration.

### Added

- **Optional overloads can be turned off.** For the project with the MSBuild properties `IronMapperGenerateInPlace`,
  `IronMapperGenerateList`, `IronMapperGenerateArray` (`true` by default), for one mapping with `GenerateInPlace`,
  `GenerateList`, `GenerateArray` on `[MapTo]`/`[MapFrom]` or `.GenerateMethods(inPlace, list, array)` in a profile
  (overrides the project value; not applied to the reverse mapping of `ReverseMap()`). `MapToX(source)` is always
  generated. `IMapper` keeps mapping a single object, `List<T>`, `T[]`, `MapCollection` and in-place for such a mapping,
  with the same results: without `MapToXList`/`MapToXArray` the registry fills the collection itself; without the public
  in-place overload its body stays as a private method for `IMapper` (so turning in-place off removes the public method,
  not the code). Names of the remaining methods and the collision rules do not change. See
  [Generated overloads and code coverage](docs/configuration.md#generated-overloads-and-code-coverage).
- **`[GeneratedCode("IronMapper", "<version>")]` on the generated class**, and with
  `<IronMapperExcludeFromCodeCoverage>true</IronMapperExcludeFromCodeCoverage>` also `[ExcludeFromCodeCoverage]`: unused
  generated methods (e.g. `MapToXList` that nothing calls) no longer lower the coverage of a project. Declared once on
  the generated class, so they cover the mapping methods, the registration for `IMapper` and the converter cache.
  `[GeneratedCode]` can be turned off with `<IronMapperGeneratedCodeAttribute>false</IronMapperGeneratedCodeAttribute>`
  (see Changed).
- Diagnostic `IM0016` (warning): an argument of `GenerateMethods(...)` is not a constant; the project value applies for
  that overload.
- **Diagnostics point to your code.** Every `IMxxxx` diagnostic has a source location: the destination property or the
  `[MapTo]`/`[MapFrom]` attribute (IM0001), the `[MapProperty]` attribute (IM0006), the `CreateMap` or `ReverseMap()`
  call, the profile name (IM0005), the member in a profile lambda (IM0011/IM0012), the converter type (IM0015), the
  duplicate definition (IM0007, with the other definitions as additional locations); the full list is in
  [diagnostics](docs/diagnostics.md). The IDE underlines the place and the build output shows file and line. Up to
  1.1.4 all diagnostics were reported without a location: only listed in the Error List, the per-file `.editorconfig`
  severities (`dotnet_diagnostic.IMxxxx.severity`) did not apply to them, and the code fixes were never offered.
- **IM0001 for profile mappings, opt-in** ([#22](https://github.com/iron-mapper/IronMapper/issues/22)): with
  `<IronMapperReportUnmappedInProfiles>true</IronMapperReportUnmappedInProfiles>` a destination property of a
  `CreateMap` mapping that nothing maps is reported at the `CreateMap` call. Mapped means `ForMember` (`MapFrom`,
  `Ignore`, `UseConverter`), `IncludeMembers`, a source property with the same name, or `[Ignore]` on the destination
  property; a mapping with `ConvertUsing` and the reverse mapping of `ReverseMap()` are not checked (as in AutoMapper,
  where reverse maps use `MemberList.None`). Off by default (planned on by default in the next major version). The
  severity is set
  the standard way, e.g. `dotnet_diagnostic.IM0001.severity = error` in `.editorconfig`. See
  [Unmapped properties in profiles](docs/configuration.md#unmapped-properties-in-profiles).
- **Code fixes work.** IM0001: "Add [Ignore] to '...'" on the destination property (adds `using IronMapper.Attributes;`
  when needed, `[property: Ignore]` on a positional record parameter) and, new, "Ignore '...' in this mapping
  (ForMember)" for profile mappings; IM0006: "Did you mean '...'?" with the destination properties of the mapped type.
  Fix All is supported. Both used to find their target by searching the message text in the document (the first
  property with that name, a type found by simple name); they now use the diagnostic's location and properties.
- **`global using IronMapper.Generated;` from the package.** C# projects with implicit usings
  (`<ImplicitUsings>enable</ImplicitUsings>`, the default in the .NET 6+ templates) no longer need
  `using IronMapper.Generated;` to call the generated `MapTo*` methods; a forgotten using failed with `CS1061` and no
  hint (the README Quick Start of 1.1.3 had this bug). Added by `buildTransitive/IronMapper.props`, so it also
  applies when the package comes through `IronMapper.Extensions.DI`. Turn it off with
  `<IronMapperImplicitUsings>false</IronMapperImplicitUsings>`. Projects without implicit usings (and C# 9 and
  earlier) are not affected and still need the explicit using; an explicit using next to the global one compiles
  without compiler warnings (a strict code style reports it as `IDE0005`, see Breaking changes). See [Global using](docs/configuration.md#global-using-of-ironmappergenerated).
- **`IMapper` maps collections by the destination type** ([#8](https://github.com/iron-mapper/IronMapper/issues/8)):
  `mapper.Map<List<UserDto>>(users)`, `Map<UserDto[]>`, `Map<IEnumerable<UserDto>>`, `Map<IList<UserDto>>`,
  `Map<ICollection<UserDto>>`, `Map<IReadOnlyList<UserDto>>`, `Map<IReadOnlyCollection<UserDto>>` (and the same with
  two type arguments, e.g. `Map<List<User>, List<UserDto>>`) work for any `IEnumerable<User>` source (`List`, array,
  `HashSet`, …) when `User → UserDto` is mapped. They call the generated `MapToUserDtoList`/`MapToUserDtoArray`: the
  result is a `List<UserDto>`, an array for `T[]`. Up to 1.1.4 these calls threw `MappingException`.
- `IMapper.Map<TDest>(object)` and `Map<TSource, TDest>` fall back to the mapping of the nearest base type of the
  source (a `DerivedEntity` without its own mapping uses the one of `Entity`); up to 1.1.4 only the exact type matched.
  `Map<TSource, TDest>` with a source declared as a base type or `object` uses the runtime type.
- `RuntimeMapper.Map(object source, Type sourceType, Type destinationType)` maps by `Type`. It is not on the `IMapper`
  interface (a new interface member would break other implementations of it).
- Namespace `IronMapper.Registration` in the `IronMapper` package: `MapperRegistry` (the registry of the generated
  mappings, mapping by `Type`, `EnsureModuleInitialized(assembly)`) and `MapperCache<TSource, TDest>` (the generated
  delegates of one pair). Used by `IMapper`; can be used without the DI package.
- **MSBuild property `IronMapperRegistration`** and feature switch `IronMapper.Registration.IsSupported`
  (`MapperRegistry.IsSupported`): whether the generated registration for `IMapper` runs and stays in a trimmed app.
  `buildTransitive/IronMapper.targets` turns it off for a `PublishAot` / `PublishTrimmed` app in which no project
  references `IronMapper.Extensions.DI` (the DI package marks itself through its own `buildTransitive` props, so a
  reference through a library counts); `true` / `false` in the application project overrides it. The trimmer (ILLink
  and NativeAOT, through `ILLink.Substitutions.xml` in `IronMapper.dll`) then removes the registration, the registry and
  the generated methods the app does not call. With the switch off, creating `RuntimeMapper` throws
  `InvalidOperationException` that names the property, and `MapperRegistry`'s `MappingException` says the same. See
  [Registration for IMapper](docs/configuration.md#registration-for-imapper-in-trimmed-and-nativeaot-apps).
- Diagnostic `IM0015` (error): a converter of `[MapConverter]`, `UseConverter<T>()` or `ConvertUsing<T>()` cannot be
  created by the generated code (no parameterless constructor, a `private`/`protected` one, an abstract class, an
  interface, a static class, an open generic type, a `private` nested type). The mapping is not generated; other
  mappings are. Up to 1.1.4 the build failed with `CS7036`, `CS1729`, `CS0122` or `CS0144` inside the generated file.
  See [IM0015](docs/diagnostics.md#im0015--converter-cannot-be-created-by-generated-code).
- Diagnostic `IM0003` (error) is reported: `[MapTo]`/`[MapFrom]` with a type that compiles but cannot be mapped, an
  open generic type (`typeof(Box<>)`) or an array. Up to 1.1.4 such a mapping was dropped without any message. Its
  title and message changed (it was declared, never reported). A type that does not resolve is not reported by
  `IM0003` (see Fixed).

### Changed

- **Coverage tools that exclude `[GeneratedCode]` no longer measure the generated mappings.** The generated class is
  marked `[GeneratedCode("IronMapper", "<version>")]` by default (see Added). With coverlet's
  `ExcludeByAttribute=GeneratedCodeAttribute` (`coverlet.runsettings`, `<ExcludeByAttribute>` of `coverlet.msbuild`,
  `--exclude-by-attribute`) or a tool set up the same way, the generated code drops out of the coverage report, so the
  percentage changes, also without `IronMapperExcludeFromCodeCoverage`. To measure it as in 1.1.x, set
  `<IronMapperGeneratedCodeAttribute>false</IronMapperGeneratedCodeAttribute>` (MSBuild property, `true` by default;
  only `false`, case-insensitive, turns it off; independent of `IronMapperExcludeFromCodeCoverage`). See
  [Attributes of the generated class](docs/configuration.md#attributes-of-the-generated-class).
- `MapperRegistry.Register<TSource, TDest>`: `mapList` and `mapArray` may be `null` (the generated registration passes
  `null` for a turned-off `MapToXList`/`MapToXArray`); the registry then maps the elements with `map`.
- A project that references the package but has no generated mappings gets an empty `IronMapper.Generated`
  namespace (file `GeneratedMappers.g.cs`), so the global using compiles there (otherwise `CS0246`). Generated code
  of projects with mappings does not change.
- **`IMapper` works without reflection, with trimming and NativeAOT** ([#26](https://github.com/iron-mapper/IronMapper/issues/26)).
  For every assembly with mappings the generator emits `GeneratedMappers.Registration.g.cs`: a module initializer
  (`[ModuleInitializer] RegisterIronMapperMappings()`) that hands the assembly's registration to
  `IronMapper.Registration.MapperRegistry`, and the registration itself, one `Register<TSource, TDest>` call per
  mapping with lambdas that call the generated methods. The mappings are registered when the first `IMapper` is
  created (an assembly first used later: at the next mapping). The module initializer only puts one delegate per
  assembly on a lock-free list and does not initialize the registry, so an app that uses only the extension methods
  registers nothing (it loads `IronMapper.dll` for the module initializer), and a trimmed or NativeAOT app without the
  DI package does not contain the registration (see `IronMapperRegistration` above). `RuntimeMapper` calls the delegates: `Map<TSource, TDest>` is a static field read and
  a delegate call. Publishing with trimming or NativeAOT reports no warnings (`AddIronMapper` and `RuntimeMapper` are
  not annotated; the `IMapper` probe runs in CI). Up to 1.1.4 `RuntimeMapper` scanned the loaded assemblies for
  `GeneratedMappers`, called the methods through `MethodInfo.Invoke`, and the NativeAOT app failed with
  `MappingException`.
- **C# 8 projects** (`LangVersion` below 9) get the registration without `[ModuleInitializer]` (C# 8 does not have
  it): `IMapper` finds their mappings only after `RegisterIronMapperMappings()` of the generated class
  (`IronMapper.Generated.GeneratedMappers_{AssemblyName}`) is called in that assembly. Projects on targets without `ModuleInitializerAttribute` (`netstandard`, .NET Framework)
  get an internal copy of the attribute in the generated file, unless the compilation already has one.
- The `MappingException` for a missing mapping says to pass the assembly with the mapping to `AddIronMapper`.
- `MappingValidator` is marked `[RequiresUnreferencedCode]` (it reads properties and constructors through
  reflection): calling it in a trimmed or NativeAOT app reports `IL2026`. On .NET 8 and other targets that get the
  `netstandard` assemblies, the attributes are internal copies with the same full names, which the trimmer recognizes.
- **Less generated code for collections.** The loops of `MapToXList`/`MapToXArray` are written once per project, in
  two private generic methods of `GeneratedMappers` (`MapElementsToList`, `MapElementsToArray`). Each mapping's
  `MapToXList`/`MapToXArray` is a one-line call of them with a private struct of the mapping that calls `MapToX`
  directly: a struct type argument makes the runtime compile a separate, specialized copy for each mapping, so there is
  no delegate or interface dispatch per element. Signatures, the `null` semantics, the fast paths for `T[]`, `List<T>`,
  `ICollection<T>` and the capacities are the same (the existing collection tests pass unchanged). Nested collection
  properties keep their per-property helpers.
- **Shape of the generated code: no LINQ, no delegates.** Public signatures, the `null` semantics and the exceptions
  are the same (pinned by new tests before the change).
  - `MapToXList`/`MapToXArray` still take `IEnumerable<T>?`, but check the runtime type first: `T[]` and `List<T>` are
    read with `for` into a result of the exact size, another `ICollection<T>` gives the size through `Count`, any
    other sequence is read with `foreach` (an array result through a `List<T>` and `ToArray`). Elements are mapped by
    a direct call. A `null` source still gives an empty list / `Array.Empty`.
  - A nested collection property is mapped by a private helper `MapCollection_{Property}{scope}` instead of
    `Enumerable.ToList(Enumerable.Select(...))` / `ToArray`. The read loop depends on the declared source type (`for`
    over the indexer for `T[]`, `List<T>`, `IList<T>`, `IReadOnlyList<T>`; `foreach` with a known count for
    `ICollection<T>`, `IReadOnlyCollection<T>` and types with a public `Count`; `foreach` otherwise). A `null` source
    collection is still `null`. An `IEnumerable<T>` destination stays lazy, as in 1.1 (a generated iterator).
  - Null checks of the generated methods use `ArgumentNullException.ThrowIfNull` when the project's target has it
    (.NET 6+); otherwise one private throw helper in `GeneratedMappers.g.cs` (with `[DoesNotReturn]` when available).
    The exception and its `ParamName` are the same.
- **A converter without state is created once and reused** (`[MapConverter]`, `UseConverter<T>()`,
  `ConvertUsing<T>()`). A converter is without state when it is a class with a public parameterless constructor, no
  instance fields anywhere in its hierarchy except `object` (auto-property backing fields and field-like events
  included), no finalizer, constructors without code, and every type of the hierarchy declared in the same project.
  Its single instance is created on first use, once per converter type for the assembly (`ConverterCache<T>` in
  `GeneratedMappers.g.cs`, initialized once by the runtime even when several threads race). Every other converter is
  still created with `new` on every call. The difference can only be observed through state kept outside the converter
  (for example a static field that records instances). See
  [Converter instances](docs/configuration.md#converter-instances).

### Fixed

- **A type that does not resolve no longer breaks the generated code.** `typeof(Missing)` in `[MapTo]`/`[MapFrom]`
  (also an inaccessible type, an unknown namespace or type argument), `CreateMap<Source, Missing>()` and an unresolved
  converter type: the compiler reports the error at the user's code (`CS0246` and similar), and the generator now skips
  that mapping. Up to 1.1.4 it still emitted the mapping, which added `CS0400`/`CS0246`/`CS0122` errors inside the
  generated file. Other mappings are generated. A profile whose only `CreateMap` is skipped this way does not get
  `IM0005` (empty profile).
- **Diagnostics are updated after an edit in the IDE.** An edit that changed only the diagnostics of a mapping (for
  example `[Ignore]` added to the destination property reported in `IM0001`, or one of two `private` members used in a
  profile lambda made `internal`, `IM0011`) kept the old diagnostics until the generator was restarted: the mapping
  model ignored its diagnostics when compared with the previous run. Command-line builds were not affected. The
  generated code does not change.

### Performance

Full run of the comparison benchmarks: 1.1.3 (the package from nuget.org) and 2.0.0 measured in one session on one
machine (Intel Core i7-12650H, Windows 11, BenchmarkDotNet 0.15.8), .NET 10.0.12 / .NET 8; ratio to hand-written code
(`Manual`) of the same run. Report and the full comparison:
[benchmarks/results/RESULTS.md](benchmarks/results/RESULTS.md#compared-with-the-previous-release). The run measured
commit `909da54` (the code prepared as 1.2.0, `benchmarks/results/2.0.0/`); 2.0.0 differs from it in the generated code
only by the name of the class (the declaration line of each generated file, see the snapshots), so it was not measured
again.

| Scenario | 1.1.3, .NET 10 | 2.0.0, .NET 10 | 1.1.3, .NET 8 | 2.0.0, .NET 8 |
|---|---:|---:|---:|---:|
| Flat | ×0.98 | ×0.99 | ×1.05 | ×1.01 |
| Nested (nested objects configured with `ForMember(... MapFrom(...))`) | ×1.25 | ×1.07 | ×1.28 | ×1.09 |
| Collection, 10 orders | ×1.15 | ×0.98 | ×1.35 | ×1.03 |
| Collection, 1 000 orders | ×1.03 | ×1.05 | ×1.12 | ×1.02 |
| Collection, 100 000 orders | ×1.17 | ×1.00 | ×1.12 | ×1.00 |
| CustomConfig (converter) | ×1.02 | ×1.00 | ×0.99 | ×0.97 |
| In-place update | ×1.10 | ×0.91 | ×0.97 | ×0.93 |
| `IMapper`, Flat | 30.2 ns, ×2.65 | 16.3 ns, ×1.41 | 36.0 ns, ×2.41 | 20.9 ns, ×1.37 |
| `IMapper`, Nested | ×1.36 | ×1.11 | ×1.52 | ×1.13 |
| `IMapper`, in-place | 31.0 ns, ×3.56 | 14.0 ns, ×1.59 | 33.4 ns, ×4.04 | 14.9 ns, ×1.68 |

Collection differences of a few percent are within the run-to-run spread (at 1 000 orders on .NET 10 the libraries
that did not change moved by up to 12 % between the two runs; a separate A/B run gave ×1.01 for 2.0.0). Mapperly in the
same run: Nested ×0.98 / ×1.21, Collection of 10 ×1.17 / ×1.10.

- Allocations: collections allocate as much as `Manual` (1.1.3: +4 % at 1 000 and 100 000 orders); Nested 2.0 →
  1.9 KB; `IMapper` allocates nothing of its own (Flat 200 → 168 B, in-place 40 → 0 B).
- NativeAOT executable of an app with the extension methods only: +255.5 KB → +2.6 KB over the app without a mapper
  (Mapperly +23.6 KB). `IMapper` under NativeAOT: fails → works, no trim/AOT warnings; +1.50 MB, almost all of it
  Microsoft.Extensions.DependencyInjection (the same hand-written mapping behind the same container: +1.05 MB).
- Cold start (process start + one mapping, JIT, over a console app without a mapper; re-measured for both versions in
  one session with the packages): extension methods +2.0 → +1.3 ms; `IMapper` +21.8 → +19.9 ms, of which the DI
  container is +15.3 ms, so IronMapper's own part went from 6.4 to 4.5 ms.
- Size: framework-dependent publish +28.2 → +47.7 KB (`IronMapper.dll` 14.8 → 28.7 KB, with `MapperRegistry`);
  `.nupkg` 83.6 → 114.4 KB (`IronMapper`, with the generator and the code fixes), 35.7 → 24.9 KB
  (`IronMapper.Extensions.DI`).
- Build time of a project with 200 entity/DTO pairs over the same project without a mapper: +0.49 → +0.60 s (Mapperly
  +0.20 / +0.31 s in the same runs); the generated code grew from 12,210 to 13,894 lines (loops instead of LINQ, the
  registration for `IMapper`).

Converters (see Changed): a converter without state is no longer allocated on every call (CustomConfig above; on
.NET 10 the escape analysis already removed the allocation in 1.1.3).

Generated code without LINQ and delegates (see Changed):

- Collections of a known size are filled in a loop with the final capacity: no `Select` iterator and no list growth,
  so mapping a collection allocates the same as hand-written code (1.1.3 allocated more).
- Nested objects, short collections and the in-place update are closer to hand-written code than in 1.1.3.
- The NativeAOT executable of an app that uses only the generated extension methods is barely larger than the same
  app without a mapper: `Enumerable.Select` + `ToList` no longer pull in the NativeAOT type loader.
- The remaining Nested gap to hand-written code is the generated null checks: they keep the JIT from inlining
  `MapToAddressDto` into `MapToCustomerDto` (checked with `DisassemblyDiagnoser`; without the checks the gap almost
  disappears). The checks stay: they are the documented behavior.

`IMapper` on the generated registry:

- No reflection per call and no allocation of `IMapper` itself (1.1.3: `MethodInfo.Invoke` with an argument array).
  `Map<TSource, TDest>` reads a static field and calls a delegate; what remains over the extension method is the call
  of the generic interface method `IMapper.Map<TSource, TDest>` (checked with `DisassemblyDiagnoser`: called on
  `RuntimeMapper` directly, the call costs the same as the extension method).
- The registry is keyed by `Type` (nested dictionaries) instead of a `(Type, Type)` tuple and never clears a concurrent
  dictionary: the tuple key made the JIT compile the dictionary code at startup, and `Clear()` started the event
  source of the concurrent collections.
- An app with only the extension methods: the module initializer puts one delegate per assembly on a list (no
  registry, no dictionaries); in a trimmed or NativeAOT app without the DI package the registration is removed.

### Build

- Tests of the generated class name: `GeneratedClassNameTests` (the name scheme, long and unusual assembly names),
  `GeneratedClassVisibilityTests` (the generated types of two assemblies have different full names; naming the class of
  the current and of a referenced project), and `GeneratedTypeNamesArchitectureTests` in
  `tests/IronMapper.Integration.Tests`: an ArchUnitNET layer rule over `IronMapper.Integration.Application` and the test
  assembly, both with mappings (it fails with one class name for all assemblies). `TngTech.ArchUnitNET.xUnit` is a
  dependency of that test project only. `eng/verify-package.ps1` and the package consumers look the generated classes up
  by their new names.
- Benchmarks: `IronMapper_StaticFormat` (an extern alias, the only place in the repository that named the generated
  class) uses `GeneratedMappers_Comparison_Mappers_IronMapperStaticFormat` with IronMapper 2.0+ and `GeneratedMappers`
  with a 1.x package (`IRONMAPPER_CLASS_PER_ASSEMBLY`, set in `benchmarks/Directory.Build.targets` from
  `UseLocalIronMapper` / `IronMapperPackageVersion`).
- Benchmarks: the results of the release run are in `benchmarks/results/2.0.0/` (was `results/1.2.0/`; the reports were
  regenerated from the same raw data with the label 2.0.0, the numbers did not change).
- Benchmarks: `scripts/run-benchmarks.ps1` runs both release runs (the published package and the local sources) in one
  session and checks them (exit code, log, the `_Not measured`, `**Quick run**`, `**Dry run**` markers of RESULTS.md,
  case-sensitive; `-Dry` checks the commands into `artifacts/bench-dry/`); `scripts/compare-results.ps1` compares two
  runs, and `generate-report.ps1` embeds the comparison into RESULTS.md. `run-all.ps1 -UseLocalIronMapper` measures cold
  start, NativeAOT, size and build time through the local sources packed as `<Version>-local.<timestamp>`
  (`IronMapperPackageVersion`, `IronMapperPackageSource`): a project reference added the `.pdb` and XML docs to the
  publish folder and rebuilt `../src` inside every timed build. RESULTS.md names the commit recorded at the start of
  the run and links the benchmarks README relative to its folder; the NativeAOT findings no longer say that
  `docs/advanced.md` lists NativeAOT as "Not verified".
- Tests of the optional overloads: every combination of the three, set by MSBuild property, attribute and profile, run
  through the generated method and the registry (`GeneratedMethodsTests`); `IMapper` with the overloads turned off gives
  the same results as with all of them (`TurnedOffOverloadsTests`). The tests that use the process-wide registry run in
  one xUnit collection (one of them turns the registration's feature switch off). `eng/verify-package.ps1` checks the
  overloads and the class attributes against the packed package, with the defaults and with everything turned off.
- `tests/IronMapper.CodeFixes.Tests` (in `IronMapper.slnx`): code fix tests with `Microsoft.CodeAnalysis.Testing`
  (`CSharpCodeFixTest` with the generator attached). `eng/verify-package.ps1` builds a profile with an unmapped
  property against the packed package: without the property it builds; with `IronMapperReportUnmappedInProfiles=true`
  the build fails with IM0001 at the `CreateMap` line; `.editorconfig` severities `error` and `none` apply.
- Diagnostics are reported by their own generator output, which resolves the locations in the current syntax trees (so
  `.editorconfig` severities and code fixes apply); the generated files are produced from pipeline data without
  diagnostics and locations, so an edit that only moves a mapping leaves every source output cached (test
  `LinesAddedAboveMappings_SourceOutputsCached_DiagnosticLocationsUpdated`).
- Generator pipeline data is compared by value (`EquatableArray<T>` for every collection, diagnostics included). The
  result of each analysis step was an `ImmutableArray` (compared by reference), so it counted as modified on every
  edit; now an equal result is `Unchanged`. The strict incremental caching tests are enabled (no `Skip`); new tests
  check that diagnostics appear and disappear after an edit.
- `IronMapper` and `IronMapper.Extensions.DI` (net10.0) are built with `IsAotCompatible`: the trim and AOT analyzers
  fail the build on an unannotated reflection use inside the libraries.
- CI: the `aot` job (`eng/verify-aot.ps1`, ubuntu, net8.0 and net10.0) publishes two probes with `PublishAot` against
  the packed packages: `tests/IronMapper.AotProbe` (attribute and profile mappings, collections, in-place update, a
  converter) must have no trim/AOT warnings and its executable must check the mapped values;
  `tests/IronMapper.AotProbe.DI` (`IMapper`: `AddIronMapper`, mapping by declared and runtime type, in-place,
  `MapCollection`, `Map<List<T>>`/`Map<T[]>`/`Map<IEnumerable<T>>`, a profile mapping with a converter, the original
  exception) must have no trim/AOT warnings either, and its executable checks the values. The ILC map file shows that
  the generated registration is absent from `AotProbe` and present with `IronMapperRegistration=true`;
  `AotProbe.DI` with `IronMapperRegistration=false` must fail with the message that names the property; and
  `tests/IronMapper.AotProbe.Transitive` (the DI package only through `IronMapper.AotProbe.Transitive.Services`, the
  mappings in `IronMapper.AotProbe.Transitive.Maps` without it) must run with the JIT and with NativeAOT. `AotProbe` and
  `AotProbe.DI` are also published trimmed without NativeAOT (`PublishTrimmed`, self-contained; ILLink): no warnings,
  they run, and the trimmed probe assembly has no registration in the first and keeps it in the second.
- `eng/verify-package.ps1` builds `tests/IronMapper.PackageConsumer.ImplicitUsings` in 24 variants against
  the packed package: no using with implicit usings, only `IronMapper.Extensions.DI` referenced, no mappings, the
  Quick Start extracted from `README.md` and `README.ru.md` (between `<!-- quickstart:begin -->` and
  `<!-- quickstart:end -->` comments), an explicit using next to the global one (also with a strict code style:
  `EnforceCodeStyleInBuild` and `IDE0005` as an error must fail with `IDE0005` at the using, and build with
  `IronMapperImplicitUsings=false`), implicit usings
  off, `LangVersion` 9, `IronMapperImplicitUsings=false` (each also checks whether the global using was generated),
  and netstandard2.0 / netstandard2.1 libraries (the generated throw helper instead of `ThrowIfNull`, with and
  without `[DoesNotReturn]`; the generated copy of `ModuleInitializerAttribute`; C# 8 without a module initializer;
  built only), and the feature switch of the `IMapper` registration in `runtimeconfig.json` (not set by default and
  with the DI package, `false` with `PublishTrimmed` and without it, `true` with `IronMapperRegistration=true`; a
  framework-dependent publish without the DI package finds the mapping in the registry). The executable variants are
  also run. The package layout check requires `buildTransitive/IronMapper.targets` and
  `buildTransitive/IronMapper.Extensions.DI.props`.
- Tests of `IMapper` on the registry (`tests/IronMapper.Integration.Tests/MapperRegistryTests.cs`): original
  exceptions, collections as the destination type, types with the same simple names in different namespaces, base
  and runtime types, `Map(object, Type, Type)`, and mappings in an assembly that nothing has used yet (profiles in
  `tests/IronMapper.Integration.Application`, entities in `tests/IronMapper.Integration.Domain`, the call from the
  test project; run outside the parallel tests), a registration after a lookup replaces the mapping found through a
  base type, the error with the switch off, and the lazy registration: nothing is registered until a mapper is created,
  mappers created in parallel run a registration once and all see it, an assembly first used after the mapper was
  created is found. Generator tests: the module initializer registers the mappings (nothing
  with the switch off), C# 8, no registration without mappings; every snapshot scenario has a
  `GeneratedMappers.Registration.g.cs`.
- Benchmarks: `scripts/run-aot.ps1` and `run-startup.ps1 -Mode Aot` work with `UseLocalIronMapper=true`: the NativeAOT
  publish passes `StartupProbeAot=true` and the probes set `PublishAot` themselves (`probes/StartupProbe.props`); a
  global `PublishAot` reached the netstandard2.0 projects in `src/` and failed with `NETSDK1207`. With the local
  sources `Directory.Build.targets` imports `src/IronMapper/buildTransitive/IronMapper.targets`, so the AOT size is
  measured as with the package.
- Benchmarks: `Manual_DI` in `StartupBenchmark` and `probes/StartupProbe.Manual_DI`: the hand-written mapping resolved
  from the same Microsoft.Extensions.DependencyInjection container as `IronMapper_IMapper`, so the cold start of
  `IronMapper_IMapper` minus `Manual_DI` is what IronMapper adds on top of the container. The descriptions of
  `IronMapper_IMapper` in `benchmarks/README.md` and the feature matrix notes cover 1.1.x and 2.0.0.
- Tests: `tests/IronMapper.Tests/CollectionShapeTests.cs` maps every pair of `List<T>`, `T[]`, `IEnumerable<T>`,
  `ICollection<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>`, `IList<T>` as a nested property (new object and
  in place: values, order, `null`, empty, `null` element) and `MapToXList`/`MapToXArray` over several runtime source
  types; a new snapshot scenario `CollectionShapes`; snapshot tests also fail on compiler warnings in the generated
  files.
  `tests/IronMapper.PackageConsumer` calls its own and a referenced library's mappings from a file without the using.
- CI runs on push only for `main`; other branches are checked through their pull requests (all jobs run there).

## [1.1.4] - 2026-10-20

The generated code and the public API are the same as in 1.1.3 (the generator only got pipeline tracking names, see
Build). This release updates the README shown on nuget.org.

### Fixed

- **The Quick Start in the README did not compile.** It was missing `using IronMapper.Generated;` (the namespace of the
  generated `MapTo*` methods), so `entity.MapToUserDto()` failed with `CS1061`. The README (also the nuget.org page) and
  Getting started now include the `using` and say that it is required in every file that calls `MapTo*` methods.

### Documentation

- README: minimum .NET SDK for the source generator (8.0.400+, Roslyn 4.11) and what happens on an older SDK
  (`CS9057`, the generator does not run); Known limitations list the property type conversions that currently fail or
  warn inside the generated file. `IMapper` is described as matching methods by signature (true since 1.1.3).
- README rewritten (also the NuGet package page): the Quick Start compiles as shown (see Fixed); "Why IronMapper"
  with measured facts; a performance table from `benchmarks/results/RESULTS.md`
  (IronMapper, Mapperly, Mapster, AutoMapper vs hand-written code, including the scenarios where IronMapper is slower);
  Known limitations with links to issues #22–#27 (flattening by convention and `null` input with nullable declarations
  added); Roadmap from the open issues; absolute links for nuget.org. Same structure in README.ru.md.
- NativeAOT is documented as verified for the generated extension methods and not supported for `IMapper`
  (previously "not verified"), with the measured warnings and sizes (`docs/advanced.md`, migration guide).
  `docs/advanced.md` (Performance) and the migration guide have numbers from the benchmarks instead of "no results
  published yet"; the migration guide lists flattening, nested single objects and constructor mapping as not
  supported and adds `using IronMapper.Generated;` to the extension-method step. `docs/diagnostics.md`: status table
  labeled 1.1.3.

### Build

- Comparison benchmarks (`benchmarks/`): the suite also targets `net8.0`; the runtime scenarios run on .NET 8 with
  `dotnet run -f net8.0` (output in `results/raw/bdn-net8.0/`, the .NET 10 results stay the reference). The 1.1.3
  results are frozen in `benchmarks/results/baseline-1.1.3/` as the reference for 1.2.0. The benchmark program exits
  with code 1 when a benchmark fails or a filter matches nothing.
- CI: the `benchmarks` job is now a smoke check of `benchmarks/` on every push and pull request (build against the
  local sources, equivalence tests, one iteration of every runtime scenario on .NET 10 and .NET 8, no published
  numbers). It replaces the run of the old `tests/IronMapper.Benchmarks` project, which is removed (IronMapper vs
  hand-written code only, superseded by `benchmarks/`). README and `docs/advanced.md` point to `benchmarks/` and its
  results instead.
- CI and publish workflows use the released .NET SDK (`dotnet-quality: 'preview'` removed).
- Snapshot tests of the generated code (Verify.SourceGenerators, `tests/IronMapper.Generator.Tests/Snapshots`) and
  incremental caching tests of the generator (`trackIncrementalGeneratorSteps`). Generator pipeline steps have
  tracking names (no behavior change).
- `.gitattributes`: CRLF for `*.cs`, `*.ps1`, `*.csproj`, `*.props`, `*.md`; LF for `*.sh` and Verify snapshots.

## [1.1.3] - 2026-10-18

### Fixed

- **A type pair defined twice no longer breaks the whole project.** `[MapTo]` + `CreateMap`, `[MapTo]` + `[MapFrom]`,
  two profiles, or `CreateMap` + another map's `ReverseMap()` for the same pair made the generator fail with `CS8785`
  (duplicate hint name), and **no mapping code was generated for the project**. The pair is now generated once and
  `IM0007` is reported (until now it was declared but never reported). Precedence: `CreateMap`, then `ReverseMap()`,
  then `[MapTo]`, then `[MapFrom]`; within one kind, the profile (or type) whose full name sorts first.
- One source type mapped to several destination types with the same simple name (other namespaces, nested in
  different types, other type arguments) failed with `CS0111`. These mappings now get qualified method names
  (`MapToAdminUserDto`, `MapToOrdersDto`, `MapToBoxOfInt32`) and `IM0013` gives the name to call; if no unique name
  exists, `IM0014` and only that group is skipped. Method names that compiled on 1.1.2 do not change.
- Hint names are built from the full names of both types: same simple names in different namespaces or outer types no
  longer collide (`CS8785`).
- Hook and transformer helpers of mappings with the same simple type names (e.g. `Shop.Item → Shop.ItemDto` and
  `Stock.Item → Stock.ItemDto` with `AddTransformer`) no longer fail with `CS0111`: they use the full names then.
- Converter types in `ConvertUsing<T>()`, `UseConverter<T>()` and `[MapConverter]` are emitted with fully qualified
  names, so nested converter types compile (previously `CS0234`).
- Nested collection properties call the element mapping by its actual (possibly qualified) name.
- `IMapper` (`IronMapper.Extensions.DI`) finds generated methods by signature instead of `MapTo{TDest.Name}`, so
  qualified names work through `IMapper` too.
- **`IronMapper.Generated.GeneratedMappers` is `internal` when all mappings of the project are internal.** It was
  always `public`, so a project with only internal mappings still exposed the class to every project that referenced
  it, and naming `GeneratedMappers` there reported `CS0436`. The accessibility is now computed over all generated
  mappings of the assembly and declared once, in `GeneratedMappers.g.cs`; it is `public` if at least one generated method is.
- A project that maps a type pair already exposed by a referenced project no longer fails with `CS0121` inside its
  own generated `MapToXList`/`MapToXArray` methods: they call the method of their own class by its simple (possibly
  qualified) name.
- The class name stays `IronMapper.Generated.GeneratedMappers` in every assembly, so code that compiles on 1.1.2
  (`using IronMapper.Generated;` + `source.MapToX()`, and `GeneratedMappers.MapToX(source)` within one project) keeps
  working. See [Multi-project solutions](docs/configuration.md#multi-project-solutions) for the remaining limitations.

### Added

- Diagnostic `IM0013` (warning): a mapping method name was qualified because of a collision; the message has the name
  to call.
- Diagnostic `IM0014` (error): a method name collision cannot be resolved; the colliding mappings are not generated.

### Build

- `tests/IronMapper.PackageConsumer` references two libraries that also use the packed package
  (`IronMapper.PackageConsumer.InternalLib`, only internal mappings; `IronMapper.PackageConsumer.PublicLib`, public
  mappings, including a method name collision with qualified names called from the consumer) and builds with
  `TreatWarningsAsErrors`.

## [1.1.2] - 2026-10-18

### Fixed

- **Mappings of `internal` types no longer fail with CS0050/CS0051.** Generated methods (`MapToX`, the in-place
  overload, `MapToXList`, `MapToXArray`) were always `public`. They now get the lower effective accessibility of the
  source and destination types (containing types and generic type arguments included): `public` or `internal`.
  Applies to `[MapTo]`/`[MapFrom]` and `MappingProfile` mappings.
- Generated code uses fully qualified type names, so public types nested in other types and generic types
  (e.g. `Box<Payload>`) as source/destination now compile (previously CS0234/CS0305).
- `IMapper` (`IronMapper.Extensions.DI`) also finds `internal` generated methods.
- **Profile lambdas no longer fail with CS0103/CS1061 in the generated file.** Lambdas in `MapFrom`, `When`,
  `ConvertUsing`, `BeforeMap`/`AfterMap` and `AddTransformer` were copied as text, so static helpers of the profile,
  types and extension methods from the profile's `using` directives and unqualified static members (`Math.Round`)
  were not found. They are now rewritten through the semantic model: fully qualified names, extension methods as
  static calls, and parameters renamed by symbol (previously `s => s.s + "s"` also renamed the member and the
  string literal).

### Added

- Diagnostic `IM0010` (error): a mapping whose source or destination is (or is nested in, or has a type argument that is)
  a `private`/`protected`/`private protected` type is not generated, and the error names the inaccessible type.
  Other mappings are still generated.
- Diagnostic `IM0011` (error): a profile lambda uses a `private`/`protected` member or type; make it `internal`.
- Diagnostic `IM0012` (error): a profile lambda uses profile instance state, `this`, or a local variable/parameter
  of the constructor. In both cases only the affected mapping is skipped.

### Build

- `eng/verify-package.ps1` resolves a relative `-PackageDir` against the PowerShell location instead of the .NET
  process directory (previously, a console started in `C:\WINDOWS\system32` tried to write packages there).

## 1.1.1

### Fixed

- **The `IronMapper` package now ships the source generator.** 1.1.0 contained only `lib/`,
  so the generator never loaded in consuming projects and no `MapTo*` methods were generated.
  `IronMapper.Generator.dll` and `IronMapper.CodeFixes.dll` are now packed into
  `analyzers/dotnet/cs`; Roslyn (`Microsoft.CodeAnalysis.*`) is not a package dependency.
- `IronMapper.Extensions.DI` no longer excludes analyzers from its `IronMapper` dependency,
  so the generator also runs in projects that reference only the DI package.
- Package metadata, README and CI badge point to <https://github.com/iron-mapper/IronMapper>.

### Build

- CI packs both packages, verifies the `.nupkg` layout and builds a consumer project
  (`tests/IronMapper.PackageConsumer`) against the packed package from a local feed
  (`eng/verify-package.ps1`). The publish workflow runs the same check before pushing
  and requires the tag to match `<Version>` in `Directory.Build.props`.
