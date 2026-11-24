namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// Verhält sich wie ein ordentlicher Client (ADR 0013): Vor jeder Änderung am Entwurf ohne eigenes If-Match wird der
/// aktuelle Stand (ETag) des Rezepts gelesen und mitgeschickt. Tests zu veralteten oder fehlenden Ständen
/// verwenden den normalen Client und setzen If-Match selbst.
/// </summary>
internal sealed class IfMatchAutomatik : DelegatingHandler
{
    private const string Entwurf = "/entwurf";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pfad = request.RequestUri!.AbsolutePath;
        var stelle = pfad.IndexOf(Entwurf, StringComparison.Ordinal);
        if (request.Method != HttpMethod.Get && stelle > 0 && request.Headers.IfMatch.Count == 0)
        {
            using var abfrage = new HttpRequestMessage(HttpMethod.Get, new Uri(request.RequestUri, pfad[..stelle]));
            abfrage.Headers.Authorization = request.Headers.Authorization; // seit ADR 0015 braucht auch das Lesen einen Token
            using var antwort = await base.SendAsync(abfrage, cancellationToken);
            if (antwort.Headers.ETag is { } etag)
            {
                request.Headers.IfMatch.Add(etag);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
