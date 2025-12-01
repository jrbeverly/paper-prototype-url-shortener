using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Common.ErrorHandling;

public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return Results.Problem(new ProblemDetails
            {
                Type = "https://api.short.io/errors/validation-error",
                Title = "Validation Error",
                Status = 400,
                Detail = "Request body is required."
            });
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<TRequest>>();
        var result = await validator.ValidateAsync(request);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var problem = new HttpValidationProblemDetails(errors)
        {
            Type = "https://api.short.io/errors/validation-error",
            Title = "Validation Error",
            Status = 400,
            Detail = "One or more fields failed validation.",
            Instance = context.HttpContext.Request.Path
        };

        var traceId = Activity.Current?.Id;
        if (!string.IsNullOrEmpty(traceId))
        {
            problem.Extensions["traceId"] = traceId;
        }

        return Results.Problem(problem);
    }
}

public static class ValidationEndpointFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder) where TRequest : class
    {
        builder.AddEndpointFilter<ValidationEndpointFilter<TRequest>>();
        return builder;
    }
}
