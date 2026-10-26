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
    /// The sandbox's fictional sign-ins, one per kind of user. Their AggieOnboard roles come from the
    /// staff whitelist like any sign-in (DevelopmentSeeder): Chris Alvarez is a recruiter, Pat Kim a
    /// Service Channel specialist, Robin Hale Unit HR, Sample User a Service Channel admin, Schedule User
    /// a police-department team member (the schedule only), and Basic User is on no list at all.
    /// The order here is the order on the sign-in page.
    /// </summary>
    public static readonly IReadOnlyList<SandboxPersona> All =
    [
        new("recruiter", "sandbox-recruiter", "Chris Alvarez", "sandbox-10004", "chris.alvarez@example.test"),
        new("service", "sandbox-service", "Pat Kim", "sandbox-10005", "pat.kim@example.test"),
        new("unithr", "sandbox-unithr", "Robin Hale", "sandbox-10006", "robin.hale@example.test"),
        new("sample", "sandbox-sample", "Sample User", "sandbox-10001", "sample@example.test"),
        new("scheduler", "sandbox-scheduler", "Schedule User", "sandbox-10003", "scheduler@example.test"),
        new("basic", "sandbox-basic", "Basic User", "sandbox-10002", "basic@example.test"),
    ];

    public sealed record SandboxPersona(string Key, string Id, string Name, string IamId, string Email);

    public static ClaimsPrincipal? CreatePrincipal(string? persona)
    {
        var p = All.FirstOrDefault(x => x.Key == persona);
        if (p is null)
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, p.Id),
            new(ClaimTypes.Name, p.Name),
            new("name", p.Name),
            new("preferred_username", p.Email),
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
