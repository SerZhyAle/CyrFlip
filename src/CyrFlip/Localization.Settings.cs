namespace CyrFlip
{
    /// <summary>The settings window's discovery furniture (WINDOWS-UI section 2.3 and section 3.5):
    /// the header commands and the search dialog. The page names and every setting caption live in the
    /// other partials; only what this ticket added is here.</summary>
    internal static partial class Localization
    {
        private static void AddSettingsStrings()
        {
            Add("Найти настройку...",
                en: "Find a setting...", uk: "Знайти налаштування...", de: "Einstellung suchen...", it: "Trova impostazione...",
                es: "Buscar un ajuste...", fr: "Rechercher un paramètre...", pt: "Localizar configuração...",
                ar: "البحث عن إعداد...", hi: "सेटिंग खोजें...", bn: "সেটিং খুঁজুন...", ur: "ترتیب تلاش کریں...", zh: "查找设置...");

            Add("Развернуть все",
                en: "Expand all", uk: "Розгорнути все", de: "Alle ausklappen", it: "Espandi tutto",
                es: "Expandir todo", fr: "Tout développer", pt: "Expandir tudo",
                ar: "توسيع الكل", hi: "सब विस्तृत करें", bn: "সব প্রসারিত করুন", ur: "سب پھیلیں", zh: "全部展开");

            Add("Свернуть все",
                en: "Collapse all", uk: "Згорнути все", de: "Alle einklappen", it: "Comprimi tutto",
                es: "Contraer todo", fr: "Tout replier", pt: "Recolher tudo",
                ar: "طي الكل", hi: "सब संकुचित करें", bn: "সব সংকুচিত করুন", ur: "سب سکیڑیں", zh: "全部折叠");

            Add("Поиск по настройкам",
                en: "Search settings", uk: "Пошук налаштувань", de: "Einstellungen durchsuchen", it: "Cerca nelle impostazioni",
                es: "Buscar en los ajustes", fr: "Rechercher dans les paramètres", pt: "Pesquisar nas configurações",
                ar: "البحث في الإعدادات", hi: "सेटिंग्स में खोजें", bn: "সেটিংসে খুঁজুন", ur: "ترتیبات میں تلاش", zh: "搜索设置");

            Add("Введите слова для поиска: совпадение находится по названию настройки и по её описанию. Enter открывает найденную настройку, Esc закрывает поиск.",
                en: "Type the words to look for: a match is found by a setting's caption and by its description. Enter opens the found setting, Esc closes the search.",
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

            Add("Ничего не найдено.",
                en: "Nothing found.", uk: "Нічого не знайдено.", de: "Nichts gefunden.", it: "Nessun risultato.",
                es: "No se encontró nada.", fr: "Aucun résultat.", pt: "Nada encontrado.",
                ar: "لم يُعثر على شيء.", hi: "कुछ नहीं मिला।", bn: "কিছু পাওয়া যায়নি।", ur: "کچھ نہیں ملا۔", zh: "未找到任何内容。");

            Add("Лицензия: MIT",
                en: "Licence: MIT", uk: "Ліцензія: MIT", de: "Lizenz: MIT", it: "Licenza: MIT",
                es: "Licencia: MIT", fr: "Licence : MIT", pt: "Licença: MIT",
                ar: "الترخيص: MIT", hi: "लाइसेंस: MIT", bn: "লাইসেন্স: MIT", ur: "لائسنس: MIT", zh: "许可证：MIT");

            Add("Политика конфиденциальности",
                en: "Privacy policy", uk: "Політика конфіденційності", de: "Datenschutzerklärung", it: "Informativa sulla privacy",
                es: "Política de privacidad", fr: "Politique de confidentialité", pt: "Política de privacidade",
                ar: "سياسة الخصوصية", hi: "गोपनीयता नीति", bn: "গোপনীয়তা নীতি", ur: "پرایویسی پالیسی", zh: "隐私政策");
        }
    }
}
