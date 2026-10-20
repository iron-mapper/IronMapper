// Explicit usings, C# 9 syntax only (no global using, no file-scoped namespace): builds with ImplicitUsings off, with
// LangVersion 9, and next to the global using of the package without CS0105/CS8933.
using System;
using IronMapper.Attributes;
using IronMapper.Generated;

namespace Consumer
{
    public static class Program
    {
        public static int Main()
        {
            var dto = new User { Id = 7, Name = "Alice" }.MapToUserDto();
            if (dto.Name != "Alice")
            {
                Console.Error.WriteLine("FAIL: mapped values");
                return 1;
            }

            Console.WriteLine("ExplicitUsing: OK");
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
}
