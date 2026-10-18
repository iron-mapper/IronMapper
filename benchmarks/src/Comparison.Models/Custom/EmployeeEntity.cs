namespace Comparison.Models.Custom;

public class EmployeeEntity
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Renamed: maps to <see cref="EmployeeDto.EmailAddress"/>.</summary>
    public string Email { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    /// <summary>Converted: <c>decimal</c> → <c>string</c> ("F2", InvariantCulture).</summary>
    public decimal Salary { get; set; }

    public DateTime HiredAt { get; set; }

    /// <summary>Ignored: <see cref="EmployeeDto.Notes"/> is never assigned by the mapping.</summary>
    public string Notes { get; set; } = string.Empty;
}
