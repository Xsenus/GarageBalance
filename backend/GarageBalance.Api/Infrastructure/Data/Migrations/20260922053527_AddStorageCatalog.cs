using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storage_objects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DataClass = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LogicalKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PolicyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyRevision = table.Column<int>(type: "integer", nullable: false),
                    CommittedGeneration = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TombstonedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_objects", x => x.Id);
                    table.CheckConstraint("CK_storage_objects_generation", "\"CommittedGeneration\" >= 0");
                    table.CheckConstraint("CK_storage_objects_size", "\"SizeBytes\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "storage_object_replicas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailureDomain = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NativeLocator = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ProviderVersionId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderChecksum = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCategory = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_object_replicas", x => x.Id);
                    table.CheckConstraint("CK_storage_object_replicas_generation", "\"Generation\" > 0");
                    table.ForeignKey(
                        name: "FK_storage_object_replicas_storage_objects_StorageObjectId",
                        column: x => x.StorageObjectId,
                        principalTable: "storage_objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storage_transfer_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageObjectReplicaId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    PolicyRevision = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaximumAttempts = table.Column<int>(type: "integer", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCategory = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_storage_transfer_jobs", x => x.Id);
                    table.CheckConstraint("CK_storage_transfer_jobs_attempts", "\"AttemptCount\" >= 0 AND \"MaximumAttempts\" > 0");
                    table.CheckConstraint("CK_storage_transfer_jobs_generation", "\"Generation\" > 0");
                    table.ForeignKey(
                        name: "FK_storage_transfer_jobs_storage_object_replicas_StorageObject~",
                        column: x => x.StorageObjectReplicaId,
                        principalTable: "storage_object_replicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_storage_transfer_jobs_storage_objects_StorageObjectId",
                        column: x => x.StorageObjectId,
                        principalTable: "storage_objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_storage_object_replicas_DestinationId_State_UpdatedAtUtc",
                table: "storage_object_replicas",
                columns: new[] { "DestinationId", "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_object_replicas_State_LastVerifiedAtUtc",
                table: "storage_object_replicas",
                columns: new[] { "State", "LastVerifiedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_object_replicas_StorageObjectId_DestinationId_Gener~",
                table: "storage_object_replicas",
                columns: new[] { "StorageObjectId", "DestinationId", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_objects_PolicyId_PolicyRevision",
                table: "storage_objects",
                columns: new[] { "PolicyId", "PolicyRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_objects_State_UpdatedAtUtc",
                table: "storage_objects",
                columns: new[] { "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_objects_TenantId_DataClass_LogicalKey",
                table: "storage_objects",
                columns: new[] { "TenantId", "DataClass", "LogicalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_objects_TenantId_DataClass_OperationId",
                table: "storage_objects",
                columns: new[] { "TenantId", "DataClass", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_transfer_jobs_DestinationId_State_DueAtUtc",
                table: "storage_transfer_jobs",
                columns: new[] { "DestinationId", "State", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_transfer_jobs_IdempotencyKey",
                table: "storage_transfer_jobs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storage_transfer_jobs_State_DueAtUtc_LeaseExpiresAtUtc",
                table: "storage_transfer_jobs",
                columns: new[] { "State", "DueAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_storage_transfer_jobs_StorageObjectId",
                table: "storage_transfer_jobs",
                column: "StorageObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_storage_transfer_jobs_StorageObjectReplicaId",
                table: "storage_transfer_jobs",
                column: "StorageObjectReplicaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_transfer_jobs");

            migrationBuilder.DropTable(
                name: "storage_object_replicas");

            migrationBuilder.DropTable(
                name: "storage_objects");
        }
    }
}
