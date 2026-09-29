using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Acordater.Data;

/// <summary>Used only by `dotnet ef` to create migrations; the app configures its own database path.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AcordaterDbContext>
{
    public AcordaterDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AcordaterDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
