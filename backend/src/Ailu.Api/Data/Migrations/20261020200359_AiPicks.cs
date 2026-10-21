using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiPicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "DailyTasks",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "DailyTasks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "CoupleId",
                table: "DailyTasks",
                type: "uuid",
                nullable: true);

            // Library tasks are seeded with ids picked by hand; personal tasks take generated ids from far above them.
            migrationBuilder.Sql("ALTER TABLE \"DailyTasks\" ALTER COLUMN \"Id\" RESTART WITH 100000;");

            migrationBuilder.CreateTable(
                name: "AiPicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Budget = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiPicks_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiPicks_Users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiPickItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PickId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    TitleKk = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BodyKk = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    CostKzt = table.Column<int>(type: "integer", nullable: false),
                    DurationMin = table.Column<int>(type: "integer", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SavedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPickItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiPickItems_AiPicks_PickId",
                        column: x => x.PickId,
                        principalTable: "AiPicks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiPickItems_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiPickItems_Users_SavedById",
                        column: x => x.SavedById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 1,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 2,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 3,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 4,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 5,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 6,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 7,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 8,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 9,
                column: "CoupleId",
                value: null);

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 10,
                column: "CoupleId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_CoupleId",
                table: "DailyTasks",
                column: "CoupleId");

            migrationBuilder.CreateIndex(
                name: "IX_AiPickItems_CoupleId_SavedAt",
                table: "AiPickItems",
                columns: new[] { "CoupleId", "SavedAt" },
                filter: "\"SavedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiPickItems_PickId_Position",
                table: "AiPickItems",
                columns: new[] { "PickId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiPickItems_SavedById",
                table: "AiPickItems",
                column: "SavedById");

            migrationBuilder.CreateIndex(
                name: "IX_AiPicks_CoupleId_CreatedAt",
                table: "AiPicks",
                columns: new[] { "CoupleId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiPicks_CoupleId_Month",
                table: "AiPicks",
                columns: new[] { "CoupleId", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_AiPicks_RequestedById",
                table: "AiPicks",
                column: "RequestedById");

            migrationBuilder.AddForeignKey(
                name: "FK_DailyTasks_Couples_CoupleId",
                table: "DailyTasks",
                column: "CoupleId",
                principalTable: "Couples",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyTasks_Couples_CoupleId",
                table: "DailyTasks");

            migrationBuilder.DropTable(
                name: "AiPickItems");

            migrationBuilder.DropTable(
                name: "AiPicks");

            migrationBuilder.DropIndex(
                name: "IX_DailyTasks_CoupleId",
                table: "DailyTasks");

            migrationBuilder.DropColumn(
                name: "CoupleId",
                table: "DailyTasks");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "DailyTasks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "DailyTasks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);
        }
    }
}
