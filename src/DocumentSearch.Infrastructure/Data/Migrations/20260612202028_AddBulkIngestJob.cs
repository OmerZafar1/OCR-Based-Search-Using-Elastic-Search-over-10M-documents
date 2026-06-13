using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentSearch.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkIngestJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BulkIngestJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceDirectory = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FilesDiscovered = table.Column<long>(type: "bigint", nullable: false),
                    Registered = table.Column<long>(type: "bigint", nullable: false),
                    Skipped = table.Column<long>(type: "bigint", nullable: false),
                    Enqueued = table.Column<long>(type: "bigint", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkIngestJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_StoragePath",
                table: "Documents",
                column: "StoragePath");

            migrationBuilder.CreateIndex(
                name: "IX_BulkIngestJobs_StartedAt",
                table: "BulkIngestJobs",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulkIngestJobs");

            migrationBuilder.DropIndex(
                name: "IX_Documents_StoragePath",
                table: "Documents");
        }
    }
}
