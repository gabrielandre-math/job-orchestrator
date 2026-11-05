using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JobOrchestrator.BuildingBlocks.Data;

/// <summary>Dispara os eventos de domínio antes do commit: quem trata grava na mesma transação.</summary>
public sealed class DispatchEventsInterceptor(IPublisher publisher) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (data.Context is { } context)
        {
            var aggregates = context.ChangeTracker.Entries<IAggregate>().Select(e => e.Entity).Where(a => a.Events.Count > 0).ToList();
            var events = aggregates.SelectMany(a => a.Events).ToList();
            aggregates.ForEach(a => a.ClearEvents());
            foreach (var evt in events) await publisher.Publish(evt, ct);
        }
        return await base.SavingChangesAsync(data, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result) =>
        throw new NotSupportedException("Use SaveChangesAsync.");
}
