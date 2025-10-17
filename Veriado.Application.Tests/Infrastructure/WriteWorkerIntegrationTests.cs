using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Appl.Abstractions;
using Veriado.Domain.Files;
using Veriado.Domain.Primitives;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Concurrency;
using Veriado.Infrastructure.DependencyInjection;
using Veriado.Infrastructure.Persistence;
using Veriado.Infrastructure.Persistence.Options;
using Xunit;

namespace Veriado.Application.Tests.Infrastructure;

public sealed class WriteWorkerIntegrationTests
{
    private static readonly Type WriteRequestType = typeof(WriteWorker).Assembly.GetType("Veriado.Infrastructure.Concurrency.WriteRequest")!;
    private static readonly MethodInfo ProcessBatchMethod = typeof(WriteWorker).GetMethod(
        "ProcessBatchAttemptAsync",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task ProcessBatch_CommitsAndIndexes()
    {
        var databasePath = CreateDatabasePath();
        var provider = await BuildProviderAsync(databasePath);
        try
        {
            var worker = ActivatorUtilities.CreateInstance<WriteWorker>(provider, 0);
            var request = CreateWriteRequest((context, token) =>
            {
                var file = CreateTestFile();
                context.Files.Add(file);
                return Task.FromResult<object?>(file.Id);
            });

            var batch = CreateBatch(request.Instance);
            await InvokeProcessBatchAsync(worker, batch, CancellationToken.None);

            var fileId = Assert.IsType<Guid>(await request.Task.ConfigureAwait(false));

            await using var scope = provider.CreateAsyncScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await dbContextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            var stored = await context.Files.SingleAsync(entity => entity.Id == fileId).ConfigureAwait(false);

            Assert.False(stored.SearchIndex.IsStale);
            Assert.NotNull(stored.SearchIndex.LastIndexedUtc);
        }
        finally
        {
            await CleanupDatabaseAsync(provider, databasePath).ConfigureAwait(false);
        }
    }

    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task ProcessBatch_RollsBackOnFailure()
    {
        var databasePath = CreateDatabasePath();
        var provider = await BuildProviderAsync(databasePath);
        try
        {
            var worker = ActivatorUtilities.CreateInstance<WriteWorker>(provider, 0);
            var request = CreateWriteRequest((context, token) =>
            {
                var file = CreateTestFile();
                context.Files.Add(file);
                throw new InvalidOperationException("Simulated failure");
            });

            var batch = CreateBatch(request.Instance);
            await InvokeProcessBatchAsync(worker, batch, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await request.Task.ConfigureAwait(false));

            await using var scope = provider.CreateAsyncScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await dbContextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.Empty(await context.Files.ToListAsync().ConfigureAwait(false));
        }
        finally
        {
            await CleanupDatabaseAsync(provider, databasePath).ConfigureAwait(false);
        }
    }

    [Fact]
    [Trait("Category", "SQLiteOnly")]
    public async Task ProcessBatch_RetriesWhenDatabaseIsLocked()
    {
        var databasePath = CreateDatabasePath();
        var provider = await BuildProviderAsync(databasePath);
        try
        {
            var worker = ActivatorUtilities.CreateInstance<WriteWorker>(provider, 0);
            var request = CreateWriteRequest((context, token) =>
            {
                var file = CreateTestFile();
                context.Files.Add(file);
                return Task.FromResult<object?>(file.Id);
            });

            var options = provider.GetRequiredService<InfrastructureOptions>();
            await using var blockingConnection = new SqliteConnection(options.ConnectionString);
            await blockingConnection.OpenAsync().ConfigureAwait(false);
            await using (var command = blockingConnection.CreateCommand())
            {
                command.CommandText = "BEGIN IMMEDIATE;";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            var batch = CreateBatch(request.Instance);
            var processingTask = InvokeProcessBatchAsync(worker, batch, CancellationToken.None);

            await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            await using (var release = blockingConnection.CreateCommand())
            {
                release.CommandText = "ROLLBACK;";
                await release.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await processingTask.ConfigureAwait(false);
            var fileId = Assert.IsType<Guid>(await request.Task.ConfigureAwait(false));

            await using var scope = provider.CreateAsyncScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var context = await dbContextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            var stored = await context.Files.SingleAsync(entity => entity.Id == fileId).ConfigureAwait(false);
            Assert.False(stored.SearchIndex.IsStale);
        }
        finally
        {
            await CleanupDatabaseAsync(provider, databasePath).ConfigureAwait(false);
        }
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "veriado-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"infrastructure-{Guid.NewGuid():N}.db");
    }

    private static FileEntity CreateTestFile()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("hello world");
        var size = ByteSize.From(content.LongLength);
        var hash = FileHash.Compute(content);
        return FileEntity.CreateNew(
            FileName.From("sample"),
            FileExtension.From("txt"),
            MimeType.From("text/plain"),
            "tester",
            Guid.NewGuid(),
            hash,
            size,
            UtcTimestamp.From(DateTimeOffset.UtcNow));
    }

    private static async Task<ServiceProvider> BuildProviderAsync(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddFilter(_ => false));
        services.AddInfrastructure(options =>
        {
            options.DbPath = databasePath;
            options.BatchMaxItems = 8;
            options.BatchMaxWindowMs = 25;
        });

        var provider = services.BuildServiceProvider();
        await provider.InitializeInfrastructureAsync().ConfigureAwait(false);
        return provider;
    }

    private static Array CreateBatch(object writeRequest)
    {
        var batch = Array.CreateInstance(WriteRequestType, 1);
        batch.SetValue(writeRequest, 0);
        return batch;
    }

    private static (object Instance, Task<object?> Task) CreateWriteRequest(Func<AppDbContext, CancellationToken, Task<object?>> work)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = Activator.CreateInstance(
            WriteRequestType,
            work,
            completion,
            null,
            CancellationToken.None,
            null)!;
        return (request, completion.Task);
    }

    private static async Task InvokeProcessBatchAsync(WriteWorker worker, Array batch, CancellationToken cancellationToken)
    {
        var task = (Task)ProcessBatchMethod.Invoke(worker, new object[] { batch, cancellationToken })!;
        await task.ConfigureAwait(false);
    }

    private static async Task CleanupDatabaseAsync(ServiceProvider provider, string databasePath)
    {
        await provider.DisposeAsync().ConfigureAwait(false);
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
