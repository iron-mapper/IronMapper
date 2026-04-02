using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using IronMapper.Attributes;
using IronMapper.Generated;

namespace IronMapper.Benchmarks;

// Non-file-scoped so the source generator can process [MapTo].

[MapTo(typeof(UserDto))]
public class UserEntity
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
    public bool IsActive { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Benchmarks comparing IronMapper generated mapping against manual (hand-written) mapping.
/// Run with: dotnet run --project tests/IronMapper.Benchmarks -c Release
/// Goal: IronMapper should be within ±5% of manual mapping (they compile to identical IL).
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class MappingBenchmarks
{
    private UserEntity _entity = null!;
    private List<UserEntity> _entities = null!;

    [GlobalSetup]
    public void Setup()
    {
        _entity = new UserEntity
        {
            Id        = 1,
            FirstName = "Alice",
            LastName  = "Smith",
            Email     = "alice@example.com",
            Age       = 30,
            IsActive  = true,
        };

        _entities = new List<UserEntity>(1000);
        for (var i = 0; i < 1000; i++)
        {
            _entities.Add(new UserEntity
            {
                Id        = i,
                FirstName = "User",
                LastName  = $"#{i}",
                Email     = $"user{i}@example.com",
                Age       = 20 + (i % 50),
                IsActive  = i % 2 == 0,
            });
        }
    }

    // -----------------------------------------------------------------------
    // Single object
    // -----------------------------------------------------------------------

    /// <summary>Baseline: hand-written object initializer.</summary>
    [Benchmark(Baseline = true)]
    public UserDto ManualMapping()
    {
        return new UserDto
        {
            Id        = _entity.Id,
            FirstName = _entity.FirstName,
            LastName  = _entity.LastName,
            Email     = _entity.Email,
            Age       = _entity.Age,
            IsActive  = _entity.IsActive,
        };
    }

    /// <summary>IronMapper: compile-time generated extension method.</summary>
    [Benchmark]
    public UserDto IronMapperMapping() => _entity.MapToUserDto();

    // -----------------------------------------------------------------------
    // Collection of 1 000 items
    // -----------------------------------------------------------------------

    /// <summary>Baseline collection: manual loop with hand-written mapping.</summary>
    [Benchmark]
    public List<UserDto> Manual_Collection_1000()
    {
        var result = new List<UserDto>(_entities.Count);
        foreach (var e in _entities)
        {
            result.Add(new UserDto
            {
                Id        = e.Id,
                FirstName = e.FirstName,
                LastName  = e.LastName,
                Email     = e.Email,
                Age       = e.Age,
                IsActive  = e.IsActive,
            });
        }
        return result;
    }

    /// <summary>IronMapper collection: foreach loop over generated extension method.</summary>
    [Benchmark]
    public List<UserDto> IronMapper_Collection_1000()
    {
        var result = new List<UserDto>(_entities.Count);
        foreach (var e in _entities)
            result.Add(e.MapToUserDto());
        return result;
    }
}
