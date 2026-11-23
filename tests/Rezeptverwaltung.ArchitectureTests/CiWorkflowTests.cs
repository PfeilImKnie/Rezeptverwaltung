using YamlDotNet.RepresentationModel;

namespace Rezeptverwaltung.ArchitectureTests;

/// <summary>
/// Sichert die Pflicht-Checks aus ADR 0007 und die Dependabot-Ausnahme aus ADR 0010 ab.
/// Die <c>if</c>-Bedingung des Jobs <c>pr-gate</c> wird mit einem kleinen Auswerter für GitHub-Ausdrücke
/// gegen typische Ereignisse durchgespielt (Stringvergleich wie bei GitHub ohne Groß-/Kleinschreibung).
/// </summary>
public sealed class CiWorkflowTests
{
    private const string Dependabot = "dependabot[bot]";
    private const string Mensch = "konditormeisterin";

    private static readonly string[] PflichtJobsOhneAusnahme = ["build-test", "mutation", "api-vertrag"];

    private static readonly YamlMappingNode Jobs = LadeJobs();

    public static TheoryData<string, string?, string, bool> Ereignisse => new()
    {
        // (1) Reines Dependabot-Update: Gate wird übersprungen.
        { "pull_request", Dependabot, Dependabot, false },
        // (2) Mensch pusht auf Dependabot-Branch, bearbeitet die Beschreibung oder öffnet den PR erneut.
        { "pull_request", Dependabot, Mensch, true },
        // (3) Normale PRs: Gate ist Pflicht, auch wenn Dependabot den Lauf auslöst (z. B. Rebase).
        { "pull_request", Mensch, Mensch, true },
        { "pull_request", Mensch, Dependabot, true },
        { "pull_request", "github-actions[bot]", "github-actions[bot]", true },
        { "pull_request", "dependabot", "dependabot", true },
        { "pull_request", "dependabot[bot]-fake", "dependabot[bot]-fake", true },
        { "pull_request", "renovate[bot]", "renovate[bot]", true },
        // GitHub vergleicht Strings ohne Groß-/Kleinschreibung.
        { "pull_request", "Dependabot[bot]", "DEPENDABOT[BOT]", false },
        // Pushes auf main: Gate läuft wie bisher nicht (kein PR).
        { "push", null, Mensch, false },
        { "push", null, Dependabot, false },
    };

    [Theory]
    [MemberData(nameof(Ereignisse))]
    public void PR_Gate_laeuft_nur_wenn_kein_reines_Dependabot_Update_vorliegt(string ereignis, string? prAutor, string ausloeser, bool erwartetLauf)
    {
        var kontext = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["github.event_name"] = ereignis,
            ["github.event.pull_request.user.login"] = prAutor,
            ["github.actor"] = ausloeser,
        };

        GitHubAusdruck.Auswerten(Bedingung("pr-gate"), kontext)
            .ShouldBe(erwartetLauf, $"Ereignis={ereignis}, PR-Autor={prAutor}, Auslöser={ausloeser}");
    }

    [Fact]
    public void PR_Gate_Bedingung_haengt_nur_von_Ereignis_Autor_und_Ausloeser_ab()
    {
        var bedingung = Bedingung("pr-gate");

        GitHubAusdruck.Kontextpfade(bedingung).ShouldBe(
            ["github.actor", "github.event.pull_request.user.login", "github.event_name"],
            ignoreOrder: true,
            "Die Ausnahme darf nicht über weitere Felder (Titel, Labels, Branch-Name …) ausgeweitet werden (ADR 0010).");
    }

    [Theory]
    [MemberData(nameof(PflichtJobs))]
    public void Uebrige_Pflicht_Jobs_gelten_ohne_Bedingung_auch_fuer_Dependabot(string job)
    {
        var knoten = (YamlMappingNode)Jobs[job];

        knoten.Children.ContainsKey(new YamlScalarNode("if"))
            .ShouldBeFalse($"Job '{job}' ist Pflicht-Check und darf nicht bedingt übersprungen werden (ADR 0010, Punkt 2).");
    }

    public static TheoryData<string> PflichtJobs => new(PflichtJobsOhneAusnahme);

    [Fact]
    public void Branch_Schutz_verlangt_alle_vier_Jobs_unter_ihrem_Anzeigenamen()
    {
        var skript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ci", "github-einrichten.sh"));

        foreach (var job in PflichtJobsOhneAusnahme.Append("pr-gate"))
        {
            var name = ((YamlScalarNode)((YamlMappingNode)Jobs[job])["name"]).Value!;
            skript.ShouldContain($"\"{name}\"", Case.Sensitive, $"Status-Check für Job '{job}' fehlt im Branch-Schutz.");
        }
    }

    [Fact]
    public void Workflow_reagiert_auf_Bearbeiten_und_Pushen_eines_PRs()
    {
        var wurzel = LadeWurzel();
        var on = (YamlMappingNode)wurzel[new YamlScalarNode("on")];
        var typen = ((YamlSequenceNode)((YamlMappingNode)on["pull_request"])["types"])
            .Select(t => ((YamlScalarNode)t).Value)
            .ToList();

        // Ohne 'edited' und 'synchronize' bliebe ein menschlicher Eingriff in einen Dependabot-PR ungeprüft.
        typen.ShouldContain("edited");
        typen.ShouldContain("synchronize");
        typen.ShouldContain("opened");
        typen.ShouldContain("reopened");
    }

    private static string Bedingung(string job) =>
        ((YamlScalarNode)((YamlMappingNode)Jobs[job])["if"]).Value!;

    private static YamlMappingNode LadeJobs() => (YamlMappingNode)LadeWurzel()["jobs"];

    private static YamlMappingNode LadeWurzel()
    {
        var yaml = new YamlStream();
        using (var reader = File.OpenText(Path.Combine(AppContext.BaseDirectory, "ci", "ci.yml")))
        {
            yaml.Load(reader);
        }

        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }
}

