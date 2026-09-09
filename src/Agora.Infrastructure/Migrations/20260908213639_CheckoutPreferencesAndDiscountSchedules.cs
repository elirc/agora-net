using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckoutPreferencesAndDiscountSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "StartsAt",
                table: "DiscountCodes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CheckoutPreferences",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShippingAddressId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ShippingMethodCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutPreferences", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_CheckoutPreferences_CustomerAddresses_ShippingAddressId",
                        column: x => x.ShippingAddressId,
                        principalTable: "CustomerAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CheckoutPreferences_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutPreferences_ShippingAddressId",
                table: "CheckoutPreferences",
                column: "ShippingAddressId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutPreferences");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "DiscountCodes");
        }
    }
}
