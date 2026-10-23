// The attributes of the generated class and the optional overloads, as set by the MSBuild properties of the package
// (CompilerVisibleProperty in buildTransitive/IronMapper.props). Built twice by eng/verify-package.ps1: with the defaults,
// and with IronMapperGenerateInPlace/List/Array=false, IronMapperExcludeFromCodeCoverage=true,
// IronMapperGeneratedCodeAttribute=false and DefineConstants=OVERLOADS_OFF. The registry used by IMapper maps List, array
// and in-place either way.
using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using IronMapper.Attributes;
using IronMapper.Registration;

#if OVERLOADS_OFF
const bool overloads = false;
#else
const bool overloads = true;
#endif

var failures = new List<string>();
// The generated class is GeneratedMappers_{assembly name with every character other than a letter, digit or _ as _}.
var mappers = typeof(User).Assembly.GetType(
    "IronMapper.Generated.GeneratedMappers_" + typeof(User).Assembly.GetName().Name!.Replace('.', '_'), throwOnError: true)!;
var methods = mappers.GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Select(m => $"{m.Name}/{m.GetParameters().Length}").ToHashSet();

if (!methods.Contains("MapToUserDto/1")) failures.Add("MapToUserDto missing");
foreach (var method in new[] { "MapToUserDto/2", "MapToUserDtoList/1", "MapToUserDtoArray/1" })
    if (methods.Contains(method) != overloads) failures.Add($"{method}: expected present = {overloads}");

var package = typeof(MapToAttribute).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
package = package.Split('+')[0];
var generatedCode = mappers.GetCustomAttribute<GeneratedCodeAttribute>();
if (overloads && (generatedCode is not { Tool: "IronMapper" } || generatedCode.Version != package))
    failures.Add($"[GeneratedCode]: expected IronMapper {package}, got {generatedCode?.Tool} {generatedCode?.Version}");
if (!overloads && generatedCode is not null)
    failures.Add("[GeneratedCode]: expected none with IronMapperGeneratedCodeAttribute=false");
if ((mappers.GetCustomAttribute<ExcludeFromCodeCoverageAttribute>() is not null) == overloads)
    failures.Add($"[ExcludeFromCodeCoverage]: expected present = {!overloads}");

var user = new User { Id = 7, Name = "Alice" };
var list = (List<UserDto>)MapperRegistry.Map(new[] { user }, typeof(User[]), typeof(List<UserDto>))!;
var array = (UserDto[])MapperRegistry.Map(new List<User> { user }, typeof(List<User>), typeof(UserDto[]))!;
MapperRegistry.TryGetMapInto(typeof(User), typeof(UserDto), out var mapInto);
var target = new UserDto();
mapInto(user, target);
if (user.MapToUserDto().Name != "Alice" || list[0].Name != "Alice" || array[0].Id != 7 || target.Name != "Alice")
    failures.Add("mapped values");

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAIL: " + string.Join("; ", failures));
    return 1;
}

Console.WriteLine("GeneratedOptions: OK");
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
