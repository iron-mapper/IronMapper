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

    // A domain type with a private constructor, created by its factory (ConstructWith).
    public sealed class Project
    {
        private Project(int id, string name) { Id = id; Name = name; }
        public int Id { get; }
        public string Name { get; }
        public string Status { get; set; } = string.Empty;
        public static Project Restore(int id, string name) => id < 0 ? throw new System.ArgumentOutOfRangeException(nameof(id)) : new(id, name);
    }

    public class ProjectCardProfile : MappingProfile
    {
        public ProjectCardProfile()
        {
            CreateMap<OwnerRow, OwnerCard>().ReverseMap();
            CreateMap<ProjectRow, ProjectCard>().ReverseMap();
            CreateMap<ProjectCard, Project>().ConstructWith(Project.Restore);
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

        [Fact]
        public void Factory_NewObject_Collections_InPlaceMappingException_ExceptionAsItIs()
        {
            var mapper = CreateMapper();
            var card = new ProjectCard(7, "p7", new OwnerCard("o7"), "paused");

            var project = mapper.Map<ProjectCard, Project>(card);

            Assert.Equal((7, "p7", "paused"), (project.Id, project.Name, project.Status));
            Assert.Equal(2, mapper.Map<List<Project>>(new[] { card, card }).Count);
            var inPlace = Assert.Throws<MappingException>(() => mapper.Map(card, Project.Restore(1, "x")));
            Assert.Equal(
                "No in-place mapping from ProjectCard to Project is generated: Project is created by factory 'Project.Restore'. Map to a new object instead.",
                inPlace.Message);
            // IMapper does not wrap exceptions (2.0.0): the factory's exception arrives as it is.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => mapper.Map<ProjectCard, Project>(card with { Id = -1 }));
        }
    }
}
