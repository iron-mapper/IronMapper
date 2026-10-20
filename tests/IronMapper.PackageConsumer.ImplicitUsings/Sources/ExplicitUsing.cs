// Explicit usings, C# 9 syntax only (no global using, no file-scoped namespace): builds with ImplicitUsings off, with
// LangVersion 9, and next to the global using of the package without CS0105/CS8933. Also built as a netstandard2.0 and
// netstandard2.1 library (generated throw helper), so it uses only APIs available there.
using System;
using System.Collections.Generic;
using IronMapper.Attributes;
using IronMapper.Generated;

namespace Consumer
{
    public static class Program
    {
        public static int Main()
        {
            var user = new User { Id = 7, Name = "Alice", Lines = new List<Line> { new Line { Sku = "A" }, new Line { Sku = "B" } } };
            var dto = user.MapToUserDto();
            var array = new[] { user }.MapToUserDtoArray();
            if (dto.Name != "Alice" || dto.Lines == null || dto.Lines.Length != 2 || dto.Lines[1].Sku != "B" || array.Length != 1)
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
        public List<Line>? Lines { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public LineDto[]? Lines { get; set; }
    }

    [MapTo(typeof(LineDto))]
    public class Line
    {
        public string Sku { get; set; } = string.Empty;
    }

    public class LineDto
    {
        public string Sku { get; set; } = string.Empty;
    }
}
