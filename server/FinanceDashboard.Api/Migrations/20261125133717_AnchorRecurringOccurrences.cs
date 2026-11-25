using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnchorRecurringOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurrenceMonth",
                table: "Transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinancialAccountId",
                table: "RecurringRules",
                type: "integer",
                nullable: true);

            // Existing postings may have been edited, moved between months, or duplicated.
            // Leave their occurrence keys unset and preserve the rule's generation cursor.
            migrationBuilder.Sql("""
                WITH accounts AS (
                    SELECT r."Id" AS rule_id, r."UserId" AS user_id,
                           min(t."FinancialAccountId") AS account_id
                    FROM "RecurringRules" r
                    JOIN "Transactions" t ON t."RecurringRuleId" = r."Id" AND t."UserId" = r."UserId"
                    GROUP BY r."Id", r."UserId", r."StartDate"
                    HAVING count(DISTINCT t."FinancialAccountId") = 1
                       AND count(*) FILTER (WHERE t."Date" = r."StartDate") = 1
                       AND count(t."FinancialAccountId") FILTER (WHERE t."Date" = r."StartDate") = 1
                )
                UPDATE "RecurringRules" r
                SET "FinancialAccountId" = accounts.account_id
                FROM accounts
                JOIN "FinancialAccounts" a ON a."Id" = accounts.account_id AND a."UserId" = accounts.user_id
                WHERE r."Id" = accounts.rule_id;

                WITH anchored AS (
                    SELECT "Id", ("StartDate" + make_interval(months =>
                        (extract(year FROM "NextOccurrenceDate")::int - extract(year FROM "StartDate")::int) * 12
                        + extract(month FROM "NextOccurrenceDate")::int - extract(month FROM "StartDate")::int
                    ))::date AS next_date
                    FROM "RecurringRules"
                    WHERE "IsActive" AND "NextOccurrenceDate" IS NOT NULL
                )
                UPDATE "RecurringRules" r
                SET "NextOccurrenceDate" = CASE WHEN anchored.next_date <= r."EndDate" THEN anchored.next_date END,
                    "IsActive" = anchored.next_date <= r."EndDate"
                FROM anchored
                WHERE r."Id" = anchored."Id" AND r."NextOccurrenceDate" <> anchored.next_date;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RecurringRuleId_RecurrenceMonth",
                table: "Transactions",
                columns: new[] { "RecurringRuleId", "RecurrenceMonth" },
                unique: true,
                filter: "\"RecurrenceMonth\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringRules_FinancialAccountId",
                table: "RecurringRules",
                column: "FinancialAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringRules_FinancialAccounts_FinancialAccountId",
                table: "RecurringRules",
                column: "FinancialAccountId",
                principalTable: "FinancialAccounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringRules_FinancialAccounts_FinancialAccountId",
                table: "RecurringRules");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RecurringRuleId_RecurrenceMonth",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_RecurringRules_FinancialAccountId",
                table: "RecurringRules");

            migrationBuilder.DropColumn(
                name: "RecurrenceMonth",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "FinancialAccountId",
                table: "RecurringRules");
        }
    }
}
