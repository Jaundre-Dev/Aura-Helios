using Helios.Contracts.Events;

namespace Helios.Application.Abstractions.Messaging;

/// <summary>
/// Every important action emits an event (plan section 28.1). Publishing fans out
/// to workers, observability, notifications and SignalR.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(HeliosEvent heliosEvent, CancellationToken cancellationToken);
}
