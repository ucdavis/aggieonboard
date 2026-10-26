using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class StaffGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_Staff_OnboardingSpecialistId",
                table: "Candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_Staff_RecruitingAssistantId",
                table: "Candidates");

            // Recruiting assistants are now in the Recruiter group: a candidate with no recruiter keeps their
            // assistant as the recruiter. The old column is then reused, empty, as the new Unit HR slot.
            migrationBuilder.Sql("UPDATE Candidates SET RecruiterId = COALESCE(RecruiterId, RecruitingAssistantId), RecruitingAssistantId = NULL");
            migrationBuilder.Sql("UPDATE Staff SET Role = 'Recruiter' WHERE Role = 'RecruitingAssistant'");
            migrationBuilder.Sql("UPDATE Staff SET Role = 'ServiceChannel' WHERE Role = 'OnboardingSpecialist'");
            migrationBuilder.Sql("UPDATE Staff SET Role = 'UnitHr' WHERE Role = 'LearningAndDevelopment'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Owner = 'Recruiter' WHERE Owner = 'RecruitingAssistant'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Owner = 'ServiceChannel' WHERE Owner = 'OnboardingSpecialist'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Owner = 'UnitHr' WHERE Owner = 'LearningAndDevelopment'");

            migrationBuilder.RenameColumn(
                name: "RecruitingAssistantId",
                table: "Candidates",
                newName: "UnitHrId");

            migrationBuilder.RenameColumn(
                name: "OnboardingSpecialistId",
                table: "Candidates",
                newName: "ServiceChannelId");

            migrationBuilder.RenameIndex(
                name: "IX_Candidates_RecruitingAssistantId",
                table: "Candidates",
                newName: "IX_Candidates_UnitHrId");

            migrationBuilder.RenameIndex(
                name: "IX_Candidates_OnboardingSpecialistId",
                table: "Candidates",
                newName: "IX_Candidates_ServiceChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_Staff_ServiceChannelId",
                table: "Candidates",
                column: "ServiceChannelId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_Staff_UnitHrId",
                table: "Candidates",
                column: "UnitHrId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Staff SET Role = 'OnboardingSpecialist' WHERE Role = 'ServiceChannel'");
            migrationBuilder.Sql("UPDATE Staff SET Role = 'LearningAndDevelopment' WHERE Role = 'UnitHr'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Owner = 'OnboardingSpecialist' WHERE Owner = 'ServiceChannel'");
            migrationBuilder.Sql("UPDATE StepDefinitions SET Owner = 'LearningAndDevelopment' WHERE Owner = 'UnitHr'");
            migrationBuilder.Sql("UPDATE Candidates SET UnitHrId = NULL");

            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_Staff_ServiceChannelId",
                table: "Candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_Staff_UnitHrId",
                table: "Candidates");

            migrationBuilder.RenameColumn(
                name: "UnitHrId",
                table: "Candidates",
                newName: "RecruitingAssistantId");

            migrationBuilder.RenameColumn(
                name: "ServiceChannelId",
                table: "Candidates",
                newName: "OnboardingSpecialistId");

            migrationBuilder.RenameIndex(
                name: "IX_Candidates_UnitHrId",
                table: "Candidates",
                newName: "IX_Candidates_RecruitingAssistantId");

            migrationBuilder.RenameIndex(
                name: "IX_Candidates_ServiceChannelId",
                table: "Candidates",
                newName: "IX_Candidates_OnboardingSpecialistId");

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_Staff_OnboardingSpecialistId",
                table: "Candidates",
                column: "OnboardingSpecialistId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_Staff_RecruitingAssistantId",
                table: "Candidates",
                column: "RecruitingAssistantId",
                principalTable: "Staff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
