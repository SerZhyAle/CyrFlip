namespace CyrFlip
{
    /// <summary>
    /// Localization strings of the traffic light (ticket S0069): the board, its settings page, the target
    /// editor, the balloon and the change journal commands. All 13 languages are authored with the strings.
    /// </summary>
    internal static partial class Localization
    {
        private static void AddTrafficLightStrings()
        {
            Add("(no label)",
                ru: "(без подписи)", uk: "(без підпису)", de: "(ohne Bezeichnung)", it: "(senza etichetta)",
                es: "(sin etiqueta)", fr: "(sans libellé)", pt: "(sem rótulo)", ar: "(بلا تسمية)",
                hi: "(बिना नाम)", bn: "(নাম নেই)", ur: "(بغیر نام)", zh: "(无名称)");

            Add("TCP port",
                ru: "TCP-порт", uk: "TCP-порт", de: "TCP-Port", it: "Porta TCP",
                es: "Puerto TCP", fr: "Port TCP", pt: "Porta TCP", ar: "منفذ TCP",
                hi: "TCP पोर्ट", bn: "TCP পোর্ট", ur: "TCP پورٹ", zh: "TCP 端口");

            Add("{0} min",
                ru: "{0} мин", uk: "{0} хв", de: "{0} Min.", it: "{0} min",
                es: "{0} min", fr: "{0} min", pt: "{0} min", ar: "{0} د",
                hi: "{0} मिनट", bn: "{0} মিনিট", ur: "{0} منٹ", zh: "{0} 分钟");

            Add("{0} ms",
                ru: "{0} мс", uk: "{0} мс", de: "{0} ms", it: "{0} ms",
                es: "{0} ms", fr: "{0} ms", pt: "{0} ms", ar: "{0} مللي ثانية",
                hi: "{0} मि.से.", bn: "{0} মি.সে.", ur: "{0} ملی سیکنڈ", zh: "{0} 毫秒");

            Add("{0} s",
                ru: "{0} с", uk: "{0} с", de: "{0} s", it: "{0} s",
                es: "{0} s", fr: "{0} s", pt: "{0} s", ar: "{0} ث",
                hi: "{0} सेकंड", bn: "{0} সেকেন্ড", ur: "{0} سیکنڈ", zh: "{0} 秒");

            Add("{0} h",
                ru: "{0} ч", uk: "{0} год", de: "{0} Std.", it: "{0} h",
                es: "{0} h", fr: "{0} h", pt: "{0} h", ar: "{0} س",
                hi: "{0} घंटे", bn: "{0} ঘণ্টা", ur: "{0} گھنٹے", zh: "{0} 小时");

            Add("Address",
                ru: "Адрес", uk: "Адреса", de: "Adresse", it: "Indirizzo",
                es: "Dirección", fr: "Adresse", pt: "Endereço", ar: "العنوان",
                hi: "पता", bn: "ঠিকানা", ur: "پتہ", zh: "地址");

            Add("Invalid address",
                ru: "Адрес записан неверно", uk: "Адресу записано неправильно", de: "Ungültige Adresse", it: "Indirizzo non valido",
                es: "Dirección no válida", fr: "Adresse non valide", pt: "Endereço inválido", ar: "العنوان غير صالح",
                hi: "पता मान्य नहीं है", bn: "ঠিকানা সঠিক নয়", ur: "پتہ درست نہیں", zh: "地址无效");

            Add("The host address is not valid: use an IP address or a name without spaces.",
                ru: "Адрес узла записан неверно: нужен IP-адрес или имя без пробелов.", uk: "Адресу вузла записано неправильно: потрібна IP-адреса або ім'я без пробілів.", de: "Die Hostadresse ist ungültig: Verwenden Sie eine IP-Adresse oder einen Namen ohne Leerzeichen.", it: "L'indirizzo dell'host non è valido: usa un indirizzo IP o un nome senza spazi.",
                es: "La dirección del host no es válida: use una dirección IP o un nombre sin espacios.", fr: "L'adresse de l'hôte n'est pas valide : saisissez une adresse IP ou un nom sans espaces.", pt: "O endereço do host não é válido: use um endereço IP ou um nome sem espaços.", ar: "عنوان المضيف غير صالح: استخدم عنوان IP أو اسمًا بلا مسافات.",
                hi: "होस्ट का पता मान्य नहीं है: IP पता या बिना स्पेस वाला नाम दें।", bn: "হোস্টের ঠিকানা সঠিক নয়: IP ঠিকানা বা ফাঁকা ছাড়া নাম দিন।", ur: "میزبان کا پتہ درست نہیں: IP پتہ یا بغیر خالی جگہ کا نام دیں۔", zh: "主机地址无效：请输入 IP 地址或不含空格的名称。");

            Add("Address:",
                ru: "Адрес:", uk: "Адреса:", de: "Adresse:", it: "Indirizzo:",
                es: "Dirección:", fr: "Adresse :", pt: "Endereço:", ar: "العنوان:",
                hi: "पता:", bn: "ঠিকানা:", ur: "پتہ:", zh: "地址：");

            Add("Enter an address, a host name, a folder or a drive.",
                ru: "Введите адрес, имя узла, папку или диск.", uk: "Введіть адресу, ім'я вузла, папку або диск.", de: "Geben Sie eine Adresse, einen Hostnamen, einen Ordner oder ein Laufwerk ein.", it: "Inserisci un indirizzo, il nome di un host, una cartella o un'unità.",
                es: "Introduzca una dirección, un nombre de host, una carpeta o una unidad.", fr: "Saisissez une adresse, un nom d'hôte, un dossier ou un lecteur.", pt: "Digite um endereço, um nome de host, uma pasta ou uma unidade.", ar: "أدخل عنوانًا أو اسم مضيف أو مجلدًا أو محرك أقراص.",
                hi: "कोई पता, होस्ट का नाम, फ़ोल्डर या ड्राइव दर्ज करें।", bn: "একটি ঠিকানা, হোস্টের নাম, ফোল্ডার বা ড্রাইভ লিখুন।", ur: "کوئی پتہ، میزبان کا نام، فولڈر یا ڈرائیو درج کریں۔", zh: "请输入地址、主机名、文件夹或驱动器。");

            Add("Enter an address.",
                ru: "Введите адрес.", uk: "Введіть адресу.", de: "Geben Sie eine Adresse ein.", it: "Inserisci un indirizzo.",
                es: "Introduzca una dirección.", fr: "Saisissez une adresse.", pt: "Digite um endereço.", ar: "أدخل عنوانًا.",
                hi: "कोई पता दर्ज करें।", bn: "একটি ঠিকানা লিখুন।", ur: "کوئی پتہ درج کریں۔", zh: "请输入地址。");

            Add("Type",
                ru: "Вид", uk: "Тип", de: "Art", it: "Tipo",
                es: "Tipo", fr: "Type", pt: "Tipo", ar: "النوع",
                hi: "प्रकार", bn: "ধরন", ur: "قسم", zh: "类型");

            Add("Type: (target kind)",
                ru: "Вид:", en: "Type:", uk: "Тип:", de: "Art:", it: "Tipo:",
                es: "Tipo:", fr: "Type :", pt: "Tipo:", ar: "النوع:",
                hi: "प्रकार:", bn: "ধরন:", ur: "قسم:", zh: "类型：");

            Add("Enable the availability traffic light",
                ru: "Включить светофор доступности", uk: "Увімкнути світлофор доступності", de: "Verfügbarkeits-Ampel aktivieren", it: "Attiva il semaforo di disponibilità",
                es: "Activar el semáforo de disponibilidad", fr: "Activer le feu de disponibilité", pt: "Ativar o semáforo de disponibilidade", ar: "تفعيل إشارة التوفر",
                hi: "उपलब्धता ट्रैफ़िक लाइट सक्षम करें", bn: "প্রাপ্যতার ট্রাফিক লাইট সক্রিয় করুন", ur: "دستیابی کی ٹریفک لائٹ فعال کریں", zh: "启用可用性信号灯");

            Add("Choose the type of target.",
                ru: "Выберите вид цели.", uk: "Виберіть тип цілі.", de: "Wählen Sie die Art des Ziels.", it: "Scegli il tipo di obiettivo.",
                es: "Elija el tipo de objetivo.", fr: "Choisissez le type de cible.", pt: "Escolha o tipo de alvo.", ar: "اختر نوع الهدف.",
                hi: "लक्ष्य का प्रकार चुनें।", bn: "লক্ষ্যের ধরন বেছে নিন।", ur: "ہدف کی قسم منتخب کریں۔", zh: "请选择目标类型。");

            Add("Up",
                ru: "Выше", uk: "Вище", de: "Nach oben", it: "Su",
                es: "Subir", fr: "Monter", pt: "Subir", ar: "أعلى",
                hi: "ऊपर", bn: "উপরে", ur: "اوپر", zh: "上移");

            Add("Group:",
                ru: "Группа:", uk: "Група:", de: "Gruppe:", it: "Gruppo:",
                es: "Grupo:", fr: "Groupe :", pt: "Grupo:", ar: "المجموعة:",
                hi: "समूह:", bn: "গ্রুপ:", ur: "گروپ:", zh: "分组：");

            Add("Keep the board on top of other windows",
                ru: "Держать доску поверх остальных окон", uk: "Тримати дошку поверх інших вікон", de: "Ampel über anderen Fenstern halten", it: "Mantieni il semaforo sopra le altre finestre",
                es: "Mantener el semáforo sobre las demás ventanas", fr: "Garder le tableau au-dessus des autres fenêtres", pt: "Manter o semáforo acima das outras janelas", ar: "إبقاء اللوحة فوق النوافذ الأخرى",
                hi: "बोर्ड को अन्य विंडो के ऊपर रखें", bn: "বোর্ডকে অন্য উইন্ডোর উপরে রাখুন", ur: "بورڈ کو دوسری ونڈوز کے اوپر رکھیں", zh: "让面板保持在其他窗口上方");

            Add("Drive",
                ru: "Диск", uk: "Диск", de: "Laufwerk", it: "Unità",
                es: "Unidad", fr: "Lecteur", pt: "Unidade", ar: "محرك الأقراص",
                hi: "ड्राइव", bn: "ড্রাইভ", ur: "ڈرائیو", zh: "驱动器");

            Add("A drive is found by its letter: if it comes back under another letter, it is shown as \"not connected\".",
                ru: "Диск ищется по букве: вернувшись под другой буквой, он будет показан как «не подключён».", uk: "Диск шукається за літерою: повернувшись під іншою літерою, він буде показаний як «не підключений».", de: "Ein Laufwerk wird über seinen Buchstaben gefunden: Kommt es unter einem anderen Buchstaben zurück, wird es als „nicht verbunden“ angezeigt.", it: "Un'unità viene cercata in base alla lettera: se torna con un'altra lettera, viene mostrata come \"non connessa\".",
                es: "Una unidad se busca por su letra: si vuelve con otra letra, se muestra como «no conectada».", fr: "Un lecteur est repéré par sa lettre : s'il revient sous une autre lettre, il est affiché comme « non connecté ».", pt: "Uma unidade é encontrada pela letra: se voltar com outra letra, é mostrada como \"não conectada\".", ar: "يُبحث عن محرك الأقراص بحرفه: إذا عاد بحرف آخر فسيظهر \"غير متصل\".",
                hi: "ड्राइव को उसके अक्षर से खोजा जाता है: दूसरे अक्षर के साथ लौटने पर वह \"कनेक्ट नहीं\" दिखेगी।", bn: "ড্রাইভ তার অক্ষর দিয়ে খোঁজা হয়: অন্য অক্ষরে ফিরে এলে সেটি \"সংযুক্ত নয়\" দেখাবে।", ur: "ڈرائیو اپنے حرف سے تلاش کی جاتی ہے: دوسرے حرف کے ساتھ واپس آئے تو وہ \"منسلک نہیں\" دکھائی جائے گی۔", zh: "驱动器按盘符查找：若以其他盘符重新出现，将显示为“未连接”。");

            Add("Add the availability traffic light targets: CyrFlip will check the addresses listed in the file",
                ru: "Добавить цели светофора доступности: CyrFlip будет проверять перечисленные в файле адреса", uk: "Додати цілі світлофора доступності: CyrFlip перевірятиме перелічені у файлі адреси", de: "Ziele der Verfügbarkeits-Ampel hinzufügen: CyrFlip prüft die in der Datei aufgeführten Adressen", it: "Aggiungi gli obiettivi del semaforo di disponibilità: CyrFlip controllerà gli indirizzi elencati nel file",
                es: "Añadir los objetivos del semáforo de disponibilidad: CyrFlip comprobará las direcciones del archivo", fr: "Ajouter les cibles du feu de disponibilité : CyrFlip vérifiera les adresses listées dans le fichier", pt: "Adicionar os alvos do semáforo de disponibilidade: o CyrFlip verificará os endereços listados no arquivo", ar: "إضافة أهداف إشارة التوفر: سيفحص CyrFlip العناوين المذكورة في الملف",
                hi: "उपलब्धता ट्रैफ़िक लाइट के लक्ष्य जोड़ें: CyrFlip फ़ाइल में सूचीबद्ध पतों की जाँच करेगा", bn: "প্রাপ্যতার ট্রাফিক লাইটের লক্ষ্য যোগ করুন: CyrFlip ফাইলে তালিকাভুক্ত ঠিকানাগুলো যাচাই করবে", ur: "دستیابی کی ٹریفک لائٹ کے اہداف شامل کریں: CyrFlip فائل میں درج پتوں کی جانچ کرے گا", zh: "添加可用性信号灯的目标：CyrFlip 将检查文件中列出的地址");

            Add("Add a target..",
                ru: "Добавить цель..", uk: "Додати ціль..", de: "Ziel hinzufügen..", it: "Aggiungi obiettivo..",
                es: "Añadir objetivo..", fr: "Ajouter une cible..", pt: "Adicionar alvo..", ar: "إضافة هدف..",
                hi: "लक्ष्य जोड़ें..", bn: "লক্ষ্য যোগ করুন..", ur: "ہدف شامل کریں..", zh: "添加目标..");

            Add("The addition was undone.",
                ru: "Добавление отменено.", uk: "Додавання скасовано.", de: "Das Hinzufügen wurde rückgängig gemacht.", it: "Aggiunta annullata.",
                es: "Se deshizo la adición.", fr: "L'ajout a été annulé.", pt: "A adição foi desfeita.", ar: "تم التراجع عن الإضافة.",
                hi: "जोड़ना रद्द किया गया।", bn: "যোগ করা বাতিল হয়েছে।", ur: "اضافہ منسوخ کر دیا گیا۔", zh: "已撤销添加。");

            Add("Targets added: {0}.",
                ru: "Добавлено целей: {0}.", uk: "Додано цілей: {0}.", de: "Ziele hinzugefügt: {0}.", it: "Obiettivi aggiunti: {0}.",
                es: "Objetivos añadidos: {0}.", fr: "Cibles ajoutées : {0}.", pt: "Alvos adicionados: {0}.", ar: "عدد الأهداف المضافة: {0}.",
                hi: "जोड़े गए लक्ष्य: {0}।", bn: "যোগ করা লক্ষ্য: {0}।", ur: "شامل کیے گئے اہداف: {0}۔", zh: "已添加目标：{0}。");

            Add("Added: \"{0}\" - {1}.",
                ru: "Добавлено: «{0}» - {1}.", uk: "Додано: «{0}» - {1}.", de: "Hinzugefügt: „{0}“ - {1}.", it: "Aggiunto: \"{0}\" - {1}.",
                es: "Añadido: «{0}» - {1}.", fr: "Ajouté : « {0} » - {1}.", pt: "Adicionado: \"{0}\" - {1}.", ar: "تمت الإضافة: \"{0}\" - {1}.",
                hi: "जोड़ा गया: \"{0}\" - {1}।", bn: "যোগ হয়েছে: \"{0}\" - {1}।", ur: "شامل کیا گیا: \"{0}\" - {1}۔", zh: "已添加：“{0}” - {1}。");

            Add("Add targets on the \"Availability traffic light\" settings page.",
                ru: "Добавьте цели на странице настроек «Светофор доступности».", uk: "Додайте цілі на сторінці налаштувань «Світлофор доступності».", de: "Fügen Sie Ziele auf der Einstellungsseite „Verfügbarkeits-Ampel“ hinzu.", it: "Aggiungi gli obiettivi nella pagina delle impostazioni \"Semaforo di disponibilità\".",
                es: "Añada objetivos en la página de ajustes «Semáforo de disponibilidad».", fr: "Ajoutez des cibles sur la page de réglages « Feu de disponibilité ».", pt: "Adicione alvos na página de configurações \"Semáforo de disponibilidade\".", ar: "أضف الأهداف في صفحة الإعدادات \"إشارة التوفر\".",
                hi: "\"उपलब्धता ट्रैफ़िक लाइट\" सेटिंग पृष्ठ पर लक्ष्य जोड़ें।", bn: "\"প্রাপ্যতার ট্রাফিক লাইট\" সেটিংস পৃষ্ঠায় লক্ষ্য যোগ করুন।", ur: "\"دستیابی کی ٹریفک لائٹ\" کے ترتیبات صفحے پر اہداف شامل کریں۔", zh: "请在“可用性信号灯”设置页添加目标。");

            Add("Advanced",
                ru: "Дополнительно", uk: "Додатково", de: "Erweitert", it: "Avanzate",
                es: "Avanzado", fr: "Avancé", pt: "Avançado", ar: "متقدم",
                hi: "उन्नत", bn: "উন্নত", ur: "اعلیٰ", zh: "高级");

            Add("Accepted HTTP status codes:",
                ru: "Допустимые коды ответа HTTP:", uk: "Допустимі коди відповіді HTTP:", de: "Zulässige HTTP-Statuscodes:", it: "Codici di stato HTTP accettati:",
                es: "Códigos de estado HTTP aceptados:", fr: "Codes d'état HTTP acceptés :", pt: "Códigos de status HTTP aceitos:", ar: "رموز حالة HTTP المقبولة:",
                hi: "स्वीकार्य HTTP स्थिति कोड:", bn: "গ্রহণযোগ্য HTTP স্ট্যাটাস কোড:", ur: "قابلِ قبول HTTP اسٹیٹس کوڈز:", zh: "可接受的 HTTP 状态码：");

            Add("Board",
                ru: "Доска", uk: "Дошка", de: "Ampel", it: "Semaforo",
                es: "Semáforo", fr: "Tableau", pt: "Semáforo", ar: "اللوحة",
                hi: "बोर्ड", bn: "বোর্ড", ur: "بورڈ", zh: "面板");

            Add("Available",
                ru: "Доступно", uk: "Доступно", de: "Erreichbar", it: "Raggiungibile",
                es: "Disponible", fr: "Disponible", pt: "Disponível", ar: "متاح",
                hi: "उपलब्ध", bn: "উপলব্ধ", ur: "دستیاب", zh: "可用");

            Add("Not checked yet",
                ru: "Ещё не проверялось", uk: "Ще не перевірялося", de: "Noch nicht geprüft", it: "Non ancora controllato",
                es: "Aún sin comprobar", fr: "Pas encore vérifié", pt: "Ainda não verificado", ar: "لم يُفحص بعد",
                hi: "अभी जाँचा नहीं गया", bn: "এখনও যাচাই হয়নি", ur: "ابھی جانچا نہیں گیا", zh: "尚未检查");

            Add("The history file does not exist yet: it is written for targets marked \"Include in change history\".",
                ru: "Журнал ещё не создан: в него пишутся цели с отметкой «Записывать изменения в журнал».", uk: "Журнал ще не створено: до нього пишуться цілі з позначкою «Записувати зміни в журнал».", de: "Das Protokoll existiert noch nicht: Es wird für Ziele mit der Markierung „Änderungen im Protokoll festhalten“ geschrieben.", it: "Il file della cronologia non esiste ancora: viene scritto per gli obiettivi con il segno «Registra le modifiche nella cronologia».",
                es: "El archivo del historial aún no existe: se escribe para los objetivos marcados con «Registrar los cambios en el historial».", fr: "Le journal n'existe pas encore : il est écrit pour les cibles cochées « Consigner les changements dans le journal ».", pt: "O arquivo do histórico ainda não existe: ele é escrito para os alvos marcados com «Registrar as alterações no histórico».", ar: "ملف السجل غير موجود بعد: يُكتب للأهداف المعلَّمة بـ \"تسجيل التغييرات في السجل\".",
                hi: "इतिहास फ़ाइल अभी नहीं बनी है: वह \"परिवर्तन इतिहास में दर्ज करें\" से चिह्नित लक्ष्यों के लिए लिखी जाती है।", bn: "ইতিহাস ফাইল এখনও তৈরি হয়নি: \"পরিবর্তনের ইতিহাসে লিখুন\" চিহ্নিত লক্ষ্যগুলোর জন্য এটি লেখা হয়।", ur: "تاریخ کی فائل ابھی نہیں بنی: یہ \"تبدیلیوں کی تاریخ میں درج کریں\" سے نشان زد اہداف کے لیے لکھی جاتی ہے۔", zh: "更改记录文件尚未创建：会为标记了“将更改写入更改记录”的目标写入。");

            Add("Change history",
                ru: "Журнал изменений", uk: "Журнал змін", de: "Änderungsprotokoll", it: "Cronologia delle modifiche",
                es: "Historial de cambios", fr: "Journal des changements", pt: "Histórico de alterações", ar: "سجل التغييرات",
                hi: "परिवर्तन इतिहास", bn: "পরিবর্তনের ইতিহাস", ur: "تبدیلیوں کی تاریخ", zh: "更改记录");

            Add("The history was cleared.",
                ru: "Журнал очищен.", uk: "Журнал очищено.", de: "Das Protokoll wurde gelöscht.", it: "Cronologia cancellata.",
                es: "Se borró el historial.", fr: "Le journal a été effacé.", pt: "O histórico foi apagado.", ar: "تم مسح السجل.",
                hi: "इतिहास साफ़ कर दिया गया।", bn: "ইতিহাস মুছে ফেলা হয়েছে।", ur: "تاریخ صاف کر دی گئی۔", zh: "更改记录已清除。");

            Add("Set the availability traffic light shortcut",
                ru: "Задать хоткей светофора доступности", uk: "Задати гарячу клавішу світлофора доступності", de: "Tastenkombination der Verfügbarkeits-Ampel festlegen", it: "Imposta la scorciatoia del semaforo di disponibilità",
                es: "Definir el atajo del semáforo de disponibilidad", fr: "Définir le raccourci du feu de disponibilité", pt: "Definir o atalho do semáforo de disponibilidade", ar: "تعيين اختصار إشارة التوفر",
                hi: "उपलब्धता ट्रैफ़िक लाइट का शॉर्टकट सेट करें", bn: "প্রাপ্যতার ট্রাফিক লাইটের শর্টকাট নির্ধারণ করুন", ur: "دستیابی کی ٹریفک لائٹ کا شارٹ کٹ مقرر کریں", zh: "设置可用性信号灯快捷键");

            Add("Latency:",
                ru: "Задержка:", uk: "Затримка:", de: "Latenz:", it: "Latenza:",
                es: "Latencia:", fr: "Latence :", pt: "Latência:", ar: "زمن الاستجابة:",
                hi: "विलंब:", bn: "বিলম্ব:", ur: "تاخیر:", zh: "延迟：");

            Add("Include in change history",
                ru: "Записывать изменения в журнал", uk: "Записувати зміни в журнал", de: "Änderungen im Protokoll festhalten", it: "Registra le modifiche nella cronologia",
                es: "Registrar los cambios en el historial", fr: "Consigner les changements dans le journal", pt: "Registrar as alterações no histórico", ar: "تسجيل التغييرات في السجل",
                hi: "परिवर्तन इतिहास में दर्ज करें", bn: "পরিবর্তনের ইতিহাসে লিখুন", ur: "تبدیلیوں کی تاریخ میں درج کریں", zh: "将更改写入更改记录");

            Add("Checking..",
                ru: "Идёт проверка..", uk: "Триває перевірка..", de: "Prüfung läuft..", it: "Controllo in corso..",
                es: "Comprobando..", fr: "Vérification en cours..", pt: "Verificando..", ar: "جارٍ الفحص..",
                hi: "जाँच चल रही है..", bn: "যাচাই চলছে..", ur: "جانچ جاری ہے..", zh: "正在检查..");

            Add("Edit targets..",
                ru: "Изменить цели..", uk: "Змінити цілі..", de: "Ziele bearbeiten..", it: "Modifica obiettivi..",
                es: "Editar objetivos..", fr: "Modifier les cibles..", pt: "Editar alvos..", ar: "تحرير الأهداف..",
                hi: "लक्ष्य बदलें..", bn: "লক্ষ্য সম্পাদনা করুন..", ur: "اہداف میں ترمیم کریں..", zh: "编辑目标..");

            Add("Edit target",
                ru: "Изменить цель", uk: "Змінити ціль", de: "Ziel bearbeiten", it: "Modifica obiettivo",
                es: "Editar objetivo", fr: "Modifier la cible", pt: "Editar alvo", ar: "تحرير الهدف",
                hi: "लक्ष्य बदलें", bn: "লক্ষ্য সম্পাদনা করুন", ur: "ہدف میں ترمیم کریں", zh: "编辑目标");

            Add("Every",
                ru: "Каждые", uk: "Кожні", de: "Alle", it: "Ogni",
                es: "Cada", fr: "Toutes les", pt: "A cada", ar: "كل",
                hi: "हर", bn: "প্রতি", ur: "ہر", zh: "每隔");

            Add("Status codes are numbers from 100 to 599, as a list or a range: 200-399,401",
                ru: "Коды ответа - числа от 100 до 599, списком или диапазоном: 200-399,401", uk: "Коди відповіді - числа від 100 до 599, списком або діапазоном: 200-399,401", de: "Statuscodes sind Zahlen von 100 bis 599, als Liste oder Bereich: 200-399,401", it: "I codici di stato sono numeri da 100 a 599, come elenco o intervallo: 200-399,401",
                es: "Los códigos de estado son números de 100 a 599, en lista o rango: 200-399,401", fr: "Les codes d'état sont des nombres de 100 à 599, en liste ou en plage : 200-399,401", pt: "Os códigos de status são números de 100 a 599, em lista ou intervalo: 200-399,401", ar: "رموز الحالة أرقام من 100 إلى 599، كقائمة أو نطاق: 200-399,401",
                hi: "स्थिति कोड 100 से 599 तक की संख्याएँ हैं, सूची या सीमा के रूप में: 200-399,401", bn: "স্ট্যাটাস কোড ১০০ থেকে ৫৯৯ পর্যন্ত সংখ্যা, তালিকা বা পরিসরে: 200-399,401", ur: "اسٹیٹس کوڈز 100 سے 599 تک کے اعداد ہیں، فہرست یا حد کی صورت میں: 200-399,401", zh: "状态码为 100 至 599 的数字，可写成列表或范围：200-399,401");

            Add("This computer is offline - checking is paused.",
                ru: "Компьютер не в сети - проверка приостановлена.", uk: "Комп'ютер не в мережі - перевірку призупинено.", de: "Dieser Computer ist offline - die Prüfung ist angehalten.", it: "Il computer è offline: il controllo è in pausa.",
                es: "El equipo no tiene red: la comprobación está en pausa.", fr: "Cet ordinateur est hors ligne - la vérification est suspendue.", pt: "O computador está offline - a verificação está em pausa.", ar: "الكمبيوتر غير متصل بالشبكة - الفحص متوقف مؤقتًا.",
                hi: "यह कंप्यूटर ऑफ़लाइन है - जाँच रोकी गई है।", bn: "এই কম্পিউটার অফলাইন - যাচাই বিরতিতে আছে।", ur: "یہ کمپیوٹر آف لائن ہے - جانچ روک دی گئی ہے۔", zh: "此计算机已离线，检查已暂停。");

            Add("Large (tiles)",
                ru: "Крупные", en: "Large", uk: "Великі", de: "Groß", it: "Grandi",
                es: "Grandes", fr: "Grandes", pt: "Grandes", ar: "كبيرة",
                hi: "बड़े", bn: "বড়", ur: "بڑے", zh: "大");

            Add("The login and password were not kept.",
                ru: "Логин и пароль не сохранены.", uk: "Логін і пароль не збережено.", de: "Anmeldename und Passwort wurden nicht übernommen.", it: "Login e password non sono stati conservati.",
                es: "No se conservaron el usuario ni la contraseña.", fr: "L'identifiant et le mot de passe n'ont pas été conservés.", pt: "O login e a senha não foram mantidos.", ar: "لم يُحفظ اسم المستخدم ولا كلمة المرور.",
                hi: "लॉगिन और पासवर्ड सहेजे नहीं गए।", bn: "লগইন ও পাসওয়ার্ড সংরক্ষণ করা হয়নি।", ur: "لاگ اِن اور پاس ورڈ محفوظ نہیں کیے گئے۔", zh: "未保留登录名和密码。");

            Add("Small (tiles)",
                ru: "Маленькие", en: "Small", uk: "Малі", de: "Klein", it: "Piccole",
                es: "Pequeños", fr: "Petites", pt: "Pequenos", ar: "صغيرة",
                hi: "छोटे", bn: "ছোট", ur: "چھوٹے", zh: "小");

            Add("Microscopic (tiles)",
                ru: "Микроскопический", en: "Microscopic", uk: "Мікроскопічний", de: "Mikroskopisch", it: "Microscopiche",
                es: "Microscópicos", fr: "Microscopiques", pt: "Microscópicos", ar: "مجهرية",
                hi: "अति सूक्ष्म", bn: "অতি ক্ষুদ্র", ur: "انتہائی چھوٹے", zh: "微型");

            Add("Microscopic: half-size squares without buttons. Drag any square to move; double-click to open these settings.",
                ru: "Микроскопический: квадраты вдвое меньше маленьких, без кнопок. Тяните за любой квадрат, чтобы переместить панель; двойной щелчок открывает эту страницу настроек.",
                uk: "Мікроскопічний: квадрати вдвічі менші за малі, без кнопок. Тягніть за будь-який квадрат, щоб перемістити панель; подвійне клацання відкриває цю сторінку налаштувань.",
                de: "Mikroskopisch: halb so große Quadrate ohne Schaltflächen. Zum Verschieben ein Quadrat ziehen; ein Doppelklick öffnet diese Einstellungen.",
                it: "Microscopiche: quadrati grandi la metà, senza pulsanti. Trascina un quadrato per spostare il pannello; fai doppio clic per aprire queste impostazioni.",
                es: "Microscópicos: cuadrados de la mitad de tamaño, sin botones. Arrastra cualquier cuadrado para mover el panel; haz doble clic para abrir estos ajustes.",
                fr: "Microscopiques : carrés deux fois plus petits, sans boutons. Faites glisser un carré pour déplacer le panneau ; un double-clic ouvre ces paramètres.",
                pt: "Microscópicos: quadrados com metade do tamanho, sem botões. Arraste qualquer quadrado para mover o painel; clique duas vezes para abrir estas configurações.",
                ar: "مجهرية: مربعات بنصف الحجم دون أزرار. اسحب أي مربع لتحريك اللوحة؛ وانقر مرتين لفتح هذه الإعدادات.",
                hi: "अति सूक्ष्म: आधे आकार के वर्ग, बिना बटन। पैनल खिसकाने के लिए कोई भी वर्ग खींचें; ये सेटिंग खोलने के लिए डबल-क्लिक करें।",
                bn: "অতি ক্ষুদ্র: অর্ধেক আকারের বর্গ, কোনো বোতাম নেই। প্যানেল সরাতে যেকোনো বর্গ টানুন; এই সেটিংস খুলতে ডাবল-ক্লিক করুন।",
                ur: "انتہائی چھوٹے: آدھے سائز کے چوکور، بغیر بٹنوں کے۔ پینل منتقل کرنے کے لیے کوئی چوکور کھینچیں؛ یہ ترتیبات کھولنے کے لیے ڈبل کلک کریں۔",
                zh: "微型：尺寸减半的方块，无按钮。拖动任意方块可移动面板；双击可打开此设置页面。");

            Add("The board holds at most {0} targets.",
                ru: "На доске не больше {0} целей.", uk: "На дошці не більше {0} цілей.", de: "Die Ampel nimmt höchstens {0} Ziele auf.", it: "Il semaforo può contenere al massimo {0} obiettivi.",
                es: "El semáforo admite como máximo {0} objetivos.", fr: "Le tableau contient au plus {0} cibles.", pt: "O semáforo comporta no máximo {0} alvos.", ar: "تتسع اللوحة لـ {0} هدفًا كحد أقصى.",
                hi: "बोर्ड पर अधिकतम {0} लक्ष्य हो सकते हैं।", bn: "বোর্ডে সর্বোচ্চ {0}টি লক্ষ্য থাকতে পারে।", ur: "بورڈ پر زیادہ سے زیادہ {0} اہداف ہو سکتے ہیں۔", zh: "面板最多容纳 {0} 个目标。");

            Add("Not chosen",
                ru: "Не выбрано", uk: "Не вибрано", de: "Nicht gewählt", it: "Non scelto",
                es: "Sin elegir", fr: "Non choisi", pt: "Não escolhido", ar: "لم يُختر",
                hi: "चुना नहीं गया", bn: "বেছে নেওয়া হয়নি", ur: "منتخب نہیں", zh: "未选择");

            Add("The availability traffic light history could not be written - new lines will be saved with the next write.",
                ru: "Не удалось записать журнал светофора доступности - новые строки сохранятся при следующей записи.", uk: "Не вдалося записати журнал світлофора доступності - нові рядки збережуться під час наступного запису.", de: "Das Protokoll der Verfügbarkeits-Ampel konnte nicht geschrieben werden - neue Zeilen werden beim nächsten Schreiben gesichert.", it: "Impossibile scrivere la cronologia del semaforo di disponibilità: le nuove righe verranno salvate alla scrittura successiva.",
                es: "No se pudo escribir el historial del semáforo de disponibilidad; las líneas nuevas se guardarán en la próxima escritura.", fr: "Le journal du feu de disponibilité n'a pas pu être écrit - les nouvelles lignes seront enregistrées à la prochaine écriture.", pt: "Não foi possível gravar o histórico do semáforo de disponibilidade - as novas linhas serão salvas na próxima gravação.", ar: "تعذّرت كتابة سجل إشارة التوفر - ستُحفظ الأسطر الجديدة عند الكتابة التالية.",
                hi: "उपलब्धता ट्रैफ़िक लाइट का इतिहास लिखा नहीं जा सका - नई पंक्तियाँ अगली बार लिखने पर सहेजी जाएँगी।", bn: "প্রাপ্যতার ট্রাফিক লাইটের ইতিহাস লেখা যায়নি - নতুন লাইনগুলো পরের লেখার সময় সংরক্ষিত হবে।", ur: "دستیابی کی ٹریفک لائٹ کی تاریخ لکھی نہ جا سکی - نئی سطریں اگلی بار لکھتے وقت محفوظ ہوں گی۔", zh: "无法写入可用性信号灯的更改记录 - 新的行将在下次写入时保存。");

            Add("The history file could not be opened.",
                ru: "Не удалось открыть журнал.", uk: "Не вдалося відкрити журнал.", de: "Das Protokoll konnte nicht geöffnet werden.", it: "Impossibile aprire la cronologia.",
                es: "No se pudo abrir el historial.", fr: "Le journal n'a pas pu être ouvert.", pt: "Não foi possível abrir o histórico.", ar: "تعذّر فتح السجل.",
                hi: "इतिहास फ़ाइल खोली नहीं जा सकी।", bn: "ইতিহাস ফাইল খোলা যায়নি।", ur: "تاریخ کی فائل کھولی نہ جا سکی۔", zh: "无法打开更改记录。");

            Add("The history could not be deleted: the file is in use by another program.",
                ru: "Не удалось удалить журнал: файл занят другой программой.", uk: "Не вдалося видалити журнал: файл зайнятий іншою програмою.", de: "Das Protokoll konnte nicht gelöscht werden: Die Datei wird von einem anderen Programm verwendet.", it: "Impossibile eliminare la cronologia: il file è in uso da un altro programma.",
                es: "No se pudo borrar el historial: otro programa está usando el archivo.", fr: "Le journal n'a pas pu être supprimé : le fichier est utilisé par un autre programme.", pt: "Não foi possível apagar o histórico: o arquivo está em uso por outro programa.", ar: "تعذّر حذف السجل: الملف مستخدم من برنامج آخر.",
                hi: "इतिहास हटाया नहीं जा सका: फ़ाइल किसी दूसरे प्रोग्राम द्वारा उपयोग में है।", bn: "ইতিহাস মুছে ফেলা যায়নি: ফাইলটি অন্য প্রোগ্রামের ব্যবহারে আছে।", ur: "تاریخ حذف نہ ہو سکی: فائل کسی دوسرے پروگرام کے استعمال میں ہے۔", zh: "无法删除更改记录：文件正被其他程序占用。");

            Add("Unavailable",
                ru: "Недоступно", uk: "Недоступно", de: "Nicht erreichbar", it: "Non raggiungibile",
                es: "No disponible", fr: "Indisponible", pt: "Indisponível", ar: "غير متاح",
                hi: "अनुपलब्ध", bn: "অনুপলব্ধ", ur: "غیر دستیاب", zh: "不可用");

            Add("Unavailable: {0}",
                ru: "Недоступно: {0}", uk: "Недоступно: {0}", de: "Nicht erreichbar: {0}", it: "Non raggiungibile: {0}",
                es: "No disponible: {0}", fr: "Indisponible : {0}", pt: "Indisponível: {0}", ar: "غير متاح: {0}",
                hi: "अनुपलब्ध: {0}", bn: "অনুপলব্ধ: {0}", ur: "غیر دستیاب: {0}", zh: "不可用：{0}");

            Add("Unavailable: {0} and {1} more",
                ru: "Недоступно: {0} и ещё {1}", uk: "Недоступно: {0} та ще {1}", de: "Nicht erreichbar: {0} und {1} weitere", it: "Non raggiungibile: {0} e altri {1}",
                es: "No disponible: {0} y {1} más", fr: "Indisponible : {0} et {1} autres", pt: "Indisponível: {0} e mais {1}", ar: "غير متاح: {0} و{1} أخرى",
                hi: "अनुपलब्ध: {0} और {1} अन्य", bn: "অনুপলব্ধ: {0} এবং আরও {1}টি", ur: "غیر دستیاب: {0} اور {1} مزید", zh: "不可用：{0} 及另外 {1} 个");

            Add("No targets are switched on",
                ru: "Нет включённых целей", uk: "Немає ввімкнених цілей", de: "Keine Ziele aktiviert", it: "Nessun obiettivo attivo",
                es: "No hay objetivos activados", fr: "Aucune cible activée", pt: "Nenhum alvo ativado", ar: "لا توجد أهداف مفعّلة",
                hi: "कोई लक्ष्य चालू नहीं है", bn: "কোনো লক্ষ্য চালু নেই", ur: "کوئی ہدف فعال نہیں", zh: "没有已启用的目标");

            Add("Down",
                ru: "Ниже", uk: "Нижче", de: "Nach unten", it: "Giù",
                es: "Bajar", fr: "Descendre", pt: "Descer", ar: "أسفل",
                hi: "नीचे", bn: "নিচে", ur: "نیچے", zh: "下移");

            Add("New target",
                ru: "Новая цель", uk: "Нова ціль", de: "Neues Ziel", it: "Nuovo obiettivo",
                es: "Nuevo objetivo", fr: "Nouvelle cible", pt: "Novo alvo", ar: "هدف جديد",
                hi: "नया लक्ष्य", bn: "নতুন লক্ষ্য", ur: "نیا ہدف", zh: "新建目标");

            Add("A website address such as https://example.org is needed, without a login or password.",
                ru: "Нужен адрес сайта вида https://example.org без логина и пароля.", uk: "Потрібна адреса сайту на кшталт https://example.org без логіна й пароля.", de: "Erforderlich ist eine Website-Adresse wie https://example.org ohne Anmeldename und Passwort.", it: "Serve l'indirizzo di un sito come https://example.org, senza login e password.",
                es: "Se necesita la dirección de un sitio como https://example.org, sin usuario ni contraseña.", fr: "Il faut l'adresse d'un site telle que https://example.org, sans identifiant ni mot de passe.", pt: "É necessário o endereço de um site como https://example.org, sem login nem senha.", ar: "المطلوب عنوان موقع مثل https://example.org بلا اسم مستخدم أو كلمة مرور.",
                hi: "https://example.org जैसा साइट का पता चाहिए, बिना लॉगिन और पासवर्ड के।", bn: "https://example.org-এর মতো সাইটের ঠিকানা দরকার, লগইন ও পাসওয়ার্ড ছাড়া।", ur: "https://example.org جیسا سائٹ کا پتہ درکار ہے، بغیر لاگ اِن اور پاس ورڈ کے۔", zh: "需要形如 https://example.org 的网站地址，不含登录名和密码。");

            Add("A path such as C:\\Folder or \\\\server\\share is needed, without the characters * ? \" < > |.",
                ru: "Нужен путь вида C:\\Папка или \\\\сервер\\ресурс, без символов * ? \" < > |.", uk: "Потрібен шлях на кшталт C:\\Папка або \\\\сервер\\ресурс, без символів * ? \" < > |.", de: "Erforderlich ist ein Pfad wie C:\\Ordner oder \\\\Server\\Freigabe ohne die Zeichen * ? \" < > |.", it: "Serve un percorso come C:\\Cartella o \\\\server\\risorsa, senza i caratteri * ? \" < > |.",
                es: "Se necesita una ruta como C:\\Carpeta o \\\\servidor\\recurso, sin los caracteres * ? \" < > |.", fr: "Il faut un chemin tel que C:\\Dossier ou \\\\serveur\\partage, sans les caractères * ? \" < > |.", pt: "É necessário um caminho como C:\\Pasta ou \\\\servidor\\recurso, sem os caracteres * ? \" < > |.", ar: "المطلوب مسار مثل C:\\Folder أو \\\\server\\share بلا الأحرف * ? \" < > |.",
                hi: "C:\\Folder या \\\\server\\share जैसा पथ चाहिए, बिना * ? \" < > | अक्षरों के।", bn: "C:\\Folder বা \\\\server\\share-এর মতো পথ দরকার, * ? \" < > | অক্ষর ছাড়া।", ur: "C:\\Folder یا \\\\server\\share جیسا راستہ درکار ہے، بغیر * ? \" < > | حروف کے۔", zh: "需要形如 C:\\Folder 或 \\\\server\\share 的路径，不含字符 * ? \" < > |。");

            Add("A single drive letter and a colon are needed, for example E:",
                ru: "Нужна одна буква диска и двоеточие, например E:", uk: "Потрібна одна літера диска й двокрапка, наприклад E:", de: "Erforderlich sind ein einzelner Laufwerksbuchstabe und ein Doppelpunkt, zum Beispiel E:", it: "Servono una sola lettera di unità e i due punti, ad esempio E:",
                es: "Se necesita una letra de unidad y dos puntos, por ejemplo E:", fr: "Il faut une seule lettre de lecteur suivie de deux-points, par exemple E:", pt: "É necessária uma única letra de unidade e dois-pontos, por exemplo E:", ar: "المطلوب حرف واحد لمحرك الأقراص ونقطتان رأسيتان، مثل E:",
                hi: "ड्राइव का एक अक्षर और कोलन चाहिए, जैसे E:", bn: "ড্রাইভের একটি অক্ষর ও কোলন দরকার, যেমন E:", ur: "ڈرائیو کا ایک حرف اور کولن درکار ہے، مثلاً E:", zh: "需要一个盘符和冒号，例如 E:");

            Add("One connection to the port, closed at once: RDP, SMB, SSH, a database.",
                ru: "Одно соединение с портом и сразу закрытие: RDP, SMB, SSH, база данных.", uk: "Одне з'єднання з портом і відразу закриття: RDP, SMB, SSH, база даних.", de: "Eine Verbindung zum Port, sofort wieder geschlossen: RDP, SMB, SSH, eine Datenbank.", it: "Una connessione alla porta, chiusa subito: RDP, SMB, SSH, un database.",
                es: "Una conexión al puerto, cerrada de inmediato: RDP, SMB, SSH, una base de datos.", fr: "Une connexion au port, fermée aussitôt : RDP, SMB, SSH, une base de données.", pt: "Uma conexão com a porta, fechada de imediato: RDP, SMB, SSH, um banco de dados.", ar: "اتصال واحد بالمنفذ يُغلق فورًا: RDP وSMB وSSH وقاعدة بيانات.",
                hi: "पोर्ट से एक कनेक्शन, तुरंत बंद: RDP, SMB, SSH, डेटाबेस।", bn: "পোর্টে একটি সংযোগ, সঙ্গে সঙ্গে বন্ধ: RDP, SMB, SSH, ডেটাবেস।", ur: "پورٹ سے ایک کنکشن، فوراً بند: RDP، SMB، SSH، ڈیٹا بیس۔", zh: "与端口建立一次连接并立即关闭：RDP、SMB、SSH、数据库。");

            Add("Open the history file",
                ru: "Открыть файл журнала", uk: "Відкрити файл журналу", de: "Protokolldatei öffnen", it: "Apri il file della cronologia",
                es: "Abrir el archivo del historial", fr: "Ouvrir le fichier du journal", pt: "Abrir o arquivo do histórico", ar: "فتح ملف السجل",
                hi: "इतिहास फ़ाइल खोलें", bn: "ইতিহাস ফাইল খুলুন", ur: "تاریخ کی فائل کھولیں", zh: "打开更改记录文件");

            Add("Undo the addition",
                ru: "Отменить добавление", uk: "Скасувати додавання", de: "Hinzufügen rückgängig machen", it: "Annulla l'aggiunta",
                es: "Deshacer la adición", fr: "Annuler l'ajout", pt: "Desfazer a adição", ar: "التراجع عن الإضافة",
                hi: "जोड़ना पूर्ववत करें", bn: "যোগ করা পূর্বাবস্থায় ফেরান", ur: "اضافہ واپس لیں", zh: "撤销添加");

            Add("A fingerprint is written as SHA256: and 43 base64 characters, and only for the exchange server.",
                ru: "Отпечаток записывается как SHA256: и 43 символа base64, и только у сервера обмена.", uk: "Відбиток записується як SHA256: і 43 символи base64, і лише для сервера обміну.", de: "Ein Fingerabdruck wird als SHA256: und 43 Base64-Zeichen geschrieben und nur für den Austauschserver.", it: "Un'impronta si scrive come SHA256: e 43 caratteri base64, e solo per il server di scambio.",
                es: "Una huella se escribe como SHA256: y 43 caracteres base64, y solo para el servidor de intercambio.", fr: "Une empreinte s'écrit SHA256: suivi de 43 caractères base64, et seulement pour le serveur d'échange.", pt: "Uma impressão digital é escrita como SHA256: e 43 caracteres base64, e somente para o servidor de troca.", ar: "تُكتب البصمة بصيغة SHA256: يليها 43 حرفًا من base64، وللخادم التبادلي فقط.",
                hi: "फ़िंगरप्रिंट SHA256: और 43 base64 अक्षरों के रूप में लिखा जाता है, और केवल एक्सचेंज सर्वर के लिए।", bn: "ফিঙ্গারপ্রিন্ট SHA256: ও ৪৩টি base64 অক্ষর হিসেবে লেখা হয়, এবং শুধু এক্সচেঞ্জ সার্ভারের জন্য।", ur: "فنگر پرنٹ SHA256: اور 43 base64 حروف کی صورت میں لکھا جاتا ہے، اور صرف تبادلہ سرور کے لیے۔", zh: "指纹格式为 SHA256: 加 43 个 base64 字符，且仅适用于交换服务器。");

            Add("Certificate fingerprint:",
                ru: "Отпечаток сертификата:", uk: "Відбиток сертифіката:", de: "Zertifikat-Fingerabdruck:", it: "Impronta del certificato:",
                es: "Huella del certificado:", fr: "Empreinte du certificat :", pt: "Impressão digital do certificado:", ar: "بصمة الشهادة:",
                hi: "प्रमाणपत्र का फ़िंगरप्रिंट:", bn: "সার্টিফিকেটের ফিঙ্গারপ্রিন্ট:", ur: "سرٹیفکیٹ کا فنگر پرنٹ:", zh: "证书指纹：");

            Add("Clear the history...",
                ru: "Очистить журнал...", uk: "Очистити журнал...", de: "Protokoll löschen...", it: "Cancella la cronologia...",
                es: "Borrar el historial...", fr: "Effacer le journal...", pt: "Apagar o histórico...", ar: "مسح السجل...",
                hi: "इतिहास साफ़ करें...", bn: "ইতিহাস মুছুন...", ur: "تاریخ صاف کریں...", zh: "清除更改记录...");

            Add("Folder",
                ru: "Папка", uk: "Папка", de: "Ordner", it: "Cartella",
                es: "Carpeta", fr: "Dossier", pt: "Pasta", ar: "مجلد",
                hi: "फ़ोल्डर", bn: "ফোল্ডার", ur: "فولڈر", zh: "文件夹");

            Add("Always on top",
                ru: "Поверх остальных окон", uk: "Поверх інших вікон", de: "Immer im Vordergrund", it: "Sempre in primo piano",
                es: "Siempre visible", fr: "Toujours au premier plan", pt: "Sempre no topo", ar: "دائمًا في المقدمة",
                hi: "हमेशा ऊपर", bn: "সবসময় উপরে", ur: "ہمیشہ اوپر", zh: "始终置顶");

            Add("Label",
                ru: "Подпись", uk: "Підпис", de: "Bezeichnung", it: "Etichetta",
                es: "Etiqueta", fr: "Libellé", pt: "Rótulo", ar: "التسمية",
                hi: "नाम", bn: "নাম", ur: "نام", zh: "名称");

            Add("Label:",
                ru: "Подпись:", uk: "Підпис:", de: "Bezeichnung:", it: "Etichetta:",
                es: "Etiqueta:", fr: "Libellé :", pt: "Rótulo:", ar: "التسمية:",
                hi: "नाम:", bn: "নাম:", ur: "نام:", zh: "名称：");

            Add("Details...",
                ru: "Подробно...", uk: "Докладно...", de: "Details...", it: "Dettagli...",
                es: "Detalles...", fr: "Détails...", pt: "Detalhes...", ar: "التفاصيل...",
                hi: "विवरण...", bn: "বিস্তারিত...", ur: "تفصیلات...", zh: "详细设置...");

            Add("Confirm unavailability after this many checks:",
                ru: "Подтверждать недоступность после проверок:", uk: "Підтверджувати недоступність після перевірок:", de: "Nicht erreichbar nach so vielen Prüfungen bestätigen:", it: "Conferma la non raggiungibilità dopo questi controlli:",
                es: "Confirmar la falta de disponibilidad tras estas comprobaciones:", fr: "Confirmer l'indisponibilité après ce nombre de vérifications :", pt: "Confirmar a indisponibilidade após esta quantidade de verificações:", ar: "تأكيد عدم التوفر بعد هذا العدد من الفحوصات:",
                hi: "इतनी जाँचों के बाद अनुपलब्धता की पुष्टि करें:", bn: "এতগুলো যাচাইয়ের পর অনুপলব্ধতা নিশ্চিত করুন:", ur: "اتنی جانچوں کے بعد عدم دستیابی کی تصدیق کریں:", zh: "连续失败多少次后确认不可用：");

            Add("Confirming: {0} of {1}",
                ru: "Подтверждение: {0} из {1}", uk: "Підтвердження: {0} з {1}", de: "Bestätigung: {0} von {1}", it: "Conferma: {0} di {1}",
                es: "Confirmando: {0} de {1}", fr: "Confirmation : {0} sur {1}", pt: "Confirmando: {0} de {1}", ar: "التأكيد: {0} من {1}",
                hi: "पुष्टि: {1} में से {0}", bn: "নিশ্চিতকরণ: {1}-এর মধ্যে {0}", ur: "تصدیق: {1} میں سے {0}", zh: "确认中：{0} / {1}");

            Add("While off, there is no timer, no shortcut, no tray item and no window, and CyrFlip makes no network checks.",
                ru: "Пока выключено, нет таймера, комбинации, пункта в трее и окна, а проверок по сети CyrFlip не делает.", uk: "Поки вимкнено, немає таймера, комбінації, пункту в треї та вікна, а перевірок мережею CyrFlip не робить.", de: "Solange die Funktion ausgeschaltet ist, gibt es keinen Timer, keine Tastenkombination, keinen Tray-Eintrag und kein Fenster, und CyrFlip führt keine Netzwerkprüfungen durch.", it: "Finché è disattivato non esistono timer, scorciatoia, voce nell'area di notifica né finestra, e CyrFlip non esegue controlli di rete.",
                es: "Mientras está desactivado no hay temporizador, atajo, elemento en la bandeja ni ventana, y CyrFlip no hace comprobaciones de red.", fr: "Tant qu'elle est désactivée, il n'y a ni minuteur, ni raccourci, ni élément dans la zone de notification, ni fenêtre, et CyrFlip ne fait aucune vérification réseau.", pt: "Enquanto estiver desativado, não há temporizador, atalho, item na bandeja nem janela, e o CyrFlip não faz verificações de rede.", ar: "ما دامت الميزة متوقفة فلا مؤقّت ولا اختصار ولا عنصر في الدرج ولا نافذة، ولا يُجري CyrFlip أي فحوصات عبر الشبكة.",
                hi: "बंद रहने तक कोई टाइमर, शॉर्टकट, ट्रे आइटम या विंडो नहीं होती, और CyrFlip नेटवर्क पर कोई जाँच नहीं करता।", bn: "বন্ধ থাকলে কোনো টাইমার, শর্টকাট, ট্রে আইটেম বা উইন্ডো থাকে না, আর CyrFlip নেটওয়ার্কে কোনো যাচাই করে না।", ur: "بند رہنے تک کوئی ٹائمر، شارٹ کٹ، ٹرے آئٹم یا ونڈو نہیں ہوتی، اور CyrFlip نیٹ ورک پر کوئی جانچ نہیں کرتا۔", zh: "关闭时没有定时器、快捷键、托盘项和窗口，CyrFlip 也不会进行任何网络检查。");

            Add("Show in folder",
                ru: "Показать в папке", uk: "Показати в папці", de: "Im Ordner anzeigen", it: "Mostra nella cartella",
                es: "Mostrar en la carpeta", fr: "Afficher dans le dossier", pt: "Mostrar na pasta", ar: "إظهار في المجلد",
                hi: "फ़ोल्डर में दिखाएँ", bn: "ফোল্ডারে দেখান", ur: "فولڈر میں دکھائیں", zh: "在文件夹中显示");

            Add("Show the board",
                ru: "Показать доску", uk: "Показати дошку", de: "Ampel anzeigen", it: "Mostra il semaforo",
                es: "Mostrar el semáforo", fr: "Afficher le tableau", pt: "Mostrar o semáforo", ar: "إظهار اللوحة",
                hi: "बोर्ड दिखाएँ", bn: "বোর্ড দেখান", ur: "بورڈ دکھائیں", zh: "显示面板");

            Add("The port is a number from 1 to 65535.",
                ru: "Порт - число от 1 до 65535.", uk: "Порт - число від 1 до 65535.", de: "Der Port ist eine Zahl von 1 bis 65535.", it: "La porta è un numero da 1 a 65535.",
                es: "El puerto es un número del 1 al 65535.", fr: "Le port est un nombre de 1 à 65535.", pt: "A porta é um número de 1 a 65535.", ar: "المنفذ رقم من 1 إلى 65535.",
                hi: "पोर्ट 1 से 65535 तक की संख्या है।", bn: "পোর্ট ১ থেকে ৬৫৫৩৫ পর্যন্ত একটি সংখ্যা।", ur: "پورٹ 1 سے 65535 تک کا عدد ہے۔", zh: "端口是 1 至 65535 之间的数字。");

            Add("Last worked:",
                ru: "Последний раз работало:", uk: "Востаннє працювало:", de: "Zuletzt erreichbar:", it: "Ultimo funzionamento:",
                es: "Último funcionamiento:", fr: "Dernier fonctionnement :", pt: "Último funcionamento:", ar: "آخر مرة عمل فيها:",
                hi: "आख़िरी बार चला:", bn: "শেষবার কাজ করেছিল:", ur: "آخری بار کام کیا:", zh: "上次正常：");

            Add("Time limit for an answer (ms):",
                ru: "Предельное время ответа (мс):", uk: "Гранична тривалість відповіді (мс):", de: "Zeitlimit für eine Antwort (ms):", it: "Tempo massimo per una risposta (ms):",
                es: "Tiempo límite de respuesta (ms):", fr: "Délai maximal de réponse (ms) :", pt: "Tempo limite de resposta (ms):", ar: "المهلة القصوى للرد (مللي ثانية):",
                hi: "उत्तर की समय-सीमा (मि.से.):", bn: "উত্তরের সময়সীমা (মি.সে.):", ur: "جواب کی مہلت (ملی سیکنڈ):", zh: "响应时限（毫秒）：");

            Add("Accept a certificate that Windows does not trust",
                ru: "Принимать сертификат, которому Windows не доверяет", uk: "Приймати сертифікат, якому Windows не довіряє", de: "Zertifikat akzeptieren, dem Windows nicht vertraut", it: "Accetta un certificato di cui Windows non si fida",
                es: "Aceptar un certificado en el que Windows no confía", fr: "Accepter un certificat auquel Windows ne fait pas confiance", pt: "Aceitar um certificado em que o Windows não confia", ar: "قبول شهادة لا يثق بها Windows",
                hi: "ऐसा प्रमाणपत्र स्वीकार करें जिस पर Windows भरोसा नहीं करता", bn: "যে সার্টিফিকেটকে Windows বিশ্বাস করে না তা গ্রহণ করুন", ur: "ایسا سرٹیفکیٹ قبول کریں جس پر Windows بھروسا نہیں کرتا", zh: "接受 Windows 不信任的证书");

            Add("A certificate that Windows does not trust was accepted.",
                ru: "Принят сертификат, которому Windows не доверяет.", uk: "Прийнято сертифікат, якому Windows не довіряє.", de: "Ein Zertifikat, dem Windows nicht vertraut, wurde akzeptiert.", it: "È stato accettato un certificato di cui Windows non si fida.",
                es: "Se aceptó un certificado en el que Windows no confía.", fr: "Un certificat auquel Windows ne fait pas confiance a été accepté.", pt: "Foi aceito um certificado em que o Windows não confia.", ar: "تم قبول شهادة لا يثق بها Windows.",
                hi: "ऐसा प्रमाणपत्र स्वीकार किया गया जिस पर Windows भरोसा नहीं करता।", bn: "যে সার্টিফিকেটকে Windows বিশ্বাস করে না তা গ্রহণ করা হয়েছে।", ur: "ایسا سرٹیفکیٹ قبول کیا گیا جس پر Windows بھروسا نہیں کرتا۔", zh: "已接受 Windows 不信任的证书。");

            Add("Checked",
                ru: "Проверено", uk: "Перевірено", de: "Geprüft", it: "Controllato",
                es: "Comprobado", fr: "Vérifié", pt: "Verificado", ar: "آخر فحص",
                hi: "जाँचा गया", bn: "যাচাই করা হয়েছে", ur: "جانچا گیا", zh: "检查于");

            Add("Checked:",
                ru: "Проверено:", uk: "Перевірено:", de: "Geprüft:", it: "Controllato:",
                es: "Comprobado:", fr: "Vérifié :", pt: "Verificado:", ar: "آخر فحص:",
                hi: "जाँचा गया:", bn: "যাচাই করা হয়েছে:", ur: "جانچا گیا:", zh: "检查于：");

            Add("Check everything now",
                ru: "Проверить всё сейчас", uk: "Перевірити все зараз", de: "Alles jetzt prüfen", it: "Controlla tutto ora",
                es: "Comprobar todo ahora", fr: "Tout vérifier maintenant", pt: "Verificar tudo agora", ar: "فحص الكل الآن",
                hi: "अभी सब कुछ जाँचें", bn: "এখনই সব যাচাই করুন", ur: "ابھی سب کچھ جانچیں", zh: "立即检查全部");

            Add("Check now",
                ru: "Проверить сейчас", uk: "Перевірити зараз", de: "Jetzt prüfen", it: "Controlla ora",
                es: "Comprobar ahora", fr: "Vérifier maintenant", pt: "Verificar agora", ar: "افحص الآن",
                hi: "अभी जाँचें", bn: "এখনই যাচাই করুন", ur: "ابھی جانچیں", zh: "立即检查");

            Add("The check was skipped.",
                ru: "Проверка пропущена.", uk: "Перевірку пропущено.", de: "Die Prüfung wurde übersprungen.", it: "Controllo saltato.",
                es: "Se omitió la comprobación.", fr: "La vérification a été ignorée.", pt: "A verificação foi ignorada.", ar: "تم تخطي الفحص.",
                hi: "जाँच छोड़ दी गई।", bn: "যাচাই বাদ দেওয়া হয়েছে।", ur: "جانچ چھوڑ دی گئی۔", zh: "已跳过检查。");

            Add("Checking: ping, then port 445..  Esc - skip.",
                ru: "Проверяется: пинг, затем порт 445..  Esc - пропустить.", uk: "Перевіряється: пінг, потім порт 445..  Esc - пропустити.", de: "Prüfung: Ping, dann Port 445..  Esc - überspringen.", it: "Controllo: ping, poi porta 445..  Esc - salta.",
                es: "Comprobando: ping y después el puerto 445..  Esc - omitir.", fr: "Vérification : ping, puis port 445..  Échap - ignorer.", pt: "Verificando: ping e depois a porta 445..  Esc - pular.", ar: "جارٍ الفحص: ping ثم المنفذ 445..  Esc - تخطّي.",
                hi: "जाँच हो रही है: पिंग, फिर पोर्ट 445..  Esc - छोड़ें।", bn: "যাচাই চলছে: পিং, তারপর পোর্ট 445..  Esc - বাদ দিন।", ur: "جانچ جاری ہے: پنگ، پھر پورٹ 445..  Esc - چھوڑیں۔", zh: "正在检查：先 ping，再检查端口 445..  Esc - 跳过。");

            Add("Check every (s):",
                ru: "Проверять каждые (с):", uk: "Перевіряти кожні (с):", de: "Prüfen alle (s):", it: "Controlla ogni (s):",
                es: "Comprobar cada (s):", fr: "Vérifier toutes les (s) :", pt: "Verificar a cada (s):", ar: "الفحص كل (ثانية):",
                hi: "हर (सेकंड) जाँचें:", bn: "প্রতি (সেকেন্ডে) যাচাই করুন:", ur: "ہر (سیکنڈ) جانچیں:", zh: "检查间隔（秒）：");

            Add("Read as {0}.",
                ru: "Прочитано как {0}.", uk: "Прочитано як {0}.", de: "Gelesen als {0}.", it: "Letto come {0}.",
                es: "Leído como {0}.", fr: "Lu comme {0}.", pt: "Lido como {0}.", ar: "قُرئ على أنه {0}.",
                hi: "{0} के रूप में पढ़ा गया।", bn: "{0} হিসেবে পড়া হয়েছে।", ur: "{0} کے طور پر پڑھا گیا۔", zh: "已识别为 {0}。");

            Add("The first {0} lines were read.",
                ru: "Прочитаны первые {0} строк.", uk: "Прочитано перші {0} рядків.", de: "Die ersten {0} Zeilen wurden gelesen.", it: "Sono state lette le prime {0} righe.",
                es: "Se leyeron las primeras {0} líneas.", fr: "Les {0} premières lignes ont été lues.", pt: "As primeiras {0} linhas foram lidas.", ar: "قُرئت أول {0} أسطر.",
                hi: "पहली {0} पंक्तियाँ पढ़ी गईं।", bn: "প্রথম {0}টি লাইন পড়া হয়েছে।", ur: "پہلی {0} سطریں پڑھی گئیں۔", zh: "已读取前 {0} 行。");

            Add("Degraded",
                ru: "Работает с замечанием", uk: "Працює із зауваженням", de: "Eingeschränkt", it: "Degradato",
                es: "Degradado", fr: "Dégradé", pt: "Degradado", ar: "يعمل بأداء متدهور",
                hi: "सीमित रूप से चालू", bn: "সীমিতভাবে চালু", ur: "محدود طور پر فعال", zh: "已降级");

            Add("Tile size:",
                ru: "Размер плиток:", uk: "Розмір плиток:", de: "Kachelgröße:", it: "Dimensione delle tessere:",
                es: "Tamaño de los mosaicos:", fr: "Taille des tuiles :", pt: "Tamanho dos blocos:", ar: "حجم المربعات:",
                hi: "टाइल का आकार:", bn: "টাইলের আকার:", ur: "ٹائل کا سائز:", zh: "磁贴大小：");

            Add("The result is out of date.",
                ru: "Результат устарел.", uk: "Результат застарів.", de: "Das Ergebnis ist veraltet.", it: "Il risultato è obsoleto.",
                es: "El resultado está desactualizado.", fr: "Le résultat est périmé.", pt: "O resultado está desatualizado.", ar: "النتيجة قديمة.",
                hi: "परिणाम पुराना है।", bn: "ফলাফল পুরোনো।", ur: "نتیجہ پرانا ہو چکا ہے۔", zh: "结果已过期。");

            Add("SFTP resource",
                ru: "Ресурс SFTP", uk: "Ресурс SFTP", de: "SFTP-Ressource", it: "Risorsa SFTP",
                es: "Recurso SFTP", fr: "Ressource SFTP", pt: "Recurso SFTP", ar: "مورد SFTP",
                hi: "SFTP संसाधन", bn: "SFTP রিসোর্স", ur: "SFTP وسیلہ", zh: "SFTP 资源");

            Add("Website (HTTP)",
                ru: "Сайт (HTTP)", uk: "Сайт (HTTP)", de: "Website (HTTP)", it: "Sito (HTTP)",
                es: "Sitio (HTTP)", fr: "Site (HTTP)", pt: "Site (HTTP)", ar: "موقع (HTTP)",
                hi: "साइट (HTTP)", bn: "সাইট (HTTP)", ur: "سائٹ (HTTP)", zh: "网站 (HTTP)");

            Add("Availability traffic light",
                ru: "Светофор доступности", uk: "Світлофор доступності", de: "Verfügbarkeits-Ampel", it: "Semaforo di disponibilità",
                es: "Semáforo de disponibilidad", fr: "Feu de disponibilité", pt: "Semáforo de disponibilidade", ar: "إشارة التوفر",
                hi: "उपलब्धता ट्रैफ़िक लाइट", bn: "প্রাপ্যতার ট্রাফিক লাইট", ur: "دستیابی کی ٹریفک لائٹ", zh: "可用性信号灯");

            Add("Exchange server",
                ru: "Сервер обмена", uk: "Сервер обміну", de: "Austauschserver", it: "Server di scambio",
                es: "Servidor de intercambio", fr: "Serveur d'échange", pt: "Servidor de troca", ar: "خادم التبادل",
                hi: "एक्सचेंज सर्वर", bn: "এক্সচেঞ্জ সার্ভার", ur: "تبادلہ سرور", zh: "交换服务器");

            Add("Hide",
                ru: "Скрыть", uk: "Сховати", de: "Ausblenden", it: "Nascondi",
                es: "Ocultar", fr: "Masquer", pt: "Ocultar", ar: "إخفاء",
                hi: "छिपाएँ", bn: "লুকান", ur: "چھپائیں", zh: "隐藏");

            Add("Next check:",
                ru: "Следующая проверка:", uk: "Наступна перевірка:", de: "Nächste Prüfung:", it: "Prossimo controllo:",
                es: "Próxima comprobación:", fr: "Prochaine vérification :", pt: "Próxima verificação:", ar: "الفحص التالي:",
                hi: "अगली जाँच:", bn: "পরবর্তী যাচাই:", ur: "اگلی جانچ:", zh: "下次检查：");

            Add("Available again: {0}",
                ru: "Снова доступно: {0}", uk: "Знову доступно: {0}", de: "Wieder erreichbar: {0}", it: "Di nuovo raggiungibile: {0}",
                es: "Disponible de nuevo: {0}", fr: "De nouveau disponible : {0}", pt: "Disponível novamente: {0}", ar: "عاد متاحًا: {0}",
                hi: "फिर उपलब्ध: {0}", bn: "আবার উপলব্ধ: {0}", ur: "دوبارہ دستیاب: {0}", zh: "已恢复可用：{0}");

            Add("Available again: {0} and {1} more",
                ru: "Снова доступно: {0} и ещё {1}", uk: "Знову доступно: {0} та ще {1}", de: "Wieder erreichbar: {0} und {1} weitere", it: "Di nuovo raggiungibile: {0} e altri {1}",
                es: "Disponible de nuevo: {0} y {1} más", fr: "De nouveau disponible : {0} et {1} autres", pt: "Disponível novamente: {0} e mais {1}", ar: "عاد متاحًا: {0} و{1} أخرى",
                hi: "फिर उपलब्ध: {0} और {1} अन्य", bn: "আবার উপলব্ধ: {0} এবং আরও {1}টি", ur: "دوبارہ دستیاب: {0} اور {1} مزید", zh: "已恢复可用：{0} 及另外 {1} 个");

            Add("Tell me in the tray when the target becomes unavailable and when it comes back",
                ru: "Сообщать в трее, когда цель станет недоступна и когда вернётся", uk: "Повідомляти в треї, коли ціль стане недоступною і коли повернеться", de: "Im Infobereich melden, wenn das Ziel nicht erreichbar wird und wenn es zurückkehrt", it: "Avvisa nell'area di notifica quando l'obiettivo diventa non raggiungibile e quando torna",
                es: "Avisar en la bandeja cuando el objetivo deje de estar disponible y cuando vuelva", fr: "Prévenir dans la zone de notification quand la cible devient indisponible et quand elle revient", pt: "Avisar na bandeja quando o alvo ficar indisponível e quando voltar", ar: "التنبيه في الدرج عندما يصبح الهدف غير متاح وعندما يعود",
                hi: "लक्ष्य के अनुपलब्ध होने और लौटने पर ट्रे में सूचित करें", bn: "লক্ষ্য অনুপলব্ধ হলে এবং ফিরে এলে ট্রেতে জানান", ur: "ہدف کے غیر دستیاب ہونے اور واپس آنے پر ٹرے میں اطلاع دیں", zh: "目标不可用及恢复时在托盘中提示");

            Add("State",
                ru: "Состояние", uk: "Стан", de: "Zustand", it: "Stato",
                es: "Estado", fr: "État", pt: "Estado", ar: "الحالة",
                hi: "स्थिति", bn: "অবস্থা", ur: "حالت", zh: "状态");

            Add("State:",
                ru: "Состояние:", uk: "Стан:", de: "Zustand:", it: "Stato:",
                es: "Estado:", fr: "État :", pt: "Estado:", ar: "الحالة:",
                hi: "स्थिति:", bn: "অবস্থা:", ur: "حالت:", zh: "状态：");

            Add("Medium (tiles)",
                ru: "Средние", en: "Medium", uk: "Середні", de: "Mittel", it: "Medie",
                es: "Medianos", fr: "Moyennes", pt: "Médios", ar: "متوسطة",
                hi: "मध्यम", bn: "মাঝারি", ur: "درمیانے", zh: "中");

            Add("The connection string was refused as a whole: {0}.",
                ru: "Строка подключения отклонена целиком: {0}.", uk: "Рядок підключення відхилено повністю: {0}.", de: "Die Verbindungszeichenfolge wurde als Ganzes abgelehnt: {0}.", it: "La stringa di connessione è stata rifiutata per intero: {0}.",
                es: "La cadena de conexión se rechazó por completo: {0}.", fr: "La chaîne de connexion a été refusée en entier : {0}.", pt: "A cadeia de conexão foi recusada por inteiro: {0}.", ar: "رُفضت سلسلة الاتصال بالكامل: {0}.",
                hi: "कनेक्शन स्ट्रिंग पूरी तरह अस्वीकार की गई: {0}।", bn: "সংযোগ স্ট্রিং সম্পূর্ণভাবে প্রত্যাখ্যাত হয়েছে: {0}।", ur: "کنکشن اسٹرنگ مکمل طور پر مسترد کر دی گئی: {0}۔", zh: "连接字符串被整体拒绝：{0}。");

            Add("Treat as slow after (ms):",
                ru: "Считать медленным после (мс):", uk: "Вважати повільним після (мс):", de: "Als langsam werten ab (ms):", it: "Considera lento dopo (ms):",
                es: "Considerar lento a partir de (ms):", fr: "Considérer comme lent après (ms) :", pt: "Considerar lento após (ms):", ar: "اعتباره بطيئًا بعد (مللي ثانية):",
                hi: "इसके बाद धीमा मानें (मि.से.):", bn: "এর পরে ধীর ধরুন (মি.সে.):", ur: "اس کے بعد سست سمجھیں (ملی سیکنڈ):", zh: "超过此值视为缓慢（毫秒）：");

            Add("It is better not to query someone else's site or server this often: it may treat it as load.",
                ru: "Так часто опрашивать чужой сайт или сервер не стоит: он может счесть это нагрузкой.", uk: "Так часто опитувати чужий сайт чи сервер не варто: він може вважати це навантаженням.", de: "Eine fremde Website oder einen fremden Server so oft abzufragen, ist nicht ratsam: Er könnte es als Last werten.", it: "Non conviene interrogare così spesso il sito o il server di altri: potrebbe considerarlo un carico.",
                es: "No conviene consultar tan a menudo el sitio o el servidor de otros: puede considerarlo carga.", fr: "Il vaut mieux ne pas interroger si souvent le site ou le serveur d'autrui : il pourrait y voir une charge.", pt: "Não convém consultar com tanta frequência o site ou o servidor de outros: ele pode considerar isso carga.", ar: "لا يُستحسن استعلام موقع أو خادم يخص غيرك بهذه الوتيرة: قد يعدّه حملًا زائدًا.",
                hi: "किसी और की साइट या सर्वर को इतनी बार जाँचना ठीक नहीं: वह इसे अतिरिक्त भार मान सकता है।", bn: "অন্যের সাইট বা সার্ভারকে এত ঘন ঘন যাচাই করা ঠিক নয়: এটি বাড়তি চাপ হিসেবে ধরা হতে পারে।", ur: "کسی اور کی سائٹ یا سرور کو اتنی کثرت سے جانچنا مناسب نہیں: وہ اسے بوجھ سمجھ سکتا ہے۔", zh: "不建议如此频繁地查询他人的网站或服务器：对方可能将其视为负载。");

            Add("Manual only",
                ru: "Только вручную", uk: "Лише вручну", de: "Nur manuell", it: "Solo manuale",
                es: "Solo manual", fr: "Manuel uniquement", pt: "Somente manual", ar: "يدويًا فقط",
                hi: "केवल हाथ से", bn: "শুধু হাতে", ur: "صرف دستی", zh: "仅手动");

            Add("Delete the availability traffic light change history? The file contains the addresses of targets. The deletion cannot be undone.",
                ru: "Удалить журнал изменений светофора доступности? Файл содержит адреса целей. Отменить удаление нельзя.", uk: "Видалити журнал змін світлофора доступності? Файл містить адреси цілей. Скасувати видалення не можна.", de: "Das Änderungsprotokoll der Verfügbarkeits-Ampel löschen? Die Datei enthält die Adressen der Ziele. Das Löschen kann nicht rückgängig gemacht werden.", it: "Eliminare la cronologia delle modifiche del semaforo di disponibilità? Il file contiene gli indirizzi degli obiettivi. L'eliminazione non può essere annullata.",
                es: "¿Borrar el historial de cambios del semáforo de disponibilidad? El archivo contiene las direcciones de los objetivos. El borrado no se puede deshacer.", fr: "Supprimer le journal des changements du feu de disponibilité ? Le fichier contient les adresses des cibles. La suppression est irréversible.", pt: "Apagar o histórico de alterações do semáforo de disponibilidade? O arquivo contém os endereços dos alvos. A exclusão não pode ser desfeita.", ar: "هل تحذف سجل تغييرات إشارة التوفر؟ يحتوي الملف على عناوين الأهداف. لا يمكن التراجع عن الحذف.",
                hi: "क्या उपलब्धता ट्रैफ़िक लाइट का परिवर्तन इतिहास हटाएँ? फ़ाइल में लक्ष्यों के पते हैं। हटाना पूर्ववत नहीं किया जा सकता।", bn: "প্রাপ্যতার ট্রাফিক লাইটের পরিবর্তনের ইতিহাস মুছবেন? ফাইলে লক্ষ্যগুলোর ঠিকানা আছে। মুছে ফেলা ফেরানো যায় না।", ur: "کیا دستیابی کی ٹریفک لائٹ کی تبدیلیوں کی تاریخ حذف کریں؟ فائل میں اہداف کے پتے ہیں۔ حذف کرنا واپس نہیں ہو سکتا۔", zh: "要删除可用性信号灯的更改记录吗？文件中包含目标的地址。删除后无法撤销。");

            Add("Delete the target \"{0}\"? The deletion cannot be undone.",
                ru: "Удалить цель «{0}»? Отменить удаление нельзя.", uk: "Видалити ціль «{0}»? Скасувати видалення не можна.", de: "Das Ziel „{0}“ löschen? Das Löschen kann nicht rückgängig gemacht werden.", it: "Eliminare l'obiettivo \"{0}\"? L'eliminazione non può essere annullata.",
                es: "¿Eliminar el objetivo «{0}»? La eliminación no se puede deshacer.", fr: "Supprimer la cible « {0} » ? La suppression est irréversible.", pt: "Excluir o alvo \"{0}\"? A exclusão não pode ser desfeita.", ar: "هل تحذف الهدف \"{0}\"؟ لا يمكن التراجع عن الحذف.",
                hi: "क्या लक्ष्य \"{0}\" हटाएँ? हटाना पूर्ववत नहीं किया जा सकता।", bn: "লক্ষ্য \"{0}\" মুছবেন? মুছে ফেলা ফেরানো যায় না।", ur: "کیا ہدف \"{0}\" حذف کریں؟ حذف کرنا واپس نہیں ہو سکتا۔", zh: "要删除目标“{0}”吗？删除后无法撤销。");

            Add("Already there: {0}.",
                ru: "Уже были: {0}.", uk: "Уже були: {0}.", de: "Bereits vorhanden: {0}.", it: "Già presenti: {0}.",
                es: "Ya estaban: {0}.", fr: "Déjà présentes : {0}.", pt: "Já existiam: {0}.", ar: "موجودة مسبقًا: {0}.",
                hi: "पहले से मौजूद: {0}।", bn: "আগে থেকেই ছিল: {0}।", ur: "پہلے سے موجود: {0}۔", zh: "已存在：{0}。");

            Add("Host (ping)",
                ru: "Узел (пинг)", uk: "Вузол (пінг)", de: "Host (Ping)", it: "Host (ping)",
                es: "Host (ping)", fr: "Hôte (ping)", pt: "Host (ping)", ar: "مضيف (ping)",
                hi: "होस्ट (पिंग)", bn: "হোস্ট (পিং)", ur: "میزبان (پنگ)", zh: "主机 (ping)");

            Add("A file with a login is not read: enter the host and the port.",
                ru: "Файл с логином не читается: введите узел и порт.", uk: "Файл із логіном не читається: введіть вузол і порт.", de: "Eine Datei mit Anmeldedaten wird nicht gelesen: Geben Sie Host und Port ein.", it: "Un file con login non viene letto: inserisci l'host e la porta.",
                es: "Un archivo con usuario no se lee: introduzca el host y el puerto.", fr: "Un fichier contenant un identifiant n'est pas lu : saisissez l'hôte et le port.", pt: "Um arquivo com login não é lido: digite o host e a porta.", ar: "لا يُقرأ ملف يحتوي على اسم مستخدم: أدخل المضيف والمنفذ.",
                hi: "लॉगिन वाली फ़ाइल नहीं पढ़ी जाती: होस्ट और पोर्ट दर्ज करें।", bn: "লগইনসহ ফাইল পড়া হয় না: হোস্ট ও পোর্ট লিখুন।", ur: "لاگ اِن والی فائل نہیں پڑھی جاتی: میزبان اور پورٹ درج کریں۔", zh: "不会读取含登录名的文件：请输入主机和端口。");

            Add("There are no targets yet - type an address in the field above and press Enter.",
                ru: "Целей пока нет - введите адрес в поле выше и нажмите Enter.", uk: "Цілей поки немає - введіть адресу в полі вище й натисніть Enter.", de: "Noch keine Ziele - geben Sie oben eine Adresse ein und drücken Sie Enter.", it: "Non ci sono ancora obiettivi: scrivi un indirizzo nel campo sopra e premi Invio.",
                es: "Aún no hay objetivos: escriba una dirección en el campo de arriba y pulse Intro.", fr: "Aucune cible pour l'instant - saisissez une adresse dans le champ ci-dessus et appuyez sur Entrée.", pt: "Ainda não há alvos - digite um endereço no campo acima e pressione Enter.", ar: "لا توجد أهداف بعد - اكتب عنوانًا في الحقل أعلاه واضغط Enter.",
                hi: "अभी कोई लक्ष्य नहीं है - ऊपर के क्षेत्र में पता लिखें और Enter दबाएँ।", bn: "এখনও কোনো লক্ষ্য নেই - উপরের ঘরে একটি ঠিকানা লিখে Enter চাপুন।", ur: "ابھی کوئی ہدف نہیں - اوپر والے خانے میں پتہ لکھیں اور Enter دبائیں۔", zh: "还没有目标 - 请在上方输入地址并按 Enter。");

            Add("Targets",
                ru: "Цели", uk: "Цілі", de: "Ziele", it: "Obiettivi",
                es: "Objetivos", fr: "Cibles", pt: "Alvos", ar: "الأهداف",
                hi: "लक्ष्य", bn: "লক্ষ্য", ur: "اہداف", zh: "目标");

            Add("Availability traffic light targets",
                ru: "Цели светофора доступности", uk: "Цілі світлофора доступності", de: "Ziele der Verfügbarkeits-Ampel", it: "Obiettivi del semaforo di disponibilità",
                es: "Objetivos del semáforo de disponibilidad", fr: "Cibles du feu de disponibilité", pt: "Alvos do semáforo de disponibilidade", ar: "أهداف إشارة التوفر",
                hi: "उपलब्धता ट्रैफ़िक लाइट के लक्ष्य", bn: "প্রাপ্যতার ট্রাফিক লাইটের লক্ষ্য", ur: "دستیابی کی ٹریفک لائٹ کے اہداف", zh: "可用性信号灯的目标");

            Add("Target is on",
                ru: "Цель включена", uk: "Ціль увімкнено", de: "Ziel ist aktiv", it: "Obiettivo attivo",
                es: "Objetivo activado", fr: "Cible activée", pt: "Alvo ativado", ar: "الهدف مفعّل",
                hi: "लक्ष्य चालू है", bn: "লক্ষ্য চালু আছে", ur: "ہدف فعال ہے", zh: "目标已启用");

            Add("These targets are already on the board.",
                ru: "Эти цели уже есть на доске.", uk: "Ці цілі вже є на дошці.", de: "Diese Ziele sind bereits auf der Ampel.", it: "Questi obiettivi sono già sul semaforo.",
                es: "Estos objetivos ya están en el semáforo.", fr: "Ces cibles sont déjà sur le tableau.", pt: "Esses alvos já estão no semáforo.", ar: "هذه الأهداف موجودة بالفعل على اللوحة.",
                hi: "ये लक्ष्य पहले से बोर्ड पर हैं।", bn: "এই লক্ষ্যগুলো আগে থেকেই বোর্ডে আছে।", ur: "یہ اہداف پہلے سے بورڈ پر موجود ہیں۔", zh: "这些目标已在面板上。");

            Add("invalid address",
                ru: "адрес записан неверно", uk: "адресу записано неправильно", de: "ungültige Adresse", it: "indirizzo non valido",
                es: "dirección no válida", fr: "adresse non valide", pt: "endereço inválido", ar: "العنوان غير صالح",
                hi: "पता मान्य नहीं है", bn: "ঠিকানা সঠিক নয়", ur: "پتہ درست نہیں", zh: "地址无效");

            Add("manual",
                ru: "вручную", uk: "вручну", de: "manuell", it: "manuale",
                es: "manual", fr: "manuel", pt: "manual", ar: "يدوي",
                hi: "हाथ से", bn: "হাতে", ur: "دستی", zh: "手动");

            Add("all is well",
                ru: "всё в порядке", uk: "усе гаразд", de: "alles in Ordnung", it: "tutto a posto",
                es: "todo en orden", fr: "tout va bien", pt: "tudo em ordem", ar: "كل شيء على ما يرام",
                hi: "सब ठीक है", bn: "সব ঠিক আছে", ur: "سب ٹھیک ہے", zh: "一切正常");

            Add("all available: {0}",
                ru: "всё доступно: {0}", uk: "усе доступно: {0}", de: "alles erreichbar: {0}", it: "tutto raggiungibile: {0}",
                es: "todo disponible: {0}", fr: "tout est disponible : {0}", pt: "tudo disponível: {0}", ar: "الكل متاح: {0}",
                hi: "सब उपलब्ध: {0}", bn: "সব উপলব্ধ: {0}", ur: "سب دستیاب: {0}", zh: "全部可用：{0}");

            Add("drive not connected",
                ru: "диск не подключён", uk: "диск не підключено", de: "Laufwerk nicht verbunden", it: "unità non connessa",
                es: "unidad no conectada", fr: "lecteur non connecté", pt: "unidade não conectada", ar: "محرك الأقراص غير متصل",
                hi: "ड्राइव कनेक्ट नहीं है", bn: "ড্রাইভ সংযুক্ত নয়", ur: "ڈرائیو منسلک نہیں", zh: "驱动器未连接");

            Add("access denied",
                ru: "доступ запрещён", uk: "доступ заборонено", de: "Zugriff verweigert", it: "accesso negato",
                es: "acceso denegado", fr: "accès refusé", pt: "acesso negado", ar: "تم رفض الوصول",
                hi: "पहुँच अस्वीकृत", bn: "অ্যাক্সেস অস্বীকৃত", ur: "رسائی مسترد", zh: "访问被拒绝");

            Add("not checked yet: {0}",
                ru: "ещё не проверено: {0}", uk: "ще не перевірено: {0}", de: "noch nicht geprüft: {0}", it: "non ancora controllati: {0}",
                es: "aún sin comprobar: {0}", fr: "pas encore vérifiées : {0}", pt: "ainda não verificados: {0}", ar: "لم يُفحص بعد: {0}",
                hi: "अभी जाँचे नहीं गए: {0}", bn: "এখনও যাচাই হয়নি: {0}", ur: "ابھی جانچے نہیں گئے: {0}", zh: "尚未检查：{0}");

            Add("name not found",
                ru: "имя не найдено", uk: "ім'я не знайдено", de: "Name nicht gefunden", it: "nome non trovato",
                es: "nombre no encontrado", fr: "nom introuvable", pt: "nome não encontrado", ar: "الاسم غير موجود",
                hi: "नाम नहीं मिला", bn: "নাম পাওয়া যায়নি", ur: "نام نہیں ملا", zh: "找不到名称");

            Add("this computer is offline",
                ru: "компьютер не в сети", uk: "комп'ютер не в мережі", de: "dieser Computer ist offline", it: "il computer è offline",
                es: "el equipo no tiene red", fr: "cet ordinateur est hors ligne", pt: "o computador está offline", ar: "الكمبيوتر غير متصل بالشبكة",
                hi: "यह कंप्यूटर ऑफ़लाइन है", bn: "এই কম্পিউটার অফলাইন", ur: "یہ کمپیوٹر آف لائن ہے", zh: "此计算机已离线");

            Add("never",
                ru: "не было", uk: "не було", de: "nie", it: "mai",
                es: "nunca", fr: "jamais", pt: "nunca", ar: "لم يحدث",
                hi: "कभी नहीं", bn: "কখনও না", ur: "کبھی نہیں", zh: "从未");

            Add("unavailable: {0} of {1}",
                ru: "недоступно: {0} из {1}", uk: "недоступно: {0} з {1}", de: "nicht erreichbar: {0} von {1}", it: "non raggiungibili: {0} su {1}",
                es: "no disponibles: {0} de {1}", fr: "indisponibles : {0} sur {1}", pt: "indisponíveis: {0} de {1}", ar: "غير متاح: {0} من {1}",
                hi: "अनुपलब्ध: {1} में से {0}", bn: "অনুপলব্ধ: {1}-এর মধ্যে {0}", ur: "غیر دستیاب: {1} میں سے {0}", zh: "不可用：{0} / {1}");

            Add("no answer within the time limit",
                ru: "нет ответа за отведённое время", uk: "немає відповіді за відведений час", de: "keine Antwort innerhalb des Zeitlimits", it: "nessuna risposta entro il tempo limite",
                es: "sin respuesta dentro del tiempo límite", fr: "pas de réponse dans le délai imparti", pt: "sem resposta dentro do tempo limite", ar: "لا ردّ خلال المهلة المحددة",
                hi: "समय-सीमा में कोई उत्तर नहीं", bn: "নির্ধারিত সময়ে কোনো উত্তর নেই", ur: "مقررہ وقت میں کوئی جواب نہیں", zh: "在时限内无应答");

            Add("server answered {0}",
                ru: "ответ сервера {0}", uk: "відповідь сервера {0}", de: "Serverantwort {0}", it: "risposta del server {0}",
                es: "respuesta del servidor {0}", fr: "réponse du serveur {0}", pt: "resposta do servidor {0}", ar: "ردّ الخادم {0}",
                hi: "सर्वर का उत्तर {0}", bn: "সার্ভারের উত্তর {0}", ur: "سرور کا جواب {0}", zh: "服务器响应 {0}");

            Add("answers slowly",
                ru: "отвечает медленно", uk: "відповідає повільно", de: "antwortet langsam", it: "risponde lentamente",
                es: "responde despacio", fr: "répond lentement", pt: "responde devagar", ar: "يردّ ببطء",
                hi: "धीरे उत्तर दे रहा है", bn: "ধীরে উত্তর দিচ্ছে", ur: "سست جواب دے رہا ہے", zh: "响应缓慢");

            Add("answers, but is not verified",
                ru: "отвечает, но подлинность не проверена", uk: "відповідає, але справжність не перевірена", de: "antwortet, ist aber nicht verifiziert", it: "risponde, ma non è verificato",
                es: "responde, pero no está verificado", fr: "répond, mais n'est pas vérifié", pt: "responde, mas não é verificado", ar: "يردّ لكن لم يُتحقق من هويته",
                hi: "उत्तर देता है, पर सत्यापित नहीं है", bn: "উত্তর দেয়, কিন্তু যাচাই করা হয়নি", ur: "جواب دیتا ہے، مگر تصدیق نہیں ہوئی", zh: "有应答，但未经验证");

            Add("secure connection failed",
                ru: "ошибка защищённого соединения", uk: "помилка захищеного з'єднання", de: "Fehler bei der gesicherten Verbindung", it: "errore di connessione sicura",
                es: "error de conexión segura", fr: "échec de la connexion sécurisée", pt: "falha na conexão segura", ar: "فشل الاتصال الآمن",
                hi: "सुरक्षित कनेक्शन विफल रहा", bn: "নিরাপদ সংযোগ ব্যর্থ হয়েছে", ur: "محفوظ کنکشن ناکام رہا", zh: "安全连接失败");

            Add("the check failed",
                ru: "проверка не удалась", uk: "перевірка не вдалася", de: "die Prüfung ist fehlgeschlagen", it: "il controllo non è riuscito",
                es: "la comprobación falló", fr: "la vérification a échoué", pt: "a verificação falhou", ar: "فشل الفحص",
                hi: "जाँच विफल रही", bn: "যাচাই ব্যর্থ হয়েছে", ur: "جانچ ناکام رہی", zh: "检查失败");

            Add("path not found",
                ru: "путь не найден", uk: "шлях не знайдено", de: "Pfad nicht gefunden", it: "percorso non trovato",
                es: "ruta no encontrada", fr: "chemin introuvable", pt: "caminho não encontrado", ar: "المسار غير موجود",
                hi: "पथ नहीं मिला", bn: "পথ পাওয়া যায়নি", ur: "راستہ نہیں ملا", zh: "找不到路径");

            Add("degraded: {0} of {1}",
                ru: "с замечанием: {0} из {1}", uk: "із зауваженням: {0} з {1}", de: "eingeschränkt: {0} von {1}", it: "degradati: {0} su {1}",
                es: "degradados: {0} de {1}", fr: "dégradées : {0} sur {1}", pt: "degradados: {0} de {1}", ar: "بأداء متدهور: {0} من {1}",
                hi: "सीमित: {1} में से {0}", bn: "সীমিত: {1}-এর মধ্যে {0}", ur: "محدود: {1} میں سے {0}", zh: "已降级：{0} / {1}");

            Add("certificate is not trusted",
                ru: "сертификат не заслуживает доверия", uk: "сертифікат не заслуговує довіри", de: "Zertifikat nicht vertrauenswürdig", it: "certificato non attendibile",
                es: "certificado no confiable", fr: "certificat non approuvé", pt: "certificado não confiável", ar: "الشهادة غير موثوقة",
                hi: "प्रमाणपत्र भरोसेमंद नहीं है", bn: "সার্টিফিকেট বিশ্বাসযোগ্য নয়", ur: "سرٹیفکیٹ قابلِ اعتماد نہیں", zh: "证书不受信任");

            Add("certificate differs from the pinned one",
                ru: "сертификат отличается от закреплённого", uk: "сертифікат відрізняється від закріпленого", de: "Zertifikat weicht vom festgelegten ab", it: "il certificato differisce da quello fissato",
                es: "el certificado difiere del fijado", fr: "le certificat diffère de celui épinglé", pt: "o certificado difere do fixado", ar: "الشهادة تختلف عن المثبّتة",
                hi: "प्रमाणपत्र निर्धारित प्रमाणपत्र से अलग है", bn: "সার্টিফিকেট নির্ধারিতটির থেকে আলাদা", ur: "سرٹیفکیٹ مقرر کردہ سے مختلف ہے", zh: "证书与固定的证书不一致");

            Add("network unreachable",
                ru: "сеть недоступна", uk: "мережа недоступна", de: "Netzwerk nicht erreichbar", it: "rete non raggiungibile",
                es: "red inaccesible", fr: "réseau inaccessible", pt: "rede inacessível", ar: "الشبكة غير قابلة للوصول",
                hi: "नेटवर्क पहुँच से बाहर है", bn: "নেটওয়ার্ক পৌঁছানো যাচ্ছে না", ur: "نیٹ ورک تک رسائی نہیں", zh: "网络不可达");

            Add("too many redirects",
                ru: "слишком много перенаправлений", uk: "забагато перенаправлень", de: "zu viele Weiterleitungen", it: "troppi reindirizzamenti",
                es: "demasiadas redirecciones", fr: "trop de redirections", pt: "redirecionamentos demais", ar: "عمليات إعادة توجيه كثيرة جدًا",
                hi: "बहुत अधिक रीडायरेक्ट", bn: "অতিরিক্ত রিডাইরেক্ট", ur: "بہت زیادہ ری ڈائریکٹ", zh: "重定向次数过多");

            Add("connection refused",
                ru: "соединение отклонено", uk: "з'єднання відхилено", de: "Verbindung abgelehnt", it: "connessione rifiutata",
                es: "conexión rechazada", fr: "connexion refusée", pt: "conexão recusada", ar: "تم رفض الاتصال",
                hi: "कनेक्शन अस्वीकार किया गया", bn: "সংযোগ প্রত্যাখ্যাত হয়েছে", ur: "کنکشن مسترد کر دیا گیا", zh: "连接被拒绝");

            Add("manual only",
                ru: "только вручную", uk: "лише вручну", de: "nur manuell", it: "solo manuale",
                es: "solo manual", fr: "manuel uniquement", pt: "somente manual", ar: "يدويًا فقط",
                hi: "केवल हाथ से", bn: "শুধু হাতে", ur: "صرف دستی", zh: "仅手动");

            Add("not an SSH server",
                ru: "это не SSH-сервер", uk: "це не SSH-сервер", de: "kein SSH-Server", it: "non è un server SSH",
                es: "no es un servidor SSH", fr: "ce n'est pas un serveur SSH", pt: "não é um servidor SSH", ar: "هذا ليس خادم SSH",
                hi: "यह SSH सर्वर नहीं है", bn: "এটি SSH সার্ভার নয়", ur: "یہ SSH سرور نہیں ہے", zh: "不是 SSH 服务器");

            Add("availability traffic light",
                ru: "светофор доступности", uk: "світлофор доступності", de: "Verfügbarkeits-Ampel", it: "semaforo di disponibilità",
                es: "semáforo de disponibilidad", fr: "feu de disponibilité", pt: "semáforo de disponibilidade", ar: "إشارة التوفر",
                hi: "उपलब्धता ट्रैफ़िक लाइट", bn: "প্রাপ্যতার ট্রাফিক লাইট", ur: "دستیابی کی ٹریفک لائٹ", zh: "可用性信号灯");

            Add("Type an address, name, folder or drive; CyrFlip works out what it is. Enter adds, Shift+Enter opens details.",
                ru: "Введите адрес, имя, папку или диск - CyrFlip сам поймёт, что это. Enter добавляет цель, Shift+Enter открывает подробности.", uk: "Введіть адресу, ім'я, папку або диск - CyrFlip сам зрозуміє, що це. Enter додає ціль, Shift+Enter відкриває подробиці.", de: "Adresse, Name, Ordner oder Laufwerk eingeben - CyrFlip erkennt selbst, was es ist. Enter fügt hinzu, Umschalt+Enter öffnet Details.", it: "Scrivi un indirizzo, un nome, una cartella o un'unità: CyrFlip capisce da solo che cos'è. Invio aggiunge, Maiusc+Invio apre i dettagli.",
                es: "Escriba una dirección, un nombre, una carpeta o una unidad: CyrFlip averigua qué es. Intro añade, Mayús+Intro abre los detalles.", fr: "Saisissez une adresse, un nom, un dossier ou un lecteur : CyrFlip reconnaît de quoi il s'agit. Entrée ajoute, Maj+Entrée ouvre les détails.", pt: "Digite um endereço, um nome, uma pasta ou uma unidade: o CyrFlip descobre o que é. Enter adiciona, Shift+Enter abre os detalhes.", ar: "اكتب عنوانًا أو اسمًا أو مجلدًا أو محرك أقراص - يتعرف CyrFlip على نوعه بنفسه. Enter يضيف، وShift+Enter يفتح التفاصيل.",
                hi: "पता, नाम, फ़ोल्डर या ड्राइव लिखें - CyrFlip खुद समझ लेता है कि वह क्या है। Enter जोड़ता है, Shift+Enter विवरण खोलता है।", bn: "ঠিকানা, নাম, ফোল্ডার বা ড্রাইভ লিখুন - CyrFlip নিজেই বুঝে নেয় সেটি কী। Enter যোগ করে, Shift+Enter বিবরণ খোলে।", ur: "پتہ، نام، فولڈر یا ڈرائیو لکھیں - CyrFlip خود سمجھ لیتا ہے کہ یہ کیا ہے۔ Enter شامل کرتا ہے، Shift+Enter تفصیلات کھولتا ہے۔", zh: "输入地址、名称、文件夹或驱动器 - CyrFlip 会自动判断类型。按 Enter 添加，按 Shift+Enter 打开详细设置。");

            Add("Include the traffic light targets in the file? It will contain your host and site addresses - keep it private.",
                ru: "Включить в файл цели светофора? В нём будут адреса ваших узлов и сайтов - храните файл как личные данные.", uk: "Включити до файлу цілі світлофора? У ньому будуть адреси ваших вузлів і сайтів - зберігайте файл як особисті дані.", de: "Die Ziele der Ampel in die Datei aufnehmen? Sie enthält die Adressen Ihrer Hosts und Websites - bewahren Sie sie privat auf.", it: "Includere nel file gli obiettivi del semaforo? Conterrà gli indirizzi dei tuoi host e siti: conservalo come dato personale.",
                es: "¿Incluir en el archivo los objetivos del semáforo? Contendrá las direcciones de sus hosts y sitios; guárdelo como datos personales.", fr: "Inclure les cibles du feu dans le fichier ? Il contiendra les adresses de vos hôtes et sites - gardez-le privé.", pt: "Incluir no arquivo os alvos do semáforo? Ele conterá os endereços dos seus hosts e sites - guarde-o como dados pessoais.", ar: "هل تضمّن أهداف الإشارة في الملف؟ سيحتوي على عناوين مضيفاتك ومواقعك - احتفظ به كبيانات شخصية.",
                hi: "क्या ट्रैफ़िक लाइट के लक्ष्य फ़ाइल में शामिल करें? उसमें आपके होस्ट और साइटों के पते होंगे - उसे निजी रखें।", bn: "ট্রাফিক লাইটের লক্ষ্যগুলো কি ফাইলে যুক্ত করবেন? তাতে আপনার হোস্ট ও সাইটের ঠিকানা থাকবে - এটি ব্যক্তিগত রাখুন।", ur: "کیا ٹریفک لائٹ کے اہداف فائل میں شامل کریں؟ اس میں آپ کے میزبانوں اور سائٹوں کے پتے ہوں گے - اسے نجی رکھیں۔", zh: "是否将信号灯的目标写入文件？文件将包含您的主机和网站地址 - 请妥善保管。");

            Add("A plain text file of state changes for marked targets. It holds addresses and never goes into a log archive.",
                ru: "Текстовый файл смен состояния для отмеченных целей. В нём адреса, и в архив логов он никогда не попадает.", uk: "Текстовий файл змін стану для позначених цілей. У ньому адреси, і в архів логів він ніколи не потрапляє.", de: "Eine Textdatei der Zustandsänderungen für markierte Ziele. Sie enthält Adressen und gelangt nie in ein Log-Archiv.", it: "Un file di testo dei cambi di stato per gli obiettivi contrassegnati. Contiene indirizzi e non finisce mai in un archivio di log.",
                es: "Un archivo de texto con los cambios de estado de los objetivos marcados. Contiene direcciones y nunca entra en un archivo de registros.", fr: "Un fichier texte des changements d'état des cibles cochées. Il contient des adresses et n'entre jamais dans une archive de journaux.", pt: "Um arquivo de texto com as mudanças de estado dos alvos marcados. Contém endereços e nunca entra num arquivo de logs.", ar: "ملف نصي بتغيّرات الحالة للأهداف المعلَّمة. يحتوي على عناوين ولا يدخل أبدًا في أرشيف السجلات.",
                hi: "चिह्नित लक्ष्यों की स्थिति-परिवर्तन की टेक्स्ट फ़ाइल। उसमें पते हैं और वह कभी लॉग संग्रह में नहीं जाती।", bn: "চিহ্নিত লক্ষ্যগুলোর অবস্থা পরিবর্তনের টেক্সট ফাইল। এতে ঠিকানা থাকে এবং এটি কখনও লগ আর্কাইভে যায় না।", ur: "نشان زد اہداف کی حالت کی تبدیلیوں کی ٹیکسٹ فائل۔ اس میں پتے ہیں اور یہ کبھی لاگ آرکائیو میں نہیں جاتی۔", zh: "记录已标记目标状态变化的文本文件。其中含有地址，绝不会进入日志压缩包。");

            // S0060 M7: one sentence (the two-sentence form below is no longer shown).
            Add("A board of tiles for your hosts, sites, folders and drives; checks run while CyrFlip runs, results stay in memory.",
                ru: "Плитки для ваших узлов, сайтов, папок и дисков; проверки идут, пока CyrFlip запущен, результаты хранятся только в памяти.",
                uk: "Плитки для ваших вузлів, сайтів, папок і дисків; перевірки йдуть, поки CyrFlip запущено, результати зберігаються лише в пам'яті.",
                de: "Eine Kachelübersicht für Ihre Hosts, Websites, Ordner und Laufwerke; geprüft wird, solange CyrFlip läuft, Ergebnisse bleiben im Speicher.",
                it: "Un riquadro di tessere per i tuoi host, siti, cartelle e unità; i controlli girano finché CyrFlip è attivo, i risultati restano in memoria.",
                es: "Un panel de mosaicos para sus hosts, sitios, carpetas y unidades; las comprobaciones se hacen mientras CyrFlip está activo y los resultados quedan en memoria.",
                fr: "Un tableau de tuiles pour vos hôtes, sites, dossiers et lecteurs ; les vérifications tournent tant que CyrFlip est actif et les résultats restent en mémoire.",
                pt: "Um painel de blocos para seus hosts, sites, pastas e unidades; as verificações ocorrem enquanto o CyrFlip está ativo e os resultados ficam na memória.",
                ar: "لوحة مربعات لمضيفاتك ومواقعك ومجلداتك ومحركاتك؛ تُجرى الفحوصات ما دام CyrFlip يعمل، وتبقى النتائج في الذاكرة فقط.",
                hi: "आपके होस्ट, साइट, फ़ोल्डर और ड्राइव की टाइलों का बोर्ड; जाँच तब तक चलती है जब तक CyrFlip चल रहा है, परिणाम केवल मेमोरी में रहते हैं।",
                bn: "আপনার হোস্ট, সাইট, ফোল্ডার ও ড্রাইভের টাইলের বোর্ড; CyrFlip চালু থাকা পর্যন্ত যাচাই চলে, ফলাফল শুধু মেমরিতে থাকে।",
                ur: "آپ کے میزبانوں، سائٹوں، فولڈروں اور ڈرائیوز کی ٹائلوں کا بورڈ؛ جانچ اس وقت تک چلتی ہے جب تک CyrFlip چل رہا ہے، نتائج صرف میموری میں رہتے ہیں۔",
                zh: "为您的主机、网站、文件夹和驱动器准备的磁贴面板；CyrFlip 运行期间持续检查，结果只保存在内存中。");

            Add("A board of tiles for your hosts, sites, folders and drives. Checks run while CyrFlip runs; results stay in memory.",
                ru: "Плитки для ваших узлов, сайтов, папок и дисков. Проверки идут, пока CyrFlip запущен; результаты хранятся только в памяти.", uk: "Плитки для ваших вузлів, сайтів, папок і дисків. Перевірки йдуть, поки CyrFlip запущено; результати зберігаються лише в пам'яті.", de: "Eine Kachelübersicht für Ihre Hosts, Websites, Ordner und Laufwerke. Geprüft wird, solange CyrFlip läuft; Ergebnisse bleiben im Speicher.", it: "Un riquadro di tessere per i tuoi host, siti, cartelle e unità. I controlli girano finché CyrFlip è attivo; i risultati restano in memoria.",
                es: "Un panel de mosaicos para sus hosts, sitios, carpetas y unidades. Las comprobaciones se hacen mientras CyrFlip está activo; los resultados quedan en memoria.", fr: "Un tableau de tuiles pour vos hôtes, sites, dossiers et lecteurs. Les vérifications tournent tant que CyrFlip est actif ; les résultats restent en mémoire.", pt: "Um painel de blocos para seus hosts, sites, pastas e unidades. As verificações ocorrem enquanto o CyrFlip está ativo; os resultados ficam na memória.", ar: "لوحة مربعات لمضيفاتك ومواقعك ومجلداتك ومحركاتك. تُجرى الفحوصات ما دام CyrFlip يعمل، وتبقى النتائج في الذاكرة فقط.",
                hi: "आपके होस्ट, साइट, फ़ोल्डर और ड्राइव की टाइलों का बोर्ड। जाँच तब तक चलती है जब तक CyrFlip चल रहा है; परिणाम केवल मेमोरी में रहते हैं।", bn: "আপনার হোস্ট, সাইট, ফোল্ডার ও ড্রাইভের টাইলের বোর্ড। CyrFlip চালু থাকা পর্যন্ত যাচাই চলে; ফলাফল শুধু মেমরিতে থাকে।", ur: "آپ کے میزبانوں، سائٹوں، فولڈروں اور ڈرائیوز کی ٹائلوں کا بورڈ۔ جانچ اس وقت تک چلتی ہے جب تک CyrFlip چل رہا ہے؛ نتائج صرف میموری میں رہتے ہیں۔", zh: "为您的主机、网站、文件夹和驱动器准备的磁贴面板。CyrFlip 运行期间持续检查；结果只保存在内存中。");

            Add("The board stays on top in a corner; drag it anywhere. It is hidden from screenshots and screen sharing.",
                ru: "Доска остаётся поверх окон в углу экрана; её можно перетащить. В снимках и при показе экрана её не видно.", uk: "Дошка залишається поверх вікон у кутку екрана; її можна перетягнути. На знімках і під час показу екрана її не видно.", de: "Die Ampel bleibt in einer Ecke im Vordergrund; sie lässt sich verschieben. In Screenshots und bei der Freigabe ist sie unsichtbar.", it: "Il semaforo resta in primo piano in un angolo; puoi trascinarlo. Non è visibile negli screenshot né condividendo lo schermo.",
                es: "El semáforo permanece visible en una esquina; puede arrastrarlo. No aparece en capturas ni al compartir la pantalla.", fr: "Le tableau reste au premier plan dans un coin ; vous pouvez le déplacer. Il est invisible dans les captures et le partage d'écran.", pt: "O semáforo fica no topo, num canto; pode ser arrastado. Não aparece em capturas nem ao compartilhar a tela.", ar: "تبقى اللوحة في المقدمة بزاوية الشاشة ويمكن سحبها. لا تظهر في لقطات الشاشة ولا عند مشاركتها.",
                hi: "बोर्ड स्क्रीन के कोने में ऊपर रहता है; उसे खींचा जा सकता है। स्क्रीनशॉट और स्क्रीन साझा करने में वह दिखाई नहीं देता।", bn: "বোর্ড স্ক্রিনের কোণে উপরে থাকে; এটি টেনে সরানো যায়। স্ক্রিনশট ও স্ক্রিন শেয়ারে এটি দেখা যায় না।", ur: "بورڈ اسکرین کے کونے میں اوپر رہتا ہے؛ اسے گھسیٹا جا سکتا ہے۔ اسکرین شاٹس اور اسکرین شیئرنگ میں یہ نظر نہیں آتا۔", zh: "面板保持在屏幕一角的最上层，可随意拖动。屏幕截图和屏幕共享中看不到它。");

            Add("A HEAD request (GET of headers on 4xx/5xx), up to five redirects, system proxy, no cookies or login.",
                ru: "Запрос HEAD (при 4xx/5xx - GET заголовков), до пяти переходов, системный прокси, без cookie и логина.", uk: "Запит HEAD (за 4xx/5xx - GET заголовків), до п'яти перенаправлень, системний проксі, без cookie і логіна.", de: "Eine HEAD-Anfrage (bei 4xx/5xx GET der Header), bis zu fünf Weiterleitungen, Systemproxy, keine Cookies oder Anmeldung.", it: "Una richiesta HEAD (GET delle intestazioni con 4xx/5xx), fino a cinque reindirizzamenti, proxy di sistema, niente cookie né login.",
                es: "Una solicitud HEAD (GET de cabeceras con 4xx/5xx), hasta cinco redirecciones, proxy del sistema, sin cookies ni inicio de sesión.", fr: "Une requête HEAD (GET des en-têtes si 4xx/5xx), jusqu'à cinq redirections, proxy système, sans cookies ni identifiant.", pt: "Uma solicitação HEAD (GET dos cabeçalhos em 4xx/5xx), até cinco redirecionamentos, proxy do sistema, sem cookies nem login.", ar: "طلب HEAD (وGET للرؤوس عند 4xx/5xx)، حتى خمس عمليات إعادة توجيه، الوكيل الخاص بالنظام، بلا ملفات تعريف ارتباط أو تسجيل دخول.",
                hi: "HEAD अनुरोध (4xx/5xx पर हेडर का GET), पाँच तक रीडायरेक्ट, सिस्टम प्रॉक्सी, कोई कुकी या लॉगिन नहीं।", bn: "HEAD অনুরোধ (4xx/5xx হলে হেডারের GET), পাঁচটি পর্যন্ত রিডাইরেক্ট, সিস্টেম প্রক্সি, কুকি বা লগইন ছাড়া।", ur: "HEAD درخواست (4xx/5xx پر ہیڈر کا GET)، پانچ تک ری ڈائریکٹ، سسٹم پراکسی، بغیر کوکیز اور لاگ اِن کے۔", zh: "HEAD 请求（遇 4xx/5xx 时用 GET 读取标头），最多五次重定向，使用系统代理，不带 Cookie 和登录。");

            Add("One ICMP echo. Windows computers often ignore it; for them use \"TCP port\" 445 or 3389.",
                ru: "Одно ICMP-эхо. Windows-компьютеры часто не отвечают; для них берите «TCP-порт» 445 или 3389.", uk: "Одне ICMP-ехо. Комп'ютери з Windows часто не відповідають; для них обирайте «TCP-порт» 445 або 3389.", de: "Ein ICMP-Echo. Windows-Computer ignorieren es oft; nehmen Sie dafür „TCP-Port“ 445 oder 3389.", it: "Un echo ICMP. I computer Windows spesso non rispondono; per loro usa \"Porta TCP\" 445 o 3389.",
                es: "Un eco ICMP. Los equipos con Windows suelen ignorarlo; para ellos use «Puerto TCP» 445 o 3389.", fr: "Un écho ICMP. Les ordinateurs Windows l'ignorent souvent ; pour eux, prenez « Port TCP » 445 ou 3389.", pt: "Um eco ICMP. Computadores com Windows costumam ignorá-lo; para eles use \"Porta TCP\" 445 ou 3389.", ar: "صدى ICMP واحد. كثيرًا ما تتجاهله حواسيب Windows؛ استخدم لها \"منفذ TCP\" 445 أو 3389.",
                hi: "एक ICMP इको। Windows कंप्यूटर अक्सर इसे अनदेखा करते हैं; उनके लिए \"TCP पोर्ट\" 445 या 3389 लें।", bn: "একটি ICMP ইকো। Windows কম্পিউটার প্রায়ই এটি উপেক্ষা করে; সেগুলোর জন্য \"TCP পোর্ট\" 445 বা 3389 নিন।", ur: "ایک ICMP ایکو۔ Windows کمپیوٹر اکثر اسے نظر انداز کرتے ہیں؛ ان کے لیے \"TCP پورٹ\" 445 یا 3389 لیں۔", zh: "发送一次 ICMP 回显。Windows 计算机常常不应答；对它们请用“TCP 端口”445 或 3389。");

            Add("The folder must open; at most one entry is read, nothing is written. If a password is needed: \"access denied\".",
                ru: "Папка должна открываться; читается не больше одной записи, ничего не пишется. Если нужен пароль - «доступ запрещён».", uk: "Папка має відкриватися; читається не більше одного запису, нічого не пишеться. Якщо потрібен пароль - «доступ заборонено».", de: "Der Ordner muss sich öffnen lassen; höchstens ein Eintrag wird gelesen, nichts geschrieben. Bei Passwortabfrage: „Zugriff verweigert“.", it: "La cartella deve aprirsi; si legge al massimo una voce, non si scrive nulla. Se serve una password: \"accesso negato\".",
                es: "La carpeta debe abrirse; se lee como máximo una entrada y no se escribe nada. Si pide contraseña: «acceso denegado».", fr: "Le dossier doit s'ouvrir ; au plus une entrée est lue, rien n'est écrit. Si un mot de passe est requis : « accès refusé ».", pt: "A pasta deve abrir; no máximo uma entrada é lida e nada é gravado. Se pedir senha: \"acesso negado\".", ar: "يجب أن يُفتح المجلد؛ تُقرأ مدخلة واحدة على الأكثر ولا يُكتب شيء. إن لزمت كلمة مرور: \"تم رفض الوصول\".",
                hi: "फ़ोल्डर खुलना चाहिए; अधिकतम एक प्रविष्टि पढ़ी जाती है, कुछ लिखा नहीं जाता। पासवर्ड ज़रूरी हो तो: \"पहुँच अस्वीकृत\"।", bn: "ফোল্ডার খুলতে হবে; সর্বোচ্চ একটি এন্ট্রি পড়া হয়, কিছু লেখা হয় না। পাসওয়ার্ড লাগলে: \"অ্যাক্সেস অস্বীকৃত\"।", ur: "فولڈر کھلنا ضروری ہے؛ زیادہ سے زیادہ ایک اندراج پڑھا جاتا ہے، کچھ لکھا نہیں جاتا۔ پاس ورڈ درکار ہو تو: \"رسائی مسترد\"۔", zh: "文件夹必须能打开；最多读取一个条目，不写入任何内容。需要密码时显示“访问被拒绝”。");

            Add("Shows the board and gives it focus; press again to hide. On the board: arrows, Enter, F5, Esc.",
                ru: "Показывает доску и отдаёт ей фокус; повторное нажатие скрывает. На доске: стрелки, Enter, F5, Esc.", uk: "Показує дошку й віддає їй фокус; повторне натискання ховає. На дошці: стрілки, Enter, F5, Esc.", de: "Zeigt die Ampel und gibt ihr den Fokus; erneutes Drücken blendet sie aus. Auf der Ampel: Pfeile, Enter, F5, Esc.", it: "Mostra il semaforo e gli dà il focus; premendo di nuovo lo nasconde. Sul semaforo: frecce, Invio, F5, Esc.",
                es: "Muestra el semáforo y le da el foco; al pulsar de nuevo se oculta. En el semáforo: flechas, Intro, F5, Esc.", fr: "Affiche le tableau et lui donne le focus ; un nouvel appui le masque. Sur le tableau : flèches, Entrée, F5, Échap.", pt: "Mostra o semáforo e dá o foco a ele; pressionar de novo o oculta. No semáforo: setas, Enter, F5, Esc.", ar: "يُظهر اللوحة ويمنحها التركيز؛ والضغط ثانيةً يُخفيها. على اللوحة: الأسهم وEnter وF5 وEsc.",
                hi: "बोर्ड दिखाता है और उसे फ़ोकस देता है; दोबारा दबाने पर छिपा देता है। बोर्ड पर: तीर, Enter, F5, Esc।", bn: "বোর্ড দেখায় এবং তাকে ফোকাস দেয়; আবার চাপলে লুকায়। বোর্ডে: তীর, Enter, F5, Esc।", ur: "بورڈ دکھاتا ہے اور اسے فوکس دیتا ہے؛ دوبارہ دبانے پر چھپا دیتا ہے۔ بورڈ پر: تیر، Enter، F5، Esc۔", zh: "显示面板并将焦点交给它；再次按下则隐藏。面板上：方向键、Enter、F5、Esc。");

            Add("Checks that an SSH server answers on the port. The resource and the server's key are not checked.",
                ru: "Проверяет, что на порту отвечает SSH-сервер. Сам ресурс и ключ сервера не проверяются.", uk: "Перевіряє, що на порту відповідає SSH-сервер. Сам ресурс і ключ сервера не перевіряються.", de: "Prüft, dass am Port ein SSH-Server antwortet. Ressource und Serverschlüssel werden nicht geprüft.", it: "Verifica che sulla porta risponda un server SSH. La risorsa e la chiave del server non vengono controllate.",
                es: "Comprueba que un servidor SSH responde en el puerto. No se comprueban el recurso ni la clave del servidor.", fr: "Vérifie qu'un serveur SSH répond sur le port. La ressource et la clé du serveur ne sont pas vérifiées.", pt: "Verifica se um servidor SSH responde na porta. O recurso e a chave do servidor não são verificados.", ar: "يتحقق من أن خادم SSH يردّ على المنفذ. لا يُفحص المورد ولا مفتاح الخادم.",
                hi: "जाँचता है कि पोर्ट पर SSH सर्वर उत्तर देता है। संसाधन और सर्वर की कुंजी की जाँच नहीं होती।", bn: "পোর্টে SSH সার্ভার সাড়া দেয় কি না যাচাই করে। রিসোর্স ও সার্ভারের কী যাচাই হয় না।", ur: "جانچتا ہے کہ پورٹ پر SSH سرور جواب دیتا ہے۔ وسیلہ اور سرور کی کلید کی جانچ نہیں ہوتی۔", zh: "检查端口上是否有 SSH 服务器应答。不检查资源和服务器密钥。");

            Add("Tile size: a small tile is a bare square - hover for its name. The others show label and detail.",
                ru: "Размер плиток: маленькая - просто квадрат, название видно при наведении. Остальные показывают подпись и подробность.", uk: "Розмір плиток: мала - просто квадрат, назву видно при наведенні. Решта показують підпис і подробиці.", de: "Kachelgröße: Eine kleine Kachel ist ein bloßes Quadrat - der Name erscheint beim Darüberfahren. Die anderen zeigen Bezeichnung und Einzelheiten.", it: "Dimensione delle tessere: la piccola è un semplice quadrato - il nome compare al passaggio del mouse. Le altre mostrano etichetta e dettaglio.",
                es: "Tamaño de los mosaicos: el pequeño es un simple cuadrado - el nombre aparece al pasar el ratón. Los demás muestran etiqueta y detalle.", fr: "Taille des tuiles : la petite est un simple carré - le nom s'affiche au survol. Les autres montrent le libellé et le détail.", pt: "Tamanho dos blocos: o pequeno é só um quadrado - o nome aparece ao passar o mouse. Os outros mostram rótulo e detalhe.", ar: "حجم المربعات: الصغير مربع بسيط - يظهر الاسم عند تمرير المؤشر. وتعرض الأخرى التسمية والتفصيل.",
                hi: "टाइल का आकार: छोटी टाइल केवल एक वर्ग है - नाम माउस ले जाने पर दिखता है। बाकी में नाम और विवरण दिखते हैं।", bn: "টাইলের আকার: ছোট টাইল শুধু একটি বর্গ - নাম দেখতে মাউস রাখুন। বাকিগুলোতে নাম ও বিবরণ দেখা যায়।", ur: "ٹائل کا سائز: چھوٹی ٹائل صرف ایک چوکور ہے - نام ماؤس لے جانے پر نظر آتا ہے۔ باقی میں نام اور تفصیل دکھائی دیتی ہے۔", zh: "磁贴大小：小磁贴只是一个方块，悬停可查看名称。其余显示名称和详细信息。");

            Add("Columns:",
                ru: "Колонок:", uk: "Стовпців:", de: "Spalten:", it: "Colonne:",
                es: "Columnas:", fr: "Colonnes :", pt: "Colunas:", ar: "الأعمدة:",
                hi: "कॉलम:", bn: "কলাম:", ur: "کالم:", zh: "列数：");

            Add("Columns: the board puts this many tiles in a row; the rows follow from the number of targets.",
                ru: "Колонок: столько плиток в ряду; рядов будет столько, сколько нужно для целей.", uk: "Стовпців: стільки плиток у ряду; рядів буде стільки, скільки потрібно для цілей.", de: "Spalten: So viele Kacheln stehen in einer Reihe; die Zeilen ergeben sich aus der Zahl der Ziele.", it: "Colonne: tante tessere per riga; le righe risultano dal numero di obiettivi.",
                es: "Columnas: tantos mosaicos por fila; las filas salen del número de objetivos.", fr: "Colonnes : nombre de tuiles par ligne ; les lignes découlent du nombre de cibles.", pt: "Colunas: quantos blocos por linha; as linhas resultam do número de alvos.", ar: "الأعمدة: عدد المربعات في الصف الواحد؛ وتنتج الصفوف من عدد الأهداف.",
                hi: "कॉलम: एक पंक्ति में इतनी टाइलें; पंक्तियाँ लक्ष्यों की संख्या से तय होती हैं।", bn: "কলাম: এক সারিতে এতগুলো টাইল; সারির সংখ্যা লক্ষ্যের সংখ্যা অনুযায়ী হয়।", ur: "کالم: ایک قطار میں اتنی ٹائلیں؛ قطاریں اہداف کی تعداد سے طے ہوتی ہیں۔", zh: "列数：每行放这么多磁贴；行数由目标数量决定。");

            Add("Board window transparency:",
                ru: "Прозрачность окна светофора:", uk: "Прозорість вікна світофора:", de: "Transparenz des Ampelfensters:", it: "Trasparenza della finestra del semaforo:",
                es: "Transparencia de la ventana del semáforo:", fr: "Transparence de la fenêtre du tableau :", pt: "Transparência da janela do semáforo:", ar: "شفافية نافذة اللوحة:",
                hi: "बोर्ड विंडो की पारदर्शिता:", bn: "বোর্ড উইন্ডোর স্বচ্ছতা:", ur: "بورڈ ونڈو کی شفافیت:", zh: "面板窗口透明度：");

            Add("Board window transparency: 0 is a solid window, 100 leaves only the buttons and the tiles visible. Applied immediately.",
                ru: "Прозрачность окна светофора: 0 - обычное плотное окно, 100 - видны только кнопки и плитки. Применяется сразу.",
                uk: "Прозорість вікна світофора: 0 - звичайне щільне вікно, 100 - видно лише кнопки й плитки. Застосовується одразу.",
                de: "Transparenz des Ampelfensters: 0 ist das gewohnte feste Fenster, 100 lässt nur die Schaltflächen und die Kacheln übrig. Wird sofort angewendet.",
                it: "Trasparenza della finestra del semaforo: 0 è la solida finestra consueta, 100 lascia visibili solo i pulsanti e le tessere. Applicata subito.",
                es: "Transparencia de la ventana del semáforo: 0 es la ventana sólida de siempre, 100 deja visibles solo los botones y los mosaicos. Se aplica al momento.",
                fr: "Transparence de la fenêtre du tableau : 0 est la fenêtre pleine habituelle, 100 ne laisse visibles que les boutons et les tuiles. Appliqué aussitôt.",
                pt: "Transparência da janela do semáforo: 0 é a janela sólida de sempre, 100 deixa visíveis apenas os botões e os blocos. Aplicada na hora.",
                ar: "شفافية نافذة اللوحة: 0 هي النافذة المصمتة المعتادة، و100 يُبقي الأزرار والمربعات وحدها مرئية. تُطبَّق فورًا.",
                hi: "बोर्ड विंडो की पारदर्शिता: 0 सामान्य भरा विंडो है, 100 पर केवल बटन और टाइलें दिखती हैं। तुरंत लागू होता है।",
                bn: "বোর্ড উইন্ডোর স্বচ্ছতা: 0 হল স্বাভাবিক ভরা উইন্ডো, 100-এ শুধু বোতাম ও টাইল দেখা যায়। সঙ্গে সঙ্গে প্রযোজ্য।",
                ur: "بورڈ ونڈو کی شفافیت: 0 معمول کی بھری ہوئی ونڈو ہے، 100 پر صرف بٹن اور ٹائلیں نظر آتی ہیں۔ فوراً لاگو ہوتا ہے۔",
                zh: "面板窗口透明度：0 是不透明的常规窗口，100 时只显示按钮和磁贴。立即生效。");

            // The settings file's caption for the exported key (the import preview's table).
            Add("Board window transparency",
                ru: "Прозрачность окна светофора", uk: "Прозорість вікна світофора", de: "Transparenz des Ampelfensters", it: "Trasparenza della finestra del semaforo",
                es: "Transparencia de la ventana del semáforo", fr: "Transparence de la fenêtre du tableau", pt: "Transparência da janela do semáforo", ar: "شفافية نافذة اللوحة",
                hi: "बोर्ड विंडो की पारदर्शिता", bn: "বোর্ড উইন্ডোর স্বচ্ছতা", ur: "بورڈ ونڈو کی شفافیت", zh: "面板窗口透明度");

            Add("Type an address, name, folder or drive; CyrFlip works out what it is.",
                ru: "Введите адрес, имя, папку или диск - CyrFlip сам поймёт, что это.", uk: "Введіть адресу, ім'я, папку або диск - CyrFlip сам зрозуміє, що це.", de: "Adresse, Name, Ordner oder Laufwerk eingeben - CyrFlip erkennt selbst, was es ist.", it: "Scrivi un indirizzo, un nome, una cartella o un'unità: CyrFlip capisce da solo che cos'è.",
                es: "Escriba una dirección, un nombre, una carpeta o una unidad: CyrFlip averigua qué es.", fr: "Saisissez une adresse, un nom, un dossier ou un lecteur : CyrFlip reconnaît de quoi il s'agit.", pt: "Digite um endereço, um nome, uma pasta ou uma unidade: o CyrFlip descobre o que é.", ar: "اكتب عنوانًا أو اسمًا أو مجلدًا أو محرك أقراص - يتعرف CyrFlip على نوعه بنفسه.",
                hi: "पता, नाम, फ़ोल्डर या ड्राइव लिखें - CyrFlip खुद समझ लेता है कि वह क्या है।", bn: "ঠিকানা, নাম, ফোল্ডার বা ড্রাইভ লিখুন - CyrFlip নিজেই বুঝে নেয় সেটি কী।", ur: "پتہ، نام، فولڈر یا ڈرائیو لکھیں - CyrFlip خود سمجھ لیتا ہے کہ یہ کیا ہے۔", zh: "输入地址、名称、文件夹或驱动器 - CyrFlip 会自动判断类型。");

            Add("Recognized: {0}.",
                ru: "Распознано: {0}.", uk: "Розпізнано: {0}.", de: "Erkannt: {0}.", it: "Riconosciuto: {0}.",
                es: "Reconocido: {0}.", fr: "Reconnu : {0}.", pt: "Reconhecido: {0}.", ar: "تم التعرف: {0}.",
                hi: "पहचाना गया: {0}।", bn: "চেনা গেছে: {0}।", ur: "پہچانا گیا: {0}۔", zh: "已识别：{0}。");

            Add("Not recognized: enter a host, a site address, a folder or a drive.",
                ru: "Не распознано: введите узел, адрес сайта, папку или диск.", uk: "Не розпізнано: введіть вузол, адресу сайту, папку або диск.", de: "Nicht erkannt: Geben Sie einen Host, eine Website-Adresse, einen Ordner oder ein Laufwerk ein.", it: "Non riconosciuto: inserisci un host, l'indirizzo di un sito, una cartella o un'unità.",
                es: "No reconocido: introduzca un host, la dirección de un sitio, una carpeta o una unidad.", fr: "Non reconnu : saisissez un hôte, l'adresse d'un site, un dossier ou un lecteur.", pt: "Não reconhecido: digite um host, o endereço de um site, uma pasta ou uma unidade.", ar: "لم يتم التعرف: أدخل مضيفًا أو عنوان موقع أو مجلدًا أو محرك أقراص.",
                hi: "पहचाना नहीं गया: कोई होस्ट, साइट का पता, फ़ोल्डर या ड्राइव दर्ज करें।", bn: "চেনা যায়নি: একটি হোস্ট, সাইটের ঠিকানা, ফোল্ডার বা ড্রাইভ লিখুন।", ur: "پہچانا نہیں گیا: کوئی میزبان، سائٹ کا پتہ، فولڈر یا ڈرائیو درج کریں۔", zh: "未识别：请输入主机、网站地址、文件夹或驱动器。");

            Add("Other options are on the settings page (the pencil button on the board).",
                ru: "Остальные параметры - на странице настроек (кнопка с карандашом на доске).", uk: "Решта параметрів - на сторінці налаштувань (кнопка з олівцем на дошці).", de: "Weitere Optionen finden Sie auf der Einstellungsseite (Stiftsymbol auf der Ampel).", it: "Le altre opzioni sono nella pagina delle impostazioni (il pulsante con la matita sul semaforo).",
                es: "Las demás opciones están en la página de ajustes (el botón del lápiz en el semáforo).", fr: "Les autres options se trouvent sur la page de réglages (le bouton crayon du tableau).", pt: "As outras opções estão na página de configurações (o botão de lápis no semáforo).", ar: "الخيارات الأخرى في صفحة الإعدادات (زر القلم على اللوحة).",
                hi: "अन्य विकल्प सेटिंग पृष्ठ पर हैं (बोर्ड पर पेंसिल बटन)।", bn: "অন্যান্য বিকল্প সেটিংস পৃষ্ঠায় আছে (বোর্ডের পেন্সিল বোতাম)।", ur: "دیگر اختیارات ترتیبات کے صفحے پر ہیں (بورڈ پر پنسل کا بٹن)۔", zh: "其他选项在设置页中（面板上的铅笔按钮）。");

            Add("The exchange server is its own type, not SFTP: a TLS handshake and a fingerprint check, no data.",
                ru: "Сервер обмена - свой вид, не SFTP: рукопожатие TLS и сверка отпечатка, без данных.", uk: "Сервер обміну - окремий тип, не SFTP: рукостискання TLS і звіряння відбитка, без даних.", de: "Der Austauschserver ist eine eigene Art, kein SFTP: TLS-Handshake und Fingerabdruck-Prüfung, keine Daten.", it: "Il server di scambio è un tipo a sé, non SFTP: handshake TLS e verifica dell'impronta, nessun dato.",
                es: "El servidor de intercambio es un tipo propio, no SFTP: protocolo TLS y comprobación de huella, sin datos.", fr: "Le serveur d'échange est un type à part, pas du SFTP : poignée de main TLS et empreinte vérifiée, aucune donnée.", pt: "O servidor de troca é um tipo próprio, não SFTP: handshake TLS e conferência da impressão digital, sem dados.", ar: "خادم التبادل نوع مستقل وليس SFTP: مصافحة TLS ومطابقة البصمة بلا أي بيانات.",
                hi: "एक्सचेंज सर्वर अलग प्रकार है, SFTP नहीं: TLS हैंडशेक और फ़िंगरप्रिंट की जाँच, कोई डेटा नहीं।", bn: "এক্সচেঞ্জ সার্ভার আলাদা ধরন, SFTP নয়: TLS হ্যান্ডশেক ও ফিঙ্গারপ্রিন্ট যাচাই, কোনো ডেটা নেই।", ur: "تبادلہ سرور الگ قسم ہے، SFTP نہیں: TLS ہینڈ شیک اور فنگر پرنٹ کی جانچ، کوئی ڈیٹا نہیں۔", zh: "交换服务器是独立的类型，不是 SFTP：仅做 TLS 握手和指纹校验，不传数据。");

        }
    }
}
