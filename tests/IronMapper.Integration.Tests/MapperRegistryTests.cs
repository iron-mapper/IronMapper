using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Exceptions;
using IronMapper.Extensions.DI;
using IronMapper.Integration.Application;
using IronMapper.Integration.Domain;
using IronMapper.Integration.Tests.Registry;
using IronMapper.Interfaces;
using IronMapper.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// -----------------------------------------------------------------------
// Fixtures: two types with the same simple names in different namespaces.
// -----------------------------------------------------------------------

namespace IronMapper.Integration.Tests.Registry.Alpha
{
    [MapTo(typeof(CustomerDto))]
    [MapTo(typeof(Beta.CustomerDto))]
    public class Customer
    {
        public string Name { get; set; } = string.Empty;
        public int Code { get; set; }
    }

    public class CustomerDto
    {
        public string Name { get; set; } = string.Empty;
    }
}

namespace IronMapper.Integration.Tests.Registry.Beta
{
    [MapTo(typeof(CustomerDto))]
    public class Customer
    {
        public int Code { get; set; }
    }

    public class CustomerDto
    {
        public int Code { get; set; }
    }
}

namespace IronMapper.Integration.Tests.Registry
{
    // Exceptions from inside the generated code: a null collection element, a converter, a hook.
    [MapTo(typeof(BasketDto))]
    public class Basket
    {
        public List<BasketItem> Items { get; set; } = new();
    }

    [MapTo(typeof(BasketItemDto))]
    public class BasketItem
    {
        public string Sku { get; set; } = string.Empty;
    }

    public class BasketDto
    {
        public List<BasketItemDto> Items { get; set; } = new();
    }

    public class BasketItemDto
    {
        public string Sku { get; set; } = string.Empty;
    }

    public class FailingConverter : ITypeConverter<string, int>
    {
        public int Convert(string source) => throw new FormatException("converter failed: " + source);
    }

    [MapTo(typeof(MeterDto))]
    public class Meter
    {
        [MapConverter(typeof(FailingConverter))]
        public string Reading { get; set; } = string.Empty;
    }

    public class MeterDto
    {
        public int Reading { get; set; }
    }

    public class HookSource
    {
        public int Id { get; set; }
    }

    public class HookDto
    {
        public int Id { get; set; }
    }

    public static class HookFailure
    {
        public static void Fail(int id) => throw new InvalidOperationException("hook failed: " + id);
    }

    public class HookProfile : MappingProfile
    {
        public HookProfile()
        {
            CreateMap<HookSource, HookDto>().AfterMap((s, d) => HookFailure.Fail(s.Id));
        }
    }

    // Collections (#8) and runtime types.
    [MapTo(typeof(ParcelDto))]
    public class Parcel
    {
        public int Id { get; set; }
    }

    public class ParcelDto
    {
        public int Id { get; set; }
    }

    // No mapping of its own: Map<ParcelDto>(object) falls back to the mapping of the base type.
    public class ExpressParcel : Parcel
    {
        public bool Priority { get; set; }
    }

    // Registered by hand in a test after a lookup has used the mapping of the base type. No other test uses it.
    public class OversizedParcel : Parcel
    {
    }
}

