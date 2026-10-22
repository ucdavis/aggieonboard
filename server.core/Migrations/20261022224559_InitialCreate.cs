using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Date);
                });

            migrationBuilder.CreateTable(
                name: "MessageTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    AuditReference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Sender = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    OpenQuestion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Staff",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StepDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    AuditReference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RequestMessageKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    AcknowledgmentMessageKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    VendorMessageKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    DeadlineAnchor = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DeadlineAmount = table.Column<int>(type: "int", nullable: false),
                    DeadlineUnit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DeadlineConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    DeadlineQuestion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WatchForSender = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WatchForFromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    WatchForSubject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StepDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MessageTemplateSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageTemplateId = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageTemplateSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageTemplateSections_MessageTemplates_MessageTemplateId",
                        column: x => x.MessageTemplateId,
                        principalTable: "MessageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Candidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PositionTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HiringManagerContact = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AnticipatedStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EmployeeId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RecruitingAssistantId = table.Column<int>(type: "int", nullable: true),
                    RecruiterId = table.Column<int>(type: "int", nullable: true),
                    OnboardingSpecialistId = table.Column<int>(type: "int", nullable: true),
                    RequiresDrugScreen = table.Column<bool>(type: "bit", nullable: false),
                    RequiresDmvPullNotice = table.Column<bool>(type: "bit", nullable: false),
                    RequiresFingerprinting = table.Column<bool>(type: "bit", nullable: false),
                    RequiresPositionDescription = table.Column<bool>(type: "bit", nullable: false),
                    EligibleForParking = table.Column<bool>(type: "bit", nullable: false),
                    I9Method = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Candidates_Staff_OnboardingSpecialistId",
                        column: x => x.OnboardingSpecialistId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Candidates_Staff_RecruiterId",
                        column: x => x.RecruiterId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Candidates_Staff_RecruitingAssistantId",
                        column: x => x.RecruitingAssistantId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CandidateQualifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CandidateId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentToSubmit = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateQualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateQualifications_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CandidateSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CandidateId = table.Column<int>(type: "int", nullable: false),
                    StepDefinitionId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    DueOverridden = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedById = table.Column<int>(type: "int", nullable: true),
                    StaffNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateSteps_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CandidateSteps_Staff_CompletedById",
                        column: x => x.CompletedById,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CandidateSteps_StepDefinitions_StepDefinitionId",
                        column: x => x.StepDefinitionId,
                        principalTable: "StepDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateQualifications_CandidateId",
                table: "CandidateQualifications",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_Email",
                table: "Candidates",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_OnboardingSpecialistId",
                table: "Candidates",
                column: "OnboardingSpecialistId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_RecruiterId",
                table: "Candidates",
                column: "RecruiterId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_RecruitingAssistantId",
                table: "Candidates",
                column: "RecruitingAssistantId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSteps_CandidateId_StepDefinitionId",
                table: "CandidateSteps",
                columns: new[] { "CandidateId", "StepDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSteps_CompletedById",
                table: "CandidateSteps",
                column: "CompletedById");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSteps_StepDefinitionId",
                table: "CandidateSteps",
                column: "StepDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageTemplates_Key_Version",
                table: "MessageTemplates",
                columns: new[] { "Key", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageTemplateSections_MessageTemplateId",
                table: "MessageTemplateSections",
                column: "MessageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_Email",
                table: "Staff",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StepDefinitions_Key",
                table: "StepDefinitions",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateQualifications");

            migrationBuilder.DropTable(
                name: "CandidateSteps");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "MessageTemplateSections");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Candidates");

            migrationBuilder.DropTable(
                name: "StepDefinitions");

            migrationBuilder.DropTable(
                name: "MessageTemplates");

            migrationBuilder.DropTable(
                name: "Staff");
        }
    }
}
