using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Veriado.Application.Abstractions;
using Veriado.Domain.Audit;
using Veriado.Domain.Files.Events;
using Veriado.Domain.Primitives;
using Veriado.Infrastructure.Persistence;

namespace Veriado.Infrastructure.Events;

/// <summary>
/// Persists domain events raised by file aggregates into the audit tables.
/// </summary>
internal sealed class AuditEventPublisher : IEventPublisher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditEventPublisher> _logger;

    public AuditEventPublisher(IServiceScopeFactory scopeFactory, ILogger<AuditEventPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PublishAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken)
    {
        if (events is null || events.Count == 0)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var hasChanges = false;

        foreach (var domainEvent in events)
        {
            switch (domainEvent)
            {
                case FileCreated created:
                    context.FileAudits.Add(FileAuditEntity.Created(created.FileId, created.Name));
                    hasChanges = true;
                    break;

                case FileRenamed renamed:
                    context.FileAudits.Add(FileAuditEntity.Renamed(renamed.FileId, renamed.OldName, renamed.NewName));
                    hasChanges = true;
                    break;

                case FileMetadataUpdated metadata:
                    context.FileAudits.Add(FileAuditEntity.MetadataUpdated(metadata.FileId, metadata.Mime, metadata.Author));
                    hasChanges = true;
                    break;

                case FileReadOnlyChanged readOnlyChanged:
                    context.FileAudits.Add(FileAuditEntity.ReadOnlyChanged(readOnlyChanged.FileId, readOnlyChanged.IsReadOnly));
                    hasChanges = true;
                    break;

                case FileContentReplaced contentReplaced:
                    context.FileContentAudits.Add(FileContentAuditEntity.Replaced(contentReplaced.FileId, contentReplaced.Hash));
                    hasChanges = true;
                    break;

                case FileValidityChanged validityChanged:
                    context.FileValidityAudits.Add(FileDocumentValidityAuditEntity.Changed(
                        validityChanged.FileId,
                        validityChanged.IssuedAt,
                        validityChanged.ValidUntil,
                        validityChanged.HasPhysicalCopy,
                        validityChanged.HasElectronicCopy));
                    hasChanges = true;
                    break;

                default:
                    _logger.LogDebug("No audit projection configured for domain event {EventType}", domainEvent.GetType().Name);
                    break;
            }
        }

        if (hasChanges)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
