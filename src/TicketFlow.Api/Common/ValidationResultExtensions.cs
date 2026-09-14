using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace TicketFlow.Api.Common;

public static class ValidationResultExtensions
{
    public static ValidationProblemDetails ToProblemDetails(this ValidationResult result)
        => new(result.ToDictionary());
}
