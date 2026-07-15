namespace CambioReal.Travelex;

/// <summary>Ambiente da Travelex Bank (TIHUM) Corebanking API.</summary>
public enum TravelexEnvironment
{
    /// <summary>Homologação — <c>https://hml-api-corebanking-external.tihum.com/</c>.</summary>
    Homologation = 0,

    /// <summary>Produção — <c>https://api-corebanking.travelexbank.com.br/</c>.</summary>
    Production = 1,
}

/// <summary>Resolve o endereço base de cada <see cref="TravelexEnvironment"/>.</summary>
public static class TravelexEnvironmentExtensions
{
    /// <summary>Endereço base, confirmado em <c>cerebro/config/travelex.php</c> e validado ao vivo (2026-07-15).</summary>
    public static Uri GetBaseAddress(this TravelexEnvironment environment) => environment switch
    {
        TravelexEnvironment.Production => new Uri("https://api-corebanking.travelexbank.com.br/", UriKind.Absolute),
        TravelexEnvironment.Homologation => new Uri("https://hml-api-corebanking-external.tihum.com/", UriKind.Absolute),
        _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Ambiente Travelex desconhecido."),
    };
}

/// <summary>Configuração do <see cref="TravelexClient"/>.</summary>
public sealed class TravelexOptions
{
    /// <summary>Nome da seção de configuração sugerida.</summary>
    public const string SectionName = "Travelex";

    /// <summary>Usuário do Basic auth do <c>POST /auth</c>. Origem: <c>pass cambio-real-v2/providers/travelex/homologation-env</c>.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Senha do Basic auth. Origem: idem.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Agência (<c>agencia</c> — query param da maioria das chamadas).</summary>
    public string BranchNumber { get; set; } = string.Empty;

    /// <summary>Conta (<c>conta</c>).</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Chave PIX recebedora da conta Travelex da plataforma (payin).</summary>
    public string PixKey { get; set; } = string.Empty;

    /// <summary>Certificado mTLS (PEM, conteúdo). Origem: <c>pass .../homologation-client-cert</c> (expira 2034).</summary>
    public string CertificatePem { get; set; } = string.Empty;

    /// <summary>Chave privada mTLS (PEM, conteúdo). Origem: <c>pass .../homologation-client-key</c>.</summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>Ambiente alvo. O padrão é <see cref="TravelexEnvironment.Homologation"/>, deliberadamente.</summary>
    public TravelexEnvironment Environment { get; set; } = TravelexEnvironment.Homologation;

    /// <summary>Sobrescreve o endereço base. Precisa terminar em <c>/</c>.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// TTL do token JWT — o <c>/auth</c> devolve o token cru SEM expiração estruturada; o legado
    /// assume 3600s e cacheia 3600−60. Mantido o mesmo comportamento, configurável.
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromSeconds(3600);

    /// <summary>Margem de renovação antecipada.</summary>
    public TimeSpan TokenExpirationSkew { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Timeout de cada requisição HTTP.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Endereço base efetivo.</summary>
    public Uri ResolveBaseAddress() => BaseAddress ?? Environment.GetBaseAddress();

    /// <summary>Valida a configuração e lança se estiver inconsistente.</summary>
    /// <exception cref="InvalidOperationException">Alguma credencial obrigatória está ausente ou o base address é inválido.</exception>
    public void Validate()
    {
        foreach (var (value, name) in new[]
        {
            (Username, nameof(Username)),
            (Password, nameof(Password)),
            (BranchNumber, nameof(BranchNumber)),
            (AccountNumber, nameof(AccountNumber)),
            (CertificatePem, nameof(CertificatePem)),
            (PrivateKeyPem, nameof(PrivateKeyPem)),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{nameof(TravelexOptions)}.{name} é obrigatório.");
            }
        }

        var baseAddress = ResolveBaseAddress();

        if (!baseAddress.IsAbsoluteUri || !baseAddress.AbsolutePath.EndsWith('/'))
        {
            throw new InvalidOperationException($"{nameof(BaseAddress)} precisa ser absoluto e terminar em '/'.");
        }

        if (TokenLifetime <= TimeSpan.Zero || Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("TokenLifetime/Timeout precisam ser positivos.");
        }

        if (TokenExpirationSkew < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(TokenExpirationSkew)} não pode ser negativo.");
        }
    }
}
