using System.Collections.Generic;
using IronMapper.Configuration;
using IronMapper.Exceptions;
using IronMapper.Extensions.DI;
using IronMapper.Integration.Tests.Constructors;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests.Constructors
{
    public class ProjectRow { public int Id { get; set; } public string Name { get; set; } = string.Empty; public OwnerRow Owner { get; set; } = new(); }
    public record ProjectCard(int Id, string Name, OwnerCard Owner, string Status = "active");
    public class OwnerRow { public string Name { get; set; } = string.Empty; }
    public record OwnerCard(string Name);

    public class ProjectCardProfile : MappingProfile
    {
        public ProjectCardProfile()
        {
            CreateMap<OwnerRow, OwnerCard>().ReverseMap();
            CreateMap<ProjectRow, ProjectCard>().ReverseMap();
        }
    }
}

namespace IronMapper.Integration.Tests
{
    /// <summary>Destinations created by a constructor (issue #24), through <see cref="IMapper"/> (the generated registry).</summary>
    public class ConstructorMapperTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(ConstructorMapperTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        private static ProjectRow Row(int id) => new() { Id = id, Name = "p" + id, Owner = new OwnerRow { Name = "o" + id } };

        [Fact]
        public void Map_NewObject_RuntimeType_Collections_Reverse()
        {
            var mapper = CreateMapper();

            Assert.Equal(new ProjectCard(1, "p1", new OwnerCard("o1")), mapper.Map<ProjectRow, ProjectCard>(Row(1)));
            Assert.Equal(new ProjectCard(2, "p2", new OwnerCard("o2")), mapper.Map<ProjectCard>((object)Row(2)));
            Assert.Equal(2, mapper.Map<List<ProjectCard>>(new[] { Row(3), Row(4) }).Count);
            // The reverse mapping (ReverseMap) of the record creates the class with an initializer, as before.
            var row = mapper.Map<ProjectCard, ProjectRow>(new ProjectCard(5, "p5", new OwnerCard("o5")));
            Assert.Equal(("p5", "o5"), (row.Name, row.Owner.Name));
        }

        [Fact]
        public void MapInPlace_DestinationCreatedByAConstructor_MappingExceptionWithTheReason()
        {
            var mapper = CreateMapper();

            var exception = Assert.Throws<MappingException>(() => mapper.Map(Row(1), new ProjectCard(0, "", new OwnerCard(""))));

            Assert.Equal(
                "No in-place mapping from ProjectRow to ProjectCard is generated: ProjectCard has no accessible parameterless constructor and is created by a constructor with parameters. Map to a new object instead.",
                exception.Message);
        }
    }
}
