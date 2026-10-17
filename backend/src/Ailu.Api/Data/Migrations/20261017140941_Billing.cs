using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Billing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tier = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AutoRenew = table.Column<bool>(type: "boolean", nullable: false),
                    Sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Users_PayerUserId",
                        column: x => x.PayerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CoupleId",
                table: "Subscriptions",
                column: "CoupleId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PayerUserId",
                table: "Subscriptions",
                column: "PayerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PeriodEnd",
                table: "Subscriptions",
                column: "PeriodEnd");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Provider_StoreId",
                table: "Subscriptions",
                columns: new[] { "Provider", "StoreId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Subscriptions");
        }
    }
}
