using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Interfaces;
using Xunit;

// Not file-scoped: the source generator must see these types.
namespace IronMapper.Tests.ConverterCacheFixtures
{
    /// <summary>Records every converter instance that converted a value (static state does not make a converter stateful).</summary>
    public static class InstanceLog
    {
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<object, byte>> Logs = new();

        public static void Add(string key, object converter)
            => Logs.GetOrAdd(key, _ => new ConcurrentDictionary<object, byte>(ReferenceEqualityComparer.Instance))
                .TryAdd(converter, 0);

        public static int Count(string key) => Logs.TryGetValue(key, out var log) ? log.Count : 0;
    }

    // ---- Stateless: cached --------------------------------------------------------------------------------------

    public sealed class StatelessAttributeConverter : ITypeConverter<int, string>
    {
        public string Convert(int source) { InstanceLog.Add(nameof(StatelessAttributeConverter), this); return $"#{source}"; }
    }

    public sealed class StatelessMemberConverter : ITypeConverter<int, string>
    {
        public string Convert(int source) { InstanceLog.Add(nameof(StatelessMemberConverter), this); return $"#{source}"; }
    }

    public sealed class StatelessObjectConverter : ITypeConverter<CacheObjectSource, CacheObjectDest>
    {
        public CacheObjectDest Convert(CacheObjectSource source)
        {
            InstanceLog.Add(nameof(StatelessObjectConverter), this);
            return new CacheObjectDest { Text = $"#{source.Value}" };
        }
    }

    /// <summary>No fields of its own, and its base class has none either (only a static field and a method).</summary>
    public abstract class StatelessConverterBase
    {
        public static int Unused;
        protected static string Format(int value) => $"#{value}";
    }

    public sealed class StatelessDerivedConverter : StatelessConverterBase, ITypeConverter<int, string>
    {
        public string Convert(int source) { InstanceLog.Add(nameof(StatelessDerivedConverter), this); return Format(source); }
    }

    /// <summary>Used only by the thread-safety test, so its cache is created there.</summary>
    public sealed class StatelessParallelConverter : ITypeConverter<int, string>
    {
        public string Convert(int source) { InstanceLog.Add(nameof(StatelessParallelConverter), this); return $"#{source}"; }
    }

    // ---- Stateful: a new instance per call ----------------------------------------------------------------------

    public sealed class FieldConverter : ITypeConverter<int, string>
    {
        private int _calls;

        public string Convert(int source)
        {
            InstanceLog.Add(nameof(FieldConverter), this);
            _calls++;
            return $"#{source}:{_calls}";
        }
    }

    public sealed class AutoPropertyConverter : ITypeConverter<int, string>
    {
        public int Calls { get; private set; }

        public string Convert(int source)
        {
            InstanceLog.Add(nameof(AutoPropertyConverter), this);
            Calls++;
            return $"#{source}:{Calls}";
        }
    }

    public abstract class ConverterBaseWithField
    {
        protected int Calls;
    }

    public sealed class DerivedFromBaseWithFieldConverter : ConverterBaseWithField, ITypeConverter<int, string>
    {
        public string Convert(int source)
        {
            InstanceLog.Add(nameof(DerivedFromBaseWithFieldConverter), this);
            Calls++;
            return $"#{source}:{Calls}";
        }
    }

    public sealed class StatefulMemberConverter : ITypeConverter<int, string>
    {
        private int _calls;

        public string Convert(int source)
        {
            InstanceLog.Add(nameof(StatefulMemberConverter), this);
            return $"#{source}:{++_calls}";
        }
    }

    public sealed class StatefulObjectConverter : ITypeConverter<StatefulObjectSource, CacheObjectDest>
    {
        private int _calls;

        public CacheObjectDest Convert(StatefulObjectSource source)
        {
            InstanceLog.Add(nameof(StatefulObjectConverter), this);
            return new CacheObjectDest { Text = $"#{source.Value}:{++_calls}" };
        }
    }

