namespace JobOrchestrator.BuildingBlocks;

/// <summary>A mensagem como viaja entre módulos: id, tipo e conteúdo em JSON.</summary>
public sealed record Envelope(Guid MessageId, string EventType, string Content);

/// <summary>Porta para publicar mensagens. O domínio não sabe como elas chegam ao outro módulo.</summary>
public interface IMessageBus
{
    Task PublishAsync(Envelope message, CancellationToken ct);
}

/// <summary>Quem trata um tipo de evento vindo de outro módulo.</summary>
public interface IEventConsumer
{
    string EventType { get; }
    Task ConsumeAsync(Envelope message, CancellationToken ct);
}
