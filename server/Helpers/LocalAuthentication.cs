using System.Security.Claims;

namespace Server.Helpers;

public static class LocalAuthentication
{
    public const string Scheme = "LocalSandbox";

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>("Auth:UseLocal");
        if (enabled && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Auth:UseLocal is only allowed in the Development environment.");
        }

        return enabled;
    }

    /// <summary>
    /// The sandbox's fictional sign-ins. Their AggieOnboard roles come from the staff whitelist like
    /// any sign-in (DevelopmentSeeder): Sample User is an onboarding specialist and admin, Schedule
    /// User a police-department team member who sees only the schedule, and Basic User is not staff.
    /// </summary>
    private static readonly Dictionary<string, (string Id, string Name, string IamId)> Personas = new()
    {
        ["sample"] = ("sandbox-sample", "Sample User", "sandbox-10001"),
        ["basic"] = ("sandbox-basic", "Basic User", "sandbox-10002"),
        ["scheduler"] = ("sandbox-scheduler", "Schedule User", "sandbox-10003"),
    };

    public static ClaimsPrincipal? CreatePrincipal(string? persona)
    {
        if (persona is null || !Personas.TryGetValue(persona, out var p))
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, p.Id),
            new(ClaimTypes.Name, p.Name),
            new("name", p.Name),
            new("preferred_username", $"{persona}@example.test"),
            new("ucdPersonIAMID", p.IamId),
            new(ClaimTypes.Role, "User"),
        };

        if (persona == "sample")
        {
            claims.Add(new Claim(ClaimTypes.Role, "SampleRole"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }
}