    /// <summary>No fields, but the constructor has a side effect: it must still run once per conversion.</summary>
    public sealed class ConstructorSideEffectConverter : ITypeConverter<int, string>
    {
        public static int Constructed;

        public ConstructorSideEffectConverter() => Interlocked.Increment(ref Constructed);

        public string Convert(int source) => $"#{source}";
    }

    // ---- Mapped types -------------------------------------------------------------------------------------------

    [MapTo(typeof(CacheAttributeDest))]
    public class CacheAttributeSource
    {
        [MapConverter(typeof(StatelessAttributeConverter))] public int Stateless { get; set; }
        [MapConverter(typeof(StatelessDerivedConverter))] public int Derived { get; set; }
        [MapConverter(typeof(FieldConverter))] public int Field { get; set; }
        [MapConverter(typeof(AutoPropertyConverter))] public int AutoProperty { get; set; }
        [MapConverter(typeof(DerivedFromBaseWithFieldConverter))] public int BaseField { get; set; }
        [MapConverter(typeof(ConstructorSideEffectConverter))] public int SideEffect { get; set; }
    }

    public class CacheAttributeDest
    {
        public string Stateless { get; set; } = "";
        public string Derived { get; set; } = "";
        public string Field { get; set; } = "";
        public string AutoProperty { get; set; } = "";
        public string BaseField { get; set; } = "";
        public string SideEffect { get; set; } = "";
    }

    [MapTo(typeof(CacheParallelDest))]
    public class CacheParallelSource
    {
        [MapConverter(typeof(StatelessParallelConverter))] public int Value { get; set; }
    }

    public class CacheParallelDest
    {
        public string Value { get; set; } = "";
    }

    public class CacheMemberSource
    {
        public int Stateless { get; set; }
        public int Stateful { get; set; }
    }

    public class CacheMemberDest
    {
        public string Stateless { get; set; } = "";
        public string Stateful { get; set; } = "";
    }

    public class CacheObjectSource { public int Value { get; set; } }
    public class StatefulObjectSource { public int Value { get; set; } }
    public class CacheObjectDest { public string Text { get; set; } = ""; }

    public class ConverterCacheProfile : MappingProfile
    {
        public ConverterCacheProfile()
        {
            CreateMap<CacheMemberSource, CacheMemberDest>()
                .ForMember(d => d.Stateless, o => o.UseConverter<StatelessMemberConverter>())
                .ForMember(d => d.Stateful, o => o.UseConverter<StatefulMemberConverter>());
            CreateMap<CacheObjectSource, CacheObjectDest>().ConvertUsing<StatelessObjectConverter>();
            CreateMap<StatefulObjectSource, CacheObjectDest>().ConvertUsing<StatefulObjectConverter>();
        }
    }
}

namespace IronMapper.Tests
{
    using IronMapper.Tests.ConverterCacheFixtures;

    /// <summary>
    /// The generated code keeps one instance of a stateless converter (no instance fields in its hierarchy, constructors
    /// without code) and creates any other converter with <c>new</c> on every call, as before.
    /// </summary>
    public class ConverterCacheTests
    {
        private static CacheAttributeSource AttributeSource(int value) => new()
        {
            Stateless = value, Derived = value, Field = value, AutoProperty = value, BaseField = value, SideEffect = value,
        };

        [Fact]
        public void MapConverter_Stateless_OneInstanceForAllCalls()
        {
            for (var i = 0; i < 5; i++)
            {
                var dto = AttributeSource(i).MapToCacheAttributeDest();
                Assert.Equal($"#{i}", dto.Stateless);
                Assert.Equal($"#{i}", dto.Derived);
            }

            var destination = new CacheAttributeDest();
            AttributeSource(7).MapToCacheAttributeDest(destination);
            Assert.Equal("#7", destination.Stateless);

            Assert.Equal(1, InstanceLog.Count(nameof(StatelessAttributeConverter)));
            Assert.Equal(1, InstanceLog.Count(nameof(StatelessDerivedConverter)));
        }

