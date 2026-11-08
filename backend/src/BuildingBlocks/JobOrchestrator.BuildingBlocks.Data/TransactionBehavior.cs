using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobOrchestrator.BuildingBlocks.Data;

/// <summary>Abre a transação do comando, salva e confirma. O handler nunca abre transação (Unit of Work, Fowler).</summary>
public abstract class TransactionBehavior<TReq, TRes, TDb>(TDb db) : IPipelineBehavior<TReq, TRes>
    where TReq : notnull where TRes : IResultOf<TRes> where TDb : DbContext
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var response = await next();
            if (!response.IsSuccess)
            {
                await transaction.RollbackAsync(ct);
                return response;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return response;
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(ct);
            return TRes.Failure(Error.Conflict("conflict", "Este registro foi alterado por outra operação."));
        }
    }
}
