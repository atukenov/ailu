using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Couples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NurEarned = table.Column<int>(type: "integer", nullable: false),
                    NurBalance = table.Column<int>(type: "integer", nullable: false),
                    StreakDays = table.Column<int>(type: "integer", nullable: false),
                    StreakLastDay = table.Column<DateOnly>(type: "date", nullable: true),
                    StreakForgivenOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Couples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CostKzt = table.Column<int>(type: "integer", nullable: false),
                    DurationMin = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InviterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhoneCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Questions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoupleActivities",
                columns: table => new
                {
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoupleActivities", x => new { x.CoupleId, x.Date, x.UserId });
                    table.ForeignKey(
                        name: "FK_CoupleActivities_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NurLedger",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NurLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NurLedger_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoupleTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyTaskId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    AcceptedAtA = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedAtB = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoupleTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoupleTasks_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoupleTasks_DailyTasks_DailyTaskId",
                        column: x => x.DailyTaskId,
                        principalTable: "DailyTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    AnswerA = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AnsweredAtA = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AnswerB = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AnsweredAtB = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevealedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyQuestions_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoupleMembers",
                columns: table => new
                {
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoupleMembers", x => new { x.CoupleId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CoupleMembers_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoupleMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "DailyTasks",
                columns: new[] { "Id", "Body", "Category", "CostKzt", "DurationMin", "Slug", "Title" },
                values: new object[,]
                {
                    { 1, "Порадуйте друг друга маленьким подарком без повода. Важно не сколько он стоит, а то, что вы помните, чего хотел человек.", "simple-joys", 3000, 30, "small-gift", "Маленький подарок без повода" },
                    { 2, "Выйдите вдвоём на полчаса и оставьте телефоны дома. Пройдите по улице, по которой давно не ходили.", "time-together", 0, 45, "evening-walk", "Вечерняя прогулка без телефонов" },
                    { 3, "Выберите блюдо, которое ни один из вас ещё не готовил, и приготовьте его вдвоём.", "time-together", 5000, 90, "cook-together", "Ужин, который готовите вместе" },
                    { 4, "Найдите самые первые совместные фото и расскажите друг другу, что вы тогда думали.", "memories", 0, 30, "old-photos", "Ваши первые фотографии" },
                    { 5, "Сыграйте партию в шахматы или любую настольную игру. Проигравший выбирает фильм на вечер.", "games", 0, 60, "chess-evening", "Партия в шахматы" },
                    { 6, "Напишите друг другу по три вещи, за которые вы благодарны на этой неделе. Обменяйтесь записками вечером.", "words", 0, 15, "thank-you-note", "Записка с благодарностью" },
                    { 7, "Сходите в музей или на выставку, где вы ещё не были. Выберите по одной работе, которая понравилась больше всего.", "dates", 4000, 120, "museum-date", "Свидание в музее" },
                    { 8, "Кто встанет раньше, готовит завтрак. Не обязательно сложный — главное, чтобы был сюрприз.", "simple-joys", 2000, 30, "breakfast-in-bed", "Завтрак для второго" },
                    { 9, "Найдите место с видом на горизонт и встретьте закат вместе. Возьмите с собой чай в термосе.", "time-together", 0, 60, "sunset", "Встретить закат" },
                    { 10, "Выберите место, куда вы хотите поехать вдвоём в этом году, и запишите три вещи, которые там сделаете.", "plans", 0, 30, "plan-trip", "Придумать поездку" }
                });

            migrationBuilder.InsertData(
                table: "Questions",
                columns: new[] { "Id", "Slug", "Text" },
                values: new object[,]
                {
                    { 1, "happiest-childhood", "Какое твоё самое счастливое воспоминание из детства?" },
                    { 2, "first-impression", "Что ты подумал(а) обо мне, когда мы впервые встретились?" },
                    { 3, "perfect-weekend", "Как выглядят идеальные выходные вдвоём?" },
                    { 4, "small-thing", "Какая мелочь, которую я делаю, радует тебя больше всего?" },
                    { 5, "dream-place", "Куда бы ты поехал(а) со мной, если бы деньги не имели значения?" },
                    { 6, "proud-of", "Чем ты гордишься в этом году?" },
                    { 7, "favorite-day", "Какой наш общий день ты бы прожил(а) ещё раз?" },
                    { 8, "learn-together", "Чему бы ты хотел(а) научиться вместе со мной?" },
                    { 9, "comfort", "Что помогает тебе, когда день не задался?" },
                    { 10, "song", "Какая песня напоминает тебе о нас?" },
                    { 11, "five-years", "Где ты видишь нас через пять лет?" },
                    { 12, "tradition", "Какую традицию ты хотел(а) бы начать вдвоём?" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoupleMembers_UserId",
                table: "CoupleMembers",
                column: "UserId",
                unique: true,
                filter: "\"LeftAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CoupleTasks_CoupleId_Date",
                table: "CoupleTasks",
                columns: new[] { "CoupleId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoupleTasks_DailyTaskId",
                table: "CoupleTasks",
                column: "DailyTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestions_CoupleId_Date",
                table: "DailyQuestions",
                columns: new[] { "CoupleId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestions_QuestionId",
                table: "DailyQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyTasks_Slug",
                table: "DailyTasks",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_Code",
                table: "Invitations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NurLedger_CoupleId",
                table: "NurLedger",
                column: "CoupleId");

            migrationBuilder.CreateIndex(
                name: "IX_NurLedger_IdempotencyKey",
                table: "NurLedger",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneCodes_Phone_CreatedAt",
                table: "PhoneCodes",
                columns: new[] { "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Slug",
                table: "Questions",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Phone",
                table: "Users",
                column: "Phone",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoupleActivities");

            migrationBuilder.DropTable(
                name: "CoupleMembers");

            migrationBuilder.DropTable(
                name: "CoupleTasks");

            migrationBuilder.DropTable(
                name: "DailyQuestions");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "NurLedger");

            migrationBuilder.DropTable(
                name: "PhoneCodes");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "DailyTasks");

            migrationBuilder.DropTable(
                name: "Questions");

            migrationBuilder.DropTable(
                name: "Couples");
        }
    }
}
