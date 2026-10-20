namespace IronMapper.Extensions.DI;

/// <summary>Texts of the trimming/NativeAOT warnings of the <c>IMapper</c> path.</summary>
internal static class TrimMessages
{
    /// <summary>
    /// <c>RuntimeMapper</c> finds the generated methods by name (<c>Assembly.GetType</c>, <c>GetMethods</c>) and calls
    /// them with <c>MethodInfo.Invoke</c>; <c>AddIronMapper</c> scans assemblies with <c>GetTypes</c> and registers the
    /// types it finds. The trimmer cannot see these uses. Removed when <c>IMapper</c> gets the generated registry
    /// (plan item 1.3).
    /// </summary>
    public const string IMapper =
        "IMapper finds and calls the generated mapping methods through reflection; trimming and NativeAOT remove them, " +
        "and mapping fails at runtime with MappingException. Call the generated MapTo* extension methods " +
        "(using IronMapper.Generated;) instead.";

    /// <summary>Issue about <c>IMapper</c> and NativeAOT.</summary>
    public const string IMapperUrl = "https://github.com/iron-mapper/IronMapper/issues/26";
}
