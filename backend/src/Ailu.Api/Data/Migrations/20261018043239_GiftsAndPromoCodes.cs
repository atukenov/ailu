using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GiftsAndPromoCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GiftOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Tier = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    PriceKzt = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CheckoutId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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

            migrationBuilder.CreateTable(
                name: "PlanCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Tier = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    MaxRedemptions = table.Column<int>(type: "integer", nullable: true),
                    Redemptions = table.Column<int>(type: "integer", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GiftOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodeRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_PlanCodes_CodeId",
                        column: x => x.CodeId,
                        principalTable: "PlanCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodeRedemptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRedemptions_CodeId_CoupleId",
                table: "CodeRedemptions",
                columns: new[] { "CodeId", "CoupleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeRedemptions_CodeId_UserId",
                table: "CodeRedemptions",
                columns: new[] { "CodeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeRedemptions_CoupleId",
                table: "CodeRedemptions",
                column: "CoupleId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeRedemptions_SubscriptionId",
                table: "CodeRedemptions",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeRedemptions_UserId",
                table: "CodeRedemptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftOrders_BuyerUserId",
                table: "GiftOrders",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftOrders_Provider_CheckoutId",
                table: "GiftOrders",
                columns: new[] { "Provider", "CheckoutId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanCodes_Code",
                table: "PlanCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanCodes_GiftOrderId",
                table: "PlanCodes",
                column: "GiftOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodeRedemptions");

            migrationBuilder.DropTable(
                name: "GiftOrders");

            migrationBuilder.DropTable(
                name: "PlanCodes");
        }
    }
}