/// <summary>
/// Minimaler Auswerter für GitHub-Actions-Ausdrücke: <c>||</c>, <c>&amp;&amp;</c>, <c>!</c>, <c>==</c>, <c>!=</c>,
/// Klammern, String-Literale in einfachen Anführungszeichen und Kontextpfade. Strings werden wie bei GitHub
/// ohne Groß-/Kleinschreibung verglichen. Unbekannte Konstrukte führen zu einer Ausnahme statt zu einem Schätzwert.
/// </summary>
internal static class GitHubAusdruck
{
    public static bool Auswerten(string ausdruck, IReadOnlyDictionary<string, string?> kontext)
    {
        var parser = new Parser(Zerlegen(ausdruck), kontext);
        var wert = parser.Oder();
        parser.Ende();
        return Wahr(wert);
    }

    public static IReadOnlySet<string> Kontextpfade(string ausdruck) =>
        Zerlegen(ausdruck)
            .Where(t => t.Art == Art.Pfad)
            .Select(t => t.Text)
            .ToHashSet(StringComparer.Ordinal);

    private enum Art { Pfad, Text, Op }

    private readonly record struct Token(Art Art, string Text);

    private static List<Token> Zerlegen(string ausdruck)
    {
        var s = ausdruck.Trim();
        if (s.StartsWith("${{", StringComparison.Ordinal) && s.EndsWith("}}", StringComparison.Ordinal))
        {
            s = s[3..^2];
        }

        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '\'')
            {
                var ende = s.IndexOf('\'', i + 1);
                if (ende < 0)
                {
                    throw new FormatException("Nicht geschlossenes String-Literal.");
                }

                tokens.Add(new Token(Art.Text, s[(i + 1)..ende]));
                i = ende + 1;
            }
            else if (i + 1 < s.Length && s.Substring(i, 2) is "&&" or "||" or "==" or "!=")
            {
                tokens.Add(new Token(Art.Op, s.Substring(i, 2)));
                i += 2;
            }
            else if (c is '!' or '(' or ')')
            {
                tokens.Add(new Token(Art.Op, c.ToString()));
                i++;
            }
            else if (char.IsLetter(c))
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '.' or '_' or '-'))
                {
                    i++;
                }

                tokens.Add(new Token(Art.Pfad, s[start..i]));
            }
            else
            {
                throw new FormatException($"Nicht unterstütztes Zeichen '{c}' im Ausdruck.");
            }
        }

        return tokens;
    }

    private static bool Wahr(object? wert) => wert switch
    {
        null => false,
        bool b => b,
        string t => t.Length > 0,
        _ => throw new InvalidOperationException(),
    };

    private static bool Gleich(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase),
        (bool x, bool y) => x == y,
        // null gegen String: GitHub wandelt null in '' um; ein Login ist nie leer.
        (null, string y) => y.Length == 0,
        (string x, null) => x.Length == 0,
        _ => throw new NotSupportedException("Vergleich gemischter Typen wird nicht nachgebildet."),
    };

    private sealed class Parser(List<Token> tokens, IReadOnlyDictionary<string, string?> kontext)
    {
        private int _pos;

        private Token? Aktuell => _pos < tokens.Count ? tokens[_pos] : null;

        private bool Nimm(string op)
        {
            if (Aktuell is { Art: Art.Op } t && t.Text == op)
            {
                _pos++;
                return true;
            }

            return false;
        }

        public void Ende()
        {
            if (_pos != tokens.Count)
            {
                throw new FormatException($"Unerwartetes Token '{tokens[_pos].Text}'.");
            }
        }

        public object? Oder()
        {
            var links = Und();
            while (Nimm("||"))
            {
                var rechts = Und();
                links = Wahr(links) || Wahr(rechts);
            }

            return links;
        }

        private object? Und()
        {
            var links = Nicht();
            while (Nimm("&&"))
            {
                var rechts = Nicht();
                links = Wahr(links) && Wahr(rechts);
            }

            return links;
        }

        private object? Nicht() => Nimm("!") ? !Wahr(Nicht()) : Vergleich();

        private object? Vergleich()
        {
            var links = Primaer();
            if (Nimm("=="))
            {
                return Gleich(links, Primaer());
            }

            if (Nimm("!="))
            {
                return !Gleich(links, Primaer());
            }

            return links;
        }

        private object? Primaer()
        {
            if (Nimm("("))
            {
                var wert = Oder();
                if (!Nimm(")"))
                {
                    throw new FormatException("Klammer nicht geschlossen.");
                }

                return wert;
            }

            var t = Aktuell ?? throw new FormatException("Ausdruck endet unerwartet.");
            _pos++;
            return t.Art switch
            {
                Art.Text => t.Text,
                Art.Pfad when t.Text is "true" or "false" => t.Text == "true",
                Art.Pfad when kontext.TryGetValue(t.Text, out var wert) => wert,
                Art.Pfad => throw new KeyNotFoundException($"Kontextpfad '{t.Text}' ist im Test nicht belegt."),
                _ => throw new FormatException($"Unerwartetes Token '{t.Text}'."),
            };
        }
    }
}
