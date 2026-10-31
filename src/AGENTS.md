# Library architecture

Read the root guide and IRONMAPPER_PLAN.md. Generator changes also need IronMapper.Generator/AGENTS.md;
public behavior needs ../tests/AGENTS.md and ../docs/AGENTS.md; packaging/MSBuild needs ../eng/AGENTS.md.

## Boundaries and compatibility

- IronMapper: runtime attributes, profiles/configuration, interfaces, exceptions, validation and registration;
  netstandard2.0;net10.0, no NuGet dependencies. Generator and CodeFixes target netstandard2.0; Roslyn dependencies
  are private. DI is a separate netstandard2.1;net10.0 package depending on DI abstractions and flowing core/analyzers
  transitively. Do not confuse consumer minimum SDK 8.0.400 (Roslyn 4.11) with repository net10 build requirements.
- Profiles describe mappings for compile-time analysis; do not instantiate them to map or register them in DI.
  Preserve external interface implementations: accepted additive configuration helpers use extension methods.
- MappingValidator intentionally uses reflection and RequiresUnreferencedCode. Keep actual reflection entry points
  annotated, including netstandard polyfills. Do not restore historical reflection warnings on the generated
  registry path. IsAotCompatible must continue detecting unannotated reflection in net10 builds.
- Keep namespaces/naming: IronMapper, *Attribute, *Profile, IM plus four digits, MapTo{Destination} and its
  in-place/List/Array overloads. See the generator guide for collision handling and visibility.

## Registry and DI

- No reflection lookup/invocation of generated methods, GetTypes profile scanning, dynamic IL, MakeGenericType or
  MakeGenericMethod. Use generated MapperRegistry.Register<S,D> delegates.
- Module initialization calls AddAssembly only when MapperRegistry.IsSupported. Queue the callback on the lock-free
  pending list; do not eagerly initialize registry dictionaries or register every pair there.
- EnsureInitialized drains callbacks under synchronization at first mapper use/lookup. Callers wait for complete
  registration. Preserve first-registration-wins, cache invalidation on new registration, concurrent and late-loaded
  assembly behavior. Exact generic pairs use MapperCache<S,D>.
- AddIronMapper(assembly) ensures initialization with RuntimeHelpers.RunModuleConstructor. On missing mappings,
  initialize loaded non-framework assemblies and retry; no generated-member scan or GetReferencedAssemblies.
- Runtime-source lookup follows source base types; do not silently add source-interface or destination-base dispatch.
  Preserve supported array/List/IEnumerable/List-interface destination mappings. Original exceptions propagate,
  including ArgumentNullException.ParamName, without TargetInvocationException wrapping.
- Feature switch IronMapper.Registration.IsSupported uses embedded ILLink.Substitutions.xml. Trimmed/AOT apps
  without DI default it off; DI's buildTransitive marker works transitively; explicit IronMapperRegistration wins.
  Switch-off IMapper use fails with an actionable property name. Automatic trimming and generator opt-out are distinct.
- Do not add assembly markers/custom-attribute scans for better error text; this was rejected due to NativeAOT size.
- Converters come from generated code, not DI. AddConverter<T> is obsolete for 2.1.0, still registers a transient type,
  and is slated for removal in 3.0. Do not remove it in 2.x or implement service resolution without a new decision.
  Values needing services are computed before mapping or assigned afterward.

## Code fixes and style

- Code fixes use diagnostic locations/properties, never message text or simple-name symbol searches. Preserve
  IM0001's profile-local ForMember Ignore, nested/collection IM0002 pair fixes and IM0006 destination choices.
  Test using the generator via GetSourceGenerators, including severity, locations and intended document changes.
- Keep EnforceCodeStyleInBuild and AnalysisLevelStyle on netstandard too; warning-level style options need the latter.
  Do not ship repository style settings. Generated auto-generated code has its own compatibility/shape contracts;
  do not reformat generated strings to follow repository-only modern C# preferences.
- Package/global-using/polyfill changes need packed consumers. Preserve global using conditions and empty generated
  namespace behavior; no-map consumers must compile. Read ../eng/AGENTS.md for all package/CI checks.
