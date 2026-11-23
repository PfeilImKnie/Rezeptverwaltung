using System.Net.Http.Json;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>
/// API-first: api/openapi.yaml ist der Vertrag. Dieser Test stellt sicher, dass die Implementierung
/// exakt die dort beschriebenen Operationen (Pfad, Methode, Statuscodes) anbietet – nicht mehr und nicht weniger.
/// Schema-Konformität der Antworten prüft zusätzlich Schemathesis in der CI.
/// </summary>
public sealed class OpenApiVertragTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] HttpMethoden = ["get", "put", "post", "delete", "patch"];

    [Fact]
    public async Task Implementierung_entspricht_dem_Vertrag()
    {
        var vertrag = OperationenAusVertrag();
        var implementiert = await OperationenAusImplementierungAsync();

        implementiert.Except(vertrag).ShouldBeEmpty("Implementiert, aber nicht im Vertrag (api/openapi.yaml)");
        vertrag.Except(implementiert).ShouldBeEmpty("Im Vertrag, aber nicht implementiert");
    }

    private static SortedSet<string> OperationenAusVertrag()
    {
        var yaml = new YamlStream();
        using (var reader = File.OpenText(Path.Combine(AppContext.BaseDirectory, "openapi.yaml")))
        {
            yaml.Load(reader);
        }

        var wurzel = (YamlMappingNode)yaml.Documents[0].RootNode;
        var operationen = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (pfadKnoten, methodenKnoten) in (YamlMappingNode)wurzel["paths"])
        {
            foreach (var (methodeKnoten, operation) in (YamlMappingNode)methodenKnoten)
            {
                var methode = ((YamlScalarNode)methodeKnoten).Value!;
                if (!HttpMethoden.Contains(methode))
                {
                    continue;
                }

                foreach (var (status, _) in (YamlMappingNode)((YamlMappingNode)operation)["responses"])
                {
                    operationen.Add($"{methode.ToUpperInvariant()} {((YamlScalarNode)pfadKnoten).Value} {((YamlScalarNode)status).Value}");
                }
            }
        }

        return operationen;
    }

    private async Task<SortedSet<string>> OperationenAusImplementierungAsync()
    {
        using var client = factory.CreateClient();
        var dokument = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);
        var operationen = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var pfad in dokument.GetProperty("paths").EnumerateObject())
        {
            foreach (var methode in pfad.Value.EnumerateObject().Where(m => HttpMethoden.Contains(m.Name)))
            {
                foreach (var status in methode.Value.GetProperty("responses").EnumerateObject())
                {
                    operationen.Add($"{methode.Name.ToUpperInvariant()} {pfad.Name} {status.Name}");
                }
            }
        }

        return operationen;
    }
}
