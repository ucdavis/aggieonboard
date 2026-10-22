using Microsoft.EntityFrameworkCore;

namespace Server.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure entity properties and relationships here if needed
        base.OnModelCreating(modelBuilder);
    }
}
