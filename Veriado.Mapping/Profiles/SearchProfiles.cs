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
        CreateMap<SearchHit, SearchHitDto>();

        CreateMap<SearchHistoryEntryEntity, SearchHistoryEntry>()
            .ForCtorParam(nameof(SearchHistoryEntry.Id), opt => opt.MapFrom(static src => src.Id))
            .ForCtorParam(nameof(SearchHistoryEntry.QueryText), opt => opt.MapFrom(static src => src.QueryText))
            .ForCtorParam(nameof(SearchHistoryEntry.MatchQuery), opt => opt.MapFrom(static src => src.Match))
            .ForCtorParam(nameof(SearchHistoryEntry.LastQueriedUtc), opt => opt.MapFrom(static src => src.CreatedUtc))
            .ForCtorParam(nameof(SearchHistoryEntry.Executions), opt => opt.MapFrom(static src => src.Executions))
            .ForCtorParam(nameof(SearchHistoryEntry.LastTotalHits), opt => opt.MapFrom(static src => src.LastTotalHits))
            .ForCtorParam(nameof(SearchHistoryEntry.IsFuzzy), opt => opt.MapFrom(static src => src.IsFuzzy));

        CreateMap<SearchFavoriteEntity, SearchFavoriteItem>()
            .ForCtorParam(nameof(SearchFavoriteItem.Id), opt => opt.MapFrom(static src => src.Id))
            .ForCtorParam(nameof(SearchFavoriteItem.Name), opt => opt.MapFrom(static src => src.Name))
            .ForCtorParam(nameof(SearchFavoriteItem.QueryText), opt => opt.MapFrom(static src => src.QueryText))
            .ForCtorParam(nameof(SearchFavoriteItem.MatchQuery), opt => opt.MapFrom(static src => src.Match))
            .ForCtorParam(nameof(SearchFavoriteItem.Position), opt => opt.MapFrom(static src => src.Position))
            .ForCtorParam(nameof(SearchFavoriteItem.CreatedUtc), opt => opt.MapFrom(static src => src.CreatedUtc))
            .ForCtorParam(nameof(SearchFavoriteItem.IsFuzzy), opt => opt.MapFrom(static src => src.IsFuzzy));
    }
}
