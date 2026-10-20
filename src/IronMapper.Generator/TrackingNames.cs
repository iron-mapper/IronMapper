namespace IronMapper.Generator;

/// <summary>
/// Names of the incremental pipeline steps (<c>WithTrackingName</c>). Only used by tests that run the driver with
/// <c>trackIncrementalGeneratorSteps</c> to check that unchanged input leaves the steps cached.
/// </summary>
internal static class TrackingNames
{
    public const string MapToTransform = nameof(MapToTransform);
    public const string MapTo = nameof(MapTo);
    public const string MapFromTransform = nameof(MapFromTransform);
    public const string MapFrom = nameof(MapFrom);
    public const string ProfileTransform = nameof(ProfileTransform);
    public const string Profiles = nameof(Profiles);
    public const string Plan = nameof(Plan);
    public const string ClassAccessibility = nameof(ClassAccessibility);
    public const string CodeOptions = nameof(CodeOptions);
}
