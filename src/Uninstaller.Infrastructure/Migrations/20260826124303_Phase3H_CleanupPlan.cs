using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uninstaller.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3H_CleanupPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExitCode",
                table: "UninstallSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "UninstallSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessId",
                table: "UninstallSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Strategy",
                table: "UninstallSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationResult",
                table: "UninstallSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "EstimatedSize",
                table: "Applications",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPresent",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemComponent",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsWindowsInstaller",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeen",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "RegistryKeyName",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegistrySource",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CleanupPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UninstallSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Warnings = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CleanupPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CleanupPlans_UninstallSessions_UninstallSessionId",
                        column: x => x.UninstallSessionId,
                        principalTable: "UninstallSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CleanupPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CleanupPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtifactType = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Classification = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfidenceScore = table.Column<int>(type: "INTEGER", nullable: false),
                    IsProtected = table.Column<bool>(type: "INTEGER", nullable: false),
                    Recommended = table.Column<bool>(type: "INTEGER", nullable: false),
                    RiskLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Reasons = table.Column<string>(type: "TEXT", nullable: false),
                    AppliedRules = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CleanupPlanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CleanupPlanItems_CleanupPlans_CleanupPlanId",
                        column: x => x.CleanupPlanId,
                        principalTable: "CleanupPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Name",
                table: "Applications",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_RegistryKeyName",
                table: "Applications",
                column: "RegistryKeyName");

            migrationBuilder.CreateIndex(
                name: "IX_CleanupPlanItems_CleanupPlanId",
                table: "CleanupPlanItems",
                column: "CleanupPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CleanupPlans_UninstallSessionId",
                table: "CleanupPlans",
                column: "UninstallSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CleanupPlanItems");

            migrationBuilder.DropTable(
                name: "CleanupPlans");

            migrationBuilder.DropIndex(
                name: "IX_Applications_Name",
                table: "Applications");

            migrationBuilder.DropIndex(
                name: "IX_Applications_RegistryKeyName",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ExitCode",
                table: "UninstallSessions");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "UninstallSessions");

            migrationBuilder.DropColumn(
                name: "ProcessId",
                table: "UninstallSessions");

            migrationBuilder.DropColumn(
                name: "Strategy",
                table: "UninstallSessions");

            migrationBuilder.DropColumn(
                name: "VerificationResult",
                table: "UninstallSessions");

            migrationBuilder.DropColumn(
                name: "EstimatedSize",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "IsPresent",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "IsSystemComponent",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "IsWindowsInstaller",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "LastSeen",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "RegistryKeyName",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "RegistrySource",
                table: "Applications");
        }
    }
}
