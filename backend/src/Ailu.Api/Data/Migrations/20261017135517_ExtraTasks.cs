using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtraTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CoupleTasks_CoupleId_Date",
                table: "CoupleTasks");

            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                table: "CoupleTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CoupleTasks_CoupleId_Date_Ordinal",
                table: "CoupleTasks",
                columns: new[] { "CoupleId", "Date", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CoupleTasks_CoupleId_Date_Ordinal",
                table: "CoupleTasks");

            migrationBuilder.DropColumn(
                name: "Ordinal",
                table: "CoupleTasks");

            migrationBuilder.CreateIndex(
                name: "IX_CoupleTasks_CoupleId_Date",
                table: "CoupleTasks",
                columns: new[] { "CoupleId", "Date" },
                unique: true);
        }
    }
}
