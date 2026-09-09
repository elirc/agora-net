using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agora.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableWorkAndCatalogSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DeletedAt",
                table: "WebhookSubscriptions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WebhookSubscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DestinationUrl",
                table: "WebhookDeliveries",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "DueAt",
                table: "WebhookDeliveries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "WebhookDeliveries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryStartsAtAttempt",
                table: "WebhookDeliveries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LeaseExpiresAt",
                table: "WebhookDeliveries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LeaseGeneration",
                table: "WebhookDeliveries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "WebhookDeliveries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CatalogRevision",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "CatalogChanges",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: true),
                    PayloadByteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogChanges", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "CatalogFeedStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCommittedSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    LastPurgedSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogFeedStates", x => x.Id);
                    table.CheckConstraint("CK_CatalogFeedStates_Singleton", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SecretDigest = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    Scopes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationApiKeys", x => x.Id);
                    table.CheckConstraint("CK_IntegrationApiKeys_DigestLength", "length(SecretDigest) = 32");
                });

            migrationBuilder.CreateTable(
                name: "OrderHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReleasedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReleasedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderHolds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportExportJob",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequesterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaidFrom = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidTo = table.Column<long>(type: "INTEGER", nullable: false),
                    QueryVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    ClaimCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CancellationRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceSnapshotAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ArtifactExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportExportJob", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportExportJob_Customers_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseAssignments",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseAssignments", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_WarehouseAssignments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    ReservedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SendInitiatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FinishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookAttempts_WebhookDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "WebhookDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookReplayBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestDigest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookReplayBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookReplayBatches_WebhookSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "WebhookSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportExportArtifact",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", maxLength: 10485760, nullable: false),
                    Digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportExportArtifact", x => x.JobId);
                    table.ForeignKey(
                        name: "FK_ReportExportArtifact_ReportExportJob_JobId",
                        column: x => x.JobId,
                        principalTable: "ReportExportJob",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookReplayResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookReplayResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookReplayResults_WebhookReplayBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "WebhookReplayBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CatalogFeedStates",
                columns: new[] { "Id", "LastCommittedSequence", "LastPurgedSequence", "Version" },
                values: new object[] { 1, 0L, 0L, 0L });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_EventId_SubscriptionId",
                table: "WebhookDeliveries",
                columns: new[] { "EventId", "SubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_CreatedAt_Number",
                table: "Orders",
                columns: new[] { "CustomerId", "CreatedAt", "Number" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogChanges_CreatedAt",
                table: "CatalogChanges",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogChanges_ProductId_ProductRevision",
                table: "CatalogChanges",
                columns: new[] { "ProductId", "ProductRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationApiKeys_CreatedAt_Id",
                table: "IntegrationApiKeys",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationApiKeys_ExpiresAt",
                table: "IntegrationApiKeys",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHolds_OrderId",
                table: "OrderHolds",
                column: "OrderId",
                unique: true,
                filter: "IsActive = 1");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHolds_OrderId_CreatedAt_Id",
                table: "OrderHolds",
                columns: new[] { "OrderId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportExportJob_RequesterId_Status",
                table: "ReportExportJob",
                columns: new[] { "RequesterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseAssignments_AssignmentId",
                table: "WarehouseAssignments",
                column: "AssignmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookAttempts_DeliveryId_AttemptNumber",
                table: "WebhookAttempts",
                columns: new[] { "DeliveryId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookReplayBatches_SubscriptionId",
                table: "WebhookReplayBatches",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookReplayResults_BatchId_EventId",
                table: "WebhookReplayResults",
                columns: new[] { "BatchId", "EventId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_OutboxEvents_EventId",
                table: "WebhookDeliveries",
                column: "EventId",
                principalTable: "OutboxEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Old workers must be stopped before this migration. Preserve their
            // recorded evidence; only future claims have individual attempt rows.
            // SQLite stores DateTimeOffset as UTC ticks in this application.
            migrationBuilder.Sql("""
                UPDATE WebhookDeliveries
                SET DestinationUrl = COALESCE(
                        (SELECT Url FROM WebhookSubscriptions
                         WHERE WebhookSubscriptions.Id = WebhookDeliveries.SubscriptionId), ''),
                    HistoryStartsAtAttempt = AttemptCount + 1,
                    DueAt = CASE
                        WHEN Status IN (0, 2) AND AttemptCount < 5
                        THEN (CAST(strftime('%s', 'now') AS INTEGER) + 62135596800) * 10000000
                        ELSE NULL
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_OutboxEvents_EventId",
                table: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "CatalogChanges");

            migrationBuilder.DropTable(
                name: "CatalogFeedStates");

            migrationBuilder.DropTable(
                name: "IntegrationApiKeys");

            migrationBuilder.DropTable(
                name: "OrderHolds");

            migrationBuilder.DropTable(
                name: "OutboxEvents");

            migrationBuilder.DropTable(
                name: "ReportExportArtifact");

            migrationBuilder.DropTable(
                name: "WarehouseAssignments");

            migrationBuilder.DropTable(
                name: "WebhookAttempts");

            migrationBuilder.DropTable(
                name: "WebhookReplayResults");

            migrationBuilder.DropTable(
                name: "ReportExportJob");

            migrationBuilder.DropTable(
                name: "WebhookReplayBatches");

            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_EventId_SubscriptionId",
                table: "WebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerId_CreatedAt_Number",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "WebhookSubscriptions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "WebhookSubscriptions");

            migrationBuilder.DropColumn(
                name: "DestinationUrl",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "HistoryStartsAtAttempt",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LeaseGeneration",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "CatalogRevision",
                table: "Products");
        }
    }
}
