using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NetIntercom.Application.Common;
using NetIntercom.Domain.Common;

namespace NetIntercom.Api.Infrastructure;

/// <summary>Maps application/domain exceptions to RFC 7807 problem details responses.</summary>
public class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            ValidationException ve => new ValidationProblemDetails(
                ve.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            },
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Resource not found", exception.Message),
            ForbiddenException => Problem(StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
            UnauthorizedException => Problem(StatusCodes.Status401Unauthorized, "Unauthorized", exception.Message),
            DomainException => Problem(StatusCodes.Status422UnprocessableEntity, "Domain rule violated", exception.Message),
            _ => null
        };

        if (problem is null)
        {
            _logger.LogError(exception, "Unhandled exception");
            problem = Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred",
                "The server encountered an error processing the request.");
        }

        problem.Extensions["correlationId"] = httpContext.Items[CorrelationIdMiddleware.Header];
        httpContext.Response.StatusCode = problem.Status ?? 500;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken: ct);
        return true;
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
