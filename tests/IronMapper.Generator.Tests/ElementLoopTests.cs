using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// <c>MapToXList</c>/<c>MapToXArray</c> call the shared generic loops of <c>GeneratedMappers.g.cs</c> with a private
/// struct per mapping. Cases where generic code could differ from the per-mapping loops it replaced: value types,
/// generic and nested internal types, a covariant array of a derived type, every source shape, <see langword="null"/>.
/// The collection shapes themselves are covered by <c>tests/IronMapper.Tests/CollectionShapeTests.cs</c>.
/// </summary>
public class ElementLoopTests
{
    [Fact]
    public void StructsGenericsNestedInternalTypesAndCovariantArrays_MapLikeBefore()
    {
        var source = """
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            using System.Linq;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App
            {
                [MapTo(typeof(PointDto))] public struct Point { public int X { get; set; } }
                public struct PointDto { public int X { get; set; } }

                [MapTo(typeof(Box<int>))] public class Holder { public int Value { get; set; } }
                public class Box<T> { public T Value { get; set; } = default!; }

                internal static class Outer
                {
                    [MapTo(typeof(SecretDto))] internal class Secret { public string Code { get; set; } = ""; }
                    internal class SecretDto { public string Code { get; set; } = ""; }
                    internal sealed class DerivedSecret : Secret { }
                }

                public static class Program
                {
                    public static string Run()
                    {
                        var points = new[] { new Point { X = 1 }, new Point { X = 2 } };
                        var a = points.MapToPointDtoList().Sum(p => p.X);
                        var b = points.ToList().MapToPointDtoArray().Sum(p => p.X);
                        var c = new Collection<Point>(points).MapToPointDtoArray().Length;
                        var d = points.Where(_ => true).MapToPointDtoArray().Length;

                        var boxes = new HashSet<Holder> { new Holder { Value = 5 } }.MapToBoxList();

                        // A Derived[] seen as IEnumerable<Secret> takes the array path (array covariance), as before.
                        IEnumerable<Outer.Secret> derived = new[] { new Outer.DerivedSecret { Code = "x" } };
                        var e = derived.MapToSecretDtoArray()[0].Code + derived.MapToSecretDtoList()[0].Code;

                        IEnumerable<Point>? none = null;
                        var f = none.MapToPointDtoList().Count + none.MapToPointDtoArray().Length;

                        return $"{a}|{b}|{c}|{d}|{boxes[0].Value}|{e}|{f}";
                    }
                }
            }
            """;

        var (_, diagnostics, errors, result) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        Assert.Empty(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("3|3|2|2|5|xx|0", result);
    }
}
