using IronMapper.Generator.Analysis;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// The name of the generated class, <c>GeneratedMappers_{AssemblyName}</c> (<see cref="GeneratedClassName"/>): unique per
/// assembly, a valid C# identifier for any assembly name, stable between builds.
/// </summary>
public class GeneratedClassNameTests
{
    [Theory]
    [InlineData("App", "GeneratedMappers_App")]
    [InlineData("Shop.Application", "GeneratedMappers_Shop_Application")]
    [InlineData("Shop.Infrastructure.EfCore", "GeneratedMappers_Shop_Infrastructure_EfCore")]
    [InlineData("My-App.Web", "GeneratedMappers_My_App_Web")]
    [InlineData("1Password.Core", "GeneratedMappers_1Password_Core")]
    [InlineData("Ünïcode.Проект", "GeneratedMappers_Ünïcode_Проект")]
    [InlineData("Has Space+Plus", "GeneratedMappers_Has_Space_Plus")]
    [InlineData("Already_Underscored", "GeneratedMappers_Already_Underscored")]
    public void ReplacesEveryCharacterThatIsNotLetterDigitOrUnderscore(string assemblyName, string expected)
        => Assert.Equal(expected, GeneratedClassName.For(assemblyName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoAssemblyName_PlainPrefix(string? assemblyName)
        => Assert.Equal("GeneratedMappers", GeneratedClassName.For(assemblyName));

    [Fact]
    public void LongName_ShortenedWithHash_StableAndDifferentForDifferentNames()
    {
        string common = new string('A', 80) + ".";
        string first = GeneratedClassName.For(common + "One");
        string second = GeneratedClassName.For(common + "Two");

        Assert.Equal("GeneratedMappers_".Length + GeneratedClassName.MaxAssemblyPartLength, first.Length);
        Assert.Equal(first.Length, second.Length);
        Assert.NotEqual(first, second);
        Assert.StartsWith("GeneratedMappers_" + new string('A', GeneratedClassName.MaxAssemblyPartLength - 9) + "_", first);
        Assert.Matches("_[0-9A-F]{8}$", first);
        // FNV-1a, not string.GetHashCode: the same in every process and on every runtime.
        Assert.Equal(first, GeneratedClassName.For(common + "One"));
    }

    [Fact]
    public void NameOfExactlyMaxLength_NotShortened()
    {
        string name = new('B', GeneratedClassName.MaxAssemblyPartLength);
        Assert.Equal("GeneratedMappers_" + name, GeneratedClassName.For(name));
    }

    [Theory]
    [InlineData("Shop.Application")]
    [InlineData("1Password.Core")]
    [InlineData("Ünïcode.Проект")]
    [InlineData("Has Space+Plus")]
    public void IsValidIdentifier(string assemblyName)
        => Assert.True(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(GeneratedClassName.For(assemblyName)));

    [Fact]
    public void LongNameHash_IsPinned()
    {
        // Pins the hash function: a change would rename the generated class of every assembly with a long name.
        string name = new('C', 70);
        Assert.Equal("GeneratedMappers_" + new string('C', 55) + "_" + Fnv1a(name).ToString("X8"), GeneratedClassName.For(name));
    }

    private static uint Fnv1a(string value)
    {
        uint hash = 2166136261;
        foreach (char c in value)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash;
    }
}
