// IronMapperRegistration=false set in the project (item 2.16 of the plan): the generator emits no registration for
// IMapper (no RegisterIronMapperMappings, no private MapInto), and the generated extension methods work. The in-place
// overload is turned off: without the registration it has no private MapInto either.
using System.Reflection;
using IronMapper.Attributes;

var failures = new List<string>();
var assembly = typeof(User).Assembly;
var mappers = assembly.GetType("IronMapper.Generated.GeneratedMappers_" + assembly.GetName().Name!.Replace('.', '_'), throwOnError: true)!;
var all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
if (mappers.GetMethod("RegisterIronMapperMappings", all) is not null) failures.Add("RegisterIronMapperMappings is generated");
if (mappers.GetMethods(all).Any(m => m.Name.StartsWith("MapInto", StringComparison.Ordinal))) failures.Add("MapInto is generated");

var dto = new User { Id = 4 }.MapToUserDto();
if (dto.Id != 4) failures.Add($"mapped {dto.Id}");

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAIL: " + string.Join("; ", failures));
    return 1;
}

Console.WriteLine("RegistrationOff: OK");
return 0;

[MapTo(typeof(UserDto), GenerateInPlace = false)]
public class User
{
    public int Id { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
}
