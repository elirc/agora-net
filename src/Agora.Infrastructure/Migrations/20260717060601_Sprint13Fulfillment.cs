using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint13Fulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fulfillments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Carrier = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TrackingNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fulfillments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fulfillments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FulfillmentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FulfillmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FulfillmentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FulfillmentItems_Fulfillments_FulfillmentId",
                        column: x => x.FulfillmentId,
                        principalTable: "Fulfillments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FulfillmentItems_FulfillmentId",
                table: "FulfillmentItems",
                column: "FulfillmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FulfillmentItems_OrderItemId",
                table: "FulfillmentItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Fulfillments_Number",
                table: "Fulfillments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fulfillments_OrderId",
                table: "Fulfillments",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FulfillmentItems");

            migrationBuilder.DropTable(
                name: "Fulfillments");
        }
    }
}
