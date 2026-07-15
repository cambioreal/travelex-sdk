using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CambioReal.Travelex.Http;
using Microsoft.Extensions.Options;

namespace CambioReal.Travelex.Auth;

/// <summary>Fornece o token JWT da Travelex, com cache e renovação sob demanda.</summary>
public interface ITravelexTokenProvider
{
    /// <summary>
    /// Devolve um token válido. <paramref name="invalidatedToken"/> força renovação quando o
    /// token informado (recebido num 401) ainda for o corrente.
    /// </summary>
    public ValueTask<string> GetTokenAsync(string? invalidatedToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>POST /auth</c> com Basic (username/password) sobre mTLS — a resposta é o **JWT cru no
/// corpo** (não JSON estruturado; confirmado ao vivo 2026-07-15 e no legado
/// <c>AbstractService::authenticate</c>). TTL não estruturado — usa
/// <see cref="TravelexOptions.TokenLifetime"/> (3600s, paridade com o legado) − skew.
/// Singleton, single-flight.
/// </summary>
internal sealed class TravelexTokenProvider : ITravelexTokenProvider, IDisposable
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly TravelexOptions options;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private (string Value, DateTimeOffset ExpiresAtUtc)? cached;

    public TravelexTokenProvider(IHttpClientFactory httpClientFactory, IOptions<TravelexOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.httpClientFactory = httpClientFactory;
        this.options = options.Value;
        this.timeProvider = timeProvider;
    }

    public async ValueTask<string> GetTokenAsync(string? invalidatedToken, CancellationToken cancellationToken = default)
    {
        if (TryUseCached(invalidatedToken, out var token))
        {
            return token;
        }

        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (TryUseCached(invalidatedToken, out token))
            {
                return token;
            }

            var fresh = await RequestTokenAsync(cancellationToken);
            cached = fresh;
            return fresh.Value;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public void Dispose() => refreshGate.Dispose();

    private bool TryUseCached(string? invalidatedToken, out string token)
    {
        token = string.Empty;
        var current = cached;

        if (current is null
            || (invalidatedToken is not null && string.Equals(current.Value.Value, invalidatedToken, StringComparison.Ordinal))
            || timeProvider.GetUtcNow() >= current.Value.ExpiresAtUtc)
        {
            return false;
        }

        token = current.Value.Value;
        return true;
    }

    private async Task<(string Value, DateTimeOffset ExpiresAtUtc)> RequestTokenAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(TravelexClientNames.Auth);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("auth", UriKind.Relative))
        {
            Content = new StringContent(string.Empty),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));

        var issuedAt = timeProvider.GetUtcNow();

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new TravelexAuthenticationException(
                response.StatusCode,
                errorType: null,
                errorCode: null,
                $"Falha ao autenticar na Travelex (HTTP {(int)response.StatusCode}).",
                requestId: null,
                errorBody);
        }

        // Corpo é o JWT cru (possivelmente entre aspas se serializado como string JSON).
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = raw.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new TravelexAuthenticationException("A Travelex devolveu um token vazio em POST /auth.");
        }

        var skew = options.TokenExpirationSkew >= options.TokenLifetime
            ? TimeSpan.FromTicks(options.TokenLifetime.Ticks / 2)
            : options.TokenExpirationSkew;

        return (token, issuedAt + options.TokenLifetime - skew);
    }
}

/// <summary>
/// Injeta <c>Authorization: Bearer {jwt}</c>, renovando uma única vez em 401.
/// </summary>
internal sealed class TravelexAuthenticationHandler : DelegatingHandler
{
    private readonly ITravelexTokenProvider tokenProvider;

    public TravelexAuthenticationHandler(ITravelexTokenProvider tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        this.tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = await tokenProvider.GetTokenAsync(invalidatedToken: null, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var retry = await request.CloneAsync(cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            retry.Dispose();
            return response;
        }

        response.Dispose();

        var refreshed = await tokenProvider.GetTokenAsync(invalidatedToken: token, cancellationToken);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);

        return await base.SendAsync(retry, cancellationToken);
    }
}

/// <summary>Nomes dos <c>HttpClient</c> registrados no container — ambos com mTLS.</summary>
internal static class TravelexClientNames
{
    /// <summary>Cliente dos recursos. Passa pelo <see cref="TravelexAuthenticationHandler"/>.</summary>
    public const string Api = "travelex.api";

    /// <summary>Cliente do <c>POST /auth</c> (sem handler de auth, COM mTLS).</summary>
    public const string Auth = "travelex.auth";
}
