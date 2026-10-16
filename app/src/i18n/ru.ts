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
