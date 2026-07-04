using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PuckDrop.Api.Contracts;

namespace PuckDrop.Api.Filters;

/// <summary>
/// Global exception filter that maps known domain exceptions to consistent HTTP error responses.
/// Controllers can let exceptions bubble without try/catch.
/// </summary>
public class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (statusCode, errorCode) = context.Exception switch
        {
            KeyNotFoundException => (404, "NOT_FOUND"),
            InvalidOperationException => (400, "INVALID_OPERATION"),
            ArgumentException => (400, "VALIDATION_ERROR"),
            _ => (0, string.Empty)
        };

        if (statusCode == 0)
            return; // Let unhandled exceptions propagate to default error handling

        context.Result = new ObjectResult(new ErrorResponse(errorCode, context.Exception.Message))
        {
            StatusCode = statusCode
        };

        context.ExceptionHandled = true;
    }
}
