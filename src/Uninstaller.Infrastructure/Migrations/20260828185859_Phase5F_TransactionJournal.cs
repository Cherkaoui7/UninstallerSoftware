using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uninstaller.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5F_TransactionJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ArtifactType",
                table: "Backups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedRegistryHive",
                table: "Backups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedRegistryKeyPath",
                table: "Backups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedShortcutTarget",
                table: "Backups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "Backups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Size",
                table: "Backups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Backups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VerificationStatus",
                table: "Backups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TransactionJournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TransactionType = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionJournalEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionJournalEntries_ItemId_SequenceNumber",
                table: "TransactionJournalEntries",
                columns: new[] { "ItemId", "SequenceNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionJournalEntries");

            migrationBuilder.DropColumn(
                name: "ArtifactType",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "ExpectedRegistryHive",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "ExpectedRegistryKeyPath",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "ExpectedShortcutTarget",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "Backups");
        }
    }
}
