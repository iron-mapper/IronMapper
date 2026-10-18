namespace Comparison.Models.Custom;

public class EmployeeDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Renamed from <see cref="EmployeeEntity.Email"/>.</summary>
    public string EmailAddress { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    /// <summary><see cref="EmployeeEntity.Salary"/> formatted with "F2" and InvariantCulture.</summary>
    public string Salary { get; set; } = string.Empty;

    public DateTime HiredAt { get; set; }

    /// <summary>Ignored: stays <see langword="null"/> after mapping.</summary>
    public string? Notes { get; set; }

    /// <summary>Computed: <c>FirstName + " " + LastName</c>.</summary>
    public string FullName { get; set; } = string.Empty;
}
