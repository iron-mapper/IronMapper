// Markdown size comparison of NativeAOT .mstat files.
//
//   MstatReport --input Manual=a.mstat --input IronMapper=b.mstat --input Mapperly=c.mstat
//               [--diff IronMapper,Mapperly] [--top 15] [--heading-level 3] [--out report.md]
//   MstatReport --dump a.mstat [--top N]   section totals and the N largest entries of one file
//
// The first input is the reference: every other input gets a "Δ vs <reference>" column. --diff A,B adds an "A − B"
// column and sorts the namespace and type tables by it (otherwise by the largest Δ vs the reference).
using System.Globalization;
using System.Text;
using MstatReport;

const string BlobSection = "Blobs";

var inputs = new List<(string Label, MstatFile File)>();
string? diffA = null, diffB = null, outPath = null, dumpPath = null;
int top = 15, headingLevel = 3;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value after {args[i]}");
    switch (args[i])
    {
        case "--input":
            string spec = Next();
            int eq = spec.IndexOf('=');
            if (eq <= 0)
            {
                throw new ArgumentException($"--input expects Label=path, got '{spec}'");
            }

            inputs.Add((spec[..eq], MstatFile.Load(spec[(eq + 1)..])));
            break;
        case "--diff":
            string[] pair = Next().Split(',');
            if (pair.Length != 2)
            {
                throw new ArgumentException("--diff expects A,B");
            }

            (diffA, diffB) = (pair[0], pair[1]);
            break;
        case "--top": top = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--heading-level": headingLevel = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--out": outPath = Next(); break;
        case "--dump": dumpPath = Next(); break;
        default: throw new ArgumentException($"Unknown argument '{args[i]}'");
    }
}

if (dumpPath is not null)
{
    Dump(MstatFile.Load(dumpPath), top);
    return 0;
}
if (inputs.Count == 0)
{
    Console.Error.WriteLine("Usage: MstatReport --input Label=file.mstat [--input ...] [--diff A,B] [--top N] [--heading-level N] [--out file.md]");
    return 1;
}
if (diffA is not null && (!inputs.Exists(x => x.Label == diffA) || !inputs.Exists(x => x.Label == diffB)))
{
    throw new ArgumentException($"--diff labels must be among the inputs: {diffA}, {diffB}");
}

string md = BuildReport();
if (outPath is null)
{
    Console.Write(md);
}
else
{
    File.WriteAllText(outPath, md, new UTF8Encoding(false));
}

return 0;

