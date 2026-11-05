namespace JobOrchestrator.Mail.Features;

/// <summary>No Worker: a cada 5 minutos o automático escreve e envia dentro dos limites, os lembretes vencidos viram rascunho e a fila agendada sai; a cada 10, lê a caixa.</summary>
internal sealed class MailWorker(IServiceScopeFactory scopes, ILogger<MailWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(40), stop);
        var tick = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var mail = scope.ServiceProvider.GetRequiredService<MailService>();
                await mail.TickAsync(stop);
                if (tick++ % 2 == 0) await mail.PollAsync(stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Mail tick failed: {Error}", ex.Message); }
            await Task.Delay(TimeSpan.FromMinutes(5), stop);
        }
    }
}
