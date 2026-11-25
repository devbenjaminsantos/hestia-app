#!/bin/sh
set -eu

# Run only on the disposable PostgreSQL service provisioned by Validate.
psql -X -v ON_ERROR_STOP=1 -d hestia_source <<'SQL'
WITH fixture AS (
    INSERT INTO "Users" (
        "Name", "Email", "EmailConfirmed", "EmailGoalAlertsEnabled", "GoalAlertThresholdPercent",
        "MonthlyReportEmailsEnabled", "MonthlyReportDay", "PublicDashboardEnabled", "IsDemoAccount",
        "PasswordHash", "FailedLoginAttempts"
    ) VALUES ('Recovery fixture', 'drill@example.invalid', false, false, 80, false, 1, false, false, '', 0)
    RETURNING "Id"
)
INSERT INTO "Transactions" ("UserId", "Description", "Category", "AmountCents", "Date", "Type", "Source", "IsRecurring")
SELECT "Id", 'Restore drill', 'Fixture', 12345, DATE '2026-01-31', 'expense', 'manual', false FROM fixture;
SQL

archive=$(mktemp)
trap 'rm -f "$archive"' EXIT
pg_dump --format=custom --no-owner --no-privileges --file="$archive" hestia_source
createdb hestia_restore
pg_restore --exit-on-error --no-owner --no-privileges --dbname=hestia_restore "$archive"

history='SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"'
source_history=$(psql -XAt -v ON_ERROR_STOP=1 -d hestia_source -c "$history")
restored_history=$(psql -XAt -v ON_ERROR_STOP=1 -d hestia_restore -c "$history")
test -n "$source_history"
test "$source_history" = "$restored_history"

restored_count=$(psql -XAt -v ON_ERROR_STOP=1 -d hestia_restore <<'SQL'
SELECT count(*) FROM "Users" u JOIN "Transactions" t ON t."UserId" = u."Id"
WHERE u."Email" = 'drill@example.invalid' AND t."AmountCents" = 12345
  AND t."Date" = DATE '2026-01-31' AND t."Description" = 'Restore drill';
SQL
)
test "$restored_count" = 1
echo "Verified migration history and restored user transaction."
