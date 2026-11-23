using Rezeptverwaltung.SharedKernel;

namespace Rezeptverwaltung.Rezeptur.Domain.Tests;

internal static class FachregelAssertions
{
    /// <summary>
    /// Prüft den stabilen Fehlercode UND dass es eine Meldung für Menschen gibt
    /// (sie wird über die API ausgeliefert und später von Alexa vorgelesen).
    /// </summary>
    public static void ShouldHaveCode(this DomainRuleViolationException verletzung, string erwarteterCode)
    {
        verletzung.Code.ShouldBe(erwarteterCode);
        verletzung.Message.ShouldNotBeNullOrWhiteSpace();
    }
}
