// ImplicitUsings is enabled and there is no using IronMapper.Generated: the package adds it as a global using.
using IronMapper.Attributes;

var user = new User { Id = 7, Name = "Alice" };
var dto = user.MapToUserDto();
var list = new[] { user }.MapToUserDtoList();
var target = new UserDto();
user.MapToUserDto(target);

if (dto.Name != "Alice" || list.Count != 1 || target.Id != 7)
{
    Console.Error.WriteLine("FAIL: mapped values");
    return 1;
}

Console.WriteLine("NoUsing: OK");
return 0;

[MapTo(typeof(UserDto))]
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
