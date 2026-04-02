using System;
using System.Linq;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;
using Xunit;

namespace IronMapper.Integration.Tests;

// =======================================================================
// Fixture types — same namespace so the source generator processes them.
// =======================================================================

// Scenario 1 — E-commerce: flat order entity
[MapTo(typeof(EcommerceOrderDto))]
public class EcommerceOrderEntity
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
}

public class EcommerceOrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
}

// Scenario 2 — API Response: user entity with date formatting via profile
public class ApiUserEntity
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime LastLoginAt { get; set; }
    public bool IsActive { get; set; }
}

public class ApiUserResponseDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string LastLoginAt { get; set; } = string.Empty;  // formatted date string
    public bool IsActive { get; set; }
}

public class ApiUserProfile : MappingProfile
{
    public ApiUserProfile()
    {
        CreateMap<ApiUserEntity, ApiUserResponseDto>()
            .ForMember(d => d.LastLoginAt,
                o => o.MapFrom(s => s.LastLoginAt.ToString("yyyy-MM-dd")));
    }
}

// Scenario 3 — Configuration: AppSettings → AppConfig with computed DisplayName
public class AppSettings
{
    public string AppName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int MaxRetries { get; set; }
    public bool EnableLogging { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
}

public class AppConfig
{
    public string AppName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int MaxRetries { get; set; }
    public bool EnableLogging { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class AppConfigProfile : MappingProfile
{
    public AppConfigProfile()
    {
        CreateMap<AppSettings, AppConfig>()
            .ForMember(d => d.DisplayName,
                o => o.MapFrom(s => s.AppName + " v" + s.Version));
    }
}

// =======================================================================
// Tests
// =======================================================================

/// <summary>
/// End-to-end tests modelling realistic domain scenarios.
/// </summary>
public class RealWorldTests
{
    // -----------------------------------------------------------------------
    // Scenario 1 — E-commerce order mapping
    // -----------------------------------------------------------------------

    [Fact]
    public void EcommerceScenario_OrderEntity_MapsToOrderDto()
    {
        var entity = new EcommerceOrderEntity
        {
            Id           = 1001,
            CustomerName = "Alice Smith",
            OrderDate    = new DateTime(2024, 3, 15),
            Status       = "Shipped",
            TotalAmount  = 249.99m,
            ItemCount    = 3,
        };

        var dto = entity.MapToEcommerceOrderDto();

        Assert.Equal(1001, dto.Id);
        Assert.Equal("Alice Smith", dto.CustomerName);
        Assert.Equal(new DateTime(2024, 3, 15), dto.OrderDate);
        Assert.Equal("Shipped", dto.Status);
        Assert.Equal(249.99m, dto.TotalAmount);
        Assert.Equal(3, dto.ItemCount);
    }

    [Fact]
    public void EcommerceScenario_Collection_AllOrdersMapped()
    {
        var orders = Enumerable.Range(1, 5).Select(i => new EcommerceOrderEntity
        {
            Id           = i,
            CustomerName = $"Customer {i}",
            TotalAmount  = i * 10.0m,
        }).ToList();

        var dtos = orders.Select(o => o.MapToEcommerceOrderDto()).ToList();

        Assert.Equal(5, dtos.Count);
        Assert.All(dtos, d => Assert.True(d.Id > 0));
    }

    // -----------------------------------------------------------------------
    // Scenario 2 — API user response with formatted date
    // -----------------------------------------------------------------------

    [Fact]
    public void ApiResponseScenario_DateFormattedAsIso8601()
    {
        var user = new ApiUserEntity
        {
            Id          = 42,
            Username    = "jdoe",
            Email       = "j@example.com",
            LastLoginAt = new DateTime(2024, 11, 5),
            IsActive    = true,
        };

        var dto = user.MapToApiUserResponseDto();

        Assert.Equal(42, dto.Id);
        Assert.Equal("jdoe", dto.Username);
        Assert.Equal("j@example.com", dto.Email);
        Assert.Equal("2024-11-05", dto.LastLoginAt);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void ApiResponseScenario_InactiveUser_IsActiveFalse()
    {
        var user = new ApiUserEntity
        {
            Id          = 7,
            Username    = "ghost",
            LastLoginAt = new DateTime(2020, 1, 1),
            IsActive    = false,
        };

        var dto = user.MapToApiUserResponseDto();

        Assert.False(dto.IsActive);
        Assert.Equal("2020-01-01", dto.LastLoginAt);
    }

    // -----------------------------------------------------------------------
    // Scenario 3 — Configuration transformation with computed field
    // -----------------------------------------------------------------------

    [Fact]
    public void ConfigScenario_DisplayNameIsComputed()
    {
        var settings = new AppSettings
        {
            AppName       = "IronMapper",
            Version       = "1.0.0",
            MaxRetries    = 3,
            EnableLogging = true,
            BaseUrl       = "https://api.example.com",
        };

        var config = settings.MapToAppConfig();

        Assert.Equal("IronMapper", config.AppName);
        Assert.Equal("1.0.0", config.Version);
        Assert.Equal("IronMapper v1.0.0", config.DisplayName);
        Assert.Equal(3, config.MaxRetries);
        Assert.True(config.EnableLogging);
        Assert.Equal("https://api.example.com", config.BaseUrl);
    }

    [Fact]
    public void ConfigScenario_LoggingDisabled_MappedCorrectly()
    {
        var settings = new AppSettings
        {
            AppName       = "TestApp",
            Version       = "2.5.1",
            EnableLogging = false,
        };

        var config = settings.MapToAppConfig();

        Assert.False(config.EnableLogging);
        Assert.Equal("TestApp v2.5.1", config.DisplayName);
    }
}
