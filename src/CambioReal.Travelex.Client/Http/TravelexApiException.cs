using System.Net;

namespace CambioReal.Travelex.Http;

/// <summary>Erro devolvido pela Travelex API.</summary>
public class TravelexApiException : Exception
{
    /// <summary>Cria uma exceção sem contexto de resposta.</summary>
    public TravelexApiException()
    {
    }

    /// <summary>Cria uma exceção com mensagem.</summary>
    public TravelexApiException(string message)
        : base(message)
    {
    }

    /// <summary>Cria uma exceção com mensagem e causa.</summary>
    public TravelexApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Cria uma exceção a partir de uma resposta da API.</summary>
    public TravelexApiException(
        HttpStatusCode statusCode, string? errorType, string? errorCode, string message, string? requestId, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
        ErrorCode = errorCode;
        RequestId = requestId;
        ResponseBody = responseBody;
    }

    /// <summary>Status HTTP da resposta.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Reservado (a Travelex não tem catálogo de tipo de erro; fica nulo).</summary>
    public string? ErrorType { get; }

    /// <summary>Mensagem extraída de <c>Error.ErrorMessage</c>/<c>error.errorMessage</c>/<c>reason</c> (formas do legado).</summary>
    public string? ErrorCode { get; }

    /// <summary>Reservado para correlação futura (a Travelex não devolve request id).</summary>
    public string? RequestId { get; }

    /// <summary>Corpo bruto da resposta, para diagnóstico.</summary>
    public string? ResponseBody { get; }
}

/// <summary>Falha na cadeia de autenticação (access/represented token) ou 401 pós-retry.</summary>
public sealed class TravelexAuthenticationException : TravelexApiException
{
    /// <inheritdoc/>
    public TravelexAuthenticationException()
    {
    }

    /// <inheritdoc/>
    public TravelexAuthenticationException(string message)
        : base(message)
    {
    }

    /// <inheritdoc/>
    public TravelexAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <inheritdoc/>
    public TravelexAuthenticationException(
        HttpStatusCode statusCode, string? errorType, string? errorCode, string message, string? requestId, string? responseBody)
        : base(statusCode, errorType, errorCode, message, requestId, responseBody)
    {
    }
}
