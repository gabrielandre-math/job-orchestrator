using Dapper;
using Npgsql;

namespace JobOrchestrator.BuildingBlocks.Data;

/// <summary>Lado de leitura do módulo (CQRS): Dapper com SQL escrito à mão. Os comandos continuam com EF Core e o agregado.</summary>
public interface IReadDb<TModule>
{
    Task<IReadOnlyList<T>> ListAsync<T>(string sql, object? args, CancellationToken ct);
    Task<T?> FirstAsync<T>(string sql, object? args, CancellationToken ct);
}

/// <summary>Implementação com Dapper. Abre a conexão do pool e devolve na hora.</summary>
internal sealed class ReadDb<TModule>(NpgsqlDataSource source) : IReadDb<TModule>, IAsyncDisposable
{
    public async Task<IReadOnlyList<T>> ListAsync<T>(string sql, object? args, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        return (await connection.QueryAsync<T>(new CommandDefinition(sql, args, cancellationToken: ct))).AsList();
    }

    public async Task<T?> FirstAsync<T>(string sql, object? args, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        return await connection.QueryFirstOrDefaultAsync<T>(new CommandDefinition(sql, args, cancellationToken: ct));
    }

    public ValueTask DisposeAsync() => source.DisposeAsync();
}

