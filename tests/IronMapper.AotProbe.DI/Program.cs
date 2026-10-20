using IronMapper.Attributes;
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;

// eng/verify-aot.ps1 expects IL2026 on the AddIronMapper line below and no other trim/AOT warning.
var services = new ServiceCollection();
services.AddIronMapper(typeof(User).Assembly);

using var provider = services.BuildServiceProvider();
var dto = provider.GetRequiredService<IMapper>().Map<User, UserDto>(new User { Id = 1 });
Console.WriteLine(dto.Id);

[MapTo(typeof(UserDto))]
public class User
{
    public int Id { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
}
