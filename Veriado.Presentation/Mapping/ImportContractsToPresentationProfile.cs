using System;
using System.Collections.Generic;
using AutoMapper;
using Veriado.Contracts.Import;
using Veriado.Presentation.Models.Import;

namespace Veriado.Presentation.Mapping;

public sealed class ImportContractsToPresentationProfile : Profile
{
    public ImportContractsToPresentationProfile()
    {
        CreateMap<ImportFolderRequest, ImportFolderRequestModel>().ReverseMap();
        CreateMap<ImportError, ImportErrorModel>().ReverseMap();

        CreateMap<ImportBatchResult, ImportBatchResultModel>();
        CreateMap<ImportBatchResultModel, ImportBatchResult>()
            .ConstructUsing((source, context) =>
            {
                var errors = context.Mapper.Map<IReadOnlyList<ImportError>>(source.Errors)
                    ?? Array.Empty<ImportError>();
                return new ImportBatchResult(source.Total, source.Succeeded, source.Failed, errors);
            });
    }
}
