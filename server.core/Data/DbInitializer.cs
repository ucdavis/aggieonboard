using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Core.Data;
using Server.Core.Seed;

public interface IDbInitializer
{
    Task InitializeAsync(bool includeSampleData, CancellationToken cancellationToken = default);
}

public class DbInitializer : IDbInitializer
{
    private readonly AppDbContext _db;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppDbContext db, ILogger<DbInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InitializeAsync(bool includeSampleData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Applying database migrations...");
        await _db.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Migrations applied.");

        var now = DateTimeOffset.UtcNow;

        // The journey's steps, messages, settings and holidays: every environment needs them.
        await ReferenceDataSeeder.SeedAsync(_db, now, cancellationToken);

        if (includeSampleData)
        {
            // Fictional staff and candidates only; never seed real candidate records.
            await DevelopmentSeeder.SeedAsync(_db, now, cancellationToken);
            _logger.LogInformation("Development fixtures seeded.");
        }
    }
}
