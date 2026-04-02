using IronMapper.Configuration;
using IronMapper.Demo.Models.Api;
using IronMapper.Demo.Models.App;

namespace IronMapper.Demo.Profiles;

/// <summary>
/// Demonstrates conditional mapping with When().
/// The generated MapToLivingAuthorSummary() method returns default (null)
/// for authors who have a death date, silently skipping them.
/// </summary>
public class ConditionalMappingProfile : MappingProfile
{
    public ConditionalMappingProfile()
    {
        // ── AuthorResponse → LivingAuthorSummary ────────────────────────────
        // When() emits: if (!(source.DeathDate == null)) return default!;
        // at the top of the generated method — mapping only proceeds for
        // living authors (DeathDate == null).
        CreateMap<AuthorResponse, LivingAuthorSummary>()
            .When(s => s.DeathDate == null)
            .ForMember(d => d.Id,
                o => o.MapFrom(s => s.Key.Replace("/authors/", "")))
            .ForMember(d => d.BirthYear,
                o => o.MapFrom(s =>
                    s.BirthDate != null && s.BirthDate.Length >= 4
                        ? s.BirthDate.Substring(s.BirthDate.Length - 4)
                        : null))
            .ForMember(d => d.ProfileUrl,
                o => o.MapFrom(s => $"https://openlibrary.org{s.Key}"));
    }
}
