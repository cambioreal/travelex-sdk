using System.Text.Json;

namespace CambioReal.Travelex.Models;

/// <summary>
/// Statuses de payout confirmados no legado (<c>PayoutService::checkPayed</c>): terminal de
/// sucesso = <c>APROVADO</c>; em progresso = <c>CRIADO</c>/<c>ABERTO</c>/<c>PARCIAL</c>; falhas =
/// <c>REJEITADO</c>/<c>TIMEOUT</c>/<c>CANCELADO</c>/<c>NAO_FINALIZADO</c>/<c>ERRO</c>/
/// <c>SUPER_LOTADO</c>/<c>REJEICAO_COMPLIANCE</c>. Payin (cob BACEN): <c>CONCLUIDA</c> = pago.
/// Conjunto aberto — DTOs usam <see cref="string"/>.
/// </summary>
public static class TravelexStatuses
{
    public const string PayoutCriado = "CRIADO";
    public const string PayoutAberto = "ABERTO";
    public const string PayoutParcial = "PARCIAL";
    public const string PayoutAprovado = "APROVADO";
    public const string PayoutRejeitado = "REJEITADO";
    public const string CobConcluida = "CONCLUIDA";
}

/// <summary>
/// Corpo de <c>PUT v1/cob/{txId}</c> — cob PIX no formato BACEN, confirmado no legado
/// (<c>PixService::create</c>): <c>agencia</c>/<c>conta</c>/<c>chave</c> + blocos padrão
/// <c>calendario</c>/<c>valor</c>/<c>devedor</c>/<c>infoAdicionais</c>.
/// </summary>
public sealed record CreateCobRequest
{
    public required string Agencia { get; init; }
    public required string Conta { get; init; }

    /// <summary>Chave PIX recebedora (da plataforma).</summary>
    public required string Chave { get; init; }

    public required CobCalendario Calendario { get; init; }
    public required CobValor Valor { get; init; }
    public required CobDevedor Devedor { get; init; }
    public string? SolicitacaoPagador { get; init; }
    public IReadOnlyList<CobInfoAdicional>? InfoAdicionais { get; init; }
}

public sealed record CobCalendario
{
    public required long Expiracao { get; init; }
    public DateTimeOffset? Criacao { get; init; }
}

public sealed record CobValor
{
    /// <summary>Decimal (o legado envia float BRL).</summary>
    public required decimal Original { get; init; }

    public int ModalidadeAlteracao { get; init; }
}

/// <summary>Devedor — exatamente um entre <see cref="Cpf"/>/<see cref="Cnpj"/>.</summary>
public sealed record CobDevedor
{
    public string? Cpf { get; init; }
    public string? Cnpj { get; init; }
    public required string Nome { get; init; }
}

public sealed record CobInfoAdicional(string Nome, string Valor);

/// <summary>Cob (payin) — resposta de PUT/GET <c>v1/cob</c>. Campos extras expostos crus.</summary>
public sealed record CobResponse
{
    public string? TxId { get; init; }

    /// <summary>Payload copia-e-cola.</summary>
    public string? PixCopiaECola { get; init; }

    /// <summary><c>CONCLUIDA</c> = pago (legado <c>getStatusPago</c>); demais = pendente.</summary>
    public string? Status { get; init; }

    public CobCalendario? Calendario { get; init; }
    public CobValor? Valor { get; init; }
    public CobDevedor? Devedor { get; init; }
    public JsonElement? Loc { get; init; }
}

/// <summary>Corpo de <c>POST v1/payout/{txId}</c> — payout PIX por chave (única modalidade suportada).</summary>
public sealed record CreatePayoutRequest
{
    public required decimal Valor { get; init; }
    public string Moeda { get; init; } = "BRL";

    /// <summary>Chave PIX do beneficiário (CPF/CNPJ só dígitos; telefone em E.164 com <c>+</c> — regra do legado).</summary>
    public required string Chave { get; init; }

    public required string Agencia { get; init; }
    public required string Conta { get; init; }
}

/// <summary>Payout — resposta de POST/GET <c>v1/payout</c>.</summary>
public sealed record PayoutResponse
{
    public string? Status { get; init; }
    public JsonElement? InformacoesAdicionais { get; init; }
}

/// <summary>Resposta de <c>GET v1/saldo</c> — validada ao vivo (valores como string <c>"R$ ..."</c>).</summary>
public sealed record SaldoResponse
{
    /// <summary>String no formato <c>"R$ 1.234,56"</c> — repassada crua (parse é do consumidor).</summary>
    public string? Saldo { get; init; }

    public string? Bloqueado { get; init; }
}

/// <summary>
/// Corpo de <c>POST v1/cob/{endToEndId}/devolucao/{agencia}/{conta}</c> — refund de payin,
/// confirmado no legado (<c>PixService::refund</c>). **FINANCIAL-WRITE.**
/// </summary>
public sealed record CreateDevolucaoRequest
{
    public required decimal Valor { get; init; }
    public string? InformacoesAdicionais { get; init; }

    /// <summary>Código BACEN de motivo (<c>MD06</c> no legado; <c>NARR</c> exige <see cref="InformacoesMotivoDevolucao"/>).</summary>
    public string CodigoMotivoDevolucao { get; init; } = "MD06";

    public string? InformacoesMotivoDevolucao { get; init; }
}
