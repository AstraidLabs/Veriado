using AutoMapper;
using Veriado.Contracts.Search;
using Veriado.Domain.Search;

namespace Veriado.Mapping.Profiles;

/// <summary>
/// Configures mappings between search domain models and contract DTOs.
/// </summary>
public sealed class SearchProfiles : Profile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SearchProfiles"/> class.
    /// </summary>
    public SearchProfiles()
    {
        CreateMap<SearchHit, SearchHitDto>().ConstructUsing(src => new SearchHitDto(
            src.FileId,
            src.Title,
            src.Mime,
            src.Snippet,
            src.Score,
            src.LastModifiedUtc));

        CreateMap<SearchHistoryEntryEntity, SearchHistoryEntry>()
            .ConvertUsing(src => new SearchHistoryEntry(
                src.Id,
                src.QueryText,
                src.Match,
                src.CreatedUtc,
                src.Executions,
                src.LastTotalHits,
                src.IsFuzzy));

        CreateMap<SearchFavoriteEntity, SearchFavoriteItem>()
            .ConvertUsing(src => new SearchFavoriteItem(
                src.Id,
                src.Name,
                src.QueryText,
                src.Match,
                src.Position,
                src.CreatedUtc,
                src.IsFuzzy));
    }
}
