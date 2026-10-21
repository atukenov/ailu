using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhotoBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoBooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CoverPhotoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoBooks_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoBookOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PriceKzt = table.Column<int>(type: "integer", nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    City = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Address = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Comment = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PayUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PrintJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoBookOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoBookOrders_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoBookOrders_PhotoBooks_BookId",
                        column: x => x.BookId,
                        principalTable: "PhotoBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoBookOrders_Users_OrderedById",
                        column: x => x.OrderedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoBookSpreads",
                columns: table => new
                {
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Layout = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PhotoIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    MemoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoBookSpreads", x => new { x.BookId, x.Position });
                    table.ForeignKey(
                        name: "FK_PhotoBookSpreads_PhotoBooks_BookId",
                        column: x => x.BookId,
                        principalTable: "PhotoBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBookOrders_BookId",
                table: "PhotoBookOrders",
                column: "BookId",
                unique: true,
                filter: "\"Status\" = 'AwaitingPayment'");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBookOrders_CoupleId",
                table: "PhotoBookOrders",
                column: "CoupleId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBookOrders_OrderedById",
                table: "PhotoBookOrders",
                column: "OrderedById");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBookOrders_PaymentId",
                table: "PhotoBookOrders",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBookOrders_PrintJobId",
                table: "PhotoBookOrders",
                column: "PrintJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBookOrders_Status",
                table: "PhotoBookOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoBooks_CoupleId_Year",
                table: "PhotoBooks",
                columns: new[] { "CoupleId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoBookOrders");

            migrationBuilder.DropTable(
                name: "PhotoBookSpreads");

            migrationBuilder.DropTable(
                name: "PhotoBooks");
        }
    }
}
