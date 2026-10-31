# Roslyn generator invariants

Read root and ../AGENTS.md, accepted decisions in IRONMAPPER_PLAN.md and the relevant detailed feature description
in root CLAUDE.md. Verification: ../../tests/AGENTS.md; diagnostics: ../../docs/AGENTS.md and ../../site/AGENTS.md.

## Incremental pipeline

- Keep IIncrementalGenerator and netstandard2.0. Analyze Roslyn objects inside transforms; carry value data into
  Analysis/Models. No ISymbol (including type/method symbols), SyntaxNode, Location or Compilation in pipeline models.
  Collections are EquatableArray<T>. Every field, including diagnostics, participates in Equals/GetHashCode; new fields
  also need copying/WithoutDiagnostics paths and ModelEqualityTests.
- Semantic transforms can rerun on a compilation change, but equal results are Unchanged and downstream work Cached.
  Do not scan every invocation to emit only used public methods: this breaks incrementality and misses external use;
  that proposal was rejected. Preserve TrackingNames and caching coverage.
- Source outputs consume MappingDescriptor/MappingPlan.WithoutDiagnostics copies (*Source/SourcePlan), with locations
  stripped. Moving mappings updates locations without regenerating source. Diagnostics contain value LocationInfo
  (path, TextSpan, LinePositionSpan), additional places and properties, and collect into one Diagnostics value.
- Report diagnostics through a separate output combined with the current compilation. LocationInfo.ToLocation(trees)
  must bind to its current syntax tree; path-only locations are insufficient for editorconfig/code fixes when a tree
  exists. This reporting output intentionally reruns each compilation and is exempt from source-cache expectations.
- Scope settings independently: unmapped-profile checks are filtered in CollectDiagnostics, not profile analysis;
  method/registration settings affect planning/outputs; coverage/GeneratedCode settings affect the class declaration.
  Assembly name must not invalidate the plan just to format IM0013. Consumer MSBuild options require GeneratorSettings
  and CompilerVisibleProperty in packaged props.

## Generated C# and public surface

- Ordinary readable C#, no reflection/dynamic IL, LINQ or per-element delegates in generated mapping hot paths, no
  unnecessary allocations. Generated registry delegates are intentional. Emit only needed shared helpers.
- Use only APIs/attributes available in the consumer compilation, with supported fallbacks. Preserve C# 8/9 and
  netstandard cases; repository LangVersion=latest does not apply to consumers. ThrowIfNull/DoesNotReturn are conditional;
  retain throw-helper control flow that avoids nullable warnings without DoesNotReturn.
- One mapping per pair. Class: IronMapper.Generated.GeneratedMappers_{assembly}, replacing non-letter/non-digit/
  non-underscore characters by _. Over 64 characters: first 55, _, eight hex FNV-1a characters of the full name.
  Preserve shared hint names, per-pair hint uniqueness and cross-assembly architecture tests.
- Methods get minimum effective source/destination accessibility, including containers/type arguments. Unsupported
  private/protected nested types produce IM0010. Class is public if any mapping is public; declare visibility and
  GeneratedCode (generator informational version)/optional ExcludeFromCodeCoverage only once across partial parts.
  With no mappings emit an empty IronMapper.Generated namespace for the package's global using.
- Duplicate pairs generate once with IM0007; profile CreateMap beats ReverseMap/attributes. Same simple destination
  names for one source get qualified names and IM0013; unresolvable collisions produce IM0014.
- MapToX(source) always exists. New-object Core exists only if called for a nested object, converted value or
  constructor/factory argument. In-place Core exists only if a generated parent in-place body calls it; preserve
  fixed-point propagation. Collection elements call public methods. Unnested maps retain the 2.0.0 shape and baseline.
- AggressiveInlining only on thin argument-check-plus-Core wrappers, including private MapInto wrappers; never on
  Core or methods containing the mapping body merely to improve a benchmark.
- Preserve assembly/per-map GenerateInPlace/List/Array and direct-only GenerateMethods overrides. With registration,
  a disabled public in-place method still needs private MapInto; disabled List/Array registers null for registry loops.
  Explicit IronMapperRegistration=false emits no registration or registration-only MapInto/stubs. Automatic trim/AOT
  selection uses _IronMapperRegistrationSwitch without changing generation on its own.
- Shared list/array loops use private generated interface/readonly struct element mappers and array/List/ICollection
  fast paths/known capacity, no static abstract interface members. Nested helpers follow declared collection shapes.
  Nested IEnumerable destinations stay lazy in 2.x; same-element collections share references by default. Do not
  silently introduce copying, materialization or collection merging.

## Mapping contracts

- Explicit configuration wins. Nested objects need an available pair map; no unrequested automatic private mapping.
  Retain supported source-base/interface matching and referenced/manual extension-method resolution where applicable.
