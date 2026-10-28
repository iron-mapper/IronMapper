using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using IronMapper.Exceptions;

namespace IronMapper.Registration;

/// <summary>
/// Registry of the generated mappings, for mapping by <see cref="Type"/> without reflection over the generated code:
/// the <c>IMapper</c> of <c>IronMapper.Extensions.DI</c> uses it, and it works with trimming and NativeAOT.
/// </summary>
/// <remarks>
/// <para>
/// The source generator emits, for every assembly with mappings, a module initializer
/// (<c>[ModuleInitializer]</c>) that passes the assembly's registration to <see cref="AddAssembly"/>. That only puts
/// one delegate on a list (no dictionaries, nothing per mapping, and the registry itself is not initialized): the
/// mappings are registered (<see cref="Register{TSource, TDest}"/>) on the first use of the registry
/// (<see cref="EnsureInitialized"/>, which creating the <c>IMapper</c> calls). An application that only calls the
/// generated <c>MapTo*</c> extension methods loads this assembly for the module initializer and does nothing else.
/// </para>
/// <para>
/// A module initializer runs when the assembly's code is first used. <see cref="EnsureModuleInitialized"/> runs it for
/// an assembly that nothing has touched yet (<c>AddIronMapper(assembly)</c> does this), and
/// <see cref="EnsureLoadedAssembliesRegistered"/> for every loaded assembly. Projects with
/// <c>LangVersion</c> below 9 get no module initializer and must call
/// <c>RegisterIronMapperMappings()</c> of their generated class (<c>IronMapper.Generated.GeneratedMappers_{AssemblyName}</c>)
/// themselves.
/// </para>
/// <para>
/// The registry is thread-safe. When two assemblies register the same type pair, the first registration wins.
/// </para>
/// <para>
/// The generated registration runs only when <see cref="IsSupported"/> is <see langword="true"/> (feature switch
/// <c>IronMapper.Registration.IsSupported</c>). For a trimmed or NativeAOT publish without
/// <c>IronMapper.Extensions.DI</c> the IronMapper package turns it off, and the trimmer removes the registration.
/// </para>
/// </remarks>
public static class MapperRegistry
{
    /// <summary>The name of the feature switch read by <see cref="IsSupported"/>.</summary>
    public const string FeatureSwitchName = "IronMapper.Registration.IsSupported";

    // Static fields with initializers: they are created when the registry is first used (EnsureInitialized and the
    // lookups), not by AddAssembly, which only touches PendingAssemblies.
    private static readonly object s_lock = new();
    private static int s_scannedAssemblyCount = -1;

    // Keyed by Type, not by a (Type, Type) tuple: the dictionaries of reference types share the precompiled code of
    // the framework, a tuple key would be JIT-compiled at startup. Written under s_lock, read without it.
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, MapperEntry>> s_entries = new();
    private static readonly ConcurrentDictionary<Type, MapperEntry[]> s_byDestination = new();

    // Maps found through a base type or a collection type, by destination and source type. A registration replaces it
    // when a lookup may have added to it (s_resolvedUsed, set before the lookup reads s_entries; both sides use a full
    // fence, so a lookup that ran before the new entry was visible is never kept). Not cleared and not checked with
    // IsEmpty: both take every lock of the dictionary, and the first time that happens the framework initializes the
    // event source of the concurrent collections.
    private static ConcurrentDictionary<Type, ConcurrentDictionary<Type, Func<object, object?>>> s_resolved = new();
    private static int s_resolvedUsed;

