using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogImportStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalJson = table.Column<string>(type: "TEXT", nullable: false),
                    Digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ErrorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    AppliedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogImportResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogImportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogImportResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogImportResults_CatalogImports_CatalogImportId",
                        column: x => x.CatalogImportId,
                        principalTable: "CatalogImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogImportResults_CatalogImportId_Position",
                table: "CatalogImportResults",
                columns: new[] { "CatalogImportId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogImportResults_CatalogImportId_RowKey",
                table: "CatalogImportResults",
                columns: new[] { "CatalogImportId", "RowKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogImports_CreatedAt_Id",
                table: "CatalogImports",
                columns: new[] { "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogImportResults");

            migrationBuilder.DropTable(
                name: "CatalogImports");
        }
    }
}