- Nested null follows destination annotations: null for nullable destinations, otherwise InvalidOperationException
  naming both members with EF Include/configuration guidance via a shared NoInlining throw helper. No null! substitution
  for non-nullable destinations or unnecessary struct null checks; preserve existing baseline code unless changing it
  is explicitly in scope (the historical struct-source ThrowIfNull remains in the 2.0.0 code-shape baseline).
- In-place preserves eligible existing nested instances recursively; null destinations get a new instance. Structs,
  inaccessible getters, constructors/factories, ConvertUsing or mapped init members can require replacement under
  CanUpdateInPlace. Referenced pairs need a callable public non-obsolete in-place overload to update an existing object.
- Skip init assignments in-place; eligible nested init objects update through getters when objects exist. Preserve
  IM0025 for skipped/conditionally updated init members on a useful public overload. No-op public overloads remain
  obsolete in 2.x; registered MapInto throws MappingException with the reason. Hooks/updatable nested init can make an
  overload useful. Do not remove obsolete public APIs early.
- Collection null elements keep documented ArgumentNullException and public MapToX calls, not unchecked Core.
  IronMapperNullSourceReturnsNull is a 2.x class-to-class public new-object opt-in using NotNullIfNotNull("source"),
  not C# 11 nameof. It changes neither IMapper nor in-place/struct signatures. Preserve NotNullElement and referenced
  assembly cases. Emit missing attribute polyfills only if IronMapperEmitPolyfills permits (PolySharp compatibility).
- Cache converters only when the entire source-declared hierarchy has no instance/backing fields, field-like events
  or finalizer, code-free constructors and a public parameterless constructor. Otherwise create per call. Unusable
  converters produce IM0015. Do not relax eligibility for referenced converters without a maintainer decision.
- Prefer accessible parameterless construction/struct initializer. Otherwise use deterministic accepted selection:
  most source-bound parameters, then total parameters, no declaration-order tie winner; use named args/defaults and
  diagnostics. No IronMapper attributes on domain constructors. Two-pass selection prefers constructors needing no
  pair conversion, so adding another file's pair does not unexpectedly change selection.
- ConstructWith matches static-method/typed-lambda parameters; ForCtorParam overrides ForMember. ConstructUsing emits
  its expression without parameter matching; it is ignored in-place, where an accessible parameterless constructor
  is required. ConstructWith/parameterized construction have no public in-place mapping; registered attempts throw.
- Pair value conversions use explicit maps with nullable lifting/direct-pair precedence, not From/Value conventions;
  no in-place value updates. Preserve nullability, referenced calls and explicit member configuration precedence.
  ReverseMap(converter) sets the reverse converter; ordinary ReverseMap maps names without forwarding ForMember/factories.
- BeforeMap sees initialized required/init members, then other assignments and AfterMap. Preserve When/hooks in all
  applicable in-place bodies, including nested/registry ones. Missing required values yield IM0026 except accepted
  factory/converter/SetsRequiredMembers cases. Per-member conditions do not exist. New-object When/default! semantics
  are unresolved in the plan: do not redesign them without a decision.
- Static method groups in profile settings must be called; unusable callable settings get diagnostics, never silent
  omission. Preserve Ignore on source/destination, IncludeMembers and ReverseMap directions with ForMember/ForCtorParam
  precedence. Constructor/factory/converter/hook exceptions propagate unchanged.

## Diagnostics

- Actionable IMxxxx at user code: identify types/members, failure and correction. Follow docs/diagnostics and location
  tests. Do not leave compiler errors/warnings in .g.cs. Error types already diagnosed by the compiler are skipped
  without redundant IM0003; valid but unsupported shapes still need their diagnostic.
- Missing-pair diagnostics depend on the final plan, including other files/referenced maps. Preserve conditional
  IfNoMappingFor/IfMappingFor logic and IM0002 nested/collection versus value-conversion code-fix properties; never
  offer impossible fixes such as adding an attribute to Guid.
- IM0004 is Warning on cycles closed by automatically mapped single nested objects; collection-only cycles are not
  warned. Explicit MapFrom/Ignore and ConvertUsing break relevant edges. Include the chain; distinguish cycles in
  types from cycles in data, without unrequested runtime tracking/MaxDepth overhead.
- IM0023 replaces nullable compiler warnings at the precise value. Use ! only with its diagnostic or the accepted
  nested-annotation widening case; never hide narrowing. Honor ReplacesWarning severity/suppression from project
  options/globalconfig, nullable-disabled inputs and the documented WarningsNotAsErrors limitation.
- Use standard editorconfig/globalconfig/WarningsAsErrors/NoWarn, not IronMapperStrictMapping. Ordinary ReverseMap
  does not opt into IM0001 profile validation. Preserve the accepted IM0019–IM0026 rejection contracts.
- Keep descriptors, AnalyzerReleases.Unshipped.md, EN/RU docs and tests aligned. Use the central descriptor factory
  and stable https://iron-mapper.ru/diagnostics/imxxxx/ helpLink builder; withdrawn codes keep source sections.
  Validate behavior, locations, severity, code fixes, snapshots, equality, caching and post-edit diagnostics as needed.
