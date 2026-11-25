using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrackTransactionImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionImportReceipts",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    BatchHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionImportReceipts", x => new { x.UserId, x.BatchHash });
                    table.ForeignKey(
                        name: "FK_TransactionImportReceipts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionImportReceipts");
        }
    }
}
