using System;
using System.Collections.Generic;
using AutoMapper;
using Veriado.Contracts.Common;
using Veriado.Presentation.Models.Common;

namespace Veriado.Presentation.Mapping;

internal sealed class PageResultToModelConverter<TSource, TDestination> :
    ITypeConverter<PageResult<TSource>, PageResultModel<TDestination>>
{
    public PageResultModel<TDestination> Convert(
        PageResult<TSource> source,
        PageResultModel<TDestination> destination,
        ResolutionContext context)
    {
        destination ??= new PageResultModel<TDestination>();

        var items = context.Mapper.Map<IReadOnlyList<TDestination>>(source.Items)
            ?? Array.Empty<TDestination>();

        destination.Items = items;
        destination.Page = source.Page;
        destination.PageSize = source.PageSize;
        destination.TotalCount = source.TotalCount;

        return destination;
    }
}

internal sealed class PageResultModelToDtoConverter<TSource, TDestination> :
    ITypeConverter<PageResultModel<TSource>, PageResult<TDestination>>
{
    public PageResult<TDestination> Convert(
        PageResultModel<TSource> source,
        PageResult<TDestination> destination,
        ResolutionContext context)
    {
        var items = context.Mapper.Map<IReadOnlyList<TDestination>>(source.Items)
            ?? Array.Empty<TDestination>();

        return new PageResult<TDestination>(items, source.Page, source.PageSize, source.TotalCount);
    }
}
