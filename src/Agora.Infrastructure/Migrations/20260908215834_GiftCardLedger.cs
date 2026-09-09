using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GiftCardLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GiftCardEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GiftCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    BalanceAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceReturnId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftCardEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiftCardEntries_GiftCards_GiftCardId",
                        column: x => x.GiftCardId,
                        principalTable: "GiftCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GiftCardEntries_GiftCardId_RecordedVersion",
                table: "GiftCardEntries",
                columns: new[] { "GiftCardId", "RecordedVersion" },
                unique: true);

            // Provider values are already cents and UTC ticks. Do not multiply Balance by 100 again.
            // Reusing the card GUID as its opening entry's independent-table ID is deterministic and unique.
            // SQLite records application time (whole UTC seconds), including when applying a generated SQL script.
            migrationBuilder.Sql("""
                INSERT INTO "GiftCardEntries"
                    ("Id", "GiftCardId", "RecordedVersion", "Kind", "Amount", "Currency", "BalanceAfter", "RecordedAt", "SourceOrderId", "SourceReturnId")
                SELECT "Id", "Id", "Version", 0, "Balance", "Currency", "Balance",
                    621355968000000000 + CAST(strftime('%s', 'now') AS INTEGER) * 10000000, NULL, NULL
                FROM "GiftCards";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GiftCardEntries");
        }
    }
}
