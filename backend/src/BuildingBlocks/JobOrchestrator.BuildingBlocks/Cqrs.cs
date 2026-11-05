using MediatR;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>Comando (CQRS): pede para mudar o estado.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Consulta (CQRS): só lê, nunca muda o estado.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Quem executa um comando.</summary>
public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>> where TCommand : ICommand<TResponse>;

/// <summary>Quem executa uma consulta.</summary>
public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>> where TQuery : IQuery<TResponse>;

/// <summary>Resposta vazia de um comando que só muda estado.</summary>
public readonly record struct Done;
