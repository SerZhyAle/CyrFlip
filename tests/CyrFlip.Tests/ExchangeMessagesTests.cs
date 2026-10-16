using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The table of messages (<c>DEVICE-EXCHANGE</c> item CZ) and the ladder of checks (item CY) as this program shows
    /// them. The English, Russian and Ukrainian words are the contract's, character for character (item DA: a text is
    /// never reworded, shortened or joined with another); the other languages must at least exist.
    /// </summary>
    public class ExchangeMessagesTests
    {
        private static string Ru(string s) => Localization.Translate("Русский", s);
        private static string Uk(string s) => Localization.Translate("Українська", s);

        // (code, English, Russian, Ukrainian) - copied from the contract's table.
        public static IEnumerable<object[]> Rows() => new[]
        {
            new object[] { ExchangeCodes.BadCredentials, "The login or the password is wrong.", "Логин или пароль неверны.", "Логін або пароль неправильні." },
            new object[] { ExchangeCodes.DeviceRevoked, "This device was removed from the account.", "Это устройство удалено из аккаунта.", "Цей пристрій видалено з акаунта." },
            new object[] { ExchangeCodes.SignedOut, "The password of this account was changed, so this device was signed out.", "Пароль этого аккаунта изменили, поэтому это устройство вышло из аккаунта.", "Пароль цього акаунта змінили, тому цей пристрій вийшов з акаунта." },
            new object[] { ExchangeCodes.VersionUnsupported, "This program and the server speak different versions of the exchange.", "Эта программа и сервер работают по разным версиям обмена.", "Ця програма і сервер працюють за різними версіями обміну." },
            new object[] { ExchangeCodes.TlsRequired, "The server accepts this only over a protected connection, and this one is not protected.", "Сервер принимает это только по защищённому соединению, а это соединение не защищено.", "Сервер приймає це лише через захищене з'єднання, а це з'єднання не захищене." },
            new object[] { ExchangeCodes.IdCollision, "The server already knows another device under this device's number. Remove this server from the list and add it again.", "Сервер уже знает другое устройство под номером этого. Удалите этот сервер из списка и добавьте его снова.", "Сервер уже знає інший пристрій під таким самим номером. Видаліть цей сервер зі списку й додайте його знову." },
            new object[] { ExchangeCodes.Capacity, "The server cannot take this: it has reached a limit of its own, or the item is too big to be listed.", "Сервер не может это принять: он достиг своего предела или объект слишком большой, чтобы показать его в списке.", "Сервер не може це прийняти: він досяг свого ліміту або об'єкт завеликий, щоб показати його в списку." },
            new object[] { ExchangeCodes.Unavailable, "The device or the server is not available right now.", "Сейчас устройство или сервер недоступны.", "Зараз пристрій або сервер недоступні." },
            new object[] { ExchangeCodes.RateLimited, "Too many attempts in a row.", "Слишком много попыток подряд.", "Забагато спроб поспіль." },
            new object[] { ExchangeCodes.LimitReached, "A limit set on the server has been reached.", "На сервере достигнут установленный предел.", "На сервері досягнуто встановленого ліміту." },
            new object[] { ExchangeCodes.NotPermitted, "A guest account cannot do this. If you need it, ask for an invitation of the kind \"own device or family\".", "Гостевой аккаунт не может это делать. Если это нужно, попросите приглашение «своё устройство или семья».", "Гостьовий акаунт не може цього робити. Якщо це потрібно, попросіть запрошення «свій пристрій або сім'я»." },
            new object[] { ExchangeCodes.Unreachable, "Cannot reach the server.", "Не удаётся связаться с сервером.", "Не вдається зв'язатися із сервером." },
            new object[] { ExchangeCodes.Unanswered, "The server did not answer.", "Сервер не ответил.", "Сервер не відповів." },
            new object[] { ExchangeCodes.TlsFailed, "A protected connection could not be set up.", "Не удалось установить защищённое соединение.", "Не вдалося встановити захищене з'єднання." },
            new object[] { ExchangeCodes.CertificateChanged, "This server does not look like the one you connected to before.", "Этот сервер не похож на тот, к которому вы подключались раньше.", "Цей сервер не схожий на той, до якого ви підключалися раніше." },
            new object[] { ExchangeCodes.EmptyCredential, "Enter the login and the password first.", "Сначала введите логин и пароль.", "Спочатку введіть логін і пароль." },
            new object[] { ExchangeCodes.ClipboardEmpty, "There is nothing in the clipboard on the server.", "Буфер на сервере пуст.", "Буфер на сервері порожній." },
            new object[] { ExchangeCodes.E2eMissing, "This device cannot receive protected content yet, so nothing is sent to it through the server.", "Это устройство пока не умеет принимать защищённое содержимое, поэтому через сервер ему ничего не отправляется.", "Цей пристрій поки не вміє приймати захищений вміст, тому через сервер йому нічого не надсилається." },
            new object[] { ExchangeCodes.KeyChanged, "The protection key of this device has changed, so nothing is sent to it. Remove the device from the account and add it again.", "Ключ защиты этого устройства изменился, поэтому ему ничего не отправляется. Удалите устройство из аккаунта и добавьте его снова.", "Ключ захисту цього пристрою змінився, тому йому нічого не надсилається. Видаліть пристрій з акаунта й додайте його знову." },
            new object[] { ExchangeCodes.ServerNoE2e, "This server cannot carry protected content, so folders, broadcasts and the clipboard do not go through it.", "Этот сервер не умеет передавать защищённое содержимое, поэтому папки, трансляции и буфер через него не идут.", "Цей сервер не вміє передавати захищений вміст, тому папки, трансляції й буфер через нього не йдуть." },
            new object[] { ExchangeCodes.SealedFailed, "This could not be opened on this device. Try again from the other device.", "Это не удалось открыть на этом устройстве. Повторите с другого устройства.", "Це не вдалося відкрити на цьому пристрої. Повторіть з іншого пристрою." },
        };

        [Theory]
        [MemberData(nameof(Rows))]
        public void EveryRowOfTheTableIsShownInTheContractsWordsInThreeLanguages(string code, string en, string ru, string uk)
        {
            Assert.Equal(en, ExchangeMessages.Describe(code, ExchangeContext.Enroll, s => s).What);
            Assert.Equal(ru, ExchangeMessages.Describe(code, ExchangeContext.Enroll, Ru).What);
            Assert.Equal(uk, ExchangeMessages.Describe(code, ExchangeContext.Enroll, Uk).What);
        }

        [Fact]
        public void LimitReachedOnASendHasItsOwnRowAndNoStep()
        {
            ExchangeMessage send = ExchangeMessages.Describe(ExchangeCodes.LimitReached, ExchangeContext.Send, s => s);
            Assert.Equal("This server does not take a clipboard of this size, or has the clipboard turned off. Send something smaller, or ask whoever runs the server.", send.What);
            Assert.False(send.HasStep);
            Assert.Equal("Этот сервер не принимает буфер такого размера или отключил буфер. Отправьте что-нибудь поменьше или обратитесь к тому, кто управляет сервером.",
                ExchangeMessages.Describe(ExchangeCodes.LimitReached, ExchangeContext.Send, Ru).What);
            Assert.Equal("Цей сервер не приймає буфер такого розміру або вимкнув буфер. Надішліть щось менше або зверніться до того, хто керує сервером.",
                ExchangeMessages.Describe(ExchangeCodes.LimitReached, ExchangeContext.Send, Uk).What);
            // on a get, or when enrolling, the general row applies
            Assert.Equal("A limit set on the server has been reached.", ExchangeMessages.Describe(ExchangeCodes.LimitReached, ExchangeContext.Get, s => s).What);
        }

        [Fact]
        public void AFailureShowsWhatHappenedThenWhatToTryAndTheCodeOnlyInTheDetails()
        {
            ExchangeMessage m = ExchangeMessages.Describe(ExchangeCodes.BadCredentials, ExchangeContext.Enroll, s => s);
            Assert.Equal("credentials", m.StepId);
            Assert.Equal("Check the login and type the password again. If you used a one-time code, ask for a new one.", m.Try);
            Assert.Equal("The login or the password is wrong.\nCheck the login and type the password again. If you used a one-time code, ask for a new one.", m.Text);
            Assert.DoesNotContain("bad-credentials", m.Text);
            Assert.Equal("bad-credentials", m.Details);
        }

        [Fact]
        public void ARowWithNoStepShowsItsOwnTextAlone()
        {
            ExchangeMessage m = ExchangeMessages.Describe(ExchangeCodes.EmptyCredential, ExchangeContext.Enroll, s => s);
            Assert.False(m.HasStep);
            Assert.Equal(m.What, m.Text);
        }

        [Fact]
        public void AnUnknownCodeIsAGenericRefusalPointingAtTheOperator()
        {
            ExchangeMessage m = ExchangeMessages.Describe("something-new", ExchangeContext.Send, s => s);
            Assert.Equal("The server refused this.", m.What);
            Assert.Equal("operator", m.StepId);
            Assert.Equal("Сервер отказал в этом.", ExchangeMessages.Describe("something-new", ExchangeContext.Send, Ru).What);
            Assert.Equal("Сервер відмовив у цьому.", ExchangeMessages.Describe("something-new", ExchangeContext.Send, Uk).What);
        }

        [Fact]
        public void TheFifteenStepsOfTheLadderAreTheContractsWordsInThreeLanguages()
        {
            var expectedRu = new Dictionary<string, string>
            {
                ["pc-asleep"] = "Включите и разбудите устройство, на котором работает сервер или сервис шаринга, и не давайте ему засыпать.",
                ["address-lan"] = "Проверьте адрес и убедитесь, что оба устройства в одной сети Wi-Fi или в одной домашней сети.",
                ["operator"] = "Обратитесь к тому, кто управляет сервером: исправить это можно только на стороне сервера.",
                ["credentials"] = "Проверьте логин и введите пароль ещё раз. Если вы вводили одноразовый код, попросите новый.",
                ["device-clock"] = "Установите на этом устройстве правильные дату и время или включите автоматическую настройку времени и попробуйте ещё раз.",
            };
            foreach (KeyValuePair<string, string> pair in expectedRu)
                Assert.Equal(pair.Value, Ru(ExchangeMessages.StepText(pair.Key)));
            Assert.Equal("Зверніться до того, хто керує сервером: виправити це можна лише на боці сервера.", Uk(ExchangeMessages.StepText("operator")));
            Assert.Equal(15, new[] { "pc-asleep", "address-lan", "guest-network", "vpn", "windows-public-network", "ip-family", "server-internet",
                "port-forward", "device-clock", "certificate", "credentials", "sign-in-rate", "invitation", "device-removed", "operator" }
                .Count(id => ExchangeMessages.StepText(id).Length > 0));
        }

        [Fact]
        public void TheLadderStartsAtTheRowsStepAndKeepsTheContractsOrder()
        {
            IReadOnlyList<string> own = ExchangeMessages.Ladder("pc-asleep", onOwnNetwork: true);
            Assert.Equal(new[] { "pc-asleep", "address-lan", "guest-network", "vpn", "windows-public-network", "ip-family", "device-clock", "certificate", "credentials", "sign-in-rate", "operator" }, own);
            IReadOnlyList<string> outside = ExchangeMessages.Ladder("pc-asleep", onOwnNetwork: false);
            Assert.Equal(new[] { "pc-asleep", "vpn", "ip-family", "server-internet", "port-forward", "device-clock", "certificate", "credentials", "sign-in-rate", "operator" }, outside);
            Assert.Equal(new[] { "certificate", "credentials", "sign-in-rate", "operator" }, ExchangeMessages.Ladder("certificate", true));
            Assert.Equal(new[] { "operator" }, ExchangeMessages.Ladder("operator", false));
        }

        [Fact]
        public void TheInvitationStepIsNeverPartOfAClipboardOperationAndDeviceRemovedOnlyWhenItStarts()
        {
            Assert.DoesNotContain("invitation", ExchangeMessages.Ladder("pc-asleep", true));
            Assert.DoesNotContain("device-removed", ExchangeMessages.Ladder("credentials", true));
            Assert.Equal(new[] { "device-removed", "operator" }, ExchangeMessages.Ladder("device-removed", true));
        }

        [Theory]
        [InlineData("192.168.1.20", true)]
        [InlineData("10.0.0.5", true)]
        [InlineData("172.16.4.1", true)]
        [InlineData("172.32.4.1", false)]
        [InlineData("169.254.3.3", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("[fe80::1]", true)]
        [InlineData("[fd00::5]", true)]
        [InlineData("8.8.8.8", false)]
        [InlineData("nas", true)]
        [InlineData("nas.local", true)]
        [InlineData("exchange.example.org", false)]
        [InlineData("", false)]
        public void AnAddressOnThePersonsOwnNetworkIsToldFromOneReachedFromTheInternet(string host, bool own)
        {
            Assert.Equal(own, ExchangeMessages.IsOwnNetwork(host));
        }

        [Fact]
        public void TheHonestTextIsTheContractsWholeInThreeLanguages()
        {
            Assert.StartsWith("The exchange server connects your devices, so it knows the names of your account, devices, folders and broadcasts", ExchangeMessages.HonestText);
            Assert.EndsWith("and it would appear in your list of devices.", ExchangeMessages.HonestText);
            Assert.Equal("Сервер обмена соединяет ваши устройства, поэтому знает имена вашего аккаунта, устройств, папок и трансляций, видит, какие устройства в сети, и сколько и когда передаётся. Открыть то, что идёт через него, он не может: папки и их файлы, трансляции и буфер шифруются на вашем устройстве и открываются только на других ваших устройствах или у тех, кому вы дали доступ. Чтобы заглянуть внутрь, тому, кто управляет сервером, пришлось бы добавить в ваш аккаунт своё устройство, и оно появилось бы в списке ваших устройств.",
                Ru(ExchangeMessages.HonestText));
            Assert.Equal("Сервер обміну з'єднує ваші пристрої, тому знає імена вашого акаунта, пристроїв, папок і трансляцій, бачить, які пристрої в мережі, і скільки й коли передається. Відкрити те, що йде через нього, він не може: папки та їхні файли, трансляції й буфер шифруються на вашому пристрої та відкриваються лише на інших ваших пристроях або в тих, кому ви дали доступ. Щоб зазирнути всередину, тому, хто керує сервером, довелося б додати до вашого акаунта свій пристрій, і він з'явився б у списку ваших пристроїв.",
                Uk(ExchangeMessages.HonestText));
        }

        [Fact]
        public void EveryCodeThisProgramCanProduceHasAMessageInEveryLanguage()
        {
            string[] codes = typeof(ExchangeCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToArray();
            Assert.True(codes.Length > 30);
            foreach (string language in Localization.Names)
            {
                foreach (string code in codes)
                {
                    foreach (ExchangeContext context in Enum.GetValues(typeof(ExchangeContext)))
                    {
                        ExchangeMessage m = ExchangeMessages.Describe(code, context, s => Localization.Translate(language, s));
                        Assert.False(string.IsNullOrWhiteSpace(m.What), code + " in " + language);
                        if (m.StepId.Length > 0) Assert.False(string.IsNullOrWhiteSpace(m.Try), code + " step in " + language);
                    }
                }
            }
        }

        [Fact]
        public void EveryEnglishSentenceOfTheTableIsRegisteredInAllThirteenLanguages()
        {
            foreach (string source in ExchangeMessages.AllSourceTexts().Concat(new[] { ExchangeMessages.HonestText }))
            {
                foreach (string language in Localization.Names)
                {
                    string translated = Localization.Translate(language, source);
                    Assert.False(string.IsNullOrWhiteSpace(translated), source + " / " + language);
                    if (language != "English")
                        Assert.NotEqual(source, translated); // an untranslated sentence would read English in this language
                }
            }
        }
    }
}
