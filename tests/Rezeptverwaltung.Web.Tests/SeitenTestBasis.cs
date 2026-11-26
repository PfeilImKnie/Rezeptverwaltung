using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Rezeptverwaltung.Web.Client.Api;

namespace Rezeptverwaltung.Web.Tests;

/// <summary>Gemeinsame Grundlage der Seitentests (bUnit): API-Attrappe und angemeldete Person mit Rechten.</summary>
public abstract class SeitenTestBasis : BunitContext
{
    internal FakeRezeptApi Api { get; } = new();

    protected static readonly Berechtigungen Mitarbeiter = new(true, false, false);
    protected static readonly Berechtigungen Konditor = new(true, true, false);
    protected static readonly Berechtigungen Inhaber = new(true, true, true);

    protected static readonly RezeptInhalt Sachertorte = new(
        [new Zutat("Butter", 140), new Zutat("Zucker", 110), new Zutat("Eier", 300)],
        Form.AlsRing(26, 6));

    protected SeitenTestBasis()
    {
        Services.AddSingleton<IRezeptApi>(Api);
    }

    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    protected static SitzungsInfo Als(Berechtigungen rechte) => new("Anna", "abmelde-token", rechte, null);

    protected IRenderedComponent<T> Seite<T>(Berechtigungen rechte, Action<ComponentParameterCollectionBuilder<T>>? parameter = null)
        where T : IComponent =>
        Render<T>(p =>
        {
            p.AddCascadingValue(Als(rechte));
            parameter?.Invoke(p);
        });
}
