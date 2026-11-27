using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure half of the External control window (REMOTE-CONTROL rule 18, `WINDOW.md`, ticket CYRFLP-0142): the T5 line
    /// made from a log line, the outcome words, the classes shown, the T1 state line, and the contract's Russian and
    /// Ukrainian words held character for character (case W4).
    /// </summary>
    public class ExternalControlWordsTests
    {
        private static string Language(string code) => Localization.Names[Array.IndexOf(Localization.Codes, code)];

        [Fact]
        public void ALogLineBecomesTheT5Shape()
        {
            string line = "2026-11-23 10:15:02 | Phone (a1b2c3d4) | status | safe | done | exit 0";
            Assert.Equal("2026-11-23 10:15 Phone - Tell whether it is ready: done", RemoteControlWords.Display(line, s => s));
        }

        [Fact]
        public void ARefusalShowsItsReasonWordAndAnUnknownReasonItsStatus()
        {
            Assert.Equal("not allowed", RemoteControlWords.OutcomeKey("refused:not-permitted", out bool known));
            Assert.True(known);
            Assert.Equal("too late", RemoteControlWords.OutcomeKey("refused:expired", out _));
            Assert.Equal("refused", RemoteControlWords.OutcomeKey("refused:io-error", out known));
            Assert.False(known);
        }

        [Fact]
        public void ABoxThatDidNotOpenIsMarkedUnverified()
        {
            string line = "2026-11-23 10:15:02 | sealed-failed (no-device-key) | from 9f8e7d6c (unverified)";
            Assert.Equal("2026-11-23 10:15 9f8e7d6c (unverified) - ?: could not be opened", RemoteControlWords.Display(line, s => s));
        }

        [Fact]
        public void T5IsNewestFirst()
        {
            var lines = new[] { "2026-11-23 10:00:00 | A (1) | quit | safe | done | exit 0", "2026-11-23 11:00:00 | B (2) | quit | safe | done | exit 0" };
            List<string> shown = RemoteControlWords.Recent(lines, s => s);
            Assert.StartsWith("2026-11-23 11:00 B", shown[0]);
            Assert.Equal(20, RemoteCommandsLog.RecentLines);
        }

        [Fact]
        public void OnlyClassesWithARemoteVerbAreShown()
            => Assert.Equal(new[] { "safe" }, RemoteControlWords.ShownClasses(AppCommandManifest.Shipped));

        [Fact]
        public void ANameLosesItsBidiControls()
            => Assert.Equal("Phone", RemoteControlWords.CleanName("Ph‮one‏"));

        [Fact]
        public void T1SaysTheFirstStateThatHolds()
        {
            Assert.Equal(RemoteThisDeviceState.Off, RemoteCommandsView.StateOf(new RemoteCommandsView()));
            var view = new RemoteCommandsView { Listening = true };
            Assert.Equal(RemoteThisDeviceState.NoServer, RemoteCommandsView.StateOf(view));
            view.Entries.Add(new RemoteCommandsEntryView { ServerCommand = true, NeedsSignIn = true });
            Assert.Equal(RemoteThisDeviceState.SignIn, RemoteCommandsView.StateOf(view));
            view.Entries[0].NeedsSignIn = false;
            view.Entries[0].State = RemoteStreamState.Retrying;
            Assert.Equal(RemoteThisDeviceState.Offline, RemoteCommandsView.StateOf(view));
            view.Entries[0].State = RemoteStreamState.Online;
            Assert.Equal(RemoteThisDeviceState.Ready, RemoteCommandsView.StateOf(view));
        }

        /// <summary>`WINDOW.md` section 6 rows a target shows: English key, Russian, Ukrainian.</summary>
        public static IEnumerable<object[]> ContractWords() => new[]
        {
            new object[] { "External control", "Внешнее управление", "Зовнішнє керування" },
            new object[] { "Listen to commands from my other devices", "Принимать команды с моих других устройств", "Приймати команди з моїх інших пристроїв" },
            new object[] { "Off until you turn it on. Only the devices you allow, and only what you allow.", "Выключено, пока вы не включите. Только устройства, которые вы разрешили, и только то, что вы разрешили.", "Вимкнено, доки ви не ввімкнете. Лише пристрої, які ви дозволили, і лише те, що ви дозволили." },
            new object[] { "Open External control", "Открыть внешнее управление", "Відкрити зовнішнє керування" },
            new object[] { "Close", "Закрыть", "Закрити" },
            new object[] { "This device", "Это устройство", "Цей пристрій" },
            new object[] { "Off. Commands from other devices are not accepted.", "Выключено. Команды с других устройств не принимаются.", "Вимкнено. Команди з інших пристроїв не приймаються." },
            new object[] { "No connected exchange server carries commands.", "Ни один подключённый сервер обмена не передаёт команды.", "Жоден під'єднаний сервер обміну не передає команди." },
            new object[] { "Open exchange settings", "Открыть настройки обмена", "Відкрити налаштування обміну" },
            new object[] { "Sign in to this account again to accept commands.", "Войдите в этот аккаунт ещё раз, чтобы принимать команды.", "Увійдіть у цей акаунт ще раз, щоб приймати команди." },
            new object[] { "Sign in again", "Войти снова", "Увійти знову" },
            new object[] { "On. The exchange server cannot be reached right now.", "Включено. Сервер обмена сейчас недоступен.", "Увімкнено. Сервер обміну зараз недоступний." },
            new object[] { "On. The devices you allow can send commands now.", "Включено. Разрешённые устройства могут отправлять команды.", "Увімкнено. Дозволені пристрої можуть надсилати команди." },
            new object[] { "Waiting for your answer", "Ждёт вашего ответа", "Чекає на вашу відповідь" },
            new object[] { "Who can control this device", "Кто может управлять этим устройством", "Хто може керувати цим пристроєм" },
            new object[] { "Turn on listening to choose devices.", "Включите приём команд, чтобы выбрать устройства.", "Увімкніть приймання команд, щоб вибрати пристрої." },
            new object[] { "No other device of your account can send commands yet. Sign in on it with the same account.", "Пока ни одно другое устройство вашего аккаунта не может отправлять команды. Войдите на нём в тот же аккаунт.", "Поки жоден інший пристрій вашого акаунта не може надсилати команди. Увійдіть на ньому в той самий акаунт." },
            new object[] { "Online", "В сети", "У мережі" },
            new object[] { "Offline since {0}", "Не в сети с {0}", "Не в мережі з {0}" },
            new object[] { "Allow this device", "Разрешить это устройство", "Дозволити цей пристрій" },
            new object[] { "Safe commands", "Безопасные команды", "Безпечні команди" },
            new object[] { "Commands that read your data", "Команды, которые читают ваши данные", "Команди, що читають ваші дані" },
            new object[] { "Commands that change your data", "Команды, которые изменяют ваши данные", "Команди, що змінюють ваші дані" },
            new object[] { "Commands that send your data off this device", "Команды, которые отправляют ваши данные с этого устройства", "Команди, що надсилають ваші дані з цього пристрою" },
            new object[] { "No access", "Нет доступа", "Немає доступу" },
            new object[] { "Ask each time", "Спрашивать каждый раз", "Питати щоразу" },
            new object[] { "Always allow", "Разрешать всегда", "Дозволяти завжди" },
            new object[] { "Always allow this kind?", "Разрешать такие команды всегда?", "Дозволяти такі команди завжди?" },
            new object[] { "{0} will run commands of this kind on this device without asking you.", "{0} будет выполнять такие команды на этом устройстве, не спрашивая вас.", "{0} виконуватиме такі команди на цьому пристрої, не питаючи вас." },
            new object[] { "Cancel", "Отмена", "Скасувати" },
            new object[] { "This change was not saved. Try again.", "Изменение не сохранено. Попробуйте ещё раз.", "Зміну не збережено. Спробуйте ще раз." },
            new object[] { "Commands this program accepts", "Команды, которые принимает эта программа", "Команди, які приймає ця програма" },
            new object[] { "Asks on this device every time", "Каждый раз спрашивает на этом устройстве", "Щоразу питає на цьому пристрої" },
            new object[] { "Recent requests", "Последние запросы", "Останні запити" },
            new object[] { "No requests yet.", "Запросов пока не было.", "Запитів поки не було." },
            new object[] { "Open the full log", "Открыть весь журнал", "Відкрити весь журнал" },
            new object[] { "{0} asks {1}", "{0} просит {1}", "{0} просить {1}" },
            new object[] { "Kind: {0}", "Вид: {0}", "Вид: {0}" },
            new object[] { "Answer within {0} s. Without an answer the command is not run.", "Ответьте в течение {0} с. Без ответа команда не выполняется.", "Дайте відповідь протягом {0} с. Без відповіді команда не виконується." },
            new object[] { "Allow", "Разрешить", "Дозволити" },
            new object[] { "Decline", "Отклонить", "Відхилити" },
            new object[] { "done", "выполнено", "виконано" },
            new object[] { "accepted", "принято", "прийнято" },
            new object[] { "busy", "занято", "зайнято" },
            new object[] { "failed", "не удалось", "не вдалося" },
            new object[] { "not allowed", "не разрешено", "не дозволено" },
            new object[] { "declined", "отклонено", "відхилено" },
            new object[] { "too late", "слишком поздно", "надто пізно" },
            new object[] { "no such command", "нет такой команды", "немає такої команди" },
            new object[] { "wrong arguments", "неверные аргументы", "хибні аргументи" },
            new object[] { "could not be opened", "не удалось открыть", "не вдалося відкрити" },
        };

        [Theory]
        [MemberData(nameof(ContractWords))]
        public void EveryWordIsTheContracts(string english, string russian, string ukrainian)
        {
            Assert.Equal(english, Localization.Translate(Language("en"), english));
            Assert.Equal(russian, Localization.Translate(Language("ru"), english));
            Assert.Equal(ukrainian, Localization.Translate(Language("uk"), english));
        }
    }
}
