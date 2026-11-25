using FinanceDashboard.Api.Data;
using FinanceDashboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Api.Services.Recurring
{
    public class RecurringTransactionGenerationService
    {
        private readonly AppDbContext _context;

        public RecurringTransactionGenerationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureTransactionsGeneratedUpToTodayAsync(int userId)
        {
            var today = DateTime.UtcNow.Date;

            var dueRules = _context.RecurringRules.Where(rule =>
                rule.UserId == userId && rule.IsActive && rule.NextOccurrenceDate <= today);

            if (!_context.Database.IsRelational())
            {
                await GenerateDueRulesAsync(dueRules, today);
                return;
            }

            if (!await dueRules.AnyAsync()) return;

            // Serialize catch-up for this user's GET requests across API instances.
            // Read the rule cursor only after obtaining the lock.
            await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync();
                if (_context.Database.IsNpgsql())
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT \"Id\" FROM \"Users\" WHERE \"Id\" = {userId} FOR NO KEY UPDATE");
                }
                else
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT [Id] FROM [Users] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {userId}");
                }

                await GenerateDueRulesAsync(dueRules, today);
                await transaction.CommitAsync();
            });
        }

        private async Task GenerateDueRulesAsync(IQueryable<RecurringRule> dueRules, DateTime today)
        {
            foreach (var rule in await dueRules.OrderBy(rule => rule.Id).ToListAsync())
            {
                await EnsureRuleGeneratedUpToDateAsync(rule, today);
            }

            await _context.SaveChangesAsync();
        }

        private async Task EnsureRuleGeneratedUpToDateAsync(RecurringRule rule, DateTime today)
        {
            var tagNames = ParseTags(rule.TagsCsv);
            var cursor = rule.NextOccurrenceDate!.Value;
            var startDate = rule.StartDate.Date;
            var monthsFromStart = (cursor.Year - startDate.Year) * 12 + cursor.Month - startDate.Month;
            var nextDate = startDate.AddMonths(monthsFromStart);
            var endDate = rule.EndDate.Date;

            while (nextDate <= today && nextDate <= endDate)
            {
                var occurrenceMonth = new DateOnly(nextDate.Year, nextDate.Month, 1);
                var exists = await _context.Transactions.AnyAsync(transaction =>
                    transaction.UserId == rule.UserId &&
                    transaction.RecurringRuleId == rule.Id &&
                    transaction.RecurrenceMonth == occurrenceMonth);

                if (!exists)
                {
                    var transaction = new Transaction
                    {
                        Description = rule.Description,
                        Category = rule.Category,
                        AmountCents = rule.AmountCents,
                        Date = nextDate,
                        Type = rule.Type,
                        Source = "manual",
                        IsRecurring = true,
                        RecurrenceEndDate = rule.EndDate.Date,
                        RecurrenceGroupId = rule.PublicId,
                        RecurringRuleId = rule.Id,
                        RecurrenceMonth = occurrenceMonth,
                        FinancialAccountId = rule.FinancialAccountId,
                        UserId = rule.UserId
                    };

                    _context.Transactions.Add(transaction);
                    await AttachTagsAsync(transaction, tagNames, rule.UserId);
                }

                rule.LastGeneratedDate = nextDate;
                monthsFromStart++;
                nextDate = startDate.AddMonths(monthsFromStart);
            }

            if (nextDate > endDate)
            {
                rule.NextOccurrenceDate = null;
                rule.IsActive = false;
            }
            else
            {
                rule.NextOccurrenceDate = nextDate;
            }
        }

        private async Task AttachTagsAsync(Transaction transaction, IReadOnlyList<string> tagNames, int userId)
        {
            if (tagNames.Count == 0)
            {
                return;
            }

            var normalizedTagKeys = tagNames
                .Select(name => name.ToLowerInvariant())
                .ToList();
            var existingTags = await _context.TransactionTags
                .Where(tag =>
                    tag.UserId == userId &&
                    normalizedTagKeys.Contains(tag.Name.ToLower()))
                .ToListAsync();

            existingTags.AddRange(_context.TransactionTags.Local.Where(tag =>
                tag.UserId == userId &&
                _context.Entry(tag).State == EntityState.Added &&
                normalizedTagKeys.Contains(tag.Name.ToLowerInvariant())));
            var existingNames = existingTags
                .Select(tag => tag.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var newTags = tagNames
                .Where(name => !existingNames.Contains(name))
                .Select(name => new TransactionTag
                {
                    Name = name,
                    UserId = userId
                })
                .ToList();

            if (newTags.Count > 0)
            {
                _context.TransactionTags.AddRange(newTags);
                existingTags.AddRange(newTags);
            }

            var tagsByName = existingTags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var tagName in tagNames)
            {
                if (!tagsByName.TryGetValue(tagName, out var tag))
                {
                    continue;
                }

                transaction.TagLinks.Add(new TransactionTagLink
                {
                    Transaction = transaction,
                    TransactionTag = tag
                });
            }
        }

        public static List<string> ParseTags(string? tagsCsv)
        {
            return (tagsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag)
                .ToList();
        }

        public static string SerializeTags(IEnumerable<string>? rawTagNames)
        {
            return string.Join(",", NormalizeTagNames(rawTagNames));
        }

        public static List<string> NormalizeTagNames(IEnumerable<string>? rawTagNames)
        {
            return (rawTagNames ?? Array.Empty<string>())
                .Select(name => name?.Trim() ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Length > 40 ? name[..40] : name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();
        }
    }
}
