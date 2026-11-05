namespace JobOrchestrator.BuildingBlocks;

/// <summary>Tipos de erro esperado. Cada um vira um status HTTP.</summary>
public enum ErrorType { Validation, NotFound, Conflict, Forbidden }

/// <summary>Um erro esperado: código estável para o cliente e mensagem em português para a tela.</summary>
public sealed record Error(string Code, string Message, ErrorType Type, IDictionary<string, string[]>? Fields = null)
{
    public static Error Validation(string code, string message, IDictionary<string, string[]>? fields = null) => new(code, message, ErrorType.Validation, fields);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}

/// <summary>Permite ao pipeline criar uma falha sem conhecer o tipo do resultado.</summary>
public interface IResultOf<TSelf> where TSelf : IResultOf<TSelf>
{
    bool IsSuccess { get; }
    static abstract TSelf Failure(Error error);
}

/// <summary>Resultado de uma operação: valor ou erro, sem usar exceção para fluxo normal.</summary>
public sealed class Result<T> : IResultOf<Result<T>>
{
    private Result(T? value, Error? error) { Value = value; Error = error; }
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Failure(Error error) => new(default, error);
    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
