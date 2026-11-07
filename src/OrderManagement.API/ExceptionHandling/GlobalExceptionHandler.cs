using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Application.Exceptions;
using System.Diagnostics;

namespace OrderManagement.API.ExceptionHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case DuplicateEmailException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Email already registered",
                    Detail = "A user with this email already exists."
                };
                break;

            case RegistrationValidationException registrationException:
                problem = new ValidationProblemDetails(
                    new Dictionary<string, string[]>
                    {
                        ["Registration"] = registrationException.Errors
                    })
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Registration validation failed."
                };
                break;

            case OrderValidationException orderException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Order validation failed",
                    Detail = orderException.Message
                };
                break;

            case DataConflictException conflictException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Operation conflicts with existing data",
                    Detail = conflictException.Message
                };
                break;

            default:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred",
                    Detail = "The request could not be completed."
                };
                break;
        }

        var traceId =
            httpContext.Items["CorrelationId"] as string
            ?? Activity.Current?.Id
            ?? httpContext.TraceIdentifier;


        problem.Instance = httpContext.Request.Path.Value;

        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] = traceId;

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled request exception. TraceId: {TraceId}",
                traceId);
        }
        else
        {
            _logger.LogWarning(
                "Request rejected with status {StatusCode}. " +
                "ExceptionType: {ExceptionType}. TraceId: {TraceId}",
                problem.Status,
                exception.GetType().Name,
                traceId);
        }

        await Results.Problem(problem).ExecuteAsync(httpContext);

        return true;
    }
}