// The generated registration without IronMapper.Extensions.DI: the module initializer of this assembly has added its
// mappings to the registry (feature switch IronMapper.Registration.IsSupported not set), and they are found by type.
using IronMapper.Attributes;
using IronMapper.Registration;

if (!MapperRegistry.TryGetMap(typeof(User), typeof(UserDto), out var map) || ((UserDto)map(new User { Id = 3 })!).Id != 3)
{
    Console.Error.WriteLine("FAIL: the mapping is not registered");
    return 1;
}

Console.WriteLine("Registry: OK");
return 0;

[MapTo(typeof(UserDto))]
public class User
{
    public int Id { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
}