    /// <summary>
    /// Whether the generated registrations add the mappings of their assemblies (checked by the generated module
    /// initializer before it calls <see cref="AddAssembly"/>): the feature switch <see cref="FeatureSwitchName"/>,
    /// <see langword="true"/> when it is not set.
    /// </summary>
    /// <remarks>
    /// The IronMapper package sets the switch from the MSBuild property <c>IronMapperRegistration</c> of the application.
    /// When the property is not set, the switch is <see langword="false"/> for a trimmed or NativeAOT publish
    /// (<c>PublishTrimmed</c> or <c>PublishAot</c>) in which no project references <c>IronMapper.Extensions.DI</c>, and
    /// not set otherwise. The trimmer replaces this property with the value of the switch (<c>ILLink.Substitutions.xml</c>
    /// in this assembly), so an application that only calls the generated extension methods does not carry the
    /// registration and the registry.
    /// </remarks>
    public static bool IsSupported => !AppContext.TryGetSwitch(FeatureSwitchName, out bool isSupported) || isSupported;

    /// <summary>
    /// Throws when <see cref="IsSupported"/> is <see langword="false"/>: the generated mappings are not registered, so
    /// mapping by type (<c>IMapper</c>) would find none. <c>RuntimeMapper</c> calls it when it is created.
    /// </summary>
    /// <exception cref="InvalidOperationException">The registration is turned off.</exception>
    public static void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException(NotSupportedMessage);
        }
    }

    /// <summary>
    /// Adds the registration of one assembly; called by the generated module initializer. The registration runs on
    /// the first use of the registry (<see cref="EnsureInitialized"/>).
    /// </summary>
    /// <param name="registerMappings">Calls <see cref="Register{TSource, TDest}"/> for every mapping of the assembly.</param>
    public static void AddAssembly(Action registerMappings)
    {
        if (registerMappings is null)
        {
            throw new ArgumentNullException(nameof(registerMappings));
        }

        PendingAssemblies.Push(registerMappings);
    }

    /// <summary>
    /// Registers the generated methods of one mapping; called by the generated registration. A pair that is already
    /// registered keeps its first registration.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDest">The destination type.</typeparam>
    /// <param name="map">Calls <c>MapToX(source)</c>.</param>
    /// <param name="mapInto">Calls <c>MapToX(source, destination)</c>.</param>
    /// <param name="mapList">Calls <c>MapToXList(source)</c>; <see langword="null"/> when the method is not generated
    /// (<c>IronMapperGenerateList</c>/<c>GenerateList = false</c>): the registry then maps the elements with
    /// <paramref name="map"/>.</param>
    /// <param name="mapArray">Calls <c>MapToXArray(source)</c>; <see langword="null"/> when the method is not generated
    /// (<c>IronMapperGenerateArray</c>/<c>GenerateArray = false</c>): the registry then maps the elements with
    /// <paramref name="map"/>.</param>
    public static void Register<TSource, TDest>(
        Func<TSource, TDest> map,
        Action<TSource, TDest> mapInto,
        Func<IEnumerable<TSource>?, List<TDest>>? mapList,
        Func<IEnumerable<TSource>?, TDest[]>? mapArray)
    {
        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        if (mapInto is null)
        {
            throw new ArgumentNullException(nameof(mapInto));
        }

        lock (s_lock)
        {
            var bySource = s_entries.GetOrAdd(typeof(TDest), static _ => new ConcurrentDictionary<Type, MapperEntry>());
            if (bySource.ContainsKey(typeof(TSource)))
            {
                return;
            }

            var entry = new MapperEntry<TSource, TDest>(map, mapInto, mapList, mapArray);
            bySource[typeof(TSource)] = entry;
            s_byDestination[typeof(TDest)] = s_byDestination.TryGetValue(typeof(TDest), out var others)
                ? Append(others, entry)
                : new MapperEntry[] { entry };
            MapperCache<TSource, TDest>.Set(map, mapInto);
            if (Interlocked.Exchange(ref s_resolvedUsed, 0) != 0)
            {
                Volatile.Write(ref s_resolved, new ConcurrentDictionary<Type, ConcurrentDictionary<Type, Func<object, object?>>>());
            }
        }
    }

    /// <summary>
    /// Registers the mappings of every assembly whose module initializer has run (<see cref="AddAssembly"/>) and that
    /// is not registered yet. Cheap when there is nothing new.
    /// </summary>
    public static void EnsureInitialized()
    {
        // Not 0 from the moment an assembly is added until its registration has finished, so a caller that sees 0 never
        // runs ahead of a registration that another thread is still doing.
        if (PendingAssemblies.Count == 0)
        {
            return;
        }

        lock (s_lock)
        {
            // A registration may load another assembly whose module initializer adds more (the lock is re-entrant).
            while (PendingAssemblies.TakeAll() is { } pending)
            {
                try
                {
                    foreach (var register in pending)
                    {
                        register();
                    }
                }
                finally
                {
                    PendingAssemblies.Done(pending.Length);
                }
            }
        }
    }

    /// <summary>
    /// Runs the module initializer of <paramref name="assembly"/> (once per process; a no-op if it has run), which adds
    /// the assembly's registration (<see cref="AddAssembly"/>); the mappings are registered on the next use of the
    /// registry. Needed for an assembly whose code nothing has used yet, e.g. one that holds only profiles. Uses
    /// <see cref="RuntimeHelpers.RunModuleConstructor"/>: no reflection over the assembly's types, safe for trimming and
    /// NativeAOT.
    /// </summary>
    /// <param name="assembly">An assembly with generated mappings (others are ignored).</param>
    public static void EnsureModuleInitialized(Assembly assembly)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        RunModuleInitializer(assembly);
    }

    /// <summary>
    /// <see cref="EnsureModuleInitialized"/> for every loaded assembly except the framework ones (names starting with
    /// <c>System.</c> or <c>Microsoft.</c>). Runs their module initializers, which the runtime would otherwise run on
    /// the first use of each assembly (at most once either way). Scans again only when more assemblies are loaded than
    /// at the previous scan. Whether an assembly references IronMapper is not checked:
    /// <see cref="Assembly.GetReferencedAssemblies"/> is not safe for trimming.
    /// </summary>
    public static void EnsureLoadedAssembliesRegistered()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (assemblies.Length != Volatile.Read(ref s_scannedAssemblyCount))
        {
            foreach (var assembly in assemblies)
            {
                if (IsFrameworkAssembly(assembly))
                {
                    continue;
                }

                try
                {
                    RunModuleInitializer(assembly);
                }
                catch (TypeInitializationException)
                {
                    // Another library's module initializer failed; it would fail at that library's first use anyway.
                    // This scan only looks for mappings, so the caller gets "no mapping" instead of that exception.
                }
            }

            Volatile.Write(ref s_scannedAssemblyCount, assemblies.Length);
        }

        EnsureInitialized();
    }

    /// <summary>
    /// Finds the mapping from <paramref name="sourceType"/> to <paramref name="destinationType"/>: the registered pair,
    /// otherwise the pair of the nearest base type of <paramref name="sourceType"/>, otherwise, when
    /// <paramref name="destinationType"/> is <c>List&lt;T&gt;</c>, <c>T[]</c>, <c>IEnumerable&lt;T&gt;</c>,
    /// <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c> or
    /// <c>IReadOnlyCollection&lt;T&gt;</c> and <paramref name="sourceType"/> is an <c>IEnumerable&lt;S&gt;</c> of a
    /// registered <c>S → T</c> mapping, the generated <c>MapToTList</c>/<c>MapToTArray</c> (a <c>List&lt;T&gt;</c> for
    /// every type except <c>T[]</c>).
    /// </summary>
    /// <param name="sourceType">The source type (usually the runtime type of the source object).</param>
    /// <param name="destinationType">The destination type.</param>
    /// <param name="map">The mapping; <see langword="null"/> when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if a mapping is found.</returns>
    public static bool TryGetMap(Type sourceType, Type destinationType, out Func<object, object?> map)
    {
        if (sourceType is null)
        {
            throw new ArgumentNullException(nameof(sourceType));
        }

        if (destinationType is null)
        {
            throw new ArgumentNullException(nameof(destinationType));
        }

        EnsureInitialized();
        var resolved = Volatile.Read(ref s_resolved);
        if (resolved.TryGetValue(destinationType, out var bySource) && bySource.TryGetValue(sourceType, out var found))
        {
            map = found;
            return true;
        }

        Interlocked.Exchange(ref s_resolvedUsed, 1);
        found = Resolve(sourceType, destinationType);
        if (found is null)
        {
            map = null!;
            return false;
        }

        resolved.GetOrAdd(destinationType, static _ => new ConcurrentDictionary<Type, Func<object, object?>>())
            .TryAdd(sourceType, found);
        map = found;
        return true;
    }

    /// <summary>
    /// Finds the in-place mapping (<c>MapToX(source, destination)</c>) from <paramref name="sourceType"/> or its
    /// nearest base type with a registered mapping to <paramref name="destinationType"/>.
    /// </summary>
    /// <param name="sourceType">The source type.</param>
    /// <param name="destinationType">The destination type.</param>
    /// <param name="mapInto">The mapping; <see langword="null"/> when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if a mapping is found.</returns>
    public static bool TryGetMapInto(Type sourceType, Type destinationType, out Action<object, object> mapInto)
    {
        if (sourceType is null)
        {
            throw new ArgumentNullException(nameof(sourceType));
        }

        if (destinationType is null)
        {
            throw new ArgumentNullException(nameof(destinationType));
        }

        EnsureInitialized();
        if (s_entries.TryGetValue(destinationType, out var bySource))
        {
            for (var type = sourceType; type is not null; type = type.BaseType)
            {
                if (bySource.TryGetValue(type, out var entry))
                {
                    mapInto = entry.MapInto;
                    return true;
                }
            }
        }

        mapInto = null!;
        return false;
    }

    /// <summary>
    /// Maps <paramref name="source"/> to <paramref name="destinationType"/> with the mapping found by
    /// <see cref="TryGetMap"/> for <paramref name="sourceType"/>.
    /// </summary>
    /// <param name="source">The object to map.</param>
    /// <param name="sourceType">The type to look the mapping up by; <paramref name="source"/> must be an instance of it.</param>
    /// <param name="destinationType">The destination type.</param>
    /// <returns>The mapped object.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not an instance of <paramref name="sourceType"/>.</exception>
    /// <exception cref="MappingException">No mapping is registered for the types.</exception>
    public static object? Map(object source, Type sourceType, Type destinationType)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (sourceType is null)
        {
            throw new ArgumentNullException(nameof(sourceType));
        }

        if (destinationType is null)
        {
            throw new ArgumentNullException(nameof(destinationType));
        }

        if (!sourceType.IsInstanceOfType(source))
        {
            throw new ArgumentException($"The source is a '{source.GetType()}', not a '{sourceType}'.", nameof(source));
        }

        if (!TryGetMap(sourceType, destinationType, out var map))
        {
            EnsureLoadedAssembliesRegistered();
            if (!TryGetMap(sourceType, destinationType, out map))
            {
                throw NoMappingException(sourceType, destinationType);
            }
        }

        return map(source);
    }

    /// <summary>The exception for a type pair without a registered mapping.</summary>
    /// <param name="sourceType">The source type.</param>
    /// <param name="destinationType">The destination type.</param>
    /// <returns>A <see cref="MappingException"/> that explains how a mapping is registered.</returns>
    public static MappingException NoMappingException(Type sourceType, Type destinationType)
        => new($"No mapping registered from '{sourceType.Name}' to '{destinationType.Name}'. " +
               (IsSupported
                   ? "Ensure a [MapTo], [MapFrom], or MappingProfile mapping exists, the " +
                     "IronMapper Source Generator has run on the project that owns these types, and that project's " +
                     "assembly is passed to AddIronMapper (or used before the first mapping). A project built with " +
                     "<IronMapperRegistration>false</IronMapperRegistration> has no registration: remove the property " +
                     "there, or call the generated MapTo methods directly."
                   : NotSupportedMessage));

    private const string NotSupportedMessage =
        "The registration of the generated mappings is turned off in this application (feature switch '" +
        FeatureSwitchName + "' is false), so mapping by type finds no mappings. The IronMapper package turns it off " +
        "for a trimmed or NativeAOT publish when no project of the application references IronMapper.Extensions.DI. " +
        "Set <IronMapperRegistration>true</IronMapperRegistration> in the application project to keep it.";

    private static Func<object, object?>? Resolve(Type sourceType, Type destinationType)
    {
        if (s_entries.TryGetValue(destinationType, out var bySource))
        {
            for (var type = sourceType; type is not null; type = type.BaseType)
            {
                if (bySource.TryGetValue(type, out var entry))
                {
                    return entry.Map;
                }
            }
        }

        if (TryGetCollectionElement(destinationType, out var element, out bool isArray)
            && s_byDestination.TryGetValue(element, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (candidate.IsSourceCollection(sourceType))
                {
                    return isArray ? candidate.MapArray : candidate.MapList;
                }
            }
        }

        return null;
    }

    // T[] → (T, array); List<T> and the collection interfaces List<T> implements → (T, list).
    private static bool TryGetCollectionElement(Type type, out Type element, out bool isArray)
    {
        isArray = type.IsArray;
        if (isArray)
        {
            element = type.GetElementType()!;
            return type.GetArrayRank() == 1;
        }

        element = null!;
        if (!type.IsGenericType || type.IsGenericTypeDefinition)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        if (definition != typeof(List<>)
            && definition != typeof(IEnumerable<>)
            && definition != typeof(IList<>)
            && definition != typeof(ICollection<>)
            && definition != typeof(IReadOnlyList<>)
            && definition != typeof(IReadOnlyCollection<>))
        {
            return false;
        }

        element = type.GetGenericArguments()[0];
        return true;
    }

    private static void RunModuleInitializer(Assembly assembly)
    {
        if (assembly.IsDynamic)
        {
            return;
        }

        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
    }

    private static bool IsFrameworkAssembly(Assembly assembly)
    {
        string? name = assembly.GetName().Name;
        return name is null
               || name.StartsWith("System.", StringComparison.Ordinal)
               || name.StartsWith("Microsoft.", StringComparison.Ordinal)
               || name == "System" || name == "mscorlib" || name == "netstandard";
    }

    // The registrations added by module initializers and not run yet: a lock-free stack, so adding one is a single
    // compare-exchange and needs nothing else of the registry. No static field initializers, so no static constructor.
    private static class PendingAssemblies
    {
        private static Node? s_head;
        private static int s_count;

        // Added and not finished yet (see EnsureInitialized).
        public static int Count => Volatile.Read(ref s_count);

        public static void Push(Action register)
        {
            Interlocked.Increment(ref s_count);
            var node = new Node(register);
            do
            {
                node.Next = Volatile.Read(ref s_head);
            }
            while (Interlocked.CompareExchange(ref s_head, node, node.Next) != node.Next);
        }

        // Every pending registration in the order they were added, or null.
        public static Action[]? TakeAll()
        {
            var node = Interlocked.Exchange(ref s_head, null);
            if (node is null)
            {
                return null;
            }

            int length = 0;
            for (var n = node; n is not null; n = n.Next)
            {
                length++;
            }

            var result = new Action[length];
            for (int i = length - 1; node is not null; node = node.Next, i--)
            {
                result[i] = node.Register;
            }

            return result;
        }

        public static void Done(int count) => Interlocked.Add(ref s_count, -count);

        // Fields, not properties: the module initializer of every assembly runs this code, so it is kept to a few methods.
        private sealed class Node
        {
            public readonly Action Register;
            public Node? Next;

            public Node(Action register) => Register = register;
        }
    }

    private static MapperEntry[] Append(MapperEntry[] entries, MapperEntry entry)
    {
        var result = new MapperEntry[entries.Length + 1];
        Array.Copy(entries, result, entries.Length);
        result[entries.Length] = entry;
        return result;
    }
}