namespace IronMapper.Integration.Tests
{
    /// <summary>
    /// <see cref="IMapper"/> on the generated registry (plan item 1.3): original exceptions, collections as the
    /// destination type (#8), types with the same simple names, runtime and base types.
    /// </summary>
    public class MapperRegistryTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(MapperRegistryTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        // ------------------------------------------------------------------
        // Original exceptions (no TargetInvocationException)
        // ------------------------------------------------------------------

        [Fact]
        public void Map_NullCollectionElement_ThrowsArgumentNullExceptionFromGeneratedCode()
        {
            var basket = new Basket { Items = { new BasketItem { Sku = "A" }, null! } };

            var ex = Assert.Throws<ArgumentNullException>(() => CreateMapper().Map<Basket, BasketDto>(basket));

            Assert.Equal("source", ex.ParamName);
            Assert.Contains("GeneratedMappers", ex.StackTrace);
        }

        [Fact]
        public void MapByRuntimeType_NullCollectionElement_ThrowsArgumentNullException()
        {
            var basket = new Basket { Items = { null! } };

            var ex = Assert.Throws<ArgumentNullException>(() => CreateMapper().Map<BasketDto>(basket));

            Assert.Equal("source", ex.ParamName);
        }

        [Fact]
        public void Map_ConverterThrows_ThrowsTheConverterException()
        {
            var ex = Assert.Throws<FormatException>(() => CreateMapper().Map<Meter, MeterDto>(new Meter { Reading = "x" }));

            Assert.Equal("converter failed: x", ex.Message);
            Assert.Contains("FailingConverter", ex.StackTrace);
        }

        [Fact]
        public void Map_HookThrows_ThrowsTheHookException()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => CreateMapper().Map<HookDto>(new HookSource { Id = 5 }));

