using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Achievements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Category = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Tier = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Body = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleKk = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    BodyKk = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoupleAchievements",
                columns: table => new
                {
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AchievementId = table.Column<int>(type: "integer", nullable: false),
                    MarkedById = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HappenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    MemoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    EarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoupleAchievements", x => new { x.CoupleId, x.AchievementId });
                    table.ForeignKey(
                        name: "FK_CoupleAchievements_Achievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "Achievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoupleAchievements_Couples_CoupleId",
                        column: x => x.CoupleId,
                        principalTable: "Couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoupleAchievements_Memories_MemoryId",
                        column: x => x.MemoryId,
                        principalTable: "Memories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CoupleAchievements_Users_MarkedById",
                        column: x => x.MarkedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Achievements",
                columns: new[] { "Id", "Body", "BodyKk", "Category", "Icon", "Position", "Slug", "Tier", "Title", "TitleKk" },
                values: new object[,]
                {
                    { 1, "Вы сходили в кофейню вдвоём.", "Екеуіңіз кофеханаға бардыңыздар.", "Firsts", "☕", 1, "first-coffee", "Free", "Первое кофе", "Алғашқы кофе" },
                    { 2, "Посмотрели фильм в кинотеатре вместе.", "Кинотеатрда бірге фильм көрдіңіздер.", "Firsts", "🎬", 2, "first-movie", "Free", "Первый фильм", "Алғашқы фильм" },
                    { 3, "Приготовили ужин вдвоём, от начала до конца.", "Кешкі асты басынан аяғына дейін екеуіңіз әзірледіңіздер.", "Firsts", "🍲", 3, "first-dinner", "Free", "Первый ужин", "Алғашқы кешкі ас" },
                    { 4, "Встретили рассвет вместе.", "Таңды бірге қарсы алдыңыздар.", "Firsts", "🌅", 4, "sunrise", "Free", "Встреча рассвета", "Таңды қарсы алу" },
                    { 5, "Устроили пикник на природе.", "Табиғат аясында пикник жасадыңыздар.", "Firsts", "🧺", 5, "first-picnic", "Free", "Первый пикник", "Алғашқы пикник" },
                    { 6, "Сделали фото вдвоём и распечатали его.", "Екеуіңіз суретке түсіп, оны қағазға басып шығардыңыздар.", "Firsts", "📸", 6, "printed-photo", "Free", "Общее фото", "Ортақ сурет" },
                    { 7, "Сделали друг другу подарок своими руками.", "Бір-біріңізге өз қолыңызбен сыйлық жасадыңыздар.", "Firsts", "🎁", 7, "handmade-gift", "Free", "Подарок своими руками", "Қолдан жасалған сыйлық" },
                    { 8, "Сходили на концерт вместе.", "Концертке бірге бардыңыздар.", "Events", "🎵", 8, "first-concert", "Plus", "Первый концерт", "Алғашқы концерт" },
                    { 9, "Сходили вместе на спектакль.", "Спектакльге бірге бардыңыздар.", "Events", "🎭", 9, "theatre", "Plus", "Вечер в театре", "Театр кеші" },
                    { 10, "Болели вместе на матче на стадионе.", "Стадионда матчқа бірге жанкүйер болдыңыздар.", "Events", "🏟️", 10, "match", "Plus", "На трибуне", "Трибунада" },
                    { 11, "Встретили Наурыз вдвоём: с наурыз-көже, на празднике или в гостях.", "Наурызды бірге қарсы алдыңыздар: наурыз көжемен, мерекеде немесе қонақта.", "Events", "🌷", 11, "nauryz", "Plus", "Наурыз вместе", "Наурыз бірге" },
                    { 12, "Встретили Новый год вместе.", "Жаңа жылды бірге қарсы алдыңыздар.", "Events", "🎄", 12, "new-year", "Plus", "Новый год вдвоём", "Жаңа жыл бірге" },
                    { 13, "Были вместе на тое и танцевали.", "Тойға бірге барып, би биледіңіздер.", "Events", "💃", 13, "toi", "Plus", "Той", "Той" },
                    { 14, "Вместе помогли кому-то: волонтёрство, сбор вещей, помощь соседям.", "Біреуге бірге көмектестіңіздер: еріктілік, зат жинау, көршілерге көмек.", "Events", "🤝", 14, "good-deed", "Plus", "Доброе дело", "Игі іс" },
                    { 15, "Уехали вдвоём хотя бы с одной ночёвкой.", "Екеуіңіз кем дегенде бір түнге сапарға шықтыңыздар.", "Travel", "🧳", 15, "first-trip", "Plus", "Первая поездка", "Алғашқы сапар" },
                    { 16, "Поднялись в горы пешком.", "Тауға жаяу көтерілдіңіздер.", "Travel", "⛰️", 16, "mountains", "Plus", "В горах", "Тауда" },
                    { 17, "Искупались вместе в море, озере или реке.", "Теңізге, көлге немесе өзенге бірге шомылдыңыздар.", "Travel", "🌊", 17, "by-the-water", "Plus", "У воды", "Су жағасында" },
                    { 18, "Проехали вместе больше 300 км по трассе.", "Тас жолмен бірге 300 км-ден астам жол жүрдіңіздер.", "Travel", "🚗", 18, "road-trip", "Plus", "Дорога вдвоём", "Екеуара жол" },
                    { 19, "Побывали в городе, где оба раньше не были.", "Екеуіңіз бұрын болмаған қалада болдыңыздар.", "Travel", "🏙️", 19, "new-city", "Plus", "Новый город", "Жаңа қала" },
                    { 20, "Съездили вместе в другую страну.", "Басқа елге бірге бардыңыздар.", "Travel", "✈️", 20, "abroad", "Plus", "За границей", "Шетелде" },
                    { 21, "Переночевали в палатке или в юрте.", "Шатырда немесе киіз үйде түнедіңіздер.", "Travel", "⛺", 21, "under-the-stars", "Plus", "Ночь под звёздами", "Жұлдыздар астында" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_Slug",
                table: "Achievements",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoupleAchievements_AchievementId",
                table: "CoupleAchievements",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_CoupleAchievements_MarkedById",
                table: "CoupleAchievements",
                column: "MarkedById");

            migrationBuilder.CreateIndex(
                name: "IX_CoupleAchievements_MemoryId",
                table: "CoupleAchievements",
                column: "MemoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoupleAchievements");

            migrationBuilder.DropTable(
                name: "Achievements");
        }
    }
}
