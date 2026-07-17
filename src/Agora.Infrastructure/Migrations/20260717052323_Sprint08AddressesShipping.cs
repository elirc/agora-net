using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint08AddressesShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WeightGrams",
                table: "ProductVariants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "EstimatedDeliveryFrom",
                table: "Orders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EstimatedDeliveryTo",
                table: "Orders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingMethodCode",
                table: "Orders",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingMethodName",
                table: "Orders",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Address_FullName = table.Column<string>(type: "TEXT", nullable: false),
                    Address_Line1 = table.Column<string>(type: "TEXT", nullable: false),
                    Address_Line2 = table.Column<string>(type: "TEXT", nullable: true),
                    Address_City = table.Column<string>(type: "TEXT", nullable: false),
                    Address_Region = table.Column<string>(type: "TEXT", nullable: false),
                    Address_PostalCode = table.Column<string>(type: "TEXT", nullable: false),
                    Address_Country = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAddresses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShippingMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RateType = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseRate = table.Column<long>(type: "INTEGER", nullable: false),
                    PerKgRate = table.Column<long>(type: "INTEGER", nullable: false),
                    FreeThreshold = table.Column<long>(type: "INTEGER", nullable: true),
                    MinDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingMethods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddresses_CustomerId",
                table: "CustomerAddresses",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingMethods_Code",
                table: "ShippingMethods",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerAddresses");

            migrationBuilder.DropTable(
                name: "ShippingMethods");

            migrationBuilder.DropColumn(
                name: "WeightGrams",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "EstimatedDeliveryFrom",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "EstimatedDeliveryTo",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingMethodCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingMethodName",
                table: "Orders");
        }
    }
}
