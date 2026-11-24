using Rezeptverwaltung.Rezeptur.Application.Rezepte;
using Rezeptverwaltung.Rezeptur.Domain.Rezepte;
using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Application.Tests;

/// <summary>Use Cases „Rezept auf Form umrechnen“ sowie Bezugsform beim Anlegen und Bearbeiten (ADR 0014).</summary>
public sealed class RezeptAufFormSkalierenUseCaseTests
{
    private readonly InMemoryRezeptRepositoryFake _repository = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<Guid> AnlegenAsync(Form? form, params ZutatEingabe[] zutaten) =>
        (await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(new RezeptAnlegen("Sachermasse", zutaten, form), _ct)).Id;

    private Task<Guid> SachermasseAsync(Form? form) =>
        AnlegenAsync(form, new ZutatEingabe("Butter", 140), new ZutatEingabe("Zucker", 140), new ZutatEingabe("Eier", 280));

    private Task<SkaliertesRezept?> UmrechnenAsync(Guid id, Form zielform, int? fassung = null) =>
        new RezeptAufFormSkalierenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(new RezeptAufFormSkalieren(id, zielform, fassung), _ct);

    private async Task FreigebenUndNeuenEntwurfMitFormAsync(Guid id, Form? form)
    {
        await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);
        await new EntwurfBearbeitenHandler(_repository, Testbenutzer.InhaberA)
            .HandleAsync(new EntwurfBearbeiten(id, [new ZutatEingabe("Butter", 100)], _repository.Stand(id), form), _ct);
    }

    [Fact]
    public async Task Anlegen_mit_Form_liefert_die_Form_in_den_Details()
    {
        var details = await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA)
            .HandleAsync(new RezeptAnlegen("Biskuit", [new ZutatEingabe("Eier", 300)], Form.Blech(60, 40, 1.5m)), _ct);

        details.Bezugsform.ShouldBe(Form.Blech(60, 40, 1.5m));
    }

    [Fact]
    public async Task Anlegen_ohne_Form_ist_weiterhin_moeglich()
    {
        var details = await new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA)
            .HandleAsync(new RezeptAnlegen("Ganache", [new ZutatEingabe("Sahne", 500)]), _ct);

        details.Bezugsform.ShouldBeNull();
    }

    [Fact]
    public async Task Anlegen_mit_ungueltiger_Form_speichert_nichts()
    {
        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(
            () => new RezeptAnlegenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA)
                .HandleAsync(new RezeptAnlegen("Biskuit", [new ZutatEingabe("Eier", 300)], Form.Ring(0.5m, 5)), _ct));

        verletzung.Code.ShouldBe("form.mass_ungueltig");
        _repository.Gespeichert.ShouldBeEmpty();
    }

    [Fact]
    public async Task Umrechnen_liefert_Faktor_Zielform_Fassung_und_Gesamtgewicht()
    {
        var id = await SachermasseAsync(Form.Ring(26, 6));

        var ergebnis = (await UmrechnenAsync(id, Form.Ring(13, 6)))!;

        ergebnis.RezeptId.ShouldBe(id);
        ergebnis.Fassung.ShouldBe(1);
        ergebnis.Faktor.ShouldBe(0.25m);
        ergebnis.Zielform.ShouldBe(Form.Ring(13, 6));
        ergebnis.ZielgewichtGramm.ShouldBe(140m);
        ergebnis.Zutaten.Select(z => z.Gramm).ShouldBe([35m, 35m, 70m]);
    }

    [Fact]
    public async Task Umrechnen_aendert_und_speichert_das_Rezept_nicht()
    {
        var id = await SachermasseAsync(Form.Ring(26, 6));

        await UmrechnenAsync(id, Form.Blech(60, 40, 2));

        _repository.Speichervorgaenge.ShouldBe(0);
        _repository.Gespeichert.Values.Single().Zutaten.Select(z => z.Menge.Gramm).ShouldBe([140m, 140m, 280m]);
    }

    [Fact]
    public async Task Ohne_Fassungsangabe_gilt_die_massgebliche_Fassung()
    {
        var id = await SachermasseAsync(Form.Ring(26, 6));
        await FreigebenUndNeuenEntwurfMitFormAsync(id, Form.Ring(13, 6));

        var ergebnis = (await UmrechnenAsync(id, Form.Ring(13, 6)))!;

        ergebnis.Fassung.ShouldBe(1);
        ergebnis.Faktor.ShouldBe(0.25m);
    }

    [Fact]
    public async Task Eine_bestimmte_Fassung_ist_waehlbar()
    {
        var id = await SachermasseAsync(Form.Ring(26, 6));
        await FreigebenUndNeuenEntwurfMitFormAsync(id, Form.Ring(13, 6));

        var ergebnis = (await UmrechnenAsync(id, Form.Ring(26, 6), fassung: 2))!;

        ergebnis.Fassung.ShouldBe(2);
        // Faktor ist laut Vertrag ungerundet; über π entsteht Rauschen in der 28. Stelle.
        Math.Abs(ergebnis.Faktor - 4m).ShouldBeLessThan(0.000_000_001m);
        ergebnis.Zutaten.ShouldHaveSingleItem().Gramm.ShouldBe(400m);
    }

    [Fact]
    public async Task Gewaehlte_Fassung_ohne_Form_ist_ein_Zustandskonflikt_auch_wenn_die_massgebliche_eine_hat()
    {
        var id = await SachermasseAsync(Form.Ring(26, 6));
        await FreigebenUndNeuenEntwurfMitFormAsync(id, form: null);

        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(() => UmrechnenAsync(id, Form.Ring(18, 6), fassung: 2));

        verletzung.Code.ShouldBe("fassung.ohne_bezugsform");
        verletzung.Art.ShouldBe(Regelart.Zustandskonflikt);
    }

    [Fact]
    public async Task Unbekanntes_Rezept_oder_unbekannte_Fassung_ergeben_null()
    {
        var id = await SachermasseAsync(Form.Ring(26, 6));

        (await UmrechnenAsync(Guid.NewGuid(), Form.Ring(18, 6))).ShouldBeNull();
        (await UmrechnenAsync(id, Form.Ring(18, 6), fassung: 2)).ShouldBeNull();
    }

    [Theory]
    [InlineData(0.5, 6, "form.mass_ungueltig")]
    [InlineData(18, 0, "form.hoehe_ungueltig")]
    [InlineData(18.05, 6, "form.zu_genau")]
    public async Task Ungueltige_Zielform_ist_eine_ungueltige_Eingabe_auch_ohne_Bezugsform_und_Zutaten(decimal durchmesser, decimal hoehe, string code)
    {
        var id = await AnlegenAsync(form: null);

        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(() => UmrechnenAsync(id, Form.Ring(durchmesser, hoehe)));

        verletzung.Code.ShouldBe(code);
        verletzung.Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    [Fact]
    public async Task Mit_Form_aber_ohne_Zutaten_ist_Umrechnen_ein_Zustandskonflikt()
    {
        var id = await AnlegenAsync(Form.Ring(26, 6));

        (await Should.ThrowAsync<DomainRuleViolationException>(() => UmrechnenAsync(id, Form.Ring(18, 6))))
            .Code.ShouldBe("rezept.ohne_zutaten");
    }

    [Fact]
    public async Task Bearbeiten_mit_ungueltiger_Form_ohne_Entwurf_meldet_zuerst_die_Eingabe()
    {
        var id = await SachermasseAsync(Form.Ring(26, 6));
        await new EntwurfFreigebenHandler(_repository, Testzeit.Anbieter, Testbenutzer.InhaberA).HandleAsync(id, _repository.Stand(id), _ct);

        var verletzung = await Should.ThrowAsync<DomainRuleViolationException>(
            () => new EntwurfBearbeitenHandler(_repository, Testbenutzer.InhaberA)
                .HandleAsync(new EntwurfBearbeiten(id, [new ZutatEingabe("Butter", 100)], _repository.Stand(id), Form.Blech(60, 40, 60)), _ct));

        verletzung.Code.ShouldBe("form.hoehe_ungueltig");
        verletzung.Art.ShouldBe(Regelart.UngueltigeEingabe);
    }

    [Fact]
    public async Task Bearbeiten_setzt_die_Form_und_Details_zeigen_die_massgebliche_Form()
    {
        var id = await SachermasseAsync(Form.Ring(26, 6));
        await FreigebenUndNeuenEntwurfMitFormAsync(id, Form.Blech(60, 40, 2));

        var entwurf = await new FassungAbfragenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(id, 2, _ct);
        var details = await new RezeptAbfragenHandler(_repository, Testbenutzer.InhaberA).HandleAsync(id, _ct);

        entwurf!.Bezugsform.ShouldBe(Form.Blech(60, 40, 2));
        details!.Bezugsform.ShouldBe(Form.Ring(26, 6));
    }
}
