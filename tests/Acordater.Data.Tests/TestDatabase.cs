using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Acordater.Data.Tests;

/// <summary>In-memory SQLite database built from the real migrations; lives as long as the open connection.</summary>
internal sealed class TestDatabase : IDbContextFactory<AcordaterDbContext>, IDisposable
{
    readonly SqliteConnection connection = new("Data Source=:memory:");

    public TestDatabase()
    {
        connection.Open();
        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public AcordaterDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AcordaterDbContext>().UseSqlite(connection).Options);

    public void Dispose() => connection.Dispose();
}
