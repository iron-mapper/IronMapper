using Comparison.Mappers.AutoMapper;

namespace Comparison.Tests;

public class AutoMapperConfigurationTests
{
    [Fact]
    public void Configuration_is_valid() => AutoMapperSetup.CreateConfiguration().AssertConfigurationIsValid();
}
