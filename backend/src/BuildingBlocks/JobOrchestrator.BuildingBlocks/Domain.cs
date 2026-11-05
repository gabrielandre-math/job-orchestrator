using MediatR;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>Evento de domínio (Evans): algo que aconteceu dentro do módulo.</summary>
public interface IDomainEvent : INotification;

/// <summary>O que o interceptador precisa saber de um agregado.</summary>
public interface IAggregate
{
    IReadOnlyList<IDomainEvent> Events { get; }
    void ClearEvents();
}

/// <summary>Raiz de agregado (Evans): a única porta para mudar o agregado. Acumula os eventos.</summary>
public abstract class AggregateRoot : IAggregate
{
    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyList<IDomainEvent> Events => _events;
    protected void Register(IDomainEvent evt) => _events.Add(evt);
    public void ClearEvents() => _events.Clear();
}

/// <summary>Regra de domínio violada. Só aparece por bug: os handlers checam antes e devolvem Result.</summary>
public sealed class DomainRuleException(string message) : InvalidOperationException(message);