string BuildReport()
{
    var sb = new StringBuilder();
    string h = new('#', headingLevel);
    var labels = inputs.Select(x => x.Label).ToList();
    string reference = labels[0];
    var others = labels.Skip(1).ToList();

    sb.Append($"{h} Size by mstat section, bytes\n\n");
    var sections = inputs.SelectMany(x => x.File.Entries.Select(e => e.Section)).Distinct().ToList();
    var bySection = Aggregate(e => e.Section, _ => true);
    // No total: the Blobs section overlaps FrozenObjects and RvaFields.
    AppendTable(sb, "Section", sections, bySection, sortByDiff: false, showTotal: false);
    sb.Append('\n');

    sb.Append($"{h} Named data blobs (not attributed to a type), bytes\n\n");
    var byBlob = Aggregate(e => e.Member, e => e.Section == BlobSection);
    AppendTable(sb, "Blob", Keys(byBlob), byBlob, sortByDiff: true);
    sb.Append('\n');

    sb.Append($"{h} Size by assembly, bytes\n\n");
    var byAssembly = Aggregate(e => e.Owner.Assembly);
    AppendTable(sb, "Assembly", Keys(byAssembly), byAssembly, sortByDiff: true);
    sb.Append('\n');

    sb.Append($"{h} Largest differences by namespace, bytes\n\n");
    var byNamespace = Aggregate(e => $"{e.Owner.Assembly}: {(e.Owner.Namespace.Length == 0 ? "(global)" : e.Owner.Namespace)}");
    AppendTable(sb, "Assembly: namespace", Keys(byNamespace), byNamespace, sortByDiff: true, limit: top);
    sb.Append('\n');

    sb.Append($"{h} Largest differences by type, bytes\n\n");
    var byType = Aggregate(e => $"{e.Owner.Assembly}: {e.Owner.FullName}");
    AppendTable(sb, "Assembly: type", Keys(byType), byType, sortByDiff: true, limit: top);
    sb.Append('\n');

    sb.Append($"Sizes are the per-node sizes recorded by ILC: method code, MethodTables and other data, attributed to the owning " +
              $"type (generic instantiations are folded into their definition, so `List<T>` code for any `T` counts as " +
              $"``System.Collections.Generic.List`1``). The assembly, namespace and type tables cover the Methods, Types, " +
              $"FrozenObjects and RvaFields sections; the Blobs section (metadata, stack trace data, ...) is shown separately, " +
              $"and its `ArrayOfFrozenObjects` and `FieldRvaData` blobs contain the FrozenObjects and RvaFields records. The mstat does not cover " +
              $"everything in the executable (PE headers, relocations, and so on), so the totals are smaller than the file size.\n");
    return sb.ToString();

    // Owner tables skip the Blobs section: its ArrayOfFrozenObjects and FieldRvaData blobs already contain the
    // FrozenObjects and RvaFields records, which are attributed to types.
    Dictionary<string, Dictionary<string, long>> Aggregate(Func<SizeEntry, string> keyOf, Func<SizeEntry, bool>? filter = null)
    {
        filter ??= static e => e.Section != BlobSection;
        var result = new Dictionary<string, Dictionary<string, long>>();
        foreach (var (label, file) in inputs)
        {
            var map = new Dictionary<string, long>();
            foreach (var e in file.Entries.Where(filter))
            {
                map[keyOf(e)] = map.GetValueOrDefault(keyOf(e)) + e.Size;
            }

            result[label] = map;
        }
        return result;
    }

    List<string> Keys(Dictionary<string, Dictionary<string, long>> data) => data.Values.SelectMany(m => m.Keys).Distinct().ToList();

    void AppendTable(StringBuilder s, string keyTitle, List<string> keys, Dictionary<string, Dictionary<string, long>> data, bool sortByDiff, int limit = int.MaxValue, bool showTotal = true)
    {
        long Get(string label, string key) => data[label].GetValueOrDefault(key);
        long SortValue(string key) => diffA is not null
            ? Math.Abs(Get(diffA, key) - Get(diffB!, key))
            : others.Select(o => Math.Abs(Get(o, key) - Get(reference, key))).DefaultIfEmpty(0).Max();

        var rows = keys.Select(k => (Key: k, Sort: SortValue(k)));
        if (sortByDiff)
        {
            rows = rows.Where(r => r.Sort != 0).OrderByDescending(r => r.Sort).ThenBy(r => r.Key, StringComparer.Ordinal);
        }

        var list = rows.ToList();

        var header = new List<string> { keyTitle };
        header.AddRange(labels);
        header.AddRange(others.Select(o => $"{o} − {reference}"));
        if (diffA is not null)
        {
            header.Add($"{diffA} − {diffB}");
        }

        s.Append("| ").Append(string.Join(" | ", header)).Append(" |\n");
        s.Append("|---").Append(string.Concat(Enumerable.Repeat("|---:", header.Count - 1))).Append("|\n");

        foreach (var (key, _) in list.Take(limit))
        {
            var cells = new List<string> { Escape(key) };
            cells.AddRange(labels.Select(l => Bytes(Get(l, key))));
            cells.AddRange(others.Select(o => Delta(Get(o, key) - Get(reference, key))));
            if (diffA is not null)
            {
                cells.Add(Delta(Get(diffA, key) - Get(diffB!, key)));
            }

            s.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
        }
        if (list.Count > limit)
        {
            s.Append($"\n_{list.Count - limit} more rows with smaller differences are not shown._\n");
        }

        if (!showTotal)
        {
            return;
        }

        var totals = new List<string> { "**Total**" };
        totals.AddRange(labels.Select(l => $"**{Bytes(data[l].Values.Sum())}**"));
        totals.AddRange(others.Select(o => $"**{Delta(data[o].Values.Sum() - data[reference].Values.Sum())}**"));
        if (diffA is not null)
        {
            totals.Add($"**{Delta(data[diffA].Values.Sum() - data[diffB!].Values.Sum())}**");
        }

        if (list.Count > limit)
        {
            s.Append('\n').Append("| ").Append(string.Join(" | ", header)).Append(" |\n")
            .Append("|---").Append(string.Concat(Enumerable.Repeat("|---:", header.Count - 1))).Append("|\n");
        }

        s.Append("| ").Append(string.Join(" | ", totals)).Append(" |\n");
    }
}

static string Bytes(long value) => value == 0 ? "–" : value.ToString("N0", CultureInfo.InvariantCulture);
static string Delta(long value) => value == 0 ? "0" : value.ToString("+#,0;−#,0", CultureInfo.InvariantCulture);
static string Escape(string text) => text.Replace("|", "\\|").Replace("<", "&lt;").Replace(">", "&gt;").Replace("`", "\\`");

static void Dump(MstatFile file, int top)
{
    Console.WriteLine($"{file.Path}: mstat version {file.Version}");
    foreach (var g in file.Entries.GroupBy(e => e.Section))
    {
        Console.WriteLine($"  {g.Key}: {g.Count()} records, {g.Sum(e => e.Size):N0} bytes");
    }

    Console.WriteLine("Largest entries:");
    foreach (var e in file.Entries.OrderByDescending(e => e.Size).Take(top))
    {
        Console.WriteLine($"  {e.Size,9:N0}  {e.Section,-10} [{e.Owner.Assembly}] {e.Owner.FullName} {e.Member}");
    }
}
