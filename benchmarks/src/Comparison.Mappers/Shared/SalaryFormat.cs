using System.Globalization;

namespace Comparison.Mappers.Shared;

/// <summary>The one decimal → string conversion of the CustomConfig scenario, shared by every library.</summary>
public static class SalaryFormat
{
    public static string Format(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
