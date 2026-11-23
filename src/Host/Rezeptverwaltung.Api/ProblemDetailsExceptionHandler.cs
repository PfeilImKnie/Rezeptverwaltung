using Microsoft.AspNetCore.Diagnostics;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Api;

/// <summary>
/// Übersetzt bekannte Fehler in RFC-9457-Problem-Details:
/// verletzte Fachregeln → 422 mit stabilem <c>code</c>, ungültige Anfragen → 400.
/// Unbekannte Fehler fallen auf die Standardbehandlung (500, ohne Details) zurück.
/// </summary>
internal sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            DomainRuleViolationException verletzung => new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Fachliche Regel verletzt",
                Detail = verletzung.Message,
                Type = $"https://rezeptverwaltung/probleme/{verletzung.Code}",
                Extensions = { ["code"] = verletzung.Code },
            },
            BadHttpRequestException ungueltig => new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = ungueltig.StatusCode,
                Title = "Ungültige Anfrage",
                Detail = "Die Anfrage ist unvollständig oder hat ein ungültiges Format.",
            },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }
}
