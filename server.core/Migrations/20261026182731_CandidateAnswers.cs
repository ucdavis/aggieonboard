using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class CandidateAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FingerprintAtiNumber",
                table: "Candidates",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "I9RepresentativeEmail",
                table: "Candidates",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "I9RepresentativeName",
                table: "Candidates",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FingerprintAtiNumber",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "I9RepresentativeEmail",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "I9RepresentativeName",
                table: "Candidates");
        }
    }
}
