using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Comparison.Benchmarks;

/// <summary>
/// Normalizes a collection benchmark by its size parameter, so different <c>Count</c> values can be compared:
/// mean time per item (ns) and allocated bytes per item.
/// </summary>
public sealed class PerItemColumn : IColumn
{
    public const string CountParameter = "Count";

    public static readonly IColumn TimePerItem = new PerItemColumn(isTime: true);
    public static readonly IColumn AllocatedPerItem = new PerItemColumn(isTime: false);

    private readonly bool _isTime;

    private PerItemColumn(bool isTime) => _isTime = isTime;

    public string Id => _isTime ? "ns/item" : "B/item";
    public string ColumnName => _isTime ? "ns/item" : "B/item";
    public bool AlwaysShow => true;
    public ColumnCategory Category => _isTime ? ColumnCategory.Statistics : ColumnCategory.Metric;
    public int PriorityInCategory => 100;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => _isTime
        ? "Mean divided by Count: nanoseconds per mapped item"
        : "Allocated divided by Count: bytes allocated per mapped item";

    public bool IsAvailable(Summary summary) => true;
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
        GetValue(summary, benchmarkCase, SummaryStyle.Default);

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var report = summary[benchmarkCase];
        if (report is null || benchmarkCase.Parameters[CountParameter] is not int count || count <= 0)
            return "NA";

        if (_isTime)
        {
            var mean = report.ResultStatistics?.Mean;
            return mean is null ? "NA" : (mean.Value / count).ToString("N1", CultureInfo.InvariantCulture);
        }

        var bytes = report.GcStats.GetBytesAllocatedPerOperation(benchmarkCase);
        return bytes is null ? "NA" : ((double)bytes.Value / count).ToString("N1", CultureInfo.InvariantCulture);
    }

    public override string ToString() => ColumnName;
}

/// <summary>Adds <see cref="PerItemColumn"/> columns to a benchmark class with a <c>Count</c> parameter.</summary>
public sealed class PerItemConfig : ManualConfig
{
    public PerItemConfig() => AddColumn(PerItemColumn.TimePerItem, PerItemColumn.AllocatedPerItem);
}
