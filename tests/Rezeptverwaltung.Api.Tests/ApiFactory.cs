using Microsoft.AspNetCore.Mvc.Testing;

namespace Rezeptverwaltung.Api.Tests;

/// <summary>Startet die komplette API im Speicher. Eine Instanz pro Testklasse.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>;
