using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Application.Tests.Domain.Files;
using Veriado.Domain.FileSystem;
using Veriado.Domain.Metadata;
using Veriado.Domain.Search.Events;
using Veriado.Domain.ValueObjects;
using Veriado.Infrastructure.Events;
using Veriado.Infrastructure.Events.Handlers;
using Veriado.Infrastructure.Persistence.Interceptors;
using Veriado.Infrastructure.Persistence.Options;
using Veriado.Infrastructure.Search;
using Xunit;

namespace Veriado.Application.Tests.Infrastructure;

public sealed class DomainEventsInterceptorTests : IAsyncLifetime
{
    private readonly ServiceProvider _serviceProvider;
    private readonly SqliteConnection _connection;

    public DomainEventsInterceptorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TestSearchIndexCoordinator>();
        services.AddSingleton<ISearchIndexCoordinator>(sp => sp.GetRequiredService<TestSearchIndexCoordinator>());
        services.AddSingleton<AuditEventProjector>();
        services.AddSingleton<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton<DomainEventsInterceptor>();
        services.AddSingleton<IDomainEventHandler<SearchReindexRequested>, SearchReindexRequestedHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _connection = new SqliteConnection("Filename=:memory:");
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task SaveChanges_DispatchesDomainEventsAndClears()
    {
        await using var context = CreateContext();

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var fileSystem = CreateFileSystem();
        fileSystem.ClearDomainEvents();
        context.FileSystems.Add(fileSystem);

        var file = FileEntityFactory.CreateSample(fileSystem.Id);
        context.Files.Add(file);

        var coordinator = _serviceProvider.GetRequiredService<TestSearchIndexCoordinator>();
        coordinator.Reset();

        await context.SaveChangesAsync();

        Assert.Empty(file.DomainEvents);
        Assert.Equal((ulong)1, file.Version);

        var requests = coordinator.Requests;
        var request = Assert.Single(requests);
        Assert.Equal(file.Id, request.FileId);
        Assert.Equal(ReindexReason.Created, request.Reason);

        var eventLogs = await context.DomainEventLog.ToListAsync();
        Assert.Equal(3, eventLogs.Count);
        Assert.All(eventLogs, log => Assert.Equal(file.Id.ToString("D"), log.AggregateId));
    }

    [Fact]
    public async Task SubsequentSave_DoesNotRedispatchDomainEvents()
    {
        await using var context = CreateContext();

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var fileSystem = CreateFileSystem();
        fileSystem.ClearDomainEvents();
        context.FileSystems.Add(fileSystem);

        var file = FileEntityFactory.CreateSample(fileSystem.Id);
        context.Files.Add(file);

        var coordinator = _serviceProvider.GetRequiredService<TestSearchIndexCoordinator>();
        coordinator.Reset();

        await context.SaveChangesAsync();
        coordinator.Reset();
        var logCount = await context.DomainEventLog.CountAsync();

        await context.SaveChangesAsync();

        Assert.Equal(logCount, await context.DomainEventLog.CountAsync());
        Assert.Empty(coordinator.Requests);
        Assert.Empty(file.DomainEvents);
    }

    [Fact]
    public async Task FailingHandler_RollsBackTransactionAndAllowsRetry()
    {
        await using var context = CreateContext();

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var fileSystem = CreateFileSystem();
        fileSystem.ClearDomainEvents();
        context.FileSystems.Add(fileSystem);

        var file = FileEntityFactory.CreateSample(fileSystem.Id);
        context.Files.Add(file);

        var coordinator = _serviceProvider.GetRequiredService<TestSearchIndexCoordinator>();
        coordinator.Reset();
        coordinator.ShouldThrow = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        await using (var verificationContext = CreateContext())
        {
            Assert.Equal(0, await verificationContext.Files.CountAsync());
            Assert.Equal(0, await verificationContext.DomainEventLog.CountAsync());
            Assert.Equal(0, await verificationContext.ReindexQueue.CountAsync());
        }

        Assert.NotEmpty(file.DomainEvents);

        coordinator.ShouldThrow = false;
        await context.SaveChangesAsync();

        Assert.Empty(file.DomainEvents);
        Assert.Equal((ulong)1, file.Version);

        await using (var verificationContext = CreateContext())
        {
            Assert.Equal(1, await verificationContext.Files.CountAsync());
            Assert.Equal(3, await verificationContext.DomainEventLog.CountAsync());
            Assert.Equal(1, await verificationContext.ReindexQueue.CountAsync());
        }
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_serviceProvider.GetRequiredService<DomainEventsInterceptor>())
            .Options;

        var infrastructureOptions = new InfrastructureOptions { DbPath = ":memory:" };
        var logger = _serviceProvider.GetRequiredService<ILogger<AppDbContext>>();
        return new AppDbContext(options, infrastructureOptions, logger);
    }

    private static FileSystemEntity CreateFileSystem()
    {
        return FileSystemEntity.CreateNew(
            StorageProvider.Local,
            StoragePath.From("/content/sample.bin"),
            FileHash.From(new string('A', 64)),
            ByteSize.From(1024),
            MimeType.From("application/octet-stream"),
            FileAttributesFlags.None,
            ownerSid: null,
            isEncrypted: false,
            createdUtc: UtcTimestamp.From(DateTimeOffset.UtcNow),
            lastWriteUtc: UtcTimestamp.From(DateTimeOffset.UtcNow),
            lastAccessUtc: UtcTimestamp.From(DateTimeOffset.UtcNow));
    }

    private sealed class TestSearchIndexCoordinator : ISearchIndexCoordinator
    {
        private readonly List<(Guid FileId, ReindexReason Reason, DateTimeOffset When)> _requests = new();

        public IReadOnlyList<(Guid FileId, ReindexReason Reason, DateTimeOffset When)> Requests => _requests;

        public bool ShouldThrow { get; set; }

        public Task EnqueueAsync(DbContext dbContext, Guid fileId, ReindexReason reason, DateTimeOffset requestedUtc, CancellationToken cancellationToken)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Coordinator failure");
            }

            _requests.Add((fileId, reason, requestedUtc));
            return Task.CompletedTask;
        }

        public void Reset()
        {
            _requests.Clear();
            ShouldThrow = false;
        }
    }
}
