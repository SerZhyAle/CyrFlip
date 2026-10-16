namespace CyrFlip
{
    /// <summary>
    /// The strings of the settings window's row anatomy and failure presentation (ticket S0060 P6/P7): the
    /// screenshot folder's hint, the failure dialog's two actions, the causes of a shortcut or an address
    /// that would not open, and the popular-layouts batch's confirmation and summary.
    /// </summary>
    internal static partial class Localization
    {
        private static void AddSettingsRowStrings()
        {
            Add("The saved pictures go here; by default it is the Windows Screenshots folder.",
                ru: "Сюда сохраняются снимки; по умолчанию это папка «Снимки экрана» Windows.",
                uk: "Сюди зберігаються знімки; за замовчуванням це папка «Знімки екрана» Windows.",
                de: "Hierhin werden die Bilder gespeichert; standardmäßig ist es der Windows-Ordner „Screenshots“.",
                it: "Le immagini salvate vanno qui; per impostazione predefinita è la cartella Schermate di Windows.",
                es: "Aquí se guardan las imágenes; por defecto es la carpeta Capturas de pantalla de Windows.",
                fr: "Les images enregistrées vont ici ; par défaut, c'est le dossier Captures d'écran de Windows.",
                pt: "As imagens salvas ficam aqui; por padrão é a pasta Capturas de Tela do Windows.",
                ar: "تُحفظ الصور هنا؛ والافتراضي هو مجلد لقطات الشاشة في Windows.",
                hi: "सहेजी गई तस्वीरें यहाँ जाती हैं; डिफ़ॉल्ट रूप से यह Windows का Screenshots फ़ोल्डर है।",
                bn: "সংরক্ষিত ছবি এখানে যায়; ডিফল্টে এটি Windows-এর Screenshots ফোল্ডার।",
                ur: "محفوظ شدہ تصاویر یہاں جاتی ہیں؛ پہلے سے طے شدہ طور پر یہ Windows کا Screenshots فولڈر ہے۔",
                zh: "保存的图片放在这里；默认是 Windows 的“屏幕截图”文件夹。");

            Add("Retry",
                ru: "Повторить", uk: "Повторити", de: "Wiederholen", it: "Riprova", es: "Reintentar", fr: "Réessayer",
                pt: "Tentar novamente", ar: "إعادة المحاولة", hi: "फिर से कोशिश करें", bn: "আবার চেষ্টা করুন",
                ur: "دوبارہ کوشش کریں", zh: "重试");

            Add("Open log folder",
                ru: "Открыть папку журналов", uk: "Відкрити папку журналів", de: "Protokollordner öffnen",
                it: "Apri la cartella dei log", es: "Abrir la carpeta de registros", fr: "Ouvrir le dossier des journaux",
                pt: "Abrir a pasta de logs", ar: "فتح مجلد السجلات", hi: "लॉग फ़ोल्डर खोलें",
                bn: "লগ ফোল্ডার খুলুন", ur: "لاگ فولڈر کھولیں", zh: "打开日志文件夹");

            Add("Could not change the shortcut: {0}",
                ru: "Не удалось изменить ярлык: {0}", uk: "Не вдалося змінити ярлик: {0}",
                de: "Die Verknüpfung konnte nicht geändert werden: {0}", it: "Impossibile modificare il collegamento: {0}",
                es: "No se pudo cambiar el acceso directo: {0}", fr: "Impossible de modifier le raccourci : {0}",
                pt: "Não foi possível alterar o atalho: {0}", ar: "تعذر تغيير الاختصار: {0}",
                hi: "शॉर्टकट बदला नहीं जा सका: {0}", bn: "শর্টকাট বদলানো যায়নি: {0}",
                ur: "شارٹ کٹ تبدیل نہیں ہو سکا: {0}", zh: "无法更改快捷方式：{0}");

            Add("Could not open the address: {0}",
                ru: "Не удалось открыть адрес: {0}", uk: "Не вдалося відкрити адресу: {0}",
                de: "Die Adresse konnte nicht geöffnet werden: {0}", it: "Impossibile aprire l'indirizzo: {0}",
                es: "No se pudo abrir la dirección: {0}", fr: "Impossible d'ouvrir l'adresse : {0}",
                pt: "Não foi possível abrir o endereço: {0}", ar: "تعذر فتح العنوان: {0}",
                hi: "पता खोला नहीं जा सका: {0}", bn: "ঠিকানা খোলা যায়নি: {0}",
                ur: "پتہ کھولا نہیں جا سکا: {0}", zh: "无法打开地址：{0}");

            Add("Nothing to add: the popular layouts are already installed.",
                ru: "Добавлять нечего: популярные раскладки уже установлены.",
                uk: "Нічого додавати: популярні розкладки вже встановлено.",
                de: "Nichts hinzuzufügen: Die gängigen Layouts sind bereits installiert.",
                it: "Niente da aggiungere: i layout più diffusi sono già installati.",
                es: "Nada que añadir: las distribuciones populares ya están instaladas.",
                fr: "Rien à ajouter : les dispositions courantes sont déjà installées.",
                pt: "Nada a adicionar: os layouts populares já estão instalados.",
                ar: "لا شيء لإضافته: التخطيطات الشائعة مثبتة بالفعل.",
                hi: "जोड़ने के लिए कुछ नहीं: लोकप्रिय लेआउट पहले से इंस्टॉल हैं।",
                bn: "যোগ করার কিছু নেই: জনপ্রিয় লেআউটগুলো ইতিমধ্যে ইনস্টল করা আছে।",
                ur: "شامل کرنے کو کچھ نہیں: مقبول لے آؤٹ پہلے سے انسٹال ہیں۔",
                zh: "没有可添加的内容：常用布局都已安装。");

            Add("Add {0} keyboard layouts to Windows?",
                ru: "Добавить в Windows раскладки клавиатуры (количество: {0})?",
                uk: "Додати до Windows розкладки клавіатури (кількість: {0})?",
                de: "{0} Tastaturlayouts zu Windows hinzufügen?",
                it: "Aggiungere {0} layout di tastiera a Windows?",
                es: "¿Añadir {0} distribuciones de teclado a Windows?",
                fr: "Ajouter {0} dispositions de clavier à Windows ?",
                pt: "Adicionar {0} layouts de teclado ao Windows?",
                ar: "هل تريد إضافة {0} من تخطيطات لوحة المفاتيح إلى Windows؟",
                hi: "क्या Windows में {0} कीबोर्ड लेआउट जोड़ें?",
                bn: "Windows-এ {0}টি কীবোর্ড লেআউট যোগ করবেন?",
                ur: "کیا Windows میں {0} کی بورڈ لے آؤٹ شامل کیے جائیں؟",
                zh: "要向 Windows 添加 {0} 个键盘布局吗？");

            Add("Added: {0}. Skipped: {1}.",
                ru: "Добавлено: {0}. Пропущено: {1}.", uk: "Додано: {0}. Пропущено: {1}.",
                de: "Hinzugefügt: {0}. Übersprungen: {1}.", it: "Aggiunti: {0}. Saltati: {1}.",
                es: "Añadidos: {0}. Omitidos: {1}.", fr: "Ajoutées : {0}. Ignorées : {1}.",
                pt: "Adicionados: {0}. Ignorados: {1}.", ar: "تمت الإضافة: {0}. تم التخطي: {1}.",
                hi: "जोड़े गए: {0}। छोड़े गए: {1}।", bn: "যোগ হয়েছে: {0}। বাদ গেছে: {1}।",
                ur: "شامل کیے گئے: {0}۔ چھوڑے گئے: {1}۔", zh: "已添加：{0}。已跳过：{1}。");
        }
    }
}
