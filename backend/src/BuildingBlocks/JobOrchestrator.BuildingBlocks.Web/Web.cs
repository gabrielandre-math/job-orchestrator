using System.Reflection;
using Carter;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JobOrchestrator.BuildingBlocks.Web;

/// <summary>Converte Result em resposta HTTP.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttp<T>(this Result<T> r, Func<T, IResult> ok) => r.IsSuccess ? ok(r.Value!) : r.Error!.ToProblem();

    /// <summary>RFC 9457: toda falha esperada sai como Problem Details.</summary>
    public static IResult ToProblem(this Error e)
    {
        var extras = new Dictionary<string, object?> { ["code"] = e.Code };
        if (e.Fields is not null) return Results.ValidationProblem(e.Fields, e.Message, title: "Dados inválidos", extensions: extras);
        var status = e.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return Results.Problem(detail: e.Message, statusCode: status, title: e.Code, extensions: extras);
    }
}

/// <summary>Encontra as rotas Carter de todos os módulos.</summary>
public static class WebRegistration
{
    public static IServiceCollection AddJoWeb(this IServiceCollection s, params Assembly[] assemblies)
    {
        var modules = assemblies.SelectMany(a => a.GetTypes()).Where(t => !t.IsAbstract && typeof(ICarterModule).IsAssignableFrom(t)).ToArray();
        s.AddCarter(configurator: c => c.WithModules(modules));
        return s;
    }
}
