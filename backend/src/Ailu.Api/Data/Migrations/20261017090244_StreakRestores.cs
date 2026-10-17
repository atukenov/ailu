using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StreakRestores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StreakLostDays",
                table: "Couples",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StreakLostLastDay",
                table: "Couples",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StreakRestartedOn",
                table: "Couples",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StreakLostDays",
                table: "Couples");

            migrationBuilder.DropColumn(
                name: "StreakLostLastDay",
                table: "Couples");

            migrationBuilder.DropColumn(
                name: "StreakRestartedOn",
                table: "Couples");
        }
    }
}
