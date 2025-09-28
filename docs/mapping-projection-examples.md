# AutoMapper projection examples

```csharp
// Registration
services.AddAutoMapper(cfg =>
{
    CommonValueConverters.Register(cfg);
    cfg.AddProfile<FileReadProfiles>();
    cfg.AddProfile<FileWriteProfiles>();
    cfg.AddProfile<SearchProfiles>();
});

var provider = services.BuildServiceProvider();
var mapperConfig = provider.GetRequiredService<AutoMapper.IConfigurationProvider>();
mapperConfig.AssertConfigurationIsValid();
mapperConfig.CompileMappings();
```

```csharp
// Query usage
var summaries = await db.Files
    .AsNoTracking()
    .ProjectTo<FileSummaryDto>(mapperConfig)
    .ToListAsync(cancellationToken);

var details = await db.Files
    .Where(f => f.Id == fileId)
    .AsNoTracking()
    .ProjectTo<FileDetailDto>(mapperConfig)
    .SingleAsync(cancellationToken);
```
