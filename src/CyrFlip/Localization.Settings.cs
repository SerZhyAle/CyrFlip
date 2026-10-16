namespace CyrFlip
{
    /// <summary>The settings window's discovery furniture (WINDOWS-UI section 2.3 and section 3.5):
    /// the header commands and the search dialog. The page names and every setting caption live in the
    /// other partials; only what this ticket added is here.</summary>
    internal static partial class Localization
    {
        private static void AddSettingsStrings()
        {
            // The still picture shown while the settings window is built for the first time
            // (SettingsLoadingNotice) - the build blocks the UI thread, so it cannot move.
            Add("Loading settings..",
                ru: "Загрузка настроек..", uk: "Завантаження налаштувань..", de: "Einstellungen werden geladen..",
                it: "Caricamento delle impostazioni..", es: "Cargando la configuración..", fr: "Chargement des paramètres..",
                pt: "Carregando as configurações..", ar: "جارٍ تحميل الإعدادات..", hi: "सेटिंग्स लोड हो रही हैं..",
                bn: "সেটিংস লোড হচ্ছে..", ur: "ترتیبات لوڈ ہو رہی ہیں..", zh: "正在加载设置..");

            Add("Find a setting...",
                ru: "Найти настройку...", uk: "Знайти налаштування...", de: "Einstellung suchen...", it: "Trova impostazione...",
                es: "Buscar un ajuste...", fr: "Rechercher un paramètre...", pt: "Localizar configuração...",
                ar: "البحث عن إعداد...", hi: "सेटिंग खोजें...", bn: "সেটিং খুঁজুন...", ur: "ترتیب تلاش کریں...", zh: "查找设置...");

            Add("Expand all",
                ru: "Развернуть все", uk: "Розгорнути все", de: "Alle ausklappen", it: "Espandi tutto",
                es: "Expandir todo", fr: "Tout développer", pt: "Expandir tudo",
                ar: "توسيع الكل", hi: "सब विस्तृत करें", bn: "সব প্রসারিত করুন", ur: "سب پھیلیں", zh: "全部展开");

            Add("Collapse all",
                ru: "Свернуть все", uk: "Згорнути все", de: "Alle einklappen", it: "Comprimi tutto",
                es: "Contraer todo", fr: "Tout replier", pt: "Recolher tudo",
                ar: "طي الكل", hi: "सब संकुचित करें", bn: "সব সংকুচিত করুন", ur: "سب سکیڑیں", zh: "全部折叠");

            Add("Search settings",
                ru: "Поиск по настройкам", uk: "Пошук налаштувань", de: "Einstellungen durchsuchen", it: "Cerca nelle impostazioni",
                es: "Buscar en los ajustes", fr: "Rechercher dans les paramètres", pt: "Pesquisar nas configurações",
                ar: "البحث في الإعدادات", hi: "सेटिंग्स में खोजें", bn: "সেটিংসে খুঁজুন", ur: "ترتیبات میں تلاش", zh: "搜索设置");

            Add("Type the words to look for: a match is found by a setting's caption and by its description. Enter opens the found setting, Esc closes the search.",
                ru: "Введите слова для поиска: совпадение находится по названию настройки и по её описанию. Enter открывает найденную настройку, Esc закрывает поиск.",
                uk: "Введіть слова для пошуку: збіг шукається за назвою налаштування та за його описом. Enter відкриває знайдене налаштування, Esc закриває пошук.",
                de: "Geben Sie die Suchwörter ein: gesucht wird nach dem Namen einer Einstellung und nach ihrer Beschreibung. Enter öffnet die gefundene Einstellung, Esc schließt die Suche.",
                it: "Digitare le parole da cercare: la corrispondenza viene cercata nel nome dell'impostazione e nella sua descrizione. Invio apre l'impostazione trovata, Esc chiude la ricerca.",
                es: "Escriba las palabras que busca: la coincidencia se busca en el nombre del ajuste y en su descripción. Intro abre el ajuste encontrado, Esc cierra la búsqueda.",
                fr: "Saisissez les mots à rechercher : la correspondance est cherchée dans le nom du paramètre et dans sa description. Entrée ouvre le paramètre trouvé, Échap ferme la recherche.",
                pt: "Digite as palavras a procurar: a correspondência é procurada no nome da configuração e na sua descrição. Enter abre a configuração encontrada, Esc fecha a pesquisa.",
                ar: "اكتب الكلمات المطلوب البحث عنها: يُبحث عن التطابق في اسم الإعداد وفي وصفه. Enter يفتح الإعداد الموجود، وEsc يغلق البحث.",
                hi: "खोजने के लिए शब्द लिखें: मेल सेटिंग के नाम और उसके विवरण में खोजा जाता है। Enter मिली सेटिंग खोलता है, Esc खोज बंद करता है।",
                bn: "খোঁজার শব্দ লিখুন: মিল সেটিং-এর নাম এবং তার বিবরণে খোঁজা হয়। Enter পাওয়া সেটিং খোলে, Esc খোঁজা বন্ধ করে।",
                ur: "تلاش کے لیے الفاظ لکھیں: مطابقت ترتیب کے نام اور اس کی وضاحت میں تلاش کی جاتی ہے۔ Enter ملنے والی ترتیب کھولتا ہے، Esc تلاش بند کرتا ہے۔",
                zh: "输入要查找的词：会按设置的名称和描述进行匹配。Enter 打开找到的设置，Esc 关闭搜索。");

            Add("Nothing found.",
                ru: "Ничего не найдено.", uk: "Нічого не знайдено.", de: "Nichts gefunden.", it: "Nessun risultato.",
                es: "No se encontró nada.", fr: "Aucun résultat.", pt: "Nada encontrado.",
                ar: "لم يُعثر على شيء.", hi: "कुछ नहीं मिला।", bn: "কিছু পাওয়া যায়নি।", ur: "کچھ نہیں ملا۔", zh: "未找到任何内容。");

            Add("Licence: MIT",
                ru: "Лицензия: MIT", uk: "Ліцензія: MIT", de: "Lizenz: MIT", it: "Licenza: MIT",
                es: "Licencia: MIT", fr: "Licence : MIT", pt: "Licença: MIT",
                ar: "الترخيص: MIT", hi: "लाइसेंस: MIT", bn: "লাইসেন্স: MIT", ur: "لائسنس: MIT", zh: "许可证：MIT");

            Add("Privacy policy",
                ru: "Политика конфиденциальности", uk: "Політика конфіденційності", de: "Datenschutzerklärung", it: "Informativa sulla privacy",
                es: "Política de privacidad", fr: "Politique de confidentialité", pt: "Política de privacidade",
                ar: "سياسة الخصوصية", hi: "गोपनीयता नीति", bn: "গোপনীয়তা নীতি", ur: "پرایویسی پالیسی", zh: "隐私政策");

            Add("Enable",
                ru: "Включение", uk: "Увімкнення", de: "Aktivierung", it: "Attivazione",
                es: "Activación", fr: "Activation", pt: "Ativação",
                ar: "التفعيل", hi: "सक्रियण", bn: "সক্রিয়করণ", ur: "فعال کرنا", zh: "启用");

            Add("Help and support",
                ru: "Помощь и поддержка", uk: "Допомога та підтримка", de: "Hilfe und Support", it: "Assistenza e supporto",
                es: "Ayuda y soporte", fr: "Aide et assistance", pt: "Ajuda e suporte",
                ar: "المساعدة والدعم", hi: "सहायता और समर्थन", bn: "সহায়তা ও সমর্থন", ur: "مدد اور معاونت", zh: "帮助与支持");

            // S0060 M5 (WINDOWS-UI section 3 rule 6): the sentence under a control that is unavailable, saying
            // which switch makes it so and what to do. One template for every master switch of the window.
            Add("Unavailable while «{0}» is off - turn that switch on first.",
                ru: "Недоступно, пока выключен переключатель «{0}» - сначала включите его.",
                uk: "Недоступно, поки вимкнено перемикач «{0}» - спершу ввімкніть його.",
                de: "Nicht verfügbar, solange «{0}» ausgeschaltet ist - schalten Sie zuerst diesen Schalter ein.",
                it: "Non disponibile finché «{0}» è disattivato: attiva prima quell'interruttore.",
                es: "No disponible mientras «{0}» esté desactivado: activa antes ese interruptor.",
                fr: "Indisponible tant que «{0}» est désactivé : activez d'abord cet interrupteur.",
                pt: "Indisponível enquanto «{0}» estiver desligado: ligue primeiro esse interruptor.",
                ar: "غير متاح ما دام «{0}» معطّلًا - فعّل ذلك المفتاح أولًا.",
                hi: "जब तक «{0}» बंद है, यह उपलब्ध नहीं - पहले वह स्विच चालू करें।",
                bn: "«{0}» বন্ধ থাকলে এটি পাওয়া যায় না - আগে ওই সুইচটি চালু করুন।",
                ur: "جب تک «{0}» بند ہے یہ دستیاب نہیں - پہلے وہ سوئچ آن کریں۔",
                zh: "在《{0}》关闭期间不可用 - 请先打开该开关。");

            // The three restore buttons (settings snapshot, keyboard layouts, language shortcuts) are unavailable
            // for the one reason that no earlier state has been saved yet.
            Add("Nothing to restore yet: CyrFlip saves the original state just before the first change.",
                ru: "Восстанавливать пока нечего: исходное состояние CyrFlip сохраняет перед первым изменением.",
                uk: "Відновлювати поки нічого: початковий стан CyrFlip зберігає перед першою зміною.",
                de: "Noch nichts wiederherzustellen: CyrFlip sichert den Ausgangszustand unmittelbar vor der ersten Änderung.",
                it: "Non c'è ancora nulla da ripristinare: CyrFlip salva lo stato originale subito prima della prima modifica.",
                es: "Aún no hay nada que restaurar: CyrFlip guarda el estado original justo antes del primer cambio.",
                fr: "Rien à restaurer pour l'instant : CyrFlip enregistre l'état d'origine juste avant la première modification.",
                pt: "Ainda não há nada para restaurar: o CyrFlip guarda o estado original logo antes da primeira alteração.",
                ar: "لا يوجد ما يمكن استعادته بعد: يحفظ CyrFlip الحالة الأصلية قبل أول تغيير مباشرة.",
                hi: "अभी पुनर्स्थापित करने को कुछ नहीं है: CyrFlip पहले बदलाव से ठीक पहले मूल स्थिति सहेजता है।",
                bn: "এখনও পুনরুদ্ধার করার কিছু নেই: প্রথম পরিবর্তনের ঠিক আগে CyrFlip মূল অবস্থা সংরক্ষণ করে।",
                ur: "ابھی بحال کرنے کو کچھ نہیں: CyrFlip پہلی تبدیلی سے عین پہلے اصل حالت محفوظ کرتا ہے۔",
                zh: "暂无可还原的内容：CyrFlip 会在第一次更改之前保存原始状态。");
        }
    }
}
