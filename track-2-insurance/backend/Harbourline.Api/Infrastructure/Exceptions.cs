using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Harbourline.Api.Infrastructure;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

/// <summary>The request shape is wrong in a way FluentValidation does not cover. Maps to 400.</summary>
public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message) { }
}

public class ApiExceptionHandler : IExceptionHandler
{
    public const string ConcurrencyMessage = "This claim was changed by someone else while you were working on it. Reload the claim and try again.";

    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (404, "Not found"),
            ConflictException => (409, "Conflict"),
            BusinessRuleException => (422, "Business rule violated"),
            BadRequestException => (400, "Bad request"),
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (409, "Conflict"),
            FluentValidation.ValidationException => (400, "Validation failed"),
            _ => (500, "Unexpected error")
        };
        if (status == 500) _logger.LogError(exception, "Unhandled exception");

        if (exception is FluentValidation.ValidationException ve)
        {
            var errors = ve.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new ValidationProblemDetails(errors) { Status = 400, Title = title }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
            return true;
        }

        var problem = new ProblemDetails { Status = status, Title = title, Detail = status == 500 ? "An unexpected error occurred."
            : exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ? ConcurrencyMessage
            : exception.Message };
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }
}
