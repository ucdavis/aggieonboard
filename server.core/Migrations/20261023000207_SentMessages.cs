using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class SentMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CandidateId = table.Column<int>(type: "int", nullable: false),
                    MessageTemplateId = table.Column<int>(type: "int", nullable: false),
                    MessageKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AuditReference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ReplyTo = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Html = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentByStaffId = table.Column<int>(type: "int", nullable: true),
                    SentByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Automatic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentMessages_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SentMessages_MessageTemplates_MessageTemplateId",
                        column: x => x.MessageTemplateId,
                        principalTable: "MessageTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SentMessages_Staff_SentByStaffId",
                        column: x => x.SentByStaffId,
                        principalTable: "Staff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_CandidateId_SentAt",
                table: "SentMessages",
                columns: new[] { "CandidateId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_MessageTemplateId",
                table: "SentMessages",
                column: "MessageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_SentByStaffId",
                table: "SentMessages",
                column: "SentByStaffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentMessages");
        }
    }
}
