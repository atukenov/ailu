using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveKaspi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GiftOrders");

            migrationBuilder.DropIndex(
                name: "IX_PlanCodes_GiftOrderId",
                table: "PlanCodes");

            migrationBuilder.DropIndex(
                name: "IX_PhotoBookOrders_PaymentId",
                table: "PhotoBookOrders");

            migrationBuilder.DropColumn(
                name: "GiftOrderId",
                table: "PlanCodes");

            migrationBuilder.DropColumn(
                name: "PayUrl",
                table: "PhotoBookOrders");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "PhotoBookOrders");

            migrationBuilder.AddColumn<string>(
                name: "PaymentNote",
                table: "PhotoBookOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentNote",
                table: "PhotoBookOrders");

            migrationBuilder.AddColumn<Guid>(
                name: "GiftOrderId",
                table: "PlanCodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayUrl",
                table: "PhotoBookOrders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentId",
                table: "PhotoBookOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GiftOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaymentUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PriceKzt = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Tier = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiftOrders_Users_BuyerUserId",
                        column: x => x.BuyerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanCodes_GiftOrderId",
                table: "PlanCodes",
                column: "GiftOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBookOrders_PaymentId",
                table: "PhotoBookOrders",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GiftOrders_BuyerUserId",
                table: "GiftOrders",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftOrders_Provider_CheckoutId",
                table: "GiftOrders",
                columns: new[] { "Provider", "CheckoutId" },
                unique: true);
        }
    }
}
