using System.Linq;
using IronMapper.Attributes;
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests
{
    // One source mapped to two destination types with the same simple name: the generated methods get
    // qualified names (IM0013, suppressed for this project in the .csproj), and IMapper must still find them.
    [MapTo(typeof(QualifiedNames.Admin.MemberDto))]
    [MapTo(typeof(QualifiedNames.Public.MemberDto))]
    public class Member
    {
        public string Name { get; set; } = string.Empty;
    }

    public class QualifiedNameMapperTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(QualifiedNameMapperTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        [Fact]
        public void Map_QualifiedMethodNames_EachDestinationMaps()
        {
            var mapper = CreateMapper();
            var member = new Member { Name = "m" };

            Assert.Equal("m", mapper.Map<Member, QualifiedNames.Admin.MemberDto>(member).Name);
            Assert.Equal("m", mapper.Map<QualifiedNames.Public.MemberDto>(member).Name);
        }

        [Fact]
        public void Map_InPlaceAndCollection_QualifiedMethodNames()
        {
            var mapper = CreateMapper();
            var target = new QualifiedNames.Public.MemberDto();

            mapper.Map(new Member { Name = "p" }, target);
            var list = mapper.MapCollection<Member, QualifiedNames.Admin.MemberDto>(new[] { new Member { Name = "a" } }).ToList();

            Assert.Equal("p", target.Name);
            Assert.Equal("a", list.Single().Name);
        }
    }
}

namespace IronMapper.Integration.Tests.QualifiedNames.Admin
{
    public class MemberDto
    {
        public string Name { get; set; } = string.Empty;
    }
}

namespace IronMapper.Integration.Tests.QualifiedNames.Public
{
    public class MemberDto
    {
        public string Name { get; set; } = string.Empty;
    }
}
