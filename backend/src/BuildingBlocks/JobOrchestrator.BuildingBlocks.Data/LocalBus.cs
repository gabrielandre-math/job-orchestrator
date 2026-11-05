using Microsoft.Extensions.DependencyInjection;

namespace JobOrchestrator.BuildingBlocks.Data;

/// <summary>
/// Barramento local: entrega a mensagem aos consumidores do mesmo processo, cada um no seu escopo.
/// A outbox só marca como publicada depois que todos trataram, e a inbox de cada consumidor evita efeito repetido.
/// </summary>
public sealed class LocalBus(IServiceScopeFactory scopes) : IMessageBus
{
    public async Task PublishAsync(Envelope message, CancellationToken ct)
    {
        await using var probe = scopes.CreateAsyncScope();
        var count = probe.ServiceProvider.GetServices<IEventConsumer>().Count(c => c.EventType == message.EventType);
        for (var i = 0; i < count; i++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var consumer = scope.ServiceProvider.GetServices<IEventConsumer>().Where(c => c.EventType == message.EventType).ElementAt(i);
            await consumer.ConsumeAsync(message, ct);
        }
    }
}
