using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ailu.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class KazakhContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextKk",
                table: "Questions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BodyKk",
                table: "DailyTasks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TitleKk",
                table: "DailyTasks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Бір-біріңізді себепсіз шағын сыйлықпен қуантыңыз. Оның бағасы емес, адамның не қалағанын есте сақтағаныңыз маңызды.", "Себепсіз шағын сыйлық" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Жарты сағатқа екеуіңіз далаға шығыңыз, телефондарыңыз үйде қалсын. Көптен бері жүрмеген көшеңізбен серуендеңіз.", "Телефонсыз кешкі серуен" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Екеуіңіз де бұрын әзірлеп көрмеген тағамды таңдап, оны бірге дайындаңыз.", "Бірге әзірлейтін кешкі ас" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Бірге түскен ең алғашқы суреттеріңізді тауып, сол кезде не ойлағаныңызды бір-біріңізге айтып беріңіз.", "Алғашқы суреттеріңіз" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Шахматтан немесе кез келген үстел ойынынан бір партия ойнаңыз. Жеңілген адам кешке көретін фильмді таңдайды.", "Бір партия шахмат" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Осы аптада бір-біріңізге не үшін алғыс айтатыныңызды үш-үштен жазыңыз. Кешке хаттарыңызбен алмасыңыз.", "Алғыс хат" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Бұрын бармаған мұражайға немесе көрмеге барыңыз. Әрқайсыңыз ең ұнаған бір туындыны таңдаңыз.", "Мұражайдағы кездесу" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Кім ертерек тұрса, сол таңғы ас әзірлейді. Күрделі болмаса да болады, бастысы тосынсый болсын.", "Серіктесіңізге таңғы ас" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Көкжиек көрінетін жер тауып, күннің батуын бірге тамашалаңыз. Термосқа шай құйып алыңыз.", "Күннің батуын бірге тамашалау" });

            migrationBuilder.UpdateData(
                table: "DailyTasks",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "BodyKk", "TitleKk" },
                values: new object[] { "Биыл екеуіңіз баратын жерді таңдап, онда істейтін үш нәрсені жазып қойыңыз.", "Сапар ойластыру" });

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 1,
                column: "TextKk",
                value: "Балалық шағыңдағы ең бақытты естелігің қандай?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 2,
                column: "TextKk",
                value: "Біз алғаш кездескенде мен туралы не ойладың?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 3,
                column: "TextKk",
                value: "Екеуміздің мінсіз демалыс күніміз қандай болар еді?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 4,
                column: "TextKk",
                value: "Мен істейтін қандай кішкентай нәрсе сені бәрінен көп қуантады?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 5,
                column: "TextKk",
                value: "Ақша мәселе болмаса, менімен бірге қайда барар едің?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 6,
                column: "TextKk",
                value: "Биыл немен мақтанасың?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 7,
                column: "TextKk",
                value: "Біздің қай ортақ күнімізді тағы бір рет өткізгің келер еді?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 8,
                column: "TextKk",
                value: "Менімен бірге неге үйренгің келеді?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 9,
                column: "TextKk",
                value: "Күнің сәтсіз өткенде саған не көмектеседі?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 10,
                column: "TextKk",
                value: "Қай ән саған бізді еске түсіреді?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 11,
                column: "TextKk",
                value: "Бес жылдан кейін бізді қандай күйде елестетесің?");

            migrationBuilder.UpdateData(
                table: "Questions",
                keyColumn: "Id",
                keyValue: 12,
                column: "TextKk",
                value: "Екеуміз бірге қандай дәстүр бастасақ дейсің?");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TextKk",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "BodyKk",
                table: "DailyTasks");

            migrationBuilder.DropColumn(
                name: "TitleKk",
                table: "DailyTasks");
        }
    }
}
