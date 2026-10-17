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
  nur: 'нұр',
  toNextPhase: (n: number, name: string) => `${n} нұр до фазы «${name}»`,
  cycle: (n: number) => `Цикл ${toRoman(n)}`,

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

  goals: 'Наши цели',
  goalsEmpty: 'Пока ни одной цели',
  goalsEmptyHint: 'Поставьте цель на двоих и разбейте её на этапы. Каждый этап, который вы закроете вместе, даёт +100 нұр.',
  newGoal: 'Новая цель',
  addGoal: 'Поставить цель',
  goalTitle: 'Название',
  goalTitlePlaceholder: 'Например, поход в горы',
  goalCategory: 'Категория',
  goalCategories: { travel: 'Путешествия', home: 'Дом', health: 'Здоровье', money: 'Финансы', together: 'Вместе' },
  goalDeadline: 'Срок',
  noDeadline: 'Без срока',
  goalRemind: 'Напоминать раз в неделю',
  goalSteps: 'Этапы',
  stepPlaceholder: (n: number) => `Этап ${n}`,
  addStep: 'Добавить этап',
  createGoal: 'Создать цель',
  goalLimitTitle: (n: number) => `В бесплатной версии — ${n} ${plural(n, 'цель', 'цели', 'целей')} одновременно`,
  goalLimitHint: 'Закройте одну из текущих или откройте Plus, чтобы ставить цели без ограничений.',
  goalProposed: (name: string) => `Ждём, когда ${name} присоединится. Тогда этапы можно будет отмечать.`,
  goalJoinNeeded: (name: string) => `${name} предлагает эту цель`,
  joinGoal: 'Присоединиться',
  declineGoal: 'Отказаться',
  withdrawGoal: 'Отозвать цель',
  goalDone: 'Цель достигнута',
  stepsProgress: (done: number, total: number) => `${done} из ${total} ${plural(total, 'этапа', 'этапов', 'этапов')}`,
  noSteps: 'Добавьте этапы — процент считается по закрытым',
  stepWaiting: 'ждём партнёра',
  stepConfirm: 'Подтвердить',
  stepMark: 'Сделали',
  stepUndo: 'Отменить',
  stepRemove: 'Убрать этап',
  removeGoal: 'Удалить цель',
  removeGoalConfirm: 'Цель удалится, когда партнёр тоже согласится. Попросить?',
  removalAsked: 'Вы попросили удалить цель. Ждём ответа партнёра.',
  removalByPartner: (name: string) => `${name} предлагает удалить эту цель`,
  removeAgree: 'Удалить',
  removeKeep: 'Оставить',
  removeCancel: 'Не удалять',
  monthYear: (ym: string) => {
    const [y, m] = ym.split('-').map(Number);
    return `${MONTHS_NOMINATIVE[m - 1]} ${y}`;
  },

  profile: 'Профиль',
  signOut: 'Выйти из аккаунта',
  language: 'Язык',
  endCouple: 'Завершить пару',
  endCoupleConfirm: 'Пара будет закрыта для вас обоих. История сохранится. Продолжить?',
  cancel: 'Отмена',

  retry: 'Попробовать ещё раз',
  somethingWrong: 'Что-то пошло не так',
};

export type Strings = typeof ru;

const MONTHS_NOMINATIVE = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь', 'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь'];

const MONTHS = ['января', 'февраля', 'марта', 'апреля', 'мая', 'июня', 'июля', 'августа', 'сентября', 'октября', 'ноября', 'декабря'];

function plural(n: number, one: string, few: string, many: string) {
  const m10 = n % 10;
  const m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}

function toRoman(n: number) {
  const map: [number, string][] = [[10, 'X'], [9, 'IX'], [5, 'V'], [4, 'IV'], [1, 'I']];
  let out = '';
  for (const [v, s] of map) while (n >= v) { out += s; n -= v; }
  return out;
}
