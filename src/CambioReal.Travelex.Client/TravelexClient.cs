using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CambioReal.Travelex.Http;
using CambioReal.Travelex.Models;
using CambioReal.Travelex.Serialization;
using Microsoft.Extensions.Options;

namespace CambioReal.Travelex;

/// <summary>
/// Cliente HTTP da Travelex Corebanking API (mTLS + Basic→JWT encapsulados nos pipelines).
/// A maioria das chamadas carrega <c>agencia</c>/<c>conta</c> como query params — injetados a
/// partir das options.
/// </summary>
public sealed class TravelexClient
{
    private readonly HttpClient httpClient;
    private readonly TravelexOptions options;

    /// <summary>Cria o cliente sobre um <see cref="HttpClient"/> já configurado (mTLS + auth).</summary>
    public TravelexClient(HttpClient httpClient, IOptions<TravelexOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        this.httpClient = httpClient;
        this.options = options.Value;
    }

    /// <summary>Chave PIX recebedora configurada (payin).</summary>
    public string PixKey => options.PixKey;

    private string AccountQuery =>
        $"agencia={Uri.EscapeDataString(options.BranchNumber)}&conta={Uri.EscapeDataString(options.AccountNumber)}";

    /// <summary>Saldo da conta. <c>GET v1/saldo</c> — validado ao vivo (2026-07-15, saldo real).</summary>
    public Task<SaldoResponse> GetBalanceAsync(CancellationToken cancellationToken = default) =>
        GetAsync<SaldoResponse>($"v1/saldo?{AccountQuery}", cancellationToken);

    /// <summary>
    /// Cria/atualiza uma cob PIX (payin). <c>PUT v1/cob/{txId}</c>. Não financeiro até pago;
    /// expira via <c>calendario.expiracao</c> (cleanup natural).
    /// </summary>
    public Task<CobResponse> PutCobAsync(string txId, CreateCobRequest request, CancellationToken cancellationToken = default)
    {
        var content = JsonContent.Create(request, options: TravelexJson.Options);
        return SendAsync<CobResponse>(HttpMethod.Put, $"v1/cob/{Uri.EscapeDataString(txId)}", content, cancellationToken);
    }

    /// <summary>Consulta uma cob. <c>GET v1/cob/{txId}</c> — fonte de verdade do payin (<c>CONCLUIDA</c> = pago). Validado ao vivo (404 de domínio p/ fictício).</summary>
    public Task<CobResponse> GetCobAsync(string txId, CancellationToken cancellationToken = default) =>
        GetAsync<CobResponse>($"v1/cob/{Uri.EscapeDataString(txId)}?{AccountQuery}", cancellationToken);

    /// <summary>
    /// Devolução (refund) de payin. <c>POST v1/cob/{endToEndId}/devolucao/{agencia}/{conta}</c>.
    /// **FINANCIAL-WRITE** — não executar sem autorização explícita (goal §0.5).
    /// </summary>
    public Task<JsonElement> CreateDevolucaoAsync(
        string endToEndId, CreateDevolucaoRequest request, CancellationToken cancellationToken = default)
    {
        var content = JsonContent.Create(request, options: TravelexJson.Options);
        var path = $"v1/cob/{Uri.EscapeDataString(endToEndId)}/devolucao/"
            + $"{Uri.EscapeDataString(options.BranchNumber)}/{Uri.EscapeDataString(options.AccountNumber)}";
        return SendAsync<JsonElement>(HttpMethod.Post, path, content, cancellationToken);
    }

    /// <summary>
    /// Cria um payout PIX por chave. <c>POST v1/payout/{txId}</c> (única modalidade — o legado
    /// rejeita payout por conta: "Travelex não suporta pagamento via conta bancária").
    /// **FINANCIAL-WRITE.**
    /// </summary>
    public Task<PayoutResponse> CreatePayoutAsync(
        string txId, CreatePayoutRequest request, CancellationToken cancellationToken = default)
    {
        var content = JsonContent.Create(request, options: TravelexJson.Options);
        return SendAsync<PayoutResponse>(HttpMethod.Post, $"v1/payout/{Uri.EscapeDataString(txId)}", content, cancellationToken);
    }

    /// <summary>Consulta um payout. <c>GET v1/payout/{txId}</c> — statuses <c>APROVADO</c>/<c>CRIADO</c>/… (fonte de verdade).</summary>
    public Task<PayoutResponse> GetPayoutAsync(string txId, CancellationToken cancellationToken = default) =>
        GetAsync<PayoutResponse>($"v1/payout/{Uri.EscapeDataString(txId)}?{AccountQuery}", cancellationToken);

    private Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken) =>
        SendAsync<TResponse>(HttpMethod.Get, path, content: null, cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = content,
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await ThrowIfUnsuccessfulAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<TResponse>(TravelexJson.Options, cancellationToken);

        return payload ?? throw new TravelexApiException(
            response.StatusCode,
            errorType: null,
            errorCode: null,
            "A Travelex devolveu um corpo JSON vazio onde um valor era esperado.",
            requestId: null,
            responseBody: null);
    }

    private static async Task ThrowIfUnsuccessfulAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var (title, detail) = TryExtractError(body);
        var message = $"A Travelex respondeu HTTP {(int)response.StatusCode} ({response.StatusCode})"
            + (detail is null && title is null ? "." : $": {detail ?? title}.");

        throw response.StatusCode == HttpStatusCode.Unauthorized
            ? new TravelexAuthenticationException(response.StatusCode, null, title, message, null, body)
            : new TravelexApiException(response.StatusCode, null, title, message, null, body);
    }

    /// <summary>
    /// Erros vêm em RFC 7807-style (<c>{type, title, status, detail, instance, properties}</c> —
    /// confirmado ao vivo 2026-07-15) ou <c>{message}</c>.
    /// </summary>
    private static (string? Title, string? Detail) TryExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            string? Get(string name) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

            return (Get("title") ?? Get("message"), Get("detail"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
