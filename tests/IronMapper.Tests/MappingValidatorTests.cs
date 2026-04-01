using System;
using IronMapper.Exceptions;
using IronMapper.Validation;
using Xunit;

namespace IronMapper.Tests;

/// <summary>
/// Tests for <see cref="MappingValidator"/> and <see cref="ValidationResult"/>.
/// </summary>
public class MappingValidatorTests
{
    // -----------------------------------------------------------------------
    // Helpers — simple in-file types
    // -----------------------------------------------------------------------

    private class Concrete
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    private class ConcreteWithExtra : Concrete
    {
        public string Extra { get; set; } = "";
    }

    private class NoCtor
    {
        public string Name { get; set; } = "";
        private NoCtor() { }
    }

    private abstract class AbstractDest { public int Id { get; set; } }

    private interface IDest { int Id { get; set; } }

    private class IncompatibleSource
    {
        public int Name { get; set; } // int ≠ string on Concrete.Name
    }

    private class CompatibleSource
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    // -----------------------------------------------------------------------
    // Null-argument guards
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_NullSourceType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MappingValidator.Validate(null!, typeof(Concrete)));
    }

    [Fact]
    public void Validate_NullDestType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MappingValidator.Validate(typeof(Concrete), null!));
    }

    // -----------------------------------------------------------------------
    // Abstract / interface destination (→ errors)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_AbstractDestination_ReturnsError()
    {
        var result = MappingValidator.Validate(typeof(Concrete), typeof(AbstractDest));

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("AbstractDest") && e.Contains("abstract"));
    }

    [Fact]
    public void Validate_InterfaceDestination_ReturnsError()
    {
        var result = MappingValidator.Validate(typeof(Concrete), typeof(IDest));

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("IDest") && e.Contains("interface"));
    }

    // -----------------------------------------------------------------------
    // No parameterless constructor (→ error)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_NoPublicParameterlessCtor_ReturnsError()
    {
        var result = MappingValidator.Validate(typeof(Concrete), typeof(NoCtor));

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("NoCtor") && e.Contains("constructor"));
    }

    // -----------------------------------------------------------------------
    // Unmapped destination properties (→ warnings)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_UnmappedDestProperty_ReturnsWarning()
    {
        // ConcreteWithExtra has an "Extra" property that Concrete (source) doesn't have.
        var result = MappingValidator.Validate(typeof(Concrete), typeof(ConcreteWithExtra));

        Assert.False(result.HasErrors);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, w => w.Contains("Extra"));
    }

    [Fact]
    public void Validate_AllPropertiesMapped_NoWarnings()
    {
        var result = MappingValidator.Validate(typeof(CompatibleSource), typeof(Concrete));

        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.True(result.IsValid);
    }

    // -----------------------------------------------------------------------
    // Incompatible property types (→ errors)
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_IncompatiblePropertyType_ReturnsError()
    {
        // IncompatibleSource.Name is int; Concrete.Name is string — not assignable.
        var result = MappingValidator.Validate(typeof(IncompatibleSource), typeof(Concrete));

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("Name") && e.Contains("Int32"));
    }

    // -----------------------------------------------------------------------
    // ValidateAndThrow
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateAndThrow_WithErrors_ThrowsMappingException()
    {
        var ex = Assert.Throws<MappingException>(() =>
            MappingValidator.ValidateAndThrow(typeof(Concrete), typeof(AbstractDest)));

        Assert.Contains("AbstractDest", ex.Message);
    }

    [Fact]
    public void ValidateAndThrow_WithoutErrors_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            MappingValidator.ValidateAndThrow(typeof(CompatibleSource), typeof(Concrete)));

        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // ValidationResult helpers
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidationResult_IsValid_TrueWhenNoErrors()
    {
        var result = new ValidationResult(
            errors: Array.Empty<string>(),
            warnings: new[] { "a warning" });

        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public void ValidationResult_FormatMessages_IncludesErrorAndWarningPrefixes()
    {
        var result = new ValidationResult(
            errors: new[] { "err1" },
            warnings: new[] { "warn1" });

        var formatted = result.FormatMessages();

        Assert.Contains("[ERROR]", formatted);
        Assert.Contains("[WARNING]", formatted);
        Assert.Contains("err1", formatted);
        Assert.Contains("warn1", formatted);
    }
}
