using Comparison.Mappers.Shared;
using IronMapper.Interfaces;

namespace Comparison.Mappers.IronMapper;

public sealed class SalaryConverter : ITypeConverter<decimal, string>
{
    public string Convert(decimal source) => SalaryFormat.Format(source);
}
