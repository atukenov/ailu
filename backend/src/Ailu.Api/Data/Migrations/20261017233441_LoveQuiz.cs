using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LoveQuiz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoveQuizRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NurAwarded = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoveQuizRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoveQuizRounds_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoveQuizAnswers",
                columns: table => new
                {
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Picks = table.Column<string[]>(type: "text[]", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoveQuizAnswers", x => new { x.RoundId, x.UserId });
                    table.ForeignKey(
                        name: "FK_LoveQuizAnswers_LoveQuizRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "LoveQuizRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoveQuizAnswers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoveQuizAnswers_UserId",
                table: "LoveQuizAnswers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LoveQuizRounds_CoupleId",
                table: "LoveQuizRounds",
                column: "CoupleId",
                unique: true,
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LoveQuizRounds_CoupleId_StartedAt",
                table: "LoveQuizRounds",
                columns: new[] { "CoupleId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoveQuizAnswers");

            migrationBuilder.DropTable(
                name: "LoveQuizRounds");
        }
    }
}
