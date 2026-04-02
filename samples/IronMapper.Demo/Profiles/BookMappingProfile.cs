using System.Collections.Generic;
using System.Linq;
using IronMapper.Configuration;
using IronMapper.Demo.Models.Api;
using IronMapper.Demo.Models.App;

namespace IronMapper.Demo.Profiles;

/// <summary>
/// Fluent mapping profile for all book- and author-related mappings.
/// The Source Generator reads this class at compile time and emits
/// strongly-typed extension methods — no reflection at runtime.
/// </summary>
public class BookMappingProfile : MappingProfile
{
    public BookMappingProfile()
    {
        // ── BookDoc → BookSummary ────────────────────────────────────────────
        // Demonstrates: ForMember with lambdas, string transformations,
        // computed properties, collection projection.
        CreateMap<BookDoc, BookSummary>()
            .ForMember(d => d.Id,
                o => o.MapFrom(s => s.Key.Replace("/works/", "")))
            .ForMember(d => d.AuthorsDisplay,
                o => o.MapFrom(s =>
                    s.AuthorName != null ? string.Join(", ", s.AuthorName) : "Unknown Author"))
            .ForMember(d => d.PublishedYear,
                o => o.MapFrom(s => s.FirstPublishYear))
            .ForMember(d => d.EditionsInfo,
                o => o.MapFrom(s =>
                    s.EditionCount.HasValue ? $"{s.EditionCount} editions" : "1 edition"))
            .ForMember(d => d.CoverUrl,
                o => o.MapFrom(s =>
                    s.CoverI.HasValue
                        ? $"https://covers.openlibrary.org/b/id/{s.CoverI}-M.jpg"
                        : ""))
            .ForMember(d => d.TopSubjects,
                o => o.MapFrom(s =>
                    s.Subject != null ? s.Subject.Take(3).ToList() : new List<string>()));

        // ── AuthorResponse → AuthorInfo ──────────────────────────────────────
        // Demonstrates: ForMember with custom converter (BioConverter),
        // computed boolean, year extraction from date string.
        CreateMap<AuthorResponse, AuthorInfo>()
            .ForMember(d => d.Id,
                o => o.MapFrom(s => s.Key.Replace("/authors/", "")))
            .ForMember(d => d.Biography,
                o => o.MapFrom(s =>
                    new global::IronMapper.Demo.Profiles.BioConverter().Convert(s.Bio)))
            .ForMember(d => d.BirthYear,
                o => o.MapFrom(s =>
                    s.BirthDate != null && s.BirthDate.Length >= 4
                        ? s.BirthDate.Substring(s.BirthDate.Length - 4)
                        : null))
            .ForMember(d => d.DeathYear,
                o => o.MapFrom(s =>
                    s.DeathDate != null && s.DeathDate.Length >= 4
                        ? s.DeathDate.Substring(s.DeathDate.Length - 4)
                        : null))
            .ForMember(d => d.IsAlive,
                o => o.MapFrom(s => s.DeathDate == null))
            .ForMember(d => d.ProfileUrl,
                o => o.MapFrom(s => $"https://openlibrary.org{s.Key}"));

        // ── WorkResponse → BookDetail ────────────────────────────────────────
        // Demonstrates: Ignore for a property set externally (Author),
        // null-safe collection mapping, cover URL computation.
        CreateMap<WorkResponse, BookDetail>()
            .ForMember(d => d.Id,
                o => o.MapFrom(s => s.Key.Replace("/works/", "")))
            .ForMember(d => d.Subjects,
                o => o.MapFrom(s => s.Subjects ?? new List<string>()))
            .ForMember(d => d.CoverUrl,
                o => o.MapFrom(s =>
                    s.Covers != null && s.Covers.Count > 0
                        ? $"https://covers.openlibrary.org/b/id/{s.Covers[0]}-L.jpg"
                        : ""))
            .Ignore(d => d.Author);
    }
}
