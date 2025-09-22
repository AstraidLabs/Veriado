using AutoMapper;
using Veriado.Contracts.Search;
using Veriado.Presentation.Models.Search;

namespace Veriado.Presentation.Mapping;

public sealed class SearchContractsToPresentationProfile : Profile
{
    public SearchContractsToPresentationProfile()
    {
        CreateMap<SearchHistoryEntry, SearchHistoryEntryModel>().ReverseMap();
        CreateMap<SearchFavoriteItem, SearchFavoriteItemModel>().ReverseMap();
        CreateMap<SearchFavoriteDefinition, SearchFavoriteDefinitionModel>().ReverseMap();
    }
}
