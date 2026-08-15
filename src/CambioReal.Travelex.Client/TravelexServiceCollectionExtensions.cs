using System.Security.Cryptography.X509Certificates;
using CambioReal.Travelex.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CambioReal.Travelex;

/// <summary>Registro do cliente Travelex no container.</summary>
public static class TravelexServiceCollectionExtensions
{
    /// <summary>Registra o cliente a partir de uma seção de configuração.</summary>
    /// <remarks>
    /// Credenciais e material mTLS chegam por secret store (<c>pass cambio-real-v2/providers/travelex/homologation-env</c>
    /// e <c>.../providers/travelex/homologation-client-{cert,key}</c>) — nunca versionados. O certificado de homologação expira em 2034.
    /// </remarks>
    public static IServiceCollection AddTravelexClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddTravelexClient(configuration.Bind);
    }

    /// <summary>
    /// Registra <see cref="TravelexClient"/>, o provedor da cadeia de tokens e os dois pipelines
    /// HTTP — AMBOS com mTLS (o handshake exige o certificado do cliente já na etapa de token).
    /// </summary>
    public static IServiceCollection AddTravelexClient(this IServiceCollection services, Action<TravelexOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddOptions<TravelexOptions>().Validate(
            options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            },
            "A configuração do TravelexOptions é inválida.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITravelexTokenProvider, TravelexTokenProvider>();

        services.AddHttpClient(TravelexClientNames.Auth, ConfigureTransport)
            .ConfigurePrimaryHttpMessageHandler(CreateMtlsHandler);

        services.AddHttpClient(TravelexClientNames.Api, ConfigureTransport)
            .ConfigurePrimaryHttpMessageHandler(CreateMtlsHandler)
            .AddHttpMessageHandler(provider =>
                new TravelexAuthenticationHandler(provider.GetRequiredService<ITravelexTokenProvider>()));

        services.TryAddTransient(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            return new TravelexClient(
                factory.CreateClient(TravelexClientNames.Api),
                provider.GetRequiredService<IOptions<TravelexOptions>>());
        });

        return services;
    }

    private static void ConfigureTransport(IServiceProvider provider, HttpClient client)
    {
        var options = provider.GetRequiredService<IOptions<TravelexOptions>>().Value;
        options.Validate();

        client.BaseAddress = options.ResolveBaseAddress();
        client.Timeout = options.Timeout;

        // O WAF da Travelex responde 403 a requisições SEM User-Agent (o HttpClient .NET não
        // envia nenhum por default; curl e o legado enviam) — descoberto ao vivo em 2026-07-15.
        // Paridade com o legado: CambioReal/1.0.
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CambioReal/1.0");
    }

    /// <summary>
    /// Handler primário com o certificado mTLS do cliente carregado a partir dos PEMs (conteúdo,
    /// não path — compatível com Secret k8s montado em env var).
    /// </summary>
    private static HttpMessageHandler CreateMtlsHandler(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<TravelexOptions>>().Value;

        // Reexportado via PKCS#12 em memória: o SslStream do Windows/Linux exige a chave privada
        // associada de forma persistível — padrão consolidado para PEM efêmero.
        using var pem = X509Certificate2.CreateFromPem(options.CertificatePem, options.PrivateKeyPem);
        var certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), password: null);

        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificate);
        return handler;
    }
}
