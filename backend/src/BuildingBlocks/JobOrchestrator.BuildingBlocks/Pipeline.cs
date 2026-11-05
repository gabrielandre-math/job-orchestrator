using System.Diagnostics;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>Comportamento do pipeline: registra só o nome do pedido e o tempo. Dado pessoal nunca vai para o log.</summary>
public sealed class LogBehavior<TReq, TRes>(ILogger<LogBehavior<TReq, TRes>> log) : IPipelineBehavior<TReq, TRes> where TReq : notnull
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var response = await next(ct);
        var elapsed = Stopwatch.GetElapsedTime(start);
        if (elapsed.TotalSeconds > 3) log.LogWarning("{Request} slow: {Ms} ms", typeof(TReq).Name, (int)elapsed.TotalMilliseconds);
        else log.LogDebug("{Request} took {Ms} ms", typeof(TReq).Name, (int)elapsed.TotalMilliseconds);
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
        if (failures.Count == 0) return await next(ct);
        var fields = failures.GroupBy(f => f.PropertyName).ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());
        return TRes.Failure(Error.Validation("invalid-data", "Confira os dados enviados.", fields));
    }
}
