namespace Comparison.Models.Flat;

/// <summary>Shared by <see cref="UserEntity"/> and <see cref="UserDto"/> (same enum type on both sides).</summary>
public enum UserStatus
{
    Pending,
    Active,
    Suspended,
    Deleted,
}
