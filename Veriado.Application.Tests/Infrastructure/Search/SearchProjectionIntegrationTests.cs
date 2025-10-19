using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Appl.Common.Exceptions;
using Veriado.Application.Tests.Domain.FileSystem;
using Veriado.Application.Tests.Domain.Files;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Files;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Infrastructure.Persistence;
using Xunit;

namespace Veriado.Application.Tests.Infrastructure.Search;

public sealed class SearchProjectionIntegrationTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"veriado-search-{Guid.NewGuid():N}.db");
    private ServiceProvider? _serviceProvider;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options =>
        {
            options.DbPath = _databasePath;
            options.BatchMaxItems = 4;
            options.BatchMaxWindowMs = 10;
        });

        _serviceProvider = services.BuildServiceProvider();
        await _serviceProvider.InitializeInfrastructureAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync().ConfigureAwait(false);
        }

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task AnalyzerChange_InlineForceReplace()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<AppDbContext>();
        var repository = services.GetRequiredService<IFileRepository>();
        var unitOfWork = services.GetRequiredService<IFilePersistenceUnitOfWork>();
        var projectionScope = services.GetRequiredService<ISearchProjectionScope>();
        var projection = services.GetRequiredService<IFileSearchProjection>();
        var signatureCalculator = services.GetRequiredService<ISearchIndexSignatureCalculator>();
        var clock = services.GetRequiredService<IClock>();
        var mapper = new MapperConfiguration(cfg => { }).CreateMapper();
        var handler = new TestFileWriteHandler(
            repository,
            clock,
            mapper,
            unitOfWork,
            projectionScope,
            projection,
            signatureCalculator);

        var fileSystem = FileSystemEntityFactory.CreateSample();
        var file = FileEntityFactory.CreateSample(fileSystem.Id);

        await handler.PersistNewAsync(file, fileSystem, CancellationToken.None).ConfigureAwait(false);

        var initialToken = file.SearchIndex.TokenHash;
        Assert.NotNull(initialToken);

        var updateTimestamp = UtcTimestamp.From(DateTimeOffset.UtcNow.AddMinutes(1));
        var newContentHash = FileHash.From(new string('B', 64));
        var newVersion = file.LinkedContentVersion.Next();
        file.LinkTo(file.FileSystemId, newContentHash, ByteSize.From(2048), newVersion, MimeType.From("text/plain"), updateTimestamp);
        file.UpdateMetadata(MimeType.From("text/plain"), "Updated Author", updateTimestamp);

        var newSignature = signatureCalculator.Compute(file);
        Assert.NotNull(newSignature.TokenHash);
        Assert.NotEqual(initialToken, newSignature.TokenHash);

        var updateSql = "UPDATE search_document SET stored_content_hash = $hash, stored_token_hash = $token WHERE file_id = $id;";
        var idParameter = new SqliteParameter("$id", SqliteType.Blob) { Value = file.Id.ToByteArray() };
        var contentParameter = new SqliteParameter("$hash", SqliteType.Text) { Value = file.ContentHash.Value };
        var tokenParameter = new SqliteParameter("$token", SqliteType.Text)
        {
            Value = (object?)newSignature.TokenHash ?? DBNull.Value
        };
        await context.Database.ExecuteSqlRawAsync(updateSql, idParameter, contentParameter, tokenParameter).ConfigureAwait(false);

        await handler.PersistAsync(file, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(newSignature.TokenHash, file.SearchIndex.TokenHash);
        Assert.Equal(file.ContentHash.Value, file.SearchIndex.IndexedContentHash);

        await using var verificationScope = _serviceProvider.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var command = verificationContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT stored_content_hash, stored_token_hash FROM search_document WHERE file_id = $id;";
        command.Parameters.Add(new SqliteParameter("$id", SqliteType.Blob) { Value = file.Id.ToByteArray() });

        if (command.Connection!.State != ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            var storedContent = reader.IsDBNull(0) ? null : reader.GetString(0);
            var storedToken = reader.IsDBNull(1) ? null : reader.GetString(1);
            Assert.Equal(file.ContentHash.Value, storedContent);
            Assert.Equal(newSignature.TokenHash, storedToken);
        }

        var reloaded = await verificationContext.Files.AsNoTracking().SingleAsync(f => f.Id == file.Id).ConfigureAwait(false);
        Assert.Equal(file.ContentHash.Value, reloaded.SearchIndex.IndexedContentHash);
        Assert.Equal(newSignature.TokenHash, reloaded.SearchIndex.TokenHash);
    }

    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task ConcurrentUpdates_OlderLoses()
    {
        await using var scope = _serviceProvider!.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<AppDbContext>();
        var repository = services.GetRequiredService<IFileRepository>();
        var unitOfWork = services.GetRequiredService<IFilePersistenceUnitOfWork>();
        var projectionScope = services.GetRequiredService<ISearchProjectionScope>();
        var projection = services.GetRequiredService<IFileSearchProjection>();
        var signatureCalculator = services.GetRequiredService<ISearchIndexSignatureCalculator>();
        var clock = services.GetRequiredService<IClock>();
        var mapper = new MapperConfiguration(cfg => { }).CreateMapper();
        var handler = new TestFileWriteHandler(
            repository,
            clock,
            mapper,
            unitOfWork,
            projectionScope,
            projection,
            signatureCalculator);

        var fileSystem = FileSystemEntityFactory.CreateSample();
        var file = FileEntityFactory.CreateSample(fileSystem.Id);
        await handler.PersistNewAsync(file, fileSystem, CancellationToken.None).ConfigureAwait(false);

        var initialContentHash = file.ContentHash.Value;
        var initialTokenHash = file.SearchIndex.TokenHash;

        var newerTimestamp = UtcTimestamp.From(DateTimeOffset.UtcNow.AddMinutes(1));
        var newerHash = FileHash.From(new string('C', 64));
        var newerVersion = file.LinkedContentVersion.Next();
        file.LinkTo(file.FileSystemId, newerHash, ByteSize.From(4096), newerVersion, MimeType.From("application/xml"), newerTimestamp);
        file.UpdateMetadata(MimeType.From("application/xml"), "Newer Author", newerTimestamp);

        await handler.PersistAsync(file, CancellationToken.None).ConfigureAwait(false);

        var latestContentHash = file.ContentHash.Value;
        var latestTokenHash = file.SearchIndex.TokenHash;

        var olderTimestamp = UtcTimestamp.From(DateTimeOffset.UtcNow.AddMinutes(2));
        var olderHash = FileHash.From(new string('D', 64));
        var olderVersion = file.LinkedContentVersion.Next();
        file.LinkTo(file.FileSystemId, olderHash, ByteSize.From(8192), olderVersion, MimeType.From("application/json"), olderTimestamp);
        file.UpdateMetadata(MimeType.From("application/json"), "Legacy Author", olderTimestamp);
        var olderSignature = signatureCalculator.Compute(file);

        await using (var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false))
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            projectionScope.EnsureActive();
            await Assert.ThrowsAsync<StaleSearchProjectionUpdateException>(() => projection.UpsertAsync(
                    file,
                    initialContentHash,
                    initialTokenHash,
                    file.ContentHash.Value,
                    olderSignature.TokenHash,
                    projectionScope,
                    CancellationToken.None))
                .ConfigureAwait(false);
        }

        var storedRow = await context.Database
            .SqlQueryRaw<(string ContentHash, string TokenHash)>(
                "SELECT stored_content_hash, stored_token_hash FROM search_document WHERE file_id = $id",
                new SqliteParameter("$id", SqliteType.Blob) { Value = file.Id.ToByteArray() })
            .SingleAsync()
            .ConfigureAwait(false);

        Assert.Equal(latestContentHash, storedRow.ContentHash);
        Assert.Equal(latestTokenHash, storedRow.TokenHash);
    }

    private sealed class TestFileWriteHandler : FileWriteHandlerBase
    {
        public TestFileWriteHandler(
            IFileRepository repository,
            IClock clock,
            IMapper mapper,
            IFilePersistenceUnitOfWork unitOfWork,
            ISearchProjectionScope projectionScope,
            IFileSearchProjection searchProjection,
            ISearchIndexSignatureCalculator signatureCalculator)
            : base(repository, clock, mapper, unitOfWork, projectionScope, searchProjection, signatureCalculator)
        {
        }

        public Task PersistNewAsync(FileEntity file, FileSystemEntity fileSystem, CancellationToken cancellationToken)
            => PersistNewAsync(file, fileSystem, FilePersistenceOptions.Default, cancellationToken);

        public Task PersistAsync(FileEntity file, CancellationToken cancellationToken)
            => PersistAsync(file, FilePersistenceOptions.Default, cancellationToken);
    }
}
