using System.Net;
using CambioReal.Travelex.Auth;
using CambioReal.Travelex.Http;
using CambioReal.Travelex.Models;
using CambioReal.Travelex.Tests.Fakes;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CambioReal.Travelex.Tests;

public sealed class TravelexClientTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static TravelexOptions NewOptions() => new()
    {
        Environment = TravelexEnvironment.Homologation,
        Username = "user-1",
        Password = "pass-1",
        BranchNumber = "0001",
        AccountNumber = "12345",
        PixKey = "chave-plataforma",
        CertificatePem = "cert",
        PrivateKeyPem = "key",
    };

    private static TravelexClient NewClient(RecordingHttpMessageHandler transport) =>
        new(new HttpClient(transport) { BaseAddress = NewOptions().ResolveBaseAddress() }, Options.Create(NewOptions()));

    [Fact]
    public void ValidOptionsPassValidation()
        => Should.NotThrow(() => NewOptions().Validate());

    [Fact]
    public void EnvironmentsResolveToOfficialHosts()
    {
        TravelexEnvironment.Homologation.GetBaseAddress().ToString().ShouldBe("https://hml-api-corebanking-external.tihum.com/");
        TravelexEnvironment.Production.GetBaseAddress().ToString().ShouldBe("https://api-corebanking.travelexbank.com.br/");
    }

    [Fact]
    public async Task AuthSendsBasicAndParsesRawJwtBody()
    {
        var transport = new RecordingHttpMessageHandler();

        // Confirmado ao vivo: a resposta do /auth é o JWT CRU no corpo, não JSON estruturado.
        transport.RespondWith(HttpStatusCode.OK, "eyJhbGciOiJIUzI1NiJ9.token.assinatura");

        var provider = new TravelexTokenProvider(
            new SingleHandlerHttpClientFactory(transport, NewOptions().ResolveBaseAddress()),
            Options.Create(NewOptions()),
            new MutableTimeProvider(Epoch));

        var token = await provider.GetTokenAsync(null);

        token.ShouldBe("eyJhbGciOiJIUzI1NiJ9.token.assinatura");
        transport.Requests.Single().Authorization!.ShouldStartWith("Basic ");
        transport.Requests.Single().RequestUri!.ToString()
            .ShouldBe("https://hml-api-corebanking-external.tihum.com/auth");

        // Cache (TTL 3600−60 herdado do legado).
        (await provider.GetTokenAsync(null)).ShouldBe(token);
        transport.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CobPutSerializesBacenShape()
    {
        var transport = new RecordingHttpMessageHandler();
        transport.RespondWith(HttpStatusCode.OK, """{"txId":"TX1","pixCopiaECola":"000201...","status":"ATIVA"}""");

        var client = NewClient(transport);

        var response = await client.PutCobAsync("TX1", new CreateCobRequest
        {
            Agencia = "0001",
            Conta = "12345",
            Chave = "chave-plataforma",
            Calendario = new CobCalendario { Expiracao = 960 },
            Valor = new CobValor { Original = 500.00m },
            Devedor = new CobDevedor { Cpf = "52998224725", Nome = "Fulano" },
            SolicitacaoPagador = "CambioReal X",
        });

        response.PixCopiaECola.ShouldBe("000201...");

        var request = transport.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Put);
        request.RequestUri!.ToString().ShouldEndWith("/v1/cob/TX1");
        request.Body!.ShouldContain("\"calendario\":{\"expiracao\":960}");
        request.Body!.ShouldContain("\"valor\":{\"original\":500.00,\"modalidadeAlteracao\":0}");
        request.Body!.ShouldContain("\"cpf\":\"52998224725\"");
        request.Body!.ShouldNotContain("cnpj");
    }

    [Fact]
    public async Task SaldoAndCobQueriesCarryAccountParams()
    {
        var transport = new RecordingHttpMessageHandler();
        transport.RespondWith(HttpStatusCode.OK, """{"saldo":"R$ 100,00","bloqueado":"R$ 0,00"}""");

        var client = NewClient(transport);
        var saldo = await client.GetBalanceAsync();

        saldo.Saldo.ShouldBe("R$ 100,00");
        transport.Requests.Single().RequestUri!.Query.ShouldContain("agencia=0001");
        transport.Requests.Single().RequestUri!.Query.ShouldContain("conta=12345");
    }

    /// <summary>Erro real observado ao vivo — RFC 7807-style com title/detail.</summary>
    [Fact]
    public async Task ProblemDetailErrorShapeIsExtracted()
    {
        var transport = new RecordingHttpMessageHandler();
        transport.RespondWith(HttpStatusCode.NotFound, """
            {"type":"about:blank","title":"Integration Error.","status":404,"detail":"Houve um erro de integração com um serviço interno."}
            """);

        var client = NewClient(transport);

        var error = await Should.ThrowAsync<TravelexApiException>(
            async () => await client.GetCobAsync("CRX"));

        error.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        error.Message.ShouldContain("erro de integração");
    }

    [Fact]
    public async Task PayoutCreatePostsKeyOnlyShape()
    {
        var transport = new RecordingHttpMessageHandler();
        transport.RespondWith(HttpStatusCode.OK, """{"status":"CRIADO"}""");

        var client = NewClient(transport);

        var response = await client.CreatePayoutAsync("TX2", new CreatePayoutRequest
        {
            Valor = 50.00m,
            Chave = "52998224725",
            Agencia = "0001",
            Conta = "12345",
        });

        response.Status.ShouldBe(TravelexStatuses.PayoutCriado);
        transport.Requests.Single().Body!.ShouldContain("\"moeda\":\"BRL\"");
        transport.Requests.Single().RequestUri!.ToString().ShouldEndWith("/v1/payout/TX2");
    }

    [Fact]
    public async Task DevolucaoPostsBacenReasonCode()
    {
        var transport = new RecordingHttpMessageHandler();
        transport.RespondWith(HttpStatusCode.OK, """{"status":"EM_PROCESSAMENTO"}""");

        var client = NewClient(transport);

        await client.CreateDevolucaoAsync("E123", new CreateDevolucaoRequest
        {
            Valor = 500.00m,
            InformacoesAdicionais = "Reembolso X",
        });

        var request = transport.Requests.Single();
        request.RequestUri!.ToString().ShouldEndWith("/v1/cob/E123/devolucao/0001/12345");
        request.Body!.ShouldContain("\"codigoMotivoDevolucao\":\"MD06\"");
    }
}
