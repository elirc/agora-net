using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint12TaxGiftCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TaxCategoryId",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GiftCardAmount",
                table: "Orders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "GiftCardCode",
                table: "Orders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GiftCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    InitialBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DefaultRate = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxZones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxZoneRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxCategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rate = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxZoneRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxZoneRates_TaxCategories_TaxCategoryId",
                        column: x => x.TaxCategoryId,
                        principalTable: "TaxCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaxZoneRates_TaxZones_TaxZoneId",
                        column: x => x.TaxZoneId,
                        principalTable: "TaxZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TaxCategoryId",
                table: "Products",
                column: "TaxCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftCards_Code",
                table: "GiftCards",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategories_Code",
                table: "TaxCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxZoneRates_TaxCategoryId",
                table: "TaxZoneRates",
                column: "TaxCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxZoneRates_TaxZoneId_TaxCategoryId",
                table: "TaxZoneRates",
                columns: new[] { "TaxZoneId", "TaxCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxZones_Code",
                table: "TaxZones",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxZones_Country_Region",
                table: "TaxZones",
                columns: new[] { "Country", "Region" });

            migrationBuilder.AddForeignKey(
                name: "FK_Products_TaxCategories_TaxCategoryId",
                table: "Products",
                column: "TaxCategoryId",
                principalTable: "TaxCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_TaxCategories_TaxCategoryId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "GiftCards");

            migrationBuilder.DropTable(
                name: "TaxZoneRates");

            migrationBuilder.DropTable(
                name: "TaxCategories");

            migrationBuilder.DropTable(
                name: "TaxZones");

            migrationBuilder.DropIndex(
                name: "IX_Products_TaxCategoryId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TaxCategoryId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "GiftCardAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GiftCardCode",
                table: "Orders");
        }
    }
}
