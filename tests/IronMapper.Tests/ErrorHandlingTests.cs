using System;
using IronMapper.Attributes;
using IronMapper.Exceptions;
using IronMapper.Generated;
using IronMapper.Validation;
using Xunit;

namespace IronMapper.Tests;

// Fixture types — same namespace so the source generator processes [MapTo].

[MapTo(typeof(ErrDto))]
public class ErrEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ErrDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Verifies that the generated mapper and the runtime validator surface errors with
/// the correct exception types and messages.
/// </summary>
public class ErrorHandlingTests
{
    // -----------------------------------------------------------------------
    // ArgumentNullException from generated mapper
    // -----------------------------------------------------------------------

    [Fact]
    public void GeneratedMapper_NullSource_ThrowsArgumentNullException()
    {
        ErrEntity? src = null;
        var ex = Assert.Throws<ArgumentNullException>(() => src!.MapToErrDto());
        Assert.Equal("source", ex.ParamName);
    }

    // -----------------------------------------------------------------------
    // MappingValidator — errors propagate via MappingException
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateAndThrow_AbstractDest_ThrowsMappingException()
    {
        var ex = Assert.Throws<MappingException>(() =>
            MappingValidator.ValidateAndThrow(typeof(ErrEntity), typeof(AbstractErrDest)));

        Assert.Contains("AbstractErrDest", ex.Message);
    }

    [Fact]
    public void ValidateAndThrow_IncompatibleTypes_ThrowsMappingExceptionWithDetails()
    {
        var ex = Assert.Throws<MappingException>(() =>
            MappingValidator.ValidateAndThrow(typeof(IncompatibleSrc), typeof(IncompatibleDst)));

        Assert.Contains("Value", ex.Message);
    }

    [Fact]
    public void ValidateAndThrow_ValidMapping_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            MappingValidator.ValidateAndThrow(typeof(ErrEntity), typeof(ErrDto)));

        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // MappingException constructor coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void MappingException_DefaultConstructor_DoesNotThrow()
    {
        var ex = new MappingException();
        Assert.NotNull(ex);
    }

    [Fact]
    public void MappingException_WithMessage_MessageIsPreserved()
    {
        var ex = new MappingException("test error");
        Assert.Equal("test error", ex.Message);
    }

    [Fact]
    public void MappingException_WithInnerException_InnerExceptionIsPreserved()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new MappingException("outer", inner);

        Assert.Equal("outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    // -----------------------------------------------------------------------
    // Private helpers — local types that don't interact with the generator
    // -----------------------------------------------------------------------

    private abstract class AbstractErrDest { }

    private class IncompatibleSrc { public string Value { get; set; } = ""; }
    private class IncompatibleDst { public int Value { get; set; } }  // int ≠ string
}
