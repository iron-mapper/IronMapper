using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Extensions.DI;
using IronMapper.Generated;
using IronMapper.Integration.Tests.Overloads;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Three pairs with the same shape: all overloads generated, all optional overloads turned off by the attribute, and by
// GenerateMethods in a profile. Nested collection properties use the element mapping of a pair without List/Array.
namespace IronMapper.Integration.Tests.Overloads
{
    [MapTo(typeof(FullDto))]
    public class Full
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Part> Parts { get; set; } = new();
    }

    public class FullDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        [Ignore] public string Untouched { get; set; } = "kept";
        public List<PartDto> Parts { get; set; } = new();
    }

    [MapTo(typeof(BareDto), GenerateInPlace = false, GenerateList = false, GenerateArray = false)]
    public class Bare
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Part> Parts { get; set; } = new();
    }

    public class BareDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        [Ignore] public string Untouched { get; set; } = "kept";
        public List<PartDto> Parts { get; set; } = new();
    }

    public class Lean
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Part> Parts { get; set; } = new();
    }

    public class LeanDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        [Ignore] public string Untouched { get; set; } = "kept";
        public PartDto[] Parts { get; set; } = Array.Empty<PartDto>();
    }

    // The element mapping of the nested collections, without MapToPartDtoList/MapToPartDtoArray.
    [MapTo(typeof(PartDto), GenerateList = false, GenerateArray = false)]
    public class Part
    {
        public string Code { get; set; } = string.Empty;
    }

    public class PartDto
    {
        public string Code { get; set; } = string.Empty;
    }

    public class LeanProfile : MappingProfile
    {
        public LeanProfile()
        {
            CreateMap<Lean, LeanDto>().GenerateMethods(inPlace: false, list: false, array: false);
        }
    }
}

