using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MftlPaymentService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPaymentsPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PaymentOptionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "application_payment_status_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationPaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RawStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NormalizedStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_payment_status_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_application_payment_status_events_application_payments_Appl~",
                        column: x => x.ApplicationPaymentId,
                        principalTable: "application_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_payment_status_events_ApplicationPaymentId",
                table: "application_payment_status_events",
                column: "ApplicationPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_application_payment_status_events_ExternalId",
                table: "application_payment_status_events",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_application_payments_ApplicationId",
                table: "application_payments",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_application_payments_ApplicationId_Verified",
                table: "application_payments",
                columns: new[] { "ApplicationId", "Verified" });

            migrationBuilder.CreateIndex(
                name: "IX_application_payments_PaymentReference",
                table: "application_payments",
                column: "PaymentReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_payment_status_events");

            migrationBuilder.DropTable(
                name: "application_payments");
        }
    }
}
