namespace JobOrchestrator.Automation.Adapters;

/// <summary>Circuito aberto e tempo esgotado viram HttpRequestException: quem consulta a fonte já trata "não respondeu" sem derrubar a rodada.</summary>
internal sealed class ResilienceErrors : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await base.SendAsync(request, ct); }
        catch (Polly.ExecutionRejectedException ex)
        {
            throw new HttpRequestException(ex is Polly.CircuitBreaker.BrokenCircuitException
                ? $"{request.RequestUri?.Host} em pausa depois de falhas seguidas; volta sozinho em 2 minutos."
                : $"{request.RequestUri?.Host} não respondeu a tempo.", ex);
        }
    }
}
