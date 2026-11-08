using System.Diagnostics;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>
/// Comportamento do pipeline: registra só o nome do pedido, o tempo e, no rastro, o resultado: todo comando, toda falha com o código
/// e a consulta lenta. Dado pessoal nunca vai para o log.
/// </summary>
public sealed class LogBehavior<TReq, TRes>(ILogger<LogBehavior<TReq, TRes>> log, Trail? trail = null) : IPipelineBehavior<TReq, TRes> where TReq : notnull
{
    private static readonly bool IsCommand = typeof(TReq).GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var name = typeof(TReq).Name;
        TRes response;
        try { response = await next(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            trail?.Error(IsCommand ? "command" : "query", name, $"{ex.GetType().Name}: {ex.Message}", (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds, "exception");
            throw;
        }
        var elapsed = Stopwatch.GetElapsedTime(start);
        var ms = (int)elapsed.TotalMilliseconds;
        if (elapsed.TotalSeconds > 3) log.LogWarning("{Request} slow: {Ms} ms", name, ms);
        else log.LogDebug("{Request} took {Ms} ms", name, ms);
        if (response is IOutcome { IsSuccess: false, Error: { } error }) trail?.Warn(IsCommand ? "command" : "query", name, error.Message, ms, error.Code,
            error.Fields is { Count: > 0 } fields ? fields.ToDictionary(f => f.Key, f => (string?)string.Join(" ", f.Value)) : null);
        else if (IsCommand) trail?.Info("command", name, null, ms);
        return response;
    }
}

/// <summary>Comportamento do pipeline: roda os validadores e barra o pedido inválido antes do handler.</summary>
public sealed class ValidationBehavior<TReq, TRes>(IEnumerable<IValidator<TReq>> validators) : IPipelineBehavior<TReq, TRes>
    where TReq : notnull where TRes : IResultOf<TRes>
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var v in validators) failures.AddRange((await v.ValidateAsync(req, ct)).Errors);
        if (failures.Count == 0) return await next();
        var fields = failures.GroupBy(f => f.PropertyName).ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());
        return TRes.Failure(Error.Validation("invalid-data", "Confira os dados enviados.", fields));
    }
}
