using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Memories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhotoCount",
                table: "Couples",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Couples",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Free"); // existing couples start on the free plan

            migrationBuilder.AddColumn<long>(
                name: "StorageBytes",
                table: "Couples",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "Memories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Place = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    HappenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CoupleTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Favorite = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memories_CoupleTasks_CoupleTaskId",
                        column: x => x.CoupleTaskId,
                        principalTable: "CoupleTasks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Memories_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Memories_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemoryPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemoryPhotos_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MemoryPhotos_Memories_MemoryId",
                        column: x => x.MemoryId,
                        principalTable: "Memories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemoryReactions",
                columns: table => new
                {
                    MemoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryReactions", x => new { x.MemoryId, x.UserId });
                    table.ForeignKey(
                        name: "FK_MemoryReactions_Memories_MemoryId",
                        column: x => x.MemoryId,
                        principalTable: "Memories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MemoryReactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Memories_AuthorId",
                table: "Memories",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Memories_CoupleId_HappenedOn_CreatedAt",
                table: "Memories",
                columns: new[] { "CoupleId", "HappenedOn", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Memories_CoupleTaskId",
                table: "Memories",
                column: "CoupleTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Memories_DeletedAt",
                table: "Memories",
                column: "DeletedAt",
                filter: "\"DeletedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryPhotos_CoupleId",
                table: "MemoryPhotos",
                column: "CoupleId");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryPhotos_MemoryId_Position",
                table: "MemoryPhotos",
                columns: new[] { "MemoryId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryReactions_UserId",
                table: "MemoryReactions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryPhotos");

            migrationBuilder.DropTable(
                name: "MemoryReactions");

            migrationBuilder.DropTable(
                name: "Memories");

            migrationBuilder.DropColumn(
                name: "PhotoCount",
                table: "Couples");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Couples");

            migrationBuilder.DropColumn(
                name: "StorageBytes",
                table: "Couples");
        }
    }
}
