using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class Dialogue
    {
        private static readonly string[][] Lines = new string[33][]
        {
            new string[6]
            {
                "Контакт. Передаю группе!",
                "Вижу движение впереди.",
                "Обнаружил! Отметил позицию.",
                "Есть контакт. Командир, прием.",
                "Внимание, впереди противник.",
                "Движение у укрытий. Докладываю."
            },
            new string[6]
            {
                "Прикрытие готово.",
                "На позиции. Вижу проход.",
                "Я на месте. Можете идти.",
                "Держу сектор, прикрою.",
                "Встал за укрытие. Готов.",
                "Наблюдаю. Жду вашей перебежки."
            },
            new string[6]
            {
                "Принял. Двигаемся вместе.",
                "Понял, иду за тобой.",
                "Перебежка. Не теряйте нас.",
                "Двигаемся к следующему укрытию.",
                "Под прикрытием, вперед.",
                "Напарник, держись рядом."
            },
            new string[6]
            {
                "На месте, за укрытием.",
                "Добрался. Занимаю сектор.",
                "Укрытие занято. Жду второго.",
                "Позицию занял, прикрываю.",
                "Здесь. Можно подтягиваться.",
                "Готов принять следующую группу."
            },
            new string[6]
            {
                "Жду подтверждения прикрытия.",
                "Пока стоим. Кто нас прикрывает?",
                "Сначала займитесь прикрытием.",
                "Не выхожу: поддержки нет.",
                "Ждем своих. Не растягиваемся.",
                "Остановился за укрытием. Жду."
            },
            new string[6]
            {
                "Перезарядка. Напарник, прикрой!",
                "Меняю магазин. Держи сектор.",
                "Перезаряжаюсь за укрытием.",
                "Пустой. Прикрой на секунду.",
                "Магазин! Не отпускай сектор.",
                "Перезарядка. Я пока не прикрываю."
            },
            new string[6]
            {
                "Прикрываю твою перезарядку.",
                "Принял. Сектор на мне.",
                "Меняй магазин, я смотрю.",
                "Я готов, оставайся за стенкой.",
                "Не спеши, прикрытие есть.",
                "Твой сектор принял."
            },
            new string[6]
            {
                "Снова готов.",
                "Зарядил. Возвращаюсь в строй.",
                "Готов прикрывать.",
                "Магазин сменил. На связи.",
                "Прикрытие восстановлено.",
                "Патроны есть. Держу позицию."
            },
            new string[6]
            {
                "Прижали! Голову не поднять.",
                "Ложусь ниже, держите сектор!",
                "Слишком плотно. Прижался.",
                "По мне огонь. Сижу за укрытием.",
                "Не могу высунуться!",
                "Весь огонь сюда. Нужна помощь."
            },
            new string[6]
            {
                "Не могу... не могу подняться.",
                "Я здесь. Только не оставляйте.",
                "Прижался. Сектор не держу.",
                "Не высовываюсь. Мне нужно время.",
                "Слышу вас... пока не могу.",
                "Я за укрытием. Не могу стрелять."
            },
            new string[6]
            {
                "Я рядом. Дыши, не высовывайся.",
                "Слышу тебя. Мы здесь.",
                "Оставайся в укрытии, прикроем.",
                "Держись. Группа тебя не бросит.",
                "Я принял твой сектор.",
                "Не торопись. Возвращайся, когда сможешь."
            },
            new string[6]
            {
                "Отпустило. Снова слышу вас.",
                "Возвращаюсь. Дайте секунду.",
                "Я в порядке. Готов наблюдать.",
                "Спасибо. Включаюсь в работу.",
                "Пришел в себя. Остаюсь с группой.",
                "Могу продолжать. Я с вами."
            },
            new string[6]
            {
                "Ранен. Остаюсь за укрытием.",
                "Попали. Прикройте меня.",
                "Задело! Ниже, ниже.",
                "Я ранен, но на связи.",
                "Есть попадание. Не высовываюсь.",
                "Задело. Мне нужно укрытие."
            },
            new string[6]
            {
                "Боец выбыл! Держим связь.",
                "Потеря. Остальные, ответьте.",
                "Один выбыл. Не рассыпаемся.",
                "Потеряли бойца. Прикрываю.",
                "Слышали? Держимся вместе.",
                "Минус один. Командир, прием."
            },
            new string[6]
            {
                "Прикрытие, на позиции. Движущиеся — ждать.",
                "Сначала готовность. Потом перебежка.",
                "Группа, занять укрытия и доложить.",
                "Держим дистанцию. Проверить прикрытие.",
                "Не торопимся. Собираю подтверждения.",
                "Первая задача — прикрыть перемещение."
            },
            new string[6]
            {
                "Прикрытие подтверждено. Группа, вперед.",
                "Вторая группа, к следующему укрытию.",
                "Разрешаю перебежку. Остальные держат.",
                "Двигаемся по приказу, вместе.",
                "Маневр начинаем. Прикрытие остается.",
                "Пара вперед. Доложить по прибытии."
            },
            new string[6]
            {
                "Все на месте? Собираем группу.",
                "Дожидаемся своих. Дальше по готовности.",
                "Закрепиться. Проверить напарника.",
                "Занять укрытие, восстановить строй.",
                "Прибыли. Готовим смену прикрытия.",
                "Не уходить дальше. Сначала собрать всех."
            },
            new string[6]
            {
                "Удерживаем. Без команды не менять позицию.",
                "Сектора распределены. Держим укрытия.",
                "Наблюдать подходы. Не выбегать.",
                "Оборона на месте. Докладывать изменения.",
                "Держать линию. Напарника не терять.",
                "Резерв, оставайтесь рядом с группой."
            },
            new string[6]
            {
                "Командование беру на себя.",
                "Я старший. Продолжаем вместе.",
                "Командир выбыл. Принимаю группу.",
                "Слушайте меня. Сначала соберемся.",
                "Принимаю командование. Проверка связи.",
                "Я веду группу. Держите позиции."
            },
            new string[6]
            {
                "Передаю запасной магазин.",
                "Держи магазин. Я прикрою.",
                "Поделюсь патронами. Оставайся рядом.",
                "Магазин тебе. Сектор на мне.",
                "Есть запас, передаю.",
                "Патроны получил? Держимся вместе."
            },
            new string[6]
            {
                "Точка рядом. Закрепиться у рубежа.",
                "Занимаем точку. Держим прикрытие.",
                "Рубеж перед нами. Не рассыпаемся.",
                "Последнее перемещение — к точке.",
                "Дошли. Закрепляемся.",
                "У точки. Проверить, все ли дошли."
            },
            new string[6]
            {
                "Прикрытие, работаем по общему контакту.",
                "Группа, вижу приоритетную цель.",
                "Прикрывающие, согласовать цель.",
                "Поддержите наших, общий контакт отмечен.",
                "Согласованный огонь. Движущиеся ждут.",
                "Цель для прикрытия передаю группе."
            },
            new string[6]
            {
                "Иду к раненому. Держите сектор.",
                "Вижу нашего. Забираю за укрытие.",
                "Нужен проход к товарищу. Прикройте.",
                "Подхожу. Не оставляйте без прикрытия.",
                "Раненый рядом. Помогаю ему.",
                "Медик на подходе. Держись."
            },
            new string[6]
            {
                "Я рядом. Оттащу за стенку.",
                "Держись за меня. Уходим чуть назад.",
                "Тяну своего. Прикрывайте нас.",
                "Забираю за ближайшее укрытие.",
                "Не вставай. Я тебя вытащу.",
                "Переносим помощь в укрытие."
            },
            new string[6]
            {
                "Накладываю жгут. Сектор пока не держу.",
                "Помогаю раненому. Прикройте.",
                "Начал перевязку. Дайте время.",
                "Держись. Оказываю помощь.",
                "Жгут использую. Не высовывайся.",
                "Остановились за укрытием. Помогаю."
            },
            new string[6]
            {
                "Помощь оказана. Отдохни здесь.",
                "Закончил. Проверяю связь.",
                "Стабилизировал. Держись рядом.",
                "Жгут наложен. Возвращаюсь к группе.",
                "Стало лучше. Не торопись.",
                "Готово. Остаемся под прикрытием."
            },
            new string[6]
            {
                "Дым поставлен. Не теряйте связь.",
                "Закрываю опасный участок дымом.",
                "Дымовая завеса. Проверяйте своих.",
                "Помогающим — дым перед вами.",
                "Дым на направлении огня.",
                "Ставлю дым. Сектор виден хуже."
            },
            new string[6]
            {
                "Использую одноразовый. Свои, берегитесь.",
                "Гранатомет готов. Контакт вижу.",
                "Одноразовый — по отмеченному контакту.",
                "Пуск. Возвращаюсь к автомату.",
                "Выстрел из гранатомета.",
                "Работаю по группе у укрытий."
            },
            new string[6]
            {
                "Слишком долго стоим. Меняю направление.",
                "Проход закрыт. Ищем другой угол.",
                "Не застреваем. Перестраиваем маневр.",
                "Обход под общим прикрытием.",
                "Меняем направление, держимся группой.",
                "Пулемет остается. Маневренные — на фланг."
            },
            new string[6]
            {
                "Разделяем маневр на два фланга.",
                "Фланги занять. Центр прикрывает.",
                "Два направления, одна группа. Связь держать.",
                "Расширяем позиции под общим прикрытием.",
                "Края занять, центр остается на месте.",
                "Фланги работают согласованно."
            },
            new string[6]
            {
                "Оттягиваемся к соседним укрытиям.",
                "Раненых назад, прикрытие остается.",
                "Сначала сохраним людей. Локальный отход.",
                "Позицию меняем. Далеко не уходить.",
                "Отходим по частям. Не бросать своих.",
                "Перегруппировка рядом. Держать связь."
            },
            new string[6]
            {
                "Меняю короб. Прикрытие временно снято.",
                "Пулемет на перезарядке. Подхватите.",
                "Короб пуст. Держите мой сектор.",
                "Заряжаю новый короб за укрытием.",
                "Смена короба. Напарник, прикрой.",
                "Перезаряжаю пулемет. На месте."
            },
            new string[6]
            {
                "Не могу идти. Нужна помощь.",
                "Сильно ранен. Я здесь.",
                "Ноги не держат. Подтащите меня.",
                "Сам не выйду. Прикройте.",
                "Мне нужна эвакуация за укрытие.",
                "Я жив. Нужен медик рядом."
            }
        };
        public const int Contact = 0;
        public const int Ready = 1;
        public const int Move = 2;
        public const int Arrive = 3;
        public const int Wait = 4;
        public const int Reload = 5;
        public const int CoverBuddy = 6;
        public const int Loaded = 7;
        public const int Pinned = 8;
        public const int Broken = 9;
        public const int Reassure = 10;
        public const int Recovered = 11;
        public const int Wounded = 12;
        public const int Loss = 13;
        public const int Prepare = 14;
        public const int CommandMove = 15;
        public const int Regroup = 16;
        public const int Defend = 17;
        public const int TakeCommand = 18;
        public const int Share = 19;
        public const int Objective = 20;
        public const int Focus = 21;
        public const int Rescue = 22;
        public const int Drag = 23;
        public const int Treat = 24;
        public const int Treated = 25;
        public const int Smoke = 26;
        public const int Launcher = 27;
        public const int AdaptFlank = 28;
        public const int Split = 29;
        public const int Withdraw = 30;
        public const int BoxReload = 31;
        public const int Critical = 32;
        public static int Count
        {
            get
            {
                int num = 0;
                string[][] lines = Lines;
                foreach (string[] array in lines)
                {
                    num += array.Length;
                }

                return num;
            }
        }

        public static bool CanAct(SilentOrders.Core.Soldier s)
        {
            return s.Available && !s.Broken && s.Suppression < 0.8f;
        }

        public static bool HasRole(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Side side, SilentOrders.Core.Role role)
        {
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == side && soldier.Role == role && CanAct(soldier))
                {
                    return true;
                }
            }

            return false;
        }

        struct VoiceContext
        {
            public int Active, Maneuver, Alive;
            public bool Medic, MachineGunner;
        }

        static VoiceContext Context(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Side side)
        {
            var result = new VoiceContext();
            var squad = b.Team(side);
            foreach (var unit in b.Soldiers)
            {
                if (unit.Side != side)
                    continue;
                if (unit.Alive)
                    result.Alive++;
                if (!CanAct(unit))
                    continue;
                result.Active++;
                if (squad.ManeuverIds.Contains(unit.Id))
                    result.Maneuver++;
                if (unit.Role == SilentOrders.Core.Role.Medic)
                    result.Medic = true;
                if (unit.Role == SilentOrders.Core.Role.MachineGunner)
                    result.MachineGunner = true;
            }

            return result;
        }

        public static string Adapt(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier speaker, string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return phrase;
            return Adapt(b, speaker, phrase, Context(b, speaker.Side));
        }

        static string Adapt(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier speaker, string phrase, VoiceContext context)
        {
            if (string.IsNullOrEmpty(phrase))
            {
                return phrase;
            }

            if (phrase.Contains("за укрытием") && !PositionLogic.Protected(b, speaker, speaker.Position))
                phrase = phrase.Replace("Я за укрытием", "Я на позиции").Replace("Сижу за укрытием", "Прижался к земле").Replace("сижу за укрытием", "прижался к земле").Replace("Остановился за укрытием", "Остановился").Replace("Ранен. Остаюсь за укрытием", "Ранен. Мне нужна защита");
            int num = context.Maneuver, num2 = context.Active;
            if (!context.Medic)
            {
                phrase = phrase.Replace("поддержку и медика", "оставшихся бойцов").Replace("Нужен медик рядом", "Нужна помощь");
                if (phrase.ToLowerInvariant().Contains("медик"))
                {
                    phrase = "Помощь по возможности. Бережём оставшихся";
                }
            }

            if (phrase.Contains("Медик на подходе") && speaker.Role != SilentOrders.Core.Role.Medic)
            {
                phrase = "Иду на помощь. Держись";
            }

            if (!context.MachineGunner)
            {
                phrase = phrase.Replace("пулемет и центр прикрывают", "оставшиеся держат позиции").Replace("пулемет прикрывает", "оставшиеся держат позиции");
                if (phrase.Contains("Пулемет остается"))
                {
                    phrase = "Меняем направление по готовности оставшихся";
                }
            }

            SilentOrders.Core.Soldier s = speaker.BuddyId >= 0 && speaker.BuddyId < b.Soldiers.Count ? b.Soldiers[speaker.BuddyId] : null;
            if (s == null || s.Id == speaker.Id || s.Side != speaker.Side || !CanAct(s))
            {
                phrase = phrase.Replace("Напарник, прикрой!", "Прикрытие нужно!").Replace("Напарник, прикрой.", "Нужно прикрытие.").Replace("Напарник, держись рядом.", "Держусь укрытий.").Replace("Проверить напарника", "Проверить связь").Replace("Напарника не терять", "Связь не терять");
                if (phrase.Contains("Жду второго"))
                {
                    phrase = phrase.Replace("Жду второго", "Проверяю сектор");
                }
            }

            if (num < 2)
            {
                phrase = phrase.Replace("Пара меняет направление", "Боец меняет направление").Replace("Пара вперед", "Боец вперед").Replace("маневренная пара обходит", "маневренный боец меняет позицию").Replace("вместе с парой по сигналу", "по сигналу командира");
            }

            if ((num2 < 3 || num < 2) && (phrase.Contains("Два фланга") || phrase.Contains("два фланга") || phrase.Contains("оба фланга")))
            {
                phrase = "Состав сокращён. Доступное направление по готовности";
            }

            if (context.Alive == 1)
            {
                string text = phrase.ToLowerInvariant();
                if (text.Contains("групп") || text.Contains("остальн") || text.Contains("оставш") || text.Contains("бережём") || text.Contains("двигаемся") || text.Contains("держимся") || text.Contains("прикроем") || text.Contains("нас") || text.Contains("все") || text.Contains("своих") || text.Contains("медик") || text.Contains("поддержк") || text.Contains("прикры") || text.Contains("напарник") || text.Contains("вместе") || text.Contains("пара") || text.Contains("фланг") || text.Contains("двое") || text.Contains("второго"))
                {
                    return speaker.Incapacitated ? "Остался один. Сам двигаться не могу" : (speaker.Broken ? "Остался один. Не могу вести бой" : "Остался один. Действую из укрытия");
                }
            }

            return phrase;
        }

        public static string Short(string phrase)
        {
            if (string.IsNullOrEmpty(phrase))
                return "";
            if (phrase.Contains("не прикрываю") || phrase.Contains("пока не готов") || phrase.Contains("Прикрытие снято"))
                return "Прикрытие снято";
            if (phrase.Contains("Проверяю сектор") || phrase.Contains("Проверяю позицию"))
                return "Проверяю позицию";
            if (phrase.Contains("простреливают") || phrase.Contains("не защищает"))
                return "Меняю сторону укрытия";
            if (phrase.Contains("Контакт потерян") || phrase.Contains("Цель скрылась"))
                return "Контакт потерян";
            if (phrase.Contains("нет, нужен") || phrase.Contains("Задача заблокирована"))
                return "Нужен новый план";
            if (phrase.Contains("Патроны закончились") || phrase.Contains("Без патронов"))
                return "Нет патронов";
            string text = phrase.ToLowerInvariant();
            if (text.Contains("остался один"))
            {
                return "Остался один";
            }

            if (text.Contains("ранен") || text.Contains("попали") || text.Contains("задело"))
            {
                return "Ранен";
            }

            if (text.Contains("приж") || text.Contains("не могу"))
            {
                return "Нужна помощь";
            }

            if (text.Contains("перезар") || text.Contains("короб") || text.Contains("меняю магазин"))
            {
                return "Перезарядка";
            }

            if (text.Contains("жгут") || text.Contains("перевяз") || text.Contains("помога") || text.Contains("помощь"))
            {
                return "Помощь своим";
            }

            if (text.Contains("дым") || text.Contains("завес"))
            {
                return "Дым";
            }

            if (text.Contains("пуск") || text.Contains("гранатом") || text.Contains("одноразов"))
            {
                return "Пуск";
            }

            if (text.Contains("контакт") || text.Contains("противник") || text.Contains("обнаруж"))
            {
                return "Контакт";
            }

            if (text.Contains("жд") || text.Contains("ожида") || text.Contains("готовност"))
            {
                return "Жду готовности";
            }

            if (text.Contains("отход") || text.Contains("отходим") || text.Contains("оттяг"))
            {
                return "Отход";
            }

            if (text.Contains("прикры"))
            {
                return "Прикрытие";
            }

            if (text.Contains("двиг") || text.Contains("перебеж") || text.Contains("маневр") || text.Contains("направлен"))
            {
                return "Меняю позицию";
            }

            if (text.Contains("на месте") || text.Contains("позици") || text.Contains("укрыти"))
            {
                return "На позиции";
            }

            if (text.Contains("принял") || text.Contains("принят"))
            {
                return "Принял";
            }

            return (phrase.Length <= 28) ? phrase : (phrase.Substring(0, 25).TrimEnd() + "…");
        }

        public static void Refresh(SilentOrders.Core.BattleSimulation b)
        {
            var green = Context(b, SilentOrders.Core.Side.Green);
            var red = Context(b, SilentOrders.Core.Side.Red);
            foreach (var soldier in b.Soldiers)
            {
                if (!soldier.Alive)
                    continue;
                var context = soldier.Side == SilentOrders.Core.Side.Green ? green : red;
                string speech = Adapt(b, soldier, soldier.Speech, context);
                if (speech != soldier.Speech || string.IsNullOrEmpty(soldier.SpeechShort))
                    soldier.SpeechShort = Short(speech);
                soldier.Speech = speech;
                soldier.Order = Adapt(b, soldier, soldier.Order, context);
            }

            RefreshSquad(b, b.Green, green);
            RefreshSquad(b, b.Red, red);
        }

        static void RefreshSquad(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad squad, VoiceContext context)
        {
            if (squad.LeaderId < 0 || squad.LeaderId >= b.Soldiers.Count)
                return;
            var speaker = b.Soldiers[squad.LeaderId];
            squad.CommanderOrder = Adapt(b, speaker, squad.CommanderOrder, context);
            squad.Plan = Adapt(b, speaker, squad.Plan, context);
            squad.Execution = Adapt(b, speaker, squad.Execution, context);
        }

        public static void Say(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, int category, bool urgent = false)
        {
            if (EventVoice.Handle(b, s, category))
                return;
            if (s.Alive && (urgent || !(b.Time < s.SpeechCooldown)))
            {
                int num = b.VoiceRandom.Int(Lines[category].Length);
                int num2 = category * 10 + num;
                if (s.LastPhrase == num2)
                {
                    num = (num + 1) % Lines[category].Length;
                    num2 = category * 10 + num;
                }

                s.LastPhrase = num2;
                string text = Lines[category][num];
                if (category == 22 && s.Role != SilentOrders.Core.Role.Medic && text.Contains("Медик"))
                {
                    text = "Иду на помощь. Держись.";
                }

                if (category == 24 && s.RescueTargetId < 0)
                {
                    text = "Оказываю себе помощь. Пока не прикрываю.";
                }

                if (category == 25 && s.RescueTargetId < 0)
                {
                    text = "Жгут наложен. Проверяю состояние.";
                }

                b.Say(s, Adapt(b, s, text), urgent);
            }
        }
    }
}