namespace IronMapper.Integration.Tests
{
    /// <summary>
    /// Overloads turned off for a mapping (<c>GenerateInPlace/List/Array = false</c>, <c>GenerateMethods(...)</c>): the
    /// public methods are gone, and <c>IMapper</c> still maps a single object, <c>List&lt;T&gt;</c>, <c>T[]</c>,
    /// <c>MapCollection</c> and in-place, with the same results as for a pair with every overload.
    /// </summary>
    public class TurnedOffOverloadsTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(TurnedOffOverloadsTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        private static readonly Full[] FullSources =
        {
            new() { Id = 1, Name = "a", Parts = new() { new() { Code = "x" }, new() { Code = "y" } } },
            new() { Id = 2, Name = "b" },
        };

        private static Bare[] BareSources => FullSources
            .Select(f => new Bare { Id = f.Id, Name = f.Name, Parts = f.Parts }).ToArray();

        private static Lean[] LeanSources => FullSources
            .Select(f => new Lean { Id = f.Id, Name = f.Name, Parts = f.Parts }).ToArray();

        private static string Describe(int id, string name, string untouched, IEnumerable<PartDto> parts)
            => $"{id}|{name}|{untouched}|{string.Join(",", parts.Select(p => p.Code))}";

        private static string Describe(FullDto d) => Describe(d.Id, d.Name, d.Untouched, d.Parts);
        private static string Describe(BareDto d) => Describe(d.Id, d.Name, d.Untouched, d.Parts);
        private static string Describe(LeanDto d) => Describe(d.Id, d.Name, d.Untouched, d.Parts);

        [Fact]
        public void PublicMethods_OnlyTheEnabledOnesExist()
        {
            // The generated class of this assembly (the referenced Application assembly has its own).
            var generated = typeof(Full).Assembly.GetType("IronMapper.Generated.GeneratedMappers_IronMapper_Integration_Tests",
                throwOnError: true)!;
            var methods = generated.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(m => m.Name + "/" + m.GetParameters().Length).ToHashSet();

            Assert.Contains("MapToFullDto/1", methods);
            Assert.Contains("MapToFullDto/2", methods);
            Assert.Contains("MapToFullDtoList/1", methods);
            Assert.Contains("MapToFullDtoArray/1", methods);

            foreach (var name in new[] { "BareDto", "LeanDto" })
            {
                Assert.Contains($"MapTo{name}/1", methods);
                Assert.DoesNotContain($"MapTo{name}/2", methods);
                Assert.DoesNotContain($"MapTo{name}List/1", methods);
                Assert.DoesNotContain($"MapTo{name}Array/1", methods);
            }

            Assert.Contains("MapToPartDto/2", methods);
            Assert.DoesNotContain("MapToPartDtoList/1", methods);
            Assert.DoesNotContain("MapToPartDtoArray/1", methods);
        }

        [Fact]
        public void IMapper_SingleObject_SameResult()
        {
            var mapper = CreateMapper();
            var expected = FullSources.Select(s => Describe(mapper.Map<Full, FullDto>(s))).ToArray();

            Assert.Equal(expected, BareSources.Select(s => Describe(mapper.Map<Bare, BareDto>(s))));
            Assert.Equal(expected, LeanSources.Select(s => Describe(mapper.Map<Lean, LeanDto>(s))));
            Assert.Equal(expected, BareSources.Select(s => Describe(mapper.Map<BareDto>(s))));
            Assert.Equal(expected, LeanSources.Select(s => Describe(mapper.Map<LeanDto>((object)s))));
            // The generated method itself is the same.
            Assert.Equal(expected, BareSources.Select(s => Describe(s.MapToBareDto())));
        }

        [Fact]
        public void IMapper_List_SameResult()
        {
            var mapper = CreateMapper();
            var expected = mapper.Map<List<FullDto>>(FullSources).Select(Describe).ToArray();

            // Array, List and a lazy sequence as the source.
            Assert.Equal(expected, mapper.Map<List<BareDto>>(BareSources).Select(Describe));
            Assert.Equal(expected, mapper.Map<List<BareDto>>(BareSources.ToList()).Select(Describe));
            Assert.Equal(expected, mapper.Map<List<LeanDto>>(LeanSources.Where(_ => true)).Select(Describe));
            Assert.Equal(expected, mapper.Map<IReadOnlyList<LeanDto>>(LeanSources).Select(Describe));
            Assert.IsType<List<BareDto>>(mapper.Map<IEnumerable<BareDto>>(BareSources));
        }

        [Fact]
        public void IMapper_Array_SameResult()
        {
            var mapper = CreateMapper();
            var expected = mapper.Map<FullDto[]>(FullSources).Select(Describe).ToArray();

            Assert.Equal(expected, mapper.Map<BareDto[]>(BareSources).Select(Describe));
            Assert.Equal(expected, mapper.Map<BareDto[]>(new HashSet<Bare>(BareSources)).Select(Describe).OrderBy(s => s));
            Assert.Equal(expected, mapper.Map<LeanDto[]>(LeanSources.Where(_ => true)).Select(Describe));
            Assert.Empty(mapper.Map<BareDto[]>(Array.Empty<Bare>()));
        }

        [Fact]
        public void IMapper_MapCollection_SameResult()
        {
            var mapper = CreateMapper();
            var expected = mapper.MapCollection<Full, FullDto>(FullSources).Select(Describe).ToArray();

            Assert.Equal(expected, mapper.MapCollection<Bare, BareDto>(BareSources).Select(Describe));
            Assert.Equal(expected, mapper.MapCollection<Lean, LeanDto>(LeanSources).Select(Describe));
        }

        [Fact]
        public void IMapper_InPlace_SameResult()
        {
            var mapper = CreateMapper();
            var expected = FullSources.Select(s =>
            {
                var d = new FullDto { Untouched = "existing" };
                mapper.Map(s, d);
                return Describe(d);
            }).ToArray();

            Assert.Equal(expected, BareSources.Select(s =>
            {
                var d = new BareDto { Untouched = "existing" };
                mapper.Map(s, d);
                return Describe(d);
            }));
            Assert.Equal(expected, LeanSources.Select(s =>
            {
                var d = new LeanDto { Untouched = "existing" };
                mapper.Map(s, d);
                return Describe(d);
            }));
        }

        [Fact]
        public void IMapper_NullArguments_SameExceptions()
        {
            var mapper = CreateMapper();

            var full = Assert.Throws<ArgumentNullException>(() => mapper.Map(new Full(), (FullDto)null!));
            var bare = Assert.Throws<ArgumentNullException>(() => mapper.Map(new Bare(), (BareDto)null!));
            Assert.Equal(full.ParamName, bare.ParamName);
            Assert.Empty(mapper.Map<List<BareDto>>(Array.Empty<Bare>()));
        }
    }
}
