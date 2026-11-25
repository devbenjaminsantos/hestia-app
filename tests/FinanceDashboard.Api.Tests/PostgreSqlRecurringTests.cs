using FinanceDashboard.Api.Data;
using FinanceDashboard.Api.Models;
using FinanceDashboard.Api.Services.Recurring;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FinanceDashboard.Api.Tests;

internal sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HESTIA_TEST_POSTGRES")))
            Skip = "Set HESTIA_TEST_POSTGRES to a disposable, migrated PostgreSQL database.";
    }
}

public class PostgreSqlRecurringTests
{
    [PostgreSqlFact]
    public async Task ConcurrentReadsGenerateOneOccurrencePerMonth()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("HESTIA_TEST_POSTGRES")!)
            .Options;
        await using var setup = new AppDbContext(options);
        var user = new User { Name = "Recurring test", Email = $"{Guid.NewGuid():N}@example.invalid" };
        var account = new FinancialAccount { User = user, Provider = "manual", AccountName = "Test" };
        setup.FinancialAccounts.Add(account);
        await setup.SaveChangesAsync();

        try
        {
            var rule = new RecurringRule
            {
                PublicId = Guid.NewGuid().ToString("N"), UserId = user.Id, FinancialAccountId = account.Id,
                Description = "Monthly test", Category = "Test", Type = "expense", AmountCents = 100,
                StartDate = new DateTime(2024, 1, 31), LastGeneratedDate = new DateTime(2024, 1, 31),
                NextOccurrenceDate = new DateTime(2024, 2, 29), EndDate = new DateTime(2024, 4, 30),
                CreatedAtUtc = DateTime.UtcNow
            };

            // The January transaction was edited to a date in March. Its original month is immutable.
            setup.Transactions.Add(new Transaction
            {
                UserId = user.Id, Description = rule.Description, Category = rule.Category, Type = rule.Type,
                AmountCents = rule.AmountCents, Date = new DateTime(2024, 3, 31), IsRecurring = true,
                RecurringRule = rule, RecurrenceGroupId = rule.PublicId, RecurrenceMonth = new DateOnly(2024, 1, 1),
                FinancialAccountId = account.Id
            });
            await setup.SaveChangesAsync();

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var workerCount = 0;
            async Task CatchUpAsync()
            {
                await using var context = new AppDbContext(options);
                await context.Database.OpenConnectionAsync();
                if (Interlocked.Increment(ref workerCount) == 3) ready.SetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await new RecurringTransactionGenerationService(context).EnsureTransactionsGeneratedUpToTodayAsync(user.Id);
            }
            await Task.WhenAll(CatchUpAsync(), CatchUpAsync(), CatchUpAsync());

            var rows = await setup.Transactions.AsNoTracking().Where(t => t.RecurringRuleId == rule.Id)
                .OrderBy(t => t.RecurrenceMonth).ToListAsync();
            Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(t => t.RecurrenceMonth!.Value.Month));
            Assert.Equal(new[] { 31, 29, 31, 30 }, rows.Select(t => t.Date.Day));
            Assert.All(rows, t => Assert.Equal(account.Id, t.FinancialAccountId));
            Assert.False(await setup.RecurringRules.Where(r => r.Id == rule.Id).Select(r => r.IsActive).SingleAsync());

            var duplicate = rows[^1];
            duplicate.Id = 0;
            duplicate.Date = duplicate.Date.AddDays(-1);
            setup.Transactions.Add(duplicate);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => setup.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }
        finally
        {
            await setup.Transactions.Where(t => t.UserId == user.Id).ExecuteDeleteAsync();
            await setup.RecurringRules.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();
            await setup.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }
    }
}
