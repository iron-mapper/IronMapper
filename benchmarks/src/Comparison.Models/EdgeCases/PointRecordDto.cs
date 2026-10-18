namespace Comparison.Models.EdgeCases;

/// <summary>Edge case: a positional record (no parameterless constructor) as the destination.</summary>
public record PointRecordDto(double X, double Y);
