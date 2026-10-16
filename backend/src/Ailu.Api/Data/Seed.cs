namespace Ailu.Api.Data;

// Starter content so a fresh database can run the daily loop. The real library is edited
// separately from code; these rows only need to be enough for a closed test.
public static class Seed
{
    public static readonly DailyTask[] Tasks =
    [
        new() { Id = 1, Slug = "small-gift", Category = "simple-joys", CostKzt = 3000, DurationMin = 30,
            Title = "Маленький подарок без повода",
            Body = "Порадуйте друг друга маленьким подарком без повода. Важно не сколько он стоит, а то, что вы помните, чего хотел человек." },
        new() { Id = 2, Slug = "evening-walk", Category = "time-together", CostKzt = 0, DurationMin = 45,
            Title = "Вечерняя прогулка без телефонов",
            Body = "Выйдите вдвоём на полчаса и оставьте телефоны дома. Пройдите по улице, по которой давно не ходили." },
        new() { Id = 3, Slug = "cook-together", Category = "time-together", CostKzt = 5000, DurationMin = 90,
            Title = "Ужин, который готовите вместе",
            Body = "Выберите блюдо, которое ни один из вас ещё не готовил, и приготовьте его вдвоём." },
        new() { Id = 4, Slug = "old-photos", Category = "memories", CostKzt = 0, DurationMin = 30,
            Title = "Ваши первые фотографии",
            Body = "Найдите самые первые совместные фото и расскажите друг другу, что вы тогда думали." },
        new() { Id = 5, Slug = "chess-evening", Category = "games", CostKzt = 0, DurationMin = 60,
            Title = "Партия в шахматы",
            Body = "Сыграйте партию в шахматы или любую настольную игру. Проигравший выбирает фильм на вечер." },
        new() { Id = 6, Slug = "thank-you-note", Category = "words", CostKzt = 0, DurationMin = 15,
            Title = "Записка с благодарностью",
            Body = "Напишите друг другу по три вещи, за которые вы благодарны на этой неделе. Обменяйтесь записками вечером." },
        new() { Id = 7, Slug = "museum-date", Category = "dates", CostKzt = 4000, DurationMin = 120,
            Title = "Свидание в музее",
            Body = "Сходите в музей или на выставку, где вы ещё не были. Выберите по одной работе, которая понравилась больше всего." },
        new() { Id = 8, Slug = "breakfast-in-bed", Category = "simple-joys", CostKzt = 2000, DurationMin = 30,
            Title = "Завтрак для второго",
            Body = "Кто встанет раньше, готовит завтрак. Не обязательно сложный — главное, чтобы был сюрприз." },
        new() { Id = 9, Slug = "sunset", Category = "time-together", CostKzt = 0, DurationMin = 60,
            Title = "Встретить закат",
            Body = "Найдите место с видом на горизонт и встретьте закат вместе. Возьмите с собой чай в термосе." },
        new() { Id = 10, Slug = "plan-trip", Category = "plans", CostKzt = 0, DurationMin = 30,
            Title = "Придумать поездку",
            Body = "Выберите место, куда вы хотите поехать вдвоём в этом году, и запишите три вещи, которые там сделаете." }
    ];

    public static readonly Question[] Questions =
    [
        new() { Id = 1, Slug = "happiest-childhood", Text = "Какое твоё самое счастливое воспоминание из детства?" },
        new() { Id = 2, Slug = "first-impression", Text = "Что ты подумал(а) обо мне, когда мы впервые встретились?" },
        new() { Id = 3, Slug = "perfect-weekend", Text = "Как выглядят идеальные выходные вдвоём?" },
        new() { Id = 4, Slug = "small-thing", Text = "Какая мелочь, которую я делаю, радует тебя больше всего?" },
        new() { Id = 5, Slug = "dream-place", Text = "Куда бы ты поехал(а) со мной, если бы деньги не имели значения?" },
        new() { Id = 6, Slug = "proud-of", Text = "Чем ты гордишься в этом году?" },
        new() { Id = 7, Slug = "favorite-day", Text = "Какой наш общий день ты бы прожил(а) ещё раз?" },
        new() { Id = 8, Slug = "learn-together", Text = "Чему бы ты хотел(а) научиться вместе со мной?" },
        new() { Id = 9, Slug = "comfort", Text = "Что помогает тебе, когда день не задался?" },
        new() { Id = 10, Slug = "song", Text = "Какая песня напоминает тебе о нас?" },
        new() { Id = 11, Slug = "five-years", Text = "Где ты видишь нас через пять лет?" },
        new() { Id = 12, Slug = "tradition", Text = "Какую традицию ты хотел(а) бы начать вдвоём?" }
    ];
}
