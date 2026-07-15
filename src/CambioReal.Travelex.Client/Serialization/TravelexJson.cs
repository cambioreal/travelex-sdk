using System.Text.Json;
using System.Text.Json.Serialization;

namespace CambioReal.Travelex.Serialization;

/// <summary>Convenções de JSON da Travelex Corebanking API.</summary>
public static class TravelexJson
{
    /// <summary>
    /// camelCase BACEN-style (<c>pixCopiaECola</c>, <c>solicitacaoPagador</c>,
    /// <c>modalidadeAlteracao</c>, <c>codigoMotivoDevolucao</c>, …) — confirmado no legado e ao
    /// vivo (2026-07-15). Statuses em UPPER pt-BR (<c>APROVADO</c>, <c>CONCLUIDA</c>, …), conjunto
    /// aberto ⇒ strings.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
