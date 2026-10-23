// A file written before the package added `global using IronMapper.Generated;` (2.0, buildTransitive/IronMapper.props):
// ImplicitUsings on, and an explicit using of the generated namespace. eng/verify-package.ps1 builds it with
// EnforceCodeStyleInBuild=true and dotnet_diagnostic.IDE0005.severity = error in .editorconfig: the build fails with
// IDE0005 at the `using IronMapper.Generated;` line (the only unnecessary using here); with
// IronMapperImplicitUsings=false it builds. System types come from the SDK's implicit usings.
using IronMapper.Attributes;
using IronMapper.Generated;

namespace Consumer;

public static class Program
{
    public static int Main()
    {
        var dto = new User { Id = 7, Name = "Alice" }.MapToUserDto();
        if (dto.Name != "Alice" || dto.Id != 7)
        {
            Console.Error.WriteLine("FAIL: mapped values");
            return 1;
        }

        Console.WriteLine("ExplicitUsingStrict: OK");
        return 0;
    }
}

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
