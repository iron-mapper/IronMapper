using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IronMapper.Validation;

/// <summary>
/// Holds the outcome of a <see cref="MappingValidator"/> check:
/// a list of hard errors (that would prevent mapping) and advisory warnings.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Hard errors that would prevent a mapping from being generated or executed.
    /// A non-empty list means the mapping is misconfigured.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Advisory warnings that indicate potential data loss or incomplete mappings.
    /// The mapping can still proceed, but the result may be surprising.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Returns true when there are no errors.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Returns true when there is at least one error.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Returns true when there is at least one warning.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Initialises a new <see cref="ValidationResult"/>.</summary>
    public ValidationResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    /// <summary>
    /// Formats all errors and warnings into a multi-line human-readable string.
    /// </summary>
    public string FormatMessages()
    {
        var sb = new StringBuilder();

        foreach (var error in Errors)
            sb.AppendLine($"[ERROR]   {error}");

        foreach (var warning in Warnings)
            sb.AppendLine($"[WARNING] {warning}");

        return sb.ToString().TrimEnd();
    }
}
