using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace JobOrchestrator.BuildingBlocks.Data;

/// <summary>Registra o banco de cada módulo: EF Core para escrita e Dapper para leitura, cada um no seu schema.</summary>
public static class DataRegistration
{
    public static string ConnectionFor(this IConfiguration config, string module) =>
        config.GetConnectionString(module) ?? config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

    public static IServiceCollection AddModuleDatabase<TDb>(this IServiceCollection s, IConfiguration config, string module) where TDb : DbContext
    {
        var connection = config.ConnectionFor(module);
        s.TryAddScoped<DispatchEventsInterceptor>();
        s.AddDbContext<TDb>((sp, o) => o.UseModuleDatabase(connection, module.ToLowerInvariant())
            .AddInterceptors(sp.GetRequiredService<DispatchEventsInterceptor>()));
        // As colunas são snake_case e os records, PascalCase: isto faz o Dapper casar os nomes.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        s.AddSingleton<IReadDb<TDb>>(_ => new ReadDb<TDb>(NpgsqlDataSource.Create(connection)));
        return s;
    }

    /// <summary>Só o lado de leitura, para módulos sem eventos de domínio nem outbox.</summary>
    public static IServiceCollection AddModuleReadDb<TDb>(this IServiceCollection s, IConfiguration config, string module)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        s.AddSingleton<IReadDb<TDb>>(_ => new ReadDb<TDb>(NpgsqlDataSource.Create(config.ConnectionFor(module))));
        return s;
    }

    public static DbContextOptionsBuilder UseModuleDatabase(this DbContextOptionsBuilder o, string connectionString, string schema) =>
        o.UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__EFMigrationsHistory", schema)).UseSnakeCaseNamingConvention();
}
