using System.Collections.Generic;
using IronMapper.Attributes;
using IronMapper.Configuration;

// One source mapped to two destination types with the same simple name: the generated methods get qualified
// names, MapToAdminMemberDto and MapToPublicMemberDto (IM0013, suppressed in the .csproj). They are public, so the
// consumer calls them (see Program.cs).
namespace PublicLib
{
    [MapTo(typeof(Admin.MemberDto))]
    [MapTo(typeof(Public.MemberDto))]
    public class Member
    {
        public string Name { get; set; } = string.Empty;
    }

    // The element mapping of Members has the qualified name MapToAdminMemberDto.
    public class Team
    {
        public List<Member> Members { get; set; } = new();
    }

    public class TeamProfile : MappingProfile
    {
        public TeamProfile()
        {
            CreateMap<Team, Admin.TeamDto>();
        }
    }
}

namespace PublicLib.Admin
{
    public class MemberDto
    {
        public string Name { get; set; } = string.Empty;
    }

    public class TeamDto
    {
        public List<MemberDto> Members { get; set; } = new();
    }
}

namespace PublicLib.Public
{
    public class MemberDto
    {
        public string Name { get; set; } = string.Empty;
    }
}
