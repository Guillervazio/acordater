using Acordater.Core;
using Microsoft.EntityFrameworkCore;

namespace Acordater.Data;

/// <summary>Persistence for reminders. Each call uses its own short-lived context.</summary>
public sealed class ReminderStore(IDbContextFactory<AcordaterDbContext> contextFactory)
{
    /// <summary>Not-done reminders, soonest first.</summary>
    public async Task<IReadOnlyList<Reminder>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Reminders.AsNoTracking()
            .Where(r => r.CompletedAt == null)
            .OrderBy(r => r.NextReminderAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Reminders.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Reminders.Update(reminder);
        await db.SaveChangesAsync(cancellationToken);
    }
}
