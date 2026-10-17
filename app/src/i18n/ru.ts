// Brand voice: warm and to the point, "вы" to the couple, never "ты" to one partner.
export const ru = {
  welcomeTitle: 'Добро пожаловать в ваше пространство любви',
  welcomeSubtitle: 'Ваша история только начинается',

  phoneTitle: 'Вход',
  phoneHint: 'Введите номер телефона, мы пришлём код',
  phoneLabel: 'Номер телефона',
  sendCode: 'Получить код',

  codeTitle: 'Код из SMS',
  codeHint: (phone: string) => `Отправили на ${phone}`,
  codeLabel: 'Шесть цифр',
  signIn: 'Войти',
  changeNumber: 'Другой номер',
  resendCode: 'Отправить ещё раз',

  nameTitle: 'Как вас зовут?',
  nameHint: 'Партнёр увидит это имя',
  nameLabel: 'Имя',
  save: 'Сохранить',

  pairTitle: 'Кто ваш человек?',
  pairHint: 'Введите номер телефона партнёра',
  invite: 'Пригласить',
  inviteSent: (phone: string) => `Приглашение отправлено на ${phone}. Когда партнёр введёт код, пара появится у вас обоих.`,
  partnerGetsCode: 'Партнёр получит код для подтверждения',
  haveCode: 'Меня пригласили',
  haveCodeHint: 'Введите код из SMS от партнёра',
  joinCouple: 'Создать пару',
  checkAgain: 'Проверить, принял ли партнёр',

  greeting: (a: string, b: string) => `Привет,\n${a} и ${b}`,
  coupleNames: (a: string, b: string) => `${a} и ${b}`,
  daysTogether: (n: number) => `${n} ${plural(n, 'день', 'дня', 'дней')} вместе`,
  someone: 'вы',
  partnerFallback: 'партнёр',

  taskOfDay: 'Задание на сегодня',
  taskMeta: (min: number, kzt: number) => `${min} мин · ${kzt === 0 ? 'бесплатно' : `до ${kzt.toLocaleString('ru-RU')} ₸`}`,
  acceptTask: 'Принять задание',
  waitingPartnerAccept: 'Вы приняли. Ждём партнёра',
  partnerAccepted: 'Партнёр уже принял',
  markDone: 'Мы сделали это',
  waitingConfirm: 'Ждём, когда партнёр подтвердит',
  confirmDone: 'Подтвердить выполнение',
  taskDone: 'Готово. +50 нұр',

  questionOfDay: 'Вопрос дня',
  answerPlaceholder: 'Ваш ответ',
  sendAnswer: 'Ответить',
  answerHidden: 'Ответ партнёра откроется после вашего',
  waitingAnswer: (name: string) => `Ответ отправлен. Ждём ${name} — тогда откроются оба.`,
  partnerAnsweredFirst: 'Партнёр уже ответил. Ваша очередь',
  yourAnswer: 'Вы',
  partnerAnswerLabel: (name: string) => name,

  streak: (n: number) => `${n} ${plural(n, 'день', 'дня', 'дней')} подряд`,
  streakZero: 'Серия начнётся, когда вы оба что-то сделаете сегодня',
  streakLapsed: 'Серия прервалась',
  streakRestarted: (n: number) => `Серия началась заново. Ваши ${n} ${plural(n, 'день', 'дня', 'дней')} остались в истории пары`,
  restoreOffer: (n: number, cost: number) => `Можно вернуть серию в ${n} ${plural(n, 'день', 'дня', 'дней')} за ${cost} нұр`,
  restoresLeft: (left: number, perMonth: number) => `Восстановлений в этом месяце: ${left} из ${perMonth}`,
  restoreStreak: 'Вернуть серию',
  restoreNoneLeft: 'Восстановления на этот месяц закончились',
  restoreNotEnoughNur: (cost: number) => `Нужно ${cost} нұр. Выполняйте задания дня, и нұр накопится`,
  nur: 'нұр',
  toNextPhase: (n: number, name: string) => `${n} нұр до фазы «${name}»`,
  cycle: (n: number) => `Цикл ${toRoman(n)}`,
  // By phase index; the server's names are Russian only.
  phases: [
    { name: 'Знакомство', moon: 'Новолуние' },
    { name: 'Первые искры', moon: 'Молодой месяц' },
    { name: 'Свой ритм', moon: 'Первая четверть' },
    { name: 'Мы двое', moon: 'Растущая луна' },
    { name: 'Родственные души', moon: 'Полнолуние' },
    { name: 'Неразлучные', moon: 'Суперлуние' },
  ] as { name: string; moon: string }[],
  number: (n: number) => n.toLocaleString('ru-RU'),

  memories: 'Воспоминания',
  memoriesEmpty: 'Пока ни одного воспоминания',
  memoriesEmptyHint: 'Каждое выполненное задание можно сохранить сюда вместе с фотографией.',
  addMemory: 'Добавить воспоминание',
  saveToMemories: 'Сохранить в воспоминания',
  showMore: 'Показать ещё',
  newMemory: 'Новое воспоминание',
  memoryTitle: 'Название',
  memoryTitlePlaceholder: 'Рим, первый вечер',
  memoryDate: 'Когда',
  memoryDatePlaceholder: 'ДД.ММ.ГГГГ',
  memoryDateInvalid: 'Дата в формате ДД.ММ.ГГГГ, не позже сегодняшней',
  memoryPlace: 'Где',
  memoryText: 'Что запомнилось',
  addPhotos: 'Добавить фото',
  photosCount: (n: number, max: number) => `${n} из ${max} фото`,
  uploading: (done: number, total: number) => `Загружаем фото ${done} из ${total}…`,
  uploadFailed: (n: number) => `${n} ${plural(n, 'фото не загрузилось', 'фото не загрузились', 'фото не загрузились')}. Добавьте их ещё раз из воспоминания.`,
  removePhoto: 'Убрать',
  by: (name: string) => `Добавил(а) ${name}`,
  heart: 'Нравится',
  partnerHearted: (name: string) => `${name} отметил(а) это воспоминание`,
  favorite: 'В избранное',
  deleteMemory: 'Удалить воспоминание',
  deleteMemoryConfirm: 'Воспоминание исчезнет у вас обоих. Удалить?',
  delete: 'Удалить',
  photosOfLimit: (n: number, limit: number) => `${n} из ${limit} фото`,
  photosStored: (n: number) => `${n} фото`,
  storageFull: (limit: number) => `Все ${limit} фото заняты. Чтобы добавить новые, удалите старые.`,
  date: (iso: string) => {
    const [y, m, d] = iso.split('-').map(Number);
    return `${d} ${MONTHS[m - 1]} ${y}`;
  },
  back: 'Назад',

  profile: 'Профиль',
  signOut: 'Выйти из аккаунта',
  language: 'Язык',
  endCouple: 'Завершить пару',
  endCoupleConfirm: 'Пара будет закрыта для вас обоих. История сохранится. Продолжить?',
  cancel: 'Отмена',

  retry: 'Попробовать ещё раз',
  somethingWrong: 'Что-то пошло не так',

  // API problems by `code`. A code missing here shows the server's own (Russian) detail.
  errors: {
    offline: 'Нет связи. Проверьте интернет и попробуйте ещё раз.',
    invalid_phone: 'Нужен казахстанский номер в формате +7 7XX XXX XX XX.',
    invalid_code: 'Неверный код.',
    wrong_code: 'Неверный код.',
    code_expired: 'Код истёк. Запросите новый.',
    session_expired: 'Сессия истекла. Войдите снова.',
    too_many_requests: 'Код уже отправлен. Повторить можно через минуту.',
    invalid_name: 'Имя от 1 до 60 символов.',
    no_couple: 'Сначала создайте пару.',
    already_in_couple: 'Кто-то из вас уже состоит в паре.',
    inviter_in_couple: 'Пригласивший уже состоит в паре.',
    invitation_not_found: 'Код не подходит или истёк. Попросите партнёра отправить новый.',
    retry: 'Не получилось. Попробуйте ещё раз.',
    not_accepted: 'Задание нужно принять вдвоём, прежде чем отмечать выполнение.',
    invalid_text: 'Текст слишком длинный.',
    invalid_title: 'Название от 1 до 120 символов.',
    invalid_place: 'Место до 120 символов.',
    invalid_happenedOn: 'Дата не может быть в будущем.',
    invalid_file: 'Эта фотография не подходит. Подойдут JPEG, PNG и WebP.',
    too_many_photos: 'В одном воспоминании до 10 фотографий.',
    storage_full: 'Место для фотографий закончилось. Чтобы добавить новые, удалите старые.',
    memory_not_found: 'Этого воспоминания больше нет.',
    not_author: 'Изменить или удалить воспоминание может только тот, кто его добавил.',
  } as Record<string, string>,
};

export type Strings = typeof ru;

const MONTHS = ['января', 'февраля', 'марта', 'апреля', 'мая', 'июня', 'июля', 'августа', 'сентября', 'октября', 'ноября', 'декабря'];

function plural(n: number, one: string, few: string, many: string) {
  const m10 = n % 10;
  const m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}

export function toRoman(n: number) {
  const map: [number, string][] = [[10, 'X'], [9, 'IX'], [5, 'V'], [4, 'IV'], [1, 'I']];
  let out = '';
  for (const [v, s] of map) while (n >= v) { out += s; n -= v; }
  return out;
}
