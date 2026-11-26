namespace Rezeptverwaltung.Web.Client.Api;

/// <summary>Setzt den Header, den der Server bei <c>/api</c> und <c>/bff</c> gegen CSRF verlangt (ADR 0016).</summary>
public sealed class CsrfHeaderHandler : DelegatingHandler
{
    public const string Header = "X-CSRF";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Add(Header, "1");
        return base.SendAsync(request, cancellationToken);
    }
}