            Assert.Equal("hook failed: 5", ex.Message);
        }

        [Fact]
        public void MapInPlace_ConverterThrows_ThrowsTheConverterException()
        {
            Assert.Throws<FormatException>(() => CreateMapper().Map(new Meter { Reading = "y" }, new MeterDto()));
        }

        [Fact]
        public void MapCollection_NullElement_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CreateMapper().MapCollection<Parcel, ParcelDto>(new[] { new Parcel(), null! }));
        }

        // ------------------------------------------------------------------
        // Collections as the destination type (#8)
        // ------------------------------------------------------------------

        private static List<Parcel> Parcels() => new() { new Parcel { Id = 1 }, new Parcel { Id = 2 }, new Parcel { Id = 3 } };

        [Fact]
        public void MapByRuntimeType_ListToList()
        {
            var result = CreateMapper().Map<List<ParcelDto>>(Parcels());

            Assert.Equal(new[] { 1, 2, 3 }, result.Select(d => d.Id));
        }

        [Fact]
        public void MapByRuntimeType_ArrayToArray()
        {
            var result = CreateMapper().Map<ParcelDto[]>(Parcels().ToArray());

            Assert.Equal(new[] { 1, 2, 3 }, result.Select(d => d.Id));
        }

        [Fact]
        public void MapByRuntimeType_HashSetToEnumerable_IsMaterialized()
        {
            var result = CreateMapper().Map<IEnumerable<ParcelDto>>(new HashSet<Parcel>(Parcels()));

            Assert.IsType<List<ParcelDto>>(result);
            Assert.Equal(new[] { 1, 2, 3 }, result.Select(d => d.Id).OrderBy(id => id));
        }

        [Fact]
        public void MapByRuntimeType_ListToArray()
        {
            var result = CreateMapper().Map<ParcelDto[]>(Parcels());

            Assert.Equal(new[] { 1, 2, 3 }, result.Select(d => d.Id));
        }

        [Fact]
        public void Map_ListToList()
        {
            var result = CreateMapper().Map<List<Parcel>, List<ParcelDto>>(Parcels());

            Assert.Equal(new[] { 1, 2, 3 }, result.Select(d => d.Id));
        }

        [Fact]
        public void Map_EnumerableToArray()
        {
            IEnumerable<Parcel> source = Parcels().Where(p => p.Id > 1);

            var result = CreateMapper().Map<IEnumerable<Parcel>, ParcelDto[]>(source);

            Assert.Equal(new[] { 2, 3 }, result.Select(d => d.Id));
        }

        [Theory]
        [InlineData(typeof(IReadOnlyList<ParcelDto>))]
        [InlineData(typeof(IReadOnlyCollection<ParcelDto>))]
        [InlineData(typeof(IList<ParcelDto>))]
        [InlineData(typeof(ICollection<ParcelDto>))]
        public void MapByType_ListToCollectionInterface_ReturnsList(Type destinationType)
        {
            var mapper = (RuntimeMapper)CreateMapper();

            object? result = mapper.Map(Parcels(), typeof(List<Parcel>), destinationType);

            var list = Assert.IsType<List<ParcelDto>>(result);
            Assert.Equal(new[] { 1, 2, 3 }, list.Select(d => d.Id));
        }

        [Fact]
        public void MapByRuntimeType_EmptyList_ReturnsEmptyList()
        {
            Assert.Empty(CreateMapper().Map<List<ParcelDto>>(new List<Parcel>()));
        }

        [Fact]
        public void MapByRuntimeType_CollectionOfUnmappedElements_ThrowsMappingException()
        {
            Assert.Throws<MappingException>(() => CreateMapper().Map<List<ParcelDto>>(new List<int> { 1 }));
        }

        // ------------------------------------------------------------------
        // Same simple names in different namespaces
        // ------------------------------------------------------------------

        [Fact]
        public void Map_SameSimpleNames_UsesTheExactTypes()
        {
            var mapper = CreateMapper();
            var alpha = new Registry.Alpha.Customer { Name = "Ann", Code = 7 };

            Assert.Equal("Ann", mapper.Map<Registry.Alpha.Customer, Registry.Alpha.CustomerDto>(alpha).Name);
            Assert.Equal(7, mapper.Map<Registry.Alpha.Customer, Registry.Beta.CustomerDto>(alpha).Code);
            Assert.Equal(9, mapper.Map<Registry.Beta.CustomerDto>(new Registry.Beta.Customer { Code = 9 }).Code);
            Assert.Equal("Ann", mapper.Map<Registry.Alpha.CustomerDto>(alpha).Name);
        }

        // ------------------------------------------------------------------
        // Runtime and base types, Map(object, Type, Type)
        // ------------------------------------------------------------------

        [Fact]
        public void MapByRuntimeType_DerivedSourceWithoutOwnMapping_UsesTheBaseTypeMapping()
        {
            var dto = CreateMapper().Map<ParcelDto>(new ExpressParcel { Id = 4 });

            Assert.Equal(4, dto.Id);
        }

        [Fact]
        public void MapByRuntimeType_ExactPairRegisteredAfterLookup_ReplacesTheBaseTypeMapping()
        {
            var mapper = CreateMapper();
            Assert.Equal(5, mapper.Map<ParcelDto>(new OversizedParcel { Id = 5 }).Id);

            MapperRegistry.Register<OversizedParcel, ParcelDto>(
                source => new ParcelDto { Id = -source.Id },
                (source, destination) => destination.Id = -source.Id,
                source => new List<ParcelDto>(),
                source => Array.Empty<ParcelDto>());

            Assert.Equal(-5, mapper.Map<ParcelDto>(new OversizedParcel { Id = 5 }).Id);
        }

        [Fact]
        public void Map_SourceDeclaredAsObject_UsesTheRuntimeType()
        {
            object parcel = new Parcel { Id = 6 };

            Assert.Equal(6, CreateMapper().Map<object, ParcelDto>(parcel).Id);
        }

        [Fact]
        public void MapByType_ReturnsMappedObject()
        {
            var mapper = (RuntimeMapper)CreateMapper();

            object? result = mapper.Map(new Parcel { Id = 8 }, typeof(Parcel), typeof(ParcelDto));

            Assert.Equal(8, Assert.IsType<ParcelDto>(result).Id);
        }

        [Fact]
        public void MapByType_UnregisteredPair_ThrowsMappingException()
        {
            var mapper = (RuntimeMapper)CreateMapper();

            Assert.Throws<MappingException>(() => mapper.Map(new Parcel(), typeof(Parcel), typeof(BasketDto)));
        }

        [Fact]
        public void MapperCache_HoldsTheGeneratedDelegates()
        {
            CreateMapper();

            Assert.NotNull(MapperCache<Parcel, ParcelDto>.Map);
            Assert.NotNull(MapperCache<Parcel, ParcelDto>.MapInto);
            Assert.Equal(3, MapperCache<Parcel, ParcelDto>.Map!(new Parcel { Id = 3 }).Id);
        }
    }

    /// <summary>
    /// Mappings declared in an assembly that nothing has touched yet: profiles in IronMapper.Integration.Application,
    /// entities in IronMapper.Integration.Domain, the call from this project. No other test uses these types. Runs
    /// outside the parallel tests: their lookups of a missing mapping run the module initializers of all loaded
    /// assemblies, which would register the Application mappings before AddIronMapper does.
    /// </summary>
    [Collection(nameof(MapperRegistryAssemblyTests))]
    public class MapperRegistryAssemblyTests
    {
        [Fact]
        public void AddIronMapper_AssemblyNotUsedYet_RegistersItsMappingsWhenTheMapperIsCreated()
        {
            // Loading the assembly does not run its module initializer. This method must not mention a type of the
            // Application assembly: compiling it would load the type, and CoreCLR runs the module initializer then.
            var application = Assembly.Load("IronMapper.Integration.Application");
            Assert.False(IsRegistered());

            // AddIronMapper runs the module initializer; the registration itself waits for the first IMapper.
            var services = new ServiceCollection();
            services.AddIronMapper(application);
            using var provider = services.BuildServiceProvider();
            Assert.False(IsRegistered());

            var mapper = provider.GetRequiredService<IMapper>();
            Assert.True(IsRegistered());
            MapAndCheck(mapper);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool IsRegistered() => MapperCache<Invoice, InvoiceDto>.Map is not null;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void MapAndCheck(IMapper mapper)
        {
            var invoice = new Invoice { Id = 1, Customer = "Bob", Lines = { new InvoiceLine { Sku = "S", Quantity = 2 } } };
            var dto = mapper.Map<Invoice, InvoiceDto>(invoice);
            Assert.Equal("Bob", dto.Customer);
            Assert.Equal(2, Assert.Single(dto.Lines).Quantity);
            Assert.Equal(1, mapper.Map<InvoiceDto>(invoice).Id);
            Assert.Single(mapper.Map<List<InvoiceLineDto>>(invoice.Lines));
        }
    }

    /// <summary>
    /// The feature switch IronMapper.Registration.IsSupported turned off (as for a trimmed or NativeAOT publish without
    /// the DI package, or by IronMapperRegistration=false): IMapper fails with an explanation instead of "no mapping".
    /// Changes a process-wide switch, so it runs outside the parallel tests. Nothing here may scan the loaded
    /// assemblies: their module initializers would run while the switch is off and never register.
    /// </summary>
    [Collection(nameof(MapperRegistryAssemblyTests))]
    public class MapperRegistryFeatureSwitchTests
    {
        [Fact]
        public void RegistrationTurnedOff_CreatingTheMapperThrowsWithTheMsBuildProperty()
        {
            var exception = WithRegistrationTurnedOff(() =>
                Assert.Throws<InvalidOperationException>(() => new RuntimeMapper(new ServiceCollection().BuildServiceProvider())));

            Assert.Contains(MapperRegistry.FeatureSwitchName, exception.Message);
            Assert.Contains("<IronMapperRegistration>true</IronMapperRegistration>", exception.Message);
        }

        [Fact]
        public void RegistrationTurnedOff_NoMappingExceptionExplainsWhy()
        {
            var exception = WithRegistrationTurnedOff(() => MapperRegistry.NoMappingException(typeof(Parcel), typeof(ParcelDto)));

            Assert.Contains("<IronMapperRegistration>true</IronMapperRegistration>", exception.Message);
            Assert.DoesNotContain("<IronMapperRegistration>", MapperRegistry.NoMappingException(typeof(Parcel), typeof(ParcelDto)).Message);
        }

        [Fact]
        public void SwitchNotSet_RegistrationIsSupported()
        {
            Assert.True(MapperRegistry.IsSupported);
        }

        private static T WithRegistrationTurnedOff<T>(Func<T> action)
        {
            AppContext.SetSwitch(MapperRegistry.FeatureSwitchName, false);
            try
            {
                Assert.False(MapperRegistry.IsSupported);
                return action();
            }
            finally
            {
                AppContext.SetSwitch(MapperRegistry.FeatureSwitchName, true);
            }
        }
    }

    /// <summary>
    /// The registration added by a module initializer (<see cref="MapperRegistry.AddAssembly"/>) runs when an
    /// <see cref="IMapper"/> is created or maps, once, also when several mappers are created at the same time. Each
    /// test adds a registration of its own types, as the module initializer of an assembly would. Runs outside the
    /// parallel tests: any mapper created elsewhere would run these registrations too.
    /// </summary>
    [Collection(nameof(MapperRegistryAssemblyTests))]
    public class MapperRegistryLazyRegistrationTests
    {
        public class Lazy1 { public int Id { get; set; } }
        public class Lazy1Dto { public int Id { get; set; } }
        public class Lazy2 { public int Id { get; set; } }
        public class Lazy2Dto { public int Id { get; set; } }
        public class Lazy3 { public int Id { get; set; } }
        public class Lazy3Dto { public int Id { get; set; } }
        public class Lazy4 { public int Id { get; set; } }
        public class Lazy4Dto { public int Id { get; set; } }

        [Fact]
        public void AddedRegistration_RunsOnlyWhenAMapperIsCreated()
        {
            int runs = 0;
            MapperRegistry.AddAssembly(() =>
            {
                runs++;
                Register<Lazy1, Lazy1Dto>(s => new Lazy1Dto { Id = s.Id });
            });

            Assert.Equal(0, runs);
            Assert.Null(MapperCache<Lazy1, Lazy1Dto>.Map);

            var mapper = CreateMapper();

            Assert.Equal(1, runs);
            Assert.Equal(5, mapper.Map<Lazy1, Lazy1Dto>(new Lazy1 { Id = 5 }).Id);
        }

        [Fact]
        public void MappersCreatedInParallel_RunTheRegistrationOnceAndEachSeesIt()
        {
            int runs = 0;
            MapperRegistry.AddAssembly(() =>
            {
                Interlocked.Increment(ref runs);
                Thread.Sleep(50); // Other threads create their mappers while this registration is running.
                Register<Lazy2, Lazy2Dto>(s => new Lazy2Dto { Id = s.Id });
            });

            const int threads = 8;
            using var start = new Barrier(threads);
            bool[] seen = new bool[threads];
            var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
            {
                start.SignalAndWait();
                CreateMapper();
                seen[i] = MapperCache<Lazy2, Lazy2Dto>.Map is not null;
            })).ToList();
            workers.ForEach(w => w.Start());
            workers.ForEach(w => w.Join());

            Assert.Equal(1, runs);
            Assert.All(seen, Assert.True);
        }

        [Fact]
        public void RegistrationAddedAfterTheMapperWasCreated_IsFound()
        {
            var mapper = CreateMapper();

            // As the module initializer of an assembly that is first used after the mapper exists.
            MapperRegistry.AddAssembly(() =>
            {
                Register<Lazy3, Lazy3Dto>(s => new Lazy3Dto { Id = s.Id });
                Register<Lazy4, Lazy4Dto>(s => new Lazy4Dto { Id = s.Id });
            });

            Assert.Equal(3, mapper.Map<Lazy3, Lazy3Dto>(new Lazy3 { Id = 3 }).Id);
            Assert.Equal(4, mapper.Map<Lazy4Dto>(new Lazy4 { Id = 4 }).Id);
        }

        private static IMapper CreateMapper() => new RuntimeMapper(new ServiceCollection().BuildServiceProvider());

        private static void Register<TSource, TDest>(Func<TSource, TDest> map)
            => MapperRegistry.Register<TSource, TDest>(
                map,
                (source, destination) => { },
                source => source!.Select(map).ToList(),
                source => source!.Select(map).ToArray());
    }

    [CollectionDefinition(nameof(MapperRegistryAssemblyTests), DisableParallelization = true)]
    public class MapperRegistryAssemblyTestsCollection
    {
    }
}
