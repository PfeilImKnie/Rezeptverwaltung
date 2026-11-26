using Rezeptverwaltung.Web.Client.Api;

namespace Rezeptverwaltung.Web.Client.Komponenten;

/// <summary>Eine Zutatenzeile, wie sie getippt wird (Menge noch als Text).</summary>
public sealed class ZeileEingabe
{
    public string Bezeichnung { get; set; } = string.Empty;

    public string Gramm { get; set; } = string.Empty;

    public bool IstLeer => string.IsNullOrWhiteSpace(Bezeichnung) && string.IsNullOrWhiteSpace(Gramm);

    public static ZeileEingabe Aus(Zutat zutat)
    {
        ArgumentNullException.ThrowIfNull(zutat);
        return new() { Bezeichnung = zutat.Bezeichnung, Gramm = Zahlen.Eingabe(zutat.Gramm) };
    }
}

/// <summary>Bezugsform, wie sie getippt wird. <see cref="Art"/> leer = ohne Form.</summary>
public sealed class FormEingabe
{
    public const int Nachkommastellen = 1;

    public string Art { get; set; } = string.Empty;

    public string Durchmesser { get; set; } = string.Empty;

    public string Laenge { get; set; } = string.Empty;

    public string Breite { get; set; } = string.Empty;

    public string Hoehe { get; set; } = string.Empty;

    public static FormEingabe Aus(Form? form) => form is null
        ? new FormEingabe()
        : new FormEingabe
        {
            Art = form.Art,
            Durchmesser = form.DurchmesserCm is { } d ? Zahlen.Eingabe(d) : string.Empty,
            Laenge = form.LaengeCm is { } l ? Zahlen.Eingabe(l) : string.Empty,
            Breite = form.BreiteCm is { } b ? Zahlen.Eingabe(b) : string.Empty,
            Hoehe = Zahlen.Eingabe(form.HoeheCm),
        };

    /// <summary>Liest die Form; Fehler werden an <paramref name="fehler"/> angehängt. Ohne Art: <c>null</c> ohne Fehler.</summary>
    public Form? Lesen(ICollection<string> fehler)
    {
        ArgumentNullException.ThrowIfNull(fehler);
        if (Art is not (Form.Ring or Form.Blech))
        {
            return null;
        }

        var anzahl = fehler.Count;
        var hoehe = Mass(Hoehe, "Höhe", fehler);
        if (Art == Form.Ring)
        {
            var durchmesser = Mass(Durchmesser, "Durchmesser", fehler);
            return fehler.Count == anzahl ? Form.AlsRing(durchmesser, hoehe) : null;
        }

        var laenge = Mass(Laenge, "Länge", fehler);
        var breite = Mass(Breite, "Breite", fehler);
        return fehler.Count == anzahl ? Form.AlsBlech(laenge, breite, hoehe) : null;
    }

    private static decimal Mass(string text, string name, ICollection<string> fehler)
    {
        var wert = Zahlen.Lesen(text, Nachkommastellen, out var meldung);
        if (wert is null)
        {
            fehler.Add($"{name}: {meldung}");
        }

        return wert ?? 0;
    }
}

/// <summary>Prüft und übersetzt die Eingaben eines Rezepts in den Inhalt für die API.</summary>
public static class Inhaltseingabe
{
    public const int GrammNachkommastellen = 2;

    /// <summary>Höchstens so viele Zutatenzeilen je Fassung (wie die API).</summary>
    public const int MaximaleZeilen = 100;

    /// <summary>Ganz leere Zeilen werden übergangen. Ist <paramref name="fehler"/> danach leer, ist der Inhalt gültig.</summary>
    public static RezeptInhalt Lesen(FormEingabe form, IReadOnlyList<ZeileEingabe> zeilen, ICollection<string> fehler)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(zeilen);
        ArgumentNullException.ThrowIfNull(fehler);

        var zutaten = new List<Zutat>();
        for (var i = 0; i < zeilen.Count; i++)
        {
            var zeile = zeilen[i];
            if (zeile.IstLeer)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(zeile.Bezeichnung))
            {
                fehler.Add($"Zeile {i + 1}: Bitte eine Zutat eintragen.");
            }

            var gramm = Zahlen.Lesen(zeile.Gramm, GrammNachkommastellen, out var meldung);
            if (gramm is null)
            {
                fehler.Add($"Zeile {i + 1}: {meldung}");
            }
            else if (!string.IsNullOrWhiteSpace(zeile.Bezeichnung))
            {
                zutaten.Add(new Zutat(zeile.Bezeichnung.Trim(), gramm.Value));
            }
        }

        if (zutaten.Count > MaximaleZeilen)
        {
            fehler.Add($"Höchstens {MaximaleZeilen} Zutaten je Rezept.");
        }

        return new RezeptInhalt(zutaten, form.Lesen(fehler));
    }
}
