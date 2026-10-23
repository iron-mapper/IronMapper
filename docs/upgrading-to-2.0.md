# Upgrading to IronMapper 2.0

Русская версия: [upgrading-to-2.0.ru.md](upgrading-to-2.0.ru.md)

2.0.0 follows 1.1.4. It changes five things that code written for 1.1.x can depend on. Each section below says what
it was, what it is now and what to do. Everything else in 2.0.0 is new functionality or a fix
([CHANGELOG](../CHANGELOG.md)).

Most projects need only the first step: build, and if the build reports `IDE0005` at `using IronMapper.Generated;`,
remove those usings.

| Change | Who is affected |
|---|---|
| [`global using IronMapper.Generated` from the package](#1-global-using-ironmappergenerated-from-the-package) | Projects with implicit usings and a strict code style in the build (`IDE0005` as an error); files with a hand-written `MapTo*` extension method of the same name |
| [The generated class is `GeneratedMappers_{AssemblyName}`](#2-the-generated-class-is-generatedmappers_assemblyname) | Code that names the class: `GeneratedMappers.MapToX(...)`, `using static`, reflection by name, `RegisterIronMapperMappings()` in C# 8 projects |
| [`IMapper` does not wrap exceptions](#3-imapper-does-not-wrap-exceptions) | Code that catches `TargetInvocationException` around `IMapper` |
| [`AddIronMapper` does not register profiles](#4-addironmapper-does-not-register-profiles-in-the-container) | Code that resolves a `MappingProfile` from the DI container |
| [`MapCollection` maps during the call](#5-mapcollection-maps-during-the-call) | Code that relies on `MapCollection` being lazy |

Calls of the generated extension methods (`source.MapToX()`, `MapToXList()`, `MapToXArray()`, the in-place overload)
and of `IMapper.Map` do not change.

---

## 1. `global using IronMapper.Generated` from the package

**Was.** The generated `MapTo*` methods are in the namespace `IronMapper.Generated`; every file that called them needed
`using IronMapper.Generated;`, otherwise `CS1061`.

**Now.** The package adds `global using IronMapper.Generated;` to C# projects with implicit usings
(`<ImplicitUsings>enable</ImplicitUsings>`, the default in the .NET 6+ templates), also when it comes through
`IronMapper.Extensions.DI`. Projects without implicit usings and C# 9 and earlier are not affected.

**What you may see.**

- An explicit `using IronMapper.Generated;` is now redundant. The compiler does not warn, but a strict code style in the
  build does: with `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` and
  `dotnet_diagnostic.IDE0005.severity = error` (or `warning` with `TreatWarningsAsErrors`) the build fails:

  ```text
  Program.cs(7,1): error IDE0005: Using directive is unnecessary.
  ```

  (The build reports `IDE0005` only with `GenerateDocumentationFile` on, as for any unnecessary using.) This case is
  built against the packed package by `eng/verify-package.ps1`.
- A file that imports another extension method with the same name and parameter type as a generated one (for example a
  hand-written `MapToUserDto` in code you migrate) gets `CS0121` (ambiguous call) at that call.

**What to do.** One of:

- Remove the explicit usings: "Remove unnecessary usings" in the IDE, or
  `dotnet format style --diagnostics IDE0005 --severity info`.
- Keep them and turn the global using off for the project (or for all projects in `Directory.Build.props`):

  ```xml
  <PropertyGroup>
    <IronMapperImplicitUsings>false</IronMapperImplicitUsings>
  </PropertyGroup>
  ```

For `CS0121`, remove or rename the hand-written method, or turn the global using off.
See [Global using](configuration.md#global-using-of-ironmappergenerated).

## 2. The generated class is `GeneratedMappers_{AssemblyName}`

**Was.** Every project got the class `IronMapper.Generated.GeneratedMappers`.

**Now.** The class name contains the assembly name: `IronMapper.Generated.GeneratedMappers_{AssemblyName}`, every
character that is not a letter, a digit or `_` replaced with `_`:

| Assembly | Class |
|---|---|
| `Shop.Application` | `GeneratedMappers_Shop_Application` |
| `Shop.Infrastructure.EfCore` | `GeneratedMappers_Shop_Infrastructure_EfCore` |
| `My-App.Web` | `GeneratedMappers_My_App_Web` |

An assembly name longer than 64 characters is shortened to its first 55 characters, followed by `_` and 8 hex digits of
a hash of the full name (stable between builds). Why: with one name in every assembly, tools that identify types by
their full name (ArchUnitNET, for example) attributed the dependencies of one assembly's generated class to another's,
and a layer rule like "Application does not depend on Infrastructure" failed; naming the class also reported `CS0436`
when a referenced project's class was visible. Details:
[Multi-project solutions](configuration.md#multi-project-solutions).

**What breaks.** Only code that names the class:

| 1.1.x | 2.0 |
|---|---|
| `GeneratedMappers.MapToUserDto(user)` | `user.MapToUserDto()` (or `GeneratedMappers_Shop_Application.MapToUserDto(user)`) |
| `users.Select(GeneratedMappers.MapToUserDto)` | `users.Select(u => u.MapToUserDto())`, or `users.MapToUserDtoList()` |
| `using static IronMapper.Generated.GeneratedMappers;` | remove it; the extension methods need only the namespace |
| `assembly.GetType("IronMapper.Generated.GeneratedMappers")` | the new name, or `MapperRegistry` (`IronMapper.Registration`) to map by `Type` |
| C# 8: `GeneratedMappers.RegisterIronMapperMappings()` | `GeneratedMappers_{AssemblyName}.RegisterIronMapperMappings()` |

The compiler reports each of them (`CS0103` "The name 'GeneratedMappers' does not exist in the current context", `CS0234`
for `using static`), except the lookup by reflection.

**What to do.** Use the extension method syntax: it does not depend on the class name, and it keeps working when an
assembly is renamed. The warning `IM0013` (qualified method names) now names the class of the project as well.

## 3. `IMapper` does not wrap exceptions

**Was.** `IMapper` (`IronMapper.Extensions.DI`) called the generated methods through `MethodInfo.Invoke`: an exception of
the generated code (`ArgumentNullException` for a `null` nested element), of your converter or of a `BeforeMap`/
`AfterMap` hook came as `TargetInvocationException`, the original in `InnerException`.

**Now.** `IMapper` calls the generated methods through delegates: the original exception reaches the caller as it is,
with the stack of the generated method.

**What to do.** Catch the original exception type instead of `TargetInvocationException`, and remove code that unwraps
`InnerException`:

```csharp
// 1.1.x
catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException inner) { ... }

// 2.0
catch (ArgumentNullException ex) { ... }
```

## 4. `AddIronMapper` does not register profiles in the container

**Was.** `AddIronMapper(assemblies)` scanned the assemblies with `GetTypes()` and added every `MappingProfile` subclass
to the container as a singleton.

**Now.** Profiles are read by the source generator at build time; `AddIronMapper` registers `IMapper` and, for every
assembly passed (also through `AddProfile<T>()` and `AddProfilesFromAssembly`), runs the assembly's generated
registration of its mappings. Nothing is added for the profiles. `AddProfilesFromAssembly(null)` throws
`ArgumentNullException` at the call (it used to fail later).

**What to do.** If your code resolves a profile from the container (`GetRequiredService<OrderProfile>()`, a profile as a
constructor parameter), register it yourself: `services.AddSingleton<OrderProfile>();`. Usually nothing needs it: a
profile does nothing at run time. Pass every assembly that holds mappings to `AddIronMapper`, in particular one that the
app does not otherwise use before the first mapping (profiles in `Application`, the call from the web project).

## 5. `MapCollection` maps during the call

**Was.** `IMapper.MapCollection<TSource, TDest>(source)` returned a lazy sequence: the elements were mapped when the
result was enumerated, again on every enumeration, and a `null` source, a `null` element or a missing mapping threw only
then.

**Now.** It returns a `List<TDest>` (typed as `IEnumerable<TDest>`, the signature is the same) filled during the call:
the source is enumerated once, exceptions are thrown at the call, enumerating the result again does not map again, and
changes of the source after the call are not seen.

**What to do.** Nothing, unless you relied on the laziness (an endless or very large sequence mapped piece by piece, or
a result enumerated after the source changed). For a lazy sequence write it explicitly:

```csharp
IEnumerable<OrderDto> lazy = orders.Select(o => mapper.Map<Order, OrderDto>(o));
```

---

## Other changes you may notice

These do not need code changes, but can show up after the upgrade:

- **Code coverage.** The generated class is marked `[GeneratedCode]`. A coverage tool that excludes it (coverlet with
  `ExcludeByAttribute=GeneratedCodeAttribute`) leaves the generated mappings out of the report, so the percentage
  changes. To measure them as before: `<IronMapperGeneratedCodeAttribute>false</IronMapperGeneratedCodeAttribute>`
  ([details](configuration.md#attributes-of-the-generated-class)).
- **New errors instead of silent gaps or errors in generated files**: `IM0003` for `[MapTo]`/`[MapFrom]` with an open
  generic type or an array (the mapping used to be dropped without a message), `IM0015` for a converter that the
  generated code cannot create (it used to be `CS7036`/`CS1729`/`CS0122` inside a `.g.cs` file).
  See [diagnostics](diagnostics.md).
- **Diagnostics have source locations**: `.editorconfig` severities (`dotnet_diagnostic.IMxxxx.severity`) now apply to
  them, so a rule you set long ago may start to apply.
- A stateless converter is created once and reused (it used to be created on every call); this is visible only through
  state kept outside the converter ([Converter instances](configuration.md#converter-instances)).
