using Acordater.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Acordater.Data;

public sealed class AcordaterDbContext(DbContextOptions<AcordaterDbContext> options) : DbContext(options)
{
    public DbSet<Reminder> Reminders => Set<Reminder>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot order or compare DateTimeOffset stored as text; the binary form sorts by instant.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var reminder = modelBuilder.Entity<Reminder>();
        reminder.HasKey(r => r.Id);
        reminder.Property(r => r.Text).IsRequired();
        reminder.Property(r => r.NextReminderAt);
        reminder.Property(r => r.CompletedAt);
        reminder.Ignore(r => r.IsDone);
        reminder.HasIndex(r => new { r.CompletedAt, r.NextReminderAt });
    }
}
