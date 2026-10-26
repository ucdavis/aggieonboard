using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class TaskStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "StepDefinitions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TaskLabel",
                table: "StepDefinitions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TaskSummary",
                table: "StepDefinitions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastPortalActivityAt",
                table: "Candidates",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CandidateId = table.Column<int>(type: "int", nullable: false),
                    CandidateStepId = table.Column<int>(type: "int", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskNotifications_CandidateSteps_CandidateStepId",
                        column: x => x.CandidateStepId,
                        principalTable: "CandidateSteps",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskNotifications_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskNotifications_CandidateId",
                table: "TaskNotifications",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskNotifications_CandidateStepId",
                table: "TaskNotifications",
                column: "CandidateStepId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskNotifications_SettledAt_OpenedAt",
                table: "TaskNotifications",
                columns: new[] { "SettledAt", "OpenedAt" });
        
            // Each step's stage and portal wording, as JourneyCatalog.Tasks (the seeder never updates existing steps).
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 1, TaskLabel = N'Add your references', TaskSummary = NULL WHERE [Key] = 'references'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 2, TaskLabel = N'Send your license and degree documents', TaskSummary = NULL WHERE [Key] = 'qualifications'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 3, TaskLabel = N'Accept your offer', TaskSummary = N'Your recruiter will send your written offer. Review it and accept it to keep your onboarding moving.' WHERE [Key] = 'offer'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 4, TaskLabel = N'Complete your employee information form', TaskSummary = NULL WHERE [Key] = 'employee-data'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Complete your background check', TaskSummary = N'Complete the background check form. We''ll let you know if anything else is needed.' WHERE [Key] = 'background-check'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Schedule your Employee Health appointment', TaskSummary = NULL WHERE [Key] = 'employee-health'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Complete your drug screen', TaskSummary = N'Complete your drug screen at a testing location and time that works for you.' WHERE [Key] = 'drug-screen'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Complete your I-9', TaskSummary = NULL WHERE [Key] = 'i9'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Name your I-9 representative', TaskSummary = NULL WHERE [Key] = 'i9-remote'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Complete the DMV Employer Pull Notice form', TaskSummary = NULL WHERE [Key] = 'dmv'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Schedule your fingerprinting', TaskSummary = NULL WHERE [Key] = 'fingerprinting'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Sign your position description', TaskSummary = NULL WHERE [Key] = 'position-description'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 5, TaskLabel = N'Claim your parking permit', TaskSummary = NULL WHERE [Key] = 'parking'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 6, TaskLabel = N'Complete your first-day forms', TaskSummary = NULL WHERE [Key] = 'cleared'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Stage = 7, TaskLabel = N'Schedule New Employee Welcome', TaskSummary = N'New Employee Welcome introduces you to UC Davis Health. Choose a session within your first 30 days.' WHERE [Key] = 'new-employee-welcome'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskNotifications");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "StepDefinitions");

            migrationBuilder.DropColumn(
                name: "TaskLabel",
                table: "StepDefinitions");

            migrationBuilder.DropColumn(
                name: "TaskSummary",
                table: "StepDefinitions");

            migrationBuilder.DropColumn(
                name: "LastPortalActivityAt",
                table: "Candidates");
        }
    }
}
