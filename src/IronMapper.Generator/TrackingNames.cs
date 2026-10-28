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
    public const string Registrations = nameof(Registrations);
    public const string SourcePlan = nameof(SourcePlan);
    public const string MapToSource = nameof(MapToSource);
    public const string MapFromSource = nameof(MapFromSource);
    public const string ProfilesSource = nameof(ProfilesSource);
    public const string Settings = nameof(Settings);
    public const string Diagnostics = nameof(Diagnostics);
    public const string MethodSettings = nameof(MethodSettings);
    public const string RegistrationSettings = nameof(RegistrationSettings);
    public const string CoverageSettings = nameof(CoverageSettings);
    public const string CodeSettings = nameof(CodeSettings);
    public const string CompilationOptions = nameof(CompilationOptions);
    public const string ClassName = nameof(ClassName);
}