        [Fact]
        public void MapConverter_WithField_NewInstancePerCall()
        {
            var before = InstanceLog.Count(nameof(FieldConverter));
            for (var i = 0; i < 5; i++)
                Assert.Equal($"#{i}:1", AttributeSource(i).MapToCacheAttributeDest().Field);

            Assert.Equal(before + 5, InstanceLog.Count(nameof(FieldConverter)));
        }

        [Fact]
        public void MapConverter_WithAutoProperty_NewInstancePerCall()
        {
            var before = InstanceLog.Count(nameof(AutoPropertyConverter));
            for (var i = 0; i < 5; i++)
                Assert.Equal($"#{i}:1", AttributeSource(i).MapToCacheAttributeDest().AutoProperty);

            Assert.Equal(before + 5, InstanceLog.Count(nameof(AutoPropertyConverter)));
        }

        [Fact]
        public void MapConverter_FieldInBaseClass_NewInstancePerCall()
        {
            var before = InstanceLog.Count(nameof(DerivedFromBaseWithFieldConverter));
            for (var i = 0; i < 5; i++)
                Assert.Equal($"#{i}:1", AttributeSource(i).MapToCacheAttributeDest().BaseField);

            Assert.Equal(before + 5, InstanceLog.Count(nameof(DerivedFromBaseWithFieldConverter)));
        }

        [Fact]
        public void MapConverter_ConstructorWithCode_RunsOnEveryCall()
        {
            var before = Volatile.Read(ref ConstructorSideEffectConverter.Constructed);
            for (var i = 0; i < 5; i++)
                AttributeSource(i).MapToCacheAttributeDest();

            // Other tests of this class map the same type in parallel: at least one construction per call here.
            Assert.True(Volatile.Read(ref ConstructorSideEffectConverter.Constructed) - before >= 5);
        }

        [Fact]
        public void UseConverter_StatelessCachedAndStatefulNew()
        {
            var before = InstanceLog.Count(nameof(StatefulMemberConverter));
            for (var i = 0; i < 5; i++)
            {
                var dto = new CacheMemberSource { Stateless = i, Stateful = i }.MapToCacheMemberDest();
                Assert.Equal($"#{i}", dto.Stateless);
                Assert.Equal($"#{i}:1", dto.Stateful);
            }

            Assert.Equal(1, InstanceLog.Count(nameof(StatelessMemberConverter)));
            Assert.Equal(before + 5, InstanceLog.Count(nameof(StatefulMemberConverter)));
        }

        [Fact]
        public void ConvertUsing_StatelessCachedAndStatefulNew()
        {
            var before = InstanceLog.Count(nameof(StatefulObjectConverter));
            for (var i = 0; i < 5; i++)
            {
                Assert.Equal($"#{i}", new CacheObjectSource { Value = i }.MapToCacheObjectDest().Text);
                Assert.Equal($"#{i}:1", new StatefulObjectSource { Value = i }.MapToCacheObjectDest().Text);
            }

            var list = new[] { new CacheObjectSource { Value = 1 }, new CacheObjectSource { Value = 2 } }.MapToCacheObjectDestList();
            Assert.Equal(new[] { "#1", "#2" }, list.Select(d => d.Text));

            Assert.Equal(1, InstanceLog.Count(nameof(StatelessObjectConverter)));
            Assert.Equal(before + 5, InstanceLog.Count(nameof(StatefulObjectConverter)));
        }

        [Fact]
        public async Task StatelessConverter_FirstUseFromManyThreads_OneInstance()
        {
            // The converter is used only here, so its cache is created by these threads.
            const int threads = 16;
            using var start = new Barrier(threads);
            var results = new ConcurrentBag<string>();

            var tasks = Enumerable.Range(0, threads).Select(t => Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                for (var i = 0; i < 1000; i++)
                    results.Add(new CacheParallelSource { Value = t }.MapToCacheParallelDest().Value);
            }, TaskCreationOptions.LongRunning)).ToArray();
            await Task.WhenAll(tasks);

            Assert.Equal(threads * 1000, results.Count);
            Assert.Equal(1, InstanceLog.Count(nameof(StatelessParallelConverter)));
        }
    }
}
