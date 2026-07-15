using CambioReal.Travelex;
using CambioReal.Travelex.Auth;
using CambioReal.Travelex.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace CambioReal.Travelex.SandboxTests;

/// <summary>
/// Integração homologação real (mTLS), opt-in — goal-loop §2.5. Variáveis a partir do
/// <c>pass cambio-real-v2/providers/travelex/homologation-env</c> (USERNAME/PASSWORD/
/// BRANCH_NUMBER/ACCOUNT_NUMBER/PIX_KEY → prefixo <c>TRAVELEX_SANDBOX_</c>) e PEMs de
/// <c>homologation-client-{cert,key}</c> (<c>TRAVELEX_SANDBOX_CERT_PEM</c>/<c>KEY_PEM</c>).
/// Leituras apenas por default; payout/devolução (financial-write) NUNCA existem aqui.
/// </summary>
public sealed class TravelexSandboxTests
{
    private readonly ITestOutputHelper output;

    public TravelexSandboxTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    [Trait("Category", "Sandbox")]
    public async Task AuthenticatesLiveOverMtlsWithRawJwtBody()
    {
        using var provider = BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITravelexTokenProvider>();

        var token = await tokenProvider.GetTokenAsync(invalidatedToken: null);

        token.ShouldNotBeNullOrWhiteSpace();
        token.ShouldStartWith("ey");
        output.WriteLine($"POST /auth (Basic+mTLS): ok, JWT length={token.Length} (masked).");
    }

    [Fact]
    [Trait("Category", "Sandbox")]
    public async Task BalanceReadsLive()
    {
        using var provider = BuildServiceProvider();
        var client = provider.GetRequiredService<TravelexClient>();

        var saldo = await client.GetBalanceAsync();

        saldo.Saldo.ShouldNotBeNullOrWhiteSpace();
        saldo.Saldo!.ShouldStartWith("R$");
        output.WriteLine("GET v1/saldo: 200, saldo real presente (valor não impresso).");
    }

    [Fact]
    [Trait("Category", "Sandbox")]
    public async Task CobByFictitiousTxIdReturnsDomain404()
    {
        using var provider = BuildServiceProvider();
        var client = provider.GetRequiredService<TravelexClient>();

        var error = await Should.ThrowAsync<TravelexApiException>(
            async () => await client.GetCobAsync("CRSDKPROBE0000000000"));

        error.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        output.WriteLine($"GET v1/cob/{{fictício}}: 404 de domínio (problem-detail) — acesso real ao recurso confirmado.");
    }

    private static ServiceProvider BuildServiceProvider()
    {
        string Req(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{name} ausente — carregue do pass (ver doc no topo).");

        var services = new ServiceCollection();
        services.AddTravelexClient(options =>
        {
            options.Environment = TravelexEnvironment.Homologation;
            options.Username = Req("TRAVELEX_SANDBOX_USERNAME");
            options.Password = Req("TRAVELEX_SANDBOX_PASSWORD");
            options.BranchNumber = Req("TRAVELEX_SANDBOX_BRANCH_NUMBER");
            options.AccountNumber = Req("TRAVELEX_SANDBOX_ACCOUNT_NUMBER");
            options.PixKey = Req("TRAVELEX_SANDBOX_PIX_KEY");
            options.CertificatePem = Req("TRAVELEX_SANDBOX_CERT_PEM");
            options.PrivateKeyPem = Req("TRAVELEX_SANDBOX_KEY_PEM");
        });

        return services.BuildServiceProvider();
    }
}
