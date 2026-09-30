namespace CyrFlip
{
    /// <summary>The open exchange file of notes and clipboard history (ticket S0023): the export dialog, the import preview and its report.</summary>
    internal static partial class Localization
    {
        private static void AddExchangeStrings()
        {
            Add("Экспорт в файл обмена",
                en: "Export to exchange file", uk: "Експорт у файл обміну", de: "Export in Austauschdatei",
                it: "Esporta nel file di scambio", es: "Exportar a archivo de intercambio", fr: "Exporter vers un fichier d'échange",
                pt: "Exportar para arquivo de troca", ar: "التصدير إلى ملف تبادل", hi: "एक्सचेंज फ़ाइल में निर्यात",
                bn: "এক্সচেঞ্জ ফাইলে রপ্তানি", ur: "ایکسچینج فائل میں ایکسپورٹ", zh: "导出到交换文件");

            Add("Импорт из файла обмена",
                en: "Import from exchange file", uk: "Імпорт із файлу обміну", de: "Import aus Austauschdatei",
                it: "Importa dal file di scambio", es: "Importar desde archivo de intercambio", fr: "Importer depuis un fichier d'échange",
                pt: "Importar do arquivo de troca", ar: "الاستيراد من ملف تبادل", hi: "एक्सचेंज फ़ाइल से आयात",
                bn: "এক্সচেঞ্জ ফাইল থেকে আমদানি", ur: "ایکسچینج فائل سے امپورٹ", zh: "从交换文件导入");

            Add("Один открытый текстовый файл: его можно прочитать в любом редакторе или на телефоне и импортировать в CyrFlip на другом компьютере. CyrFlip никуда его не отправляет.",
                en: "One open text file: you can read it in any editor or on a phone and import it into CyrFlip on another computer. CyrFlip does not send it anywhere.",
                uk: "Один відкритий текстовий файл: його можна прочитати в будь-якому редакторі чи на телефоні та імпортувати в CyrFlip на іншому комп'ютері. CyrFlip нікуди його не надсилає.",
                de: "Eine offene Textdatei: Sie lässt sich in jedem Editor oder auf dem Handy lesen und auf einem anderen Computer in CyrFlip importieren. CyrFlip sendet sie nirgendwohin.",
                it: "Un unico file di testo in chiaro: si può leggere in qualsiasi editor o sul telefono e importare in CyrFlip su un altro computer. CyrFlip non lo invia da nessuna parte.",
                es: "Un único archivo de texto abierto: se puede leer en cualquier editor o en el teléfono e importar en CyrFlip en otro equipo. CyrFlip no lo envía a ninguna parte.",
                fr: "Un seul fichier texte en clair : il se lit dans n'importe quel éditeur ou sur un téléphone et s'importe dans CyrFlip sur un autre ordinateur. CyrFlip ne l'envoie nulle part.",
                pt: "Um único arquivo de texto aberto: pode ser lido em qualquer editor ou no celular e importado no CyrFlip em outro computador. O CyrFlip não o envia a lugar nenhum.",
                ar: "ملف نصي مفتوح واحد: يمكن قراءته في أي محرر أو على الهاتف واستيراده إلى CyrFlip على كمبيوتر آخر. لا يرسله CyrFlip إلى أي مكان.",
                hi: "एक खुली टेक्स्ट फ़ाइल: इसे किसी भी एडिटर या फ़ोन पर पढ़ा जा सकता है और दूसरे कंप्यूटर पर CyrFlip में आयात किया जा सकता है। CyrFlip इसे कहीं नहीं भेजता।",
                bn: "একটি খোলা টেক্সট ফাইল: যেকোনো এডিটরে বা ফোনে পড়া যায় এবং অন্য কম্পিউটারে CyrFlip-এ আমদানি করা যায়। CyrFlip এটি কোথাও পাঠায় না।",
                ur: "ایک کھلی ٹیکسٹ فائل: اسے کسی بھی ایڈیٹر یا فون پر پڑھا جا سکتا ہے اور دوسرے کمپیوٹر پر CyrFlip میں امپورٹ کیا جا سکتا ہے۔ CyrFlip اسے کہیں نہیں بھیجتا۔",
                zh: "一个公开的文本文件：可以在任何编辑器或手机上阅读，也可以在另一台电脑上导入 CyrFlip。CyrFlip 不会把它发送到任何地方。");

            Add("Закреплённые элементы истории",
                en: "Pinned history items", uk: "Закріплені елементи історії", de: "Angeheftete Verlaufseinträge",
                it: "Elementi fissati della cronologia", es: "Elementos fijados del historial", fr: "Éléments épinglés de l'historique",
                pt: "Itens fixados do histórico", ar: "عناصر السجل المثبتة", hi: "पिन किए गए इतिहास आइटम",
                bn: "পিন করা ইতিহাসের আইটেম", ur: "پن کیے گئے ہسٹری آئٹمز", zh: "已固定的历史记录项");

            Add("Остальная история буфера",
                en: "The rest of the clipboard history", uk: "Решта історії буфера", de: "Übriger Zwischenablage-Verlauf",
                it: "Resto della cronologia appunti", es: "Resto del historial del portapapeles", fr: "Reste de l'historique du presse-papiers",
                pt: "Restante do histórico da área de transferência", ar: "بقية سجل الحافظة", hi: "बाकी क्लिपबोर्ड इतिहास",
                bn: "বাকি ক্লিপবোর্ড ইতিহাস", ur: "باقی کلپ بورڈ ہسٹری", zh: "其余剪贴板历史");

            Add("последние 50",
                en: "last 50", uk: "останні 50", de: "letzte 50", it: "ultimi 50", es: "últimos 50", fr: "50 derniers",
                pt: "últimos 50", ar: "آخر 50", hi: "अंतिम 50", bn: "শেষ 50টি", ur: "آخری 50", zh: "最近 50 条");

            Add("последние 100",
                en: "last 100", uk: "останні 100", de: "letzte 100", it: "ultimi 100", es: "últimos 100", fr: "100 derniers",
                pt: "últimos 100", ar: "آخر 100", hi: "अंतिम 100", bn: "শেষ 100টি", ur: "آخری 100", zh: "最近 100 条");

            Add("за последние 7 дней",
                en: "last 7 days", uk: "за останні 7 днів", de: "letzte 7 Tage", it: "ultimi 7 giorni", es: "últimos 7 días",
                fr: "7 derniers jours", pt: "últimos 7 dias", ar: "آخر 7 أيام", hi: "पिछले 7 दिन", bn: "গত 7 দিন",
                ur: "پچھلے 7 دن", zh: "最近 7 天");

            Add("вся история",
                en: "entire history", uk: "уся історія", de: "gesamter Verlauf", it: "tutta la cronologia", es: "todo el historial",
                fr: "tout l'historique", pt: "todo o histórico", ar: "السجل بالكامل", hi: "पूरा इतिहास", bn: "সম্পূর্ণ ইতিহাস",
                ur: "پوری ہسٹری", zh: "全部历史");

            Add("Файл будет обычным незашифрованным текстом. Не передавайте его через небезопасные каналы и удалите после переноса, если он больше не нужен.",
                en: "The file will be ordinary unencrypted text. Do not send it over insecure channels, and delete it after the transfer if you no longer need it.",
                uk: "Файл буде звичайним незашифрованим текстом. Не передавайте його небезпечними каналами та видаліть після перенесення, якщо він більше не потрібен.",
                de: "Die Datei ist gewöhnlicher, unverschlüsselter Text. Übertragen Sie sie nicht über unsichere Kanäle und löschen Sie sie nach der Übertragung, wenn Sie sie nicht mehr brauchen.",
                it: "Il file sarà normale testo non cifrato. Non trasmetterlo su canali non sicuri ed eliminalo dopo il trasferimento, se non serve più.",
                es: "El archivo será texto normal sin cifrar. No lo envíes por canales inseguros y elimínalo tras la transferencia si ya no lo necesitas.",
                fr: "Le fichier sera du texte ordinaire non chiffré. Ne le transmettez pas par des canaux non sécurisés et supprimez-le après le transfert si vous n'en avez plus besoin.",
                pt: "O arquivo será texto comum sem criptografia. Não o envie por canais inseguros e exclua-o após a transferência se não precisar mais dele.",
                ar: "سيكون الملف نصًا عاديًا غير مشفّر. لا ترسله عبر قنوات غير آمنة واحذفه بعد النقل إذا لم تعد بحاجة إليه.",
                hi: "फ़ाइल साधारण, बिना एन्क्रिप्शन वाला टेक्स्ट होगी। इसे असुरक्षित माध्यमों से न भेजें और ज़रूरत न रहने पर स्थानांतरण के बाद इसे हटा दें।",
                bn: "ফাইলটি সাধারণ, এনক্রিপ্ট না করা টেক্সট হবে। এটি অনিরাপদ মাধ্যমে পাঠাবেন না এবং প্রয়োজন না থাকলে স্থানান্তরের পরে মুছে ফেলুন।",
                ur: "فائل عام، غیر خفیہ کردہ ٹیکسٹ ہوگی۔ اسے غیر محفوظ ذرائع سے نہ بھیجیں اور ضرورت نہ رہے تو منتقلی کے بعد اسے حذف کر دیں۔",
                zh: "该文件将是未加密的普通文本。请勿通过不安全的渠道传输，如果不再需要，请在传输后将其删除。");

            Add("Больше не предупреждать для заметок",
                en: "Don't warn again for notes", uk: "Більше не попереджати для нотаток", de: "Bei Notizen nicht mehr warnen",
                it: "Non avvisare più per le note", es: "No volver a avisar para las notas", fr: "Ne plus avertir pour les notes",
                pt: "Não avisar mais para notas", ar: "عدم التحذير مجددًا للملاحظات", hi: "नोट्स के लिए फिर चेतावनी न दें",
                bn: "নোটের জন্য আর সতর্ক করবেন না", ur: "نوٹس کے لیے دوبارہ خبردار نہ کریں", zh: "导出笔记时不再提醒");

            Add("Экспортировать...",
                en: "Export...", uk: "Експортувати...", de: "Exportieren...", it: "Esporta...", es: "Exportar...",
                fr: "Exporter...", pt: "Exportar...", ar: "تصدير...", hi: "निर्यात करें...", bn: "রপ্তানি করুন...",
                ur: "ایکسپورٹ کریں...", zh: "导出...");

            Add("Импортировать...",
                en: "Import...", uk: "Імпортувати...", de: "Importieren...", it: "Importa...", es: "Importar...",
                fr: "Importer...", pt: "Importar...", ar: "استيراد...", hi: "आयात करें...", bn: "আমদানি করুন...",
                ur: "امپورٹ کریں...", zh: "导入...");

            Add("Перенос...",
                en: "Transfer...", uk: "Перенесення...", de: "Übertragen...", it: "Trasferisci...", es: "Transferir...",
                fr: "Transférer...", pt: "Transferir...", ar: "نقل...", hi: "स्थानांतरण...", bn: "স্থানান্তর...",
                ur: "منتقلی...", zh: "迁移...");

            Add("Перенос на другой компьютер",
                en: "Moving to another computer", uk: "Перенесення на інший комп'ютер", de: "Auf einen anderen Computer übertragen",
                it: "Trasferimento su un altro computer", es: "Traslado a otro equipo", fr: "Transfert vers un autre ordinateur",
                pt: "Transferência para outro computador", ar: "النقل إلى كمبيوتر آخر", hi: "दूसरे कंप्यूटर पर स्थानांतरण",
                bn: "অন্য কম্পিউটারে স্থানান্তর", ur: "دوسرے کمپیوٹر پر منتقلی", zh: "迁移到另一台电脑");

            Add("Один открытый текстовый файл с заметками и выбранной историей буфера: его можно прочитать в любом редакторе или на телефоне и импортировать в CyrFlip на другом компьютере. Файл не шифруется. Импорт показывает, что будет добавлено, и ничего не удаляет.",
                en: "One open text file with your notes and the clipboard history you choose: read it in any editor or on a phone, and import it into CyrFlip on another computer. The file is not encrypted. The import shows what will be added and deletes nothing.",
                uk: "Один відкритий текстовий файл із нотатками та вибраною історією буфера: його можна прочитати в будь-якому редакторі чи на телефоні та імпортувати в CyrFlip на іншому комп'ютері. Файл не шифрується. Імпорт показує, що буде додано, і нічого не видаляє.",
                de: "Eine offene Textdatei mit Ihren Notizen und dem gewählten Zwischenablage-Verlauf: in jedem Editor oder auf dem Handy lesbar und auf einem anderen Computer in CyrFlip importierbar. Die Datei wird nicht verschlüsselt. Der Import zeigt, was hinzugefügt wird, und löscht nichts.",
                it: "Un unico file di testo in chiaro con le note e la cronologia appunti scelta: si legge in qualsiasi editor o sul telefono e si importa in CyrFlip su un altro computer. Il file non è cifrato. L'importazione mostra cosa verrà aggiunto e non elimina nulla.",
                es: "Un único archivo de texto abierto con las notas y el historial del portapapeles que elijas: se lee en cualquier editor o en el teléfono y se importa en CyrFlip en otro equipo. El archivo no se cifra. La importación muestra lo que se añadirá y no elimina nada.",
                fr: "Un seul fichier texte en clair avec vos notes et l'historique du presse-papiers choisi : il se lit dans n'importe quel éditeur ou sur un téléphone et s'importe dans CyrFlip sur un autre ordinateur. Le fichier n'est pas chiffré. L'importation montre ce qui sera ajouté et ne supprime rien.",
                pt: "Um único arquivo de texto aberto com as notas e o histórico da área de transferência escolhido: pode ser lido em qualquer editor ou no celular e importado no CyrFlip em outro computador. O arquivo não é criptografado. A importação mostra o que será adicionado e não exclui nada.",
                ar: "ملف نصي مفتوح واحد يضم ملاحظاتك وسجل الحافظة الذي تختاره: يمكن قراءته في أي محرر أو على الهاتف واستيراده إلى CyrFlip على كمبيوتر آخر. الملف غير مشفّر. يعرض الاستيراد ما ستتم إضافته ولا يحذف شيئًا.",
                hi: "आपके नोट्स और चुने गए क्लिपबोर्ड इतिहास वाली एक खुली टेक्स्ट फ़ाइल: इसे किसी भी एडिटर या फ़ोन पर पढ़ा जा सकता है और दूसरे कंप्यूटर पर CyrFlip में आयात किया जा सकता है। फ़ाइल एन्क्रिप्ट नहीं होती। आयात दिखाता है कि क्या जोड़ा जाएगा और कुछ भी नहीं हटाता।",
                bn: "আপনার নোট ও বেছে নেওয়া ক্লিপবোর্ড ইতিহাস নিয়ে একটি খোলা টেক্সট ফাইল: যেকোনো এডিটরে বা ফোনে পড়া যায় এবং অন্য কম্পিউটারে CyrFlip-এ আমদানি করা যায়। ফাইলটি এনক্রিপ্ট করা হয় না। আমদানি দেখায় কী যোগ হবে এবং কিছুই মোছে না।",
                ur: "آپ کے نوٹس اور منتخب کلپ بورڈ ہسٹری والی ایک کھلی ٹیکسٹ فائل: اسے کسی بھی ایڈیٹر یا فون پر پڑھا جا سکتا ہے اور دوسرے کمپیوٹر پر CyrFlip میں امپورٹ کیا جا سکتا ہے۔ فائل خفیہ نہیں کی جاتی۔ امپورٹ دکھاتا ہے کہ کیا شامل ہوگا اور کچھ حذف نہیں کرتا۔",
                zh: "一个包含笔记和所选剪贴板历史的公开文本文件：可在任何编辑器或手机上阅读，也可在另一台电脑上导入 CyrFlip。该文件不加密。导入会先显示将添加的内容，且不会删除任何数据。");

            Add("Файл обмена CyrFlip",
                en: "CyrFlip exchange file", uk: "Файл обміну CyrFlip", de: "CyrFlip-Austauschdatei", it: "File di scambio CyrFlip",
                es: "Archivo de intercambio de CyrFlip", fr: "Fichier d'échange CyrFlip", pt: "Arquivo de troca do CyrFlip",
                ar: "ملف تبادل CyrFlip", hi: "CyrFlip एक्सचेंज फ़ाइल", bn: "CyrFlip এক্সচেঞ্জ ফাইল", ur: "CyrFlip ایکسچینج فائل",
                zh: "CyrFlip 交换文件");

            Add("Все файлы",
                en: "All files", uk: "Усі файли", de: "Alle Dateien", it: "Tutti i file", es: "Todos los archivos",
                fr: "Tous les fichiers", pt: "Todos os arquivos", ar: "كل الملفات", hi: "सभी फ़ाइलें", bn: "সব ফাইল",
                ur: "تمام فائلیں", zh: "所有文件");

            Add("Экспортировано заметок: {0}, записей истории: {1}.",
                en: "Exported notes: {0}, history entries: {1}.", uk: "Експортовано нотаток: {0}, записів історії: {1}.",
                de: "Exportierte Notizen: {0}, Verlaufseinträge: {1}.", it: "Note esportate: {0}, voci della cronologia: {1}.",
                es: "Notas exportadas: {0}, entradas del historial: {1}.", fr: "Notes exportées : {0}, entrées d'historique : {1}.",
                pt: "Notas exportadas: {0}, entradas do histórico: {1}.", ar: "الملاحظات المصدَّرة: {0}، إدخالات السجل: {1}.",
                hi: "निर्यात किए गए नोट्स: {0}, इतिहास प्रविष्टियाँ: {1}।", bn: "রপ্তানি করা নোট: {0}, ইতিহাসের এন্ট্রি: {1}।",
                ur: "ایکسپورٹ شدہ نوٹس: {0}، ہسٹری اندراجات: {1}۔", zh: "已导出笔记：{0}，历史记录：{1}。");

            Add("Не удалось прочитать файл: {0}",
                en: "Could not read the file: {0}", uk: "Не вдалося прочитати файл: {0}", de: "Datei konnte nicht gelesen werden: {0}",
                it: "Impossibile leggere il file: {0}", es: "No se pudo leer el archivo: {0}", fr: "Impossible de lire le fichier : {0}",
                pt: "Não foi possível ler o arquivo: {0}", ar: "تعذّرت قراءة الملف: {0}", hi: "फ़ाइल पढ़ी नहीं जा सकी: {0}",
                bn: "ফাইলটি পড়া যায়নি: {0}", ur: "فائل پڑھی نہیں جا سکی: {0}", zh: "无法读取文件：{0}");

            Add("В файле нет ни одной заметки или записи истории, которую можно импортировать.",
                en: "The file contains no note or history entry that can be imported.",
                uk: "У файлі немає жодної нотатки чи запису історії, які можна імпортувати.",
                de: "Die Datei enthält keine Notiz und keinen Verlaufseintrag, der sich importieren lässt.",
                it: "Il file non contiene note né voci della cronologia importabili.",
                es: "El archivo no contiene ninguna nota ni entrada del historial que se pueda importar.",
                fr: "Le fichier ne contient aucune note ni entrée d'historique importable.",
                pt: "O arquivo não contém nenhuma nota ou entrada do histórico que possa ser importada.",
                ar: "لا يحتوي الملف على أي ملاحظة أو إدخال سجل يمكن استيراده.",
                hi: "फ़ाइल में ऐसा कोई नोट या इतिहास प्रविष्टि नहीं है जिसे आयात किया जा सके।",
                bn: "ফাইলে আমদানি করার মতো কোনো নোট বা ইতিহাসের এন্ট্রি নেই।",
                ur: "فائل میں کوئی ایسا نوٹ یا ہسٹری اندراج نہیں جسے امپورٹ کیا جا سکے۔",
                zh: "文件中没有可导入的笔记或历史记录。");

            Add("Заметок без Id в файле: {0} - похоже, они написаны вручную. Импортировать их как новые заметки?",
                en: "Notes without an Id in the file: {0} - they look hand-written. Import them as new notes?",
                uk: "Нотаток без Id у файлі: {0} - схоже, їх написано вручну. Імпортувати їх як нові нотатки?",
                de: "Notizen ohne Id in der Datei: {0} - offenbar von Hand geschrieben. Als neue Notizen importieren?",
                it: "Note senza Id nel file: {0} - sembrano scritte a mano. Importarle come nuove note?",
                es: "Notas sin Id en el archivo: {0} - parecen escritas a mano. ¿Importarlas como notas nuevas?",
                fr: "Notes sans Id dans le fichier : {0} - elles semblent écrites à la main. Les importer comme nouvelles notes ?",
                pt: "Notas sem Id no arquivo: {0} - parecem escritas à mão. Importá-las como notas novas?",
                ar: "ملاحظات بلا Id في الملف: {0} - يبدو أنها كُتبت يدويًا. هل تريد استيرادها كملاحظات جديدة؟",
                hi: "फ़ाइल में बिना Id वाले नोट्स: {0} - लगता है ये हाथ से लिखे गए हैं। क्या इन्हें नए नोट्स के रूप में आयात करें?",
                bn: "ফাইলে Id ছাড়া নোট: {0} - মনে হচ্ছে এগুলো হাতে লেখা। নতুন নোট হিসেবে আমদানি করবেন?",
                ur: "فائل میں بغیر Id کے نوٹس: {0} - لگتا ہے یہ ہاتھ سے لکھے گئے ہیں۔ کیا انہیں نئے نوٹس کے طور پر امپورٹ کریں؟",
                zh: "文件中没有 Id 的笔记：{0} 条 - 似乎是手动编写的。要作为新笔记导入吗？");

            Add("Найдено заметок: {0}, записей истории: {1}.",
                en: "Found notes: {0}, history entries: {1}.", uk: "Знайдено нотаток: {0}, записів історії: {1}.",
                de: "Gefundene Notizen: {0}, Verlaufseinträge: {1}.", it: "Note trovate: {0}, voci della cronologia: {1}.",
                es: "Notas encontradas: {0}, entradas del historial: {1}.", fr: "Notes trouvées : {0}, entrées d'historique : {1}.",
                pt: "Notas encontradas: {0}, entradas do histórico: {1}.", ar: "الملاحظات الموجودة: {0}، إدخالات السجل: {1}.",
                hi: "मिले नोट्स: {0}, इतिहास प्रविष्टियाँ: {1}।", bn: "পাওয়া নোট: {0}, ইতিহাসের এন্ট্রি: {1}।",
                ur: "ملے نوٹس: {0}، ہسٹری اندراجات: {1}۔", zh: "找到笔记：{0}，历史记录：{1}。");

            Add("Быстрые заметки выключены - заметки из файла будут пропущены.",
                en: "Quick notes are off - the notes in the file will be skipped.",
                uk: "Швидкі нотатки вимкнено - нотатки з файлу буде пропущено.",
                de: "Schnellnotizen sind aus - die Notizen aus der Datei werden übersprungen.",
                it: "Le note rapide sono disattivate - le note del file verranno saltate.",
                es: "Las notas rápidas están desactivadas - se omitirán las notas del archivo.",
                fr: "Les notes rapides sont désactivées - les notes du fichier seront ignorées.",
                pt: "As notas rápidas estão desativadas - as notas do arquivo serão ignoradas.",
                ar: "الملاحظات السريعة متوقفة - سيتم تخطي الملاحظات الموجودة في الملف.",
                hi: "त्वरित नोट्स बंद हैं - फ़ाइल के नोट्स छोड़ दिए जाएँगे।",
                bn: "দ্রুত নোট বন্ধ - ফাইলের নোটগুলো বাদ দেওয়া হবে।",
                ur: "فوری نوٹس بند ہیں - فائل کے نوٹس چھوڑ دیے جائیں گے۔",
                zh: "快速笔记已关闭 - 将跳过文件中的笔记。");

            Add("История буфера выключена - записи истории будут пропущены.",
                en: "Clipboard history is off - the history entries will be skipped.",
                uk: "Історію буфера вимкнено - записи історії буде пропущено.",
                de: "Der Zwischenablage-Verlauf ist aus - die Verlaufseinträge werden übersprungen.",
                it: "La cronologia appunti è disattivata - le voci della cronologia verranno saltate.",
                es: "El historial del portapapeles está desactivado - se omitirán las entradas del historial.",
                fr: "L'historique du presse-papiers est désactivé - les entrées d'historique seront ignorées.",
                pt: "O histórico da área de transferência está desativado - as entradas do histórico serão ignoradas.",
                ar: "سجل الحافظة متوقف - سيتم تخطي إدخالات السجل.",
                hi: "क्लिपबोर्ड इतिहास बंद है - इतिहास प्रविष्टियाँ छोड़ दी जाएँगी।",
                bn: "ক্লিপবোর্ড ইতিহাস বন্ধ - ইতিহাসের এন্ট্রিগুলো বাদ দেওয়া হবে।",
                ur: "کلپ بورڈ ہسٹری بند ہے - ہسٹری اندراجات چھوڑ دیے جائیں گے۔",
                zh: "剪贴板历史已关闭 - 将跳过历史记录。");

            Add("Будет добавлено: заметок {0}, записей истории {1}.",
                en: "To be added: {0} notes, {1} history entries.", uk: "Буде додано: нотаток {0}, записів історії {1}.",
                de: "Wird hinzugefügt: {0} Notizen, {1} Verlaufseinträge.", it: "Da aggiungere: {0} note, {1} voci della cronologia.",
                es: "Se añadirán: {0} notas, {1} entradas del historial.", fr: "À ajouter : {0} notes, {1} entrées d'historique.",
                pt: "Serão adicionadas: {0} notas, {1} entradas do histórico.", ar: "ستتم الإضافة: {0} ملاحظات، {1} إدخالات سجل.",
                hi: "जोड़े जाएँगे: {0} नोट्स, {1} इतिहास प्रविष्टियाँ।", bn: "যোগ করা হবে: {0}টি নোট, {1}টি ইতিহাসের এন্ট্রি।",
                ur: "شامل کیے جائیں گے: {0} نوٹس، {1} ہسٹری اندراجات۔", zh: "将添加：{0} 条笔记，{1} 条历史记录。");

            Add("Будет обновлено заметок (в файле более новая версия): {0}.",
                en: "Notes to be updated (the file has a newer version): {0}.",
                uk: "Буде оновлено нотаток (у файлі новіша версія): {0}.",
                de: "Zu aktualisierende Notizen (die Datei enthält eine neuere Version): {0}.",
                it: "Note da aggiornare (il file ha una versione più recente): {0}.",
                es: "Notas que se actualizarán (el archivo tiene una versión más reciente): {0}.",
                fr: "Notes à mettre à jour (le fichier en contient une version plus récente) : {0}.",
                pt: "Notas a atualizar (o arquivo tem uma versão mais recente): {0}.",
                ar: "ملاحظات سيتم تحديثها (في الملف نسخة أحدث): {0}.",
                hi: "अपडेट होने वाले नोट्स (फ़ाइल में नया संस्करण है): {0}।",
                bn: "হালনাগাদ হবে এমন নোট (ফাইলে নতুন সংস্করণ আছে): {0}।",
                ur: "اپ ڈیٹ ہونے والے نوٹس (فائل میں نیا ورژن ہے): {0}۔",
                zh: "将更新的笔记（文件中的版本更新）：{0}。");

            Add("Уже есть: заметок {0}, записей истории {1}.",
                en: "Already here: {0} notes, {1} history entries.", uk: "Уже є: нотаток {0}, записів історії {1}.",
                de: "Bereits vorhanden: {0} Notizen, {1} Verlaufseinträge.", it: "Già presenti: {0} note, {1} voci della cronologia.",
                es: "Ya existentes: {0} notas, {1} entradas del historial.", fr: "Déjà présents : {0} notes, {1} entrées d'historique.",
                pt: "Já existentes: {0} notas, {1} entradas do histórico.", ar: "موجودة مسبقًا: {0} ملاحظات، {1} إدخالات سجل.",
                hi: "पहले से मौजूद: {0} नोट्स, {1} इतिहास प्रविष्टियाँ।", bn: "আগে থেকেই আছে: {0}টি নোট, {1}টি ইতিহাসের এন্ট্রি।",
                ur: "پہلے سے موجود: {0} نوٹس، {1} ہسٹری اندراجات۔", zh: "已存在：{0} 条笔记，{1} 条历史记录。");

            Add("Не удалось прочитать блоков: {0}.",
                en: "Blocks that could not be read: {0}.", uk: "Не вдалося прочитати блоків: {0}.", de: "Nicht lesbare Blöcke: {0}.",
                it: "Blocchi non leggibili: {0}.", es: "Bloques que no se pudieron leer: {0}.", fr: "Blocs illisibles : {0}.",
                pt: "Blocos que não puderam ser lidos: {0}.", ar: "كتل تعذّرت قراءتها: {0}.", hi: "न पढ़े जा सके ब्लॉक: {0}।",
                bn: "যে ব্লক পড়া যায়নি: {0}।", ur: "جو بلاک پڑھے نہ جا سکے: {0}۔", zh: "无法读取的块：{0}。");

            Add("Импортировать нечего.",
                en: "Nothing to import.", uk: "Імпортувати нічого.", de: "Nichts zu importieren.", it: "Niente da importare.",
                es: "No hay nada que importar.", fr: "Rien à importer.", pt: "Nada para importar.", ar: "لا يوجد ما يُستورد.",
                hi: "आयात करने के लिए कुछ नहीं है।", bn: "আমদানি করার কিছু নেই।", ur: "امپورٹ کرنے کو کچھ نہیں۔",
                zh: "没有可导入的内容。");

            Add("Импорт только добавляет и обновляет - ничего из текущих данных не удаляется. Импортировать?",
                en: "The import only adds and updates - nothing already here is deleted. Import?",
                uk: "Імпорт лише додає й оновлює - нічого з поточних даних не видаляється. Імпортувати?",
                de: "Der Import fügt nur hinzu und aktualisiert - vorhandene Daten werden nicht gelöscht. Importieren?",
                it: "L'importazione aggiunge e aggiorna soltanto - nulla dei dati attuali viene eliminato. Importare?",
                es: "La importación solo añade y actualiza - no se elimina nada de los datos actuales. ¿Importar?",
                fr: "L'importation ne fait qu'ajouter et mettre à jour - rien de l'existant n'est supprimé. Importer ?",
                pt: "A importação apenas adiciona e atualiza - nada dos dados atuais é excluído. Importar?",
                ar: "الاستيراد يضيف ويحدّث فقط - لا يُحذف شيء من البيانات الحالية. هل تريد الاستيراد؟",
                hi: "आयात केवल जोड़ता और अपडेट करता है - मौजूदा डेटा में से कुछ भी नहीं हटता। आयात करें?",
                bn: "আমদানি শুধু যোগ ও হালনাগাদ করে - বর্তমান ডেটা থেকে কিছুই মোছে না। আমদানি করবেন?",
                ur: "امپورٹ صرف شامل اور اپ ڈیٹ کرتا ہے - موجودہ ڈیٹا میں سے کچھ حذف نہیں ہوتا۔ امپورٹ کریں؟",
                zh: "导入只会添加和更新 - 不会删除现有数据。要导入吗？");

            Add("Импорт завершён: добавлено заметок {0}, обновлено {1}, добавлено записей истории {2}.",
                en: "Import finished: {0} notes added, {1} updated, {2} history entries added.",
                uk: "Імпорт завершено: додано нотаток {0}, оновлено {1}, додано записів історії {2}.",
                de: "Import abgeschlossen: {0} Notizen hinzugefügt, {1} aktualisiert, {2} Verlaufseinträge hinzugefügt.",
                it: "Importazione completata: {0} note aggiunte, {1} aggiornate, {2} voci della cronologia aggiunte.",
                es: "Importación completada: {0} notas añadidas, {1} actualizadas, {2} entradas del historial añadidas.",
                fr: "Importation terminée : {0} notes ajoutées, {1} mises à jour, {2} entrées d'historique ajoutées.",
                pt: "Importação concluída: {0} notas adicionadas, {1} atualizadas, {2} entradas do histórico adicionadas.",
                ar: "اكتمل الاستيراد: أُضيفت {0} ملاحظات، وحُدّثت {1}، وأُضيفت {2} إدخالات سجل.",
                hi: "आयात पूरा: {0} नोट्स जोड़े गए, {1} अपडेट हुए, {2} इतिहास प्रविष्टियाँ जोड़ी गईं।",
                bn: "আমদানি সম্পন্ন: {0}টি নোট যোগ, {1}টি হালনাগাদ, {2}টি ইতিহাসের এন্ট্রি যোগ হয়েছে।",
                ur: "امپورٹ مکمل: {0} نوٹس شامل، {1} اپ ڈیٹ، {2} ہسٹری اندراجات شامل ہوئے۔",
                zh: "导入完成：添加 {0} 条笔记，更新 {1} 条，添加 {2} 条历史记录。");

            Add("Файл записан другой версией формата обмена CyrFlip. Обновите CyrFlip, чтобы его импортировать.",
                en: "The file was written in another version of the CyrFlip exchange format. Update CyrFlip to import it.",
                uk: "Файл записано іншою версією формату обміну CyrFlip. Оновіть CyrFlip, щоб імпортувати його.",
                de: "Die Datei wurde in einer anderen Version des CyrFlip-Austauschformats geschrieben. Aktualisieren Sie CyrFlip, um sie zu importieren.",
                it: "Il file è stato scritto con un'altra versione del formato di scambio di CyrFlip. Aggiorna CyrFlip per importarlo.",
                es: "El archivo se escribió con otra versión del formato de intercambio de CyrFlip. Actualiza CyrFlip para importarlo.",
                fr: "Le fichier a été écrit dans une autre version du format d'échange de CyrFlip. Mettez CyrFlip à jour pour l'importer.",
                pt: "O arquivo foi gravado em outra versão do formato de troca do CyrFlip. Atualize o CyrFlip para importá-lo.",
                ar: "كُتب الملف بإصدار آخر من تنسيق تبادل CyrFlip. حدِّث CyrFlip لاستيراده.",
                hi: "फ़ाइल CyrFlip एक्सचेंज फ़ॉर्मेट के किसी दूसरे संस्करण में लिखी गई है। इसे आयात करने के लिए CyrFlip अपडेट करें।",
                bn: "ফাইলটি CyrFlip এক্সচেঞ্জ ফরম্যাটের অন্য সংস্করণে লেখা। এটি আমদানি করতে CyrFlip হালনাগাদ করুন।",
                ur: "فائل CyrFlip ایکسچینج فارمیٹ کے کسی دوسرے ورژن میں لکھی گئی ہے۔ اسے امپورٹ کرنے کے لیے CyrFlip اپ ڈیٹ کریں۔",
                zh: "该文件使用其他版本的 CyrFlip 交换格式写入。请更新 CyrFlip 后再导入。");

            Add("Файл больше {0} МБ - такой файл не импортируется.",
                en: "The file is larger than {0} MB - such a file is not imported.",
                uk: "Файл більший за {0} МБ - такий файл не імпортується.",
                de: "Die Datei ist größer als {0} MB - eine solche Datei wird nicht importiert.",
                it: "Il file supera {0} MB - un file così non viene importato.",
                es: "El archivo supera {0} MB - un archivo así no se importa.",
                fr: "Le fichier dépasse {0} Mo - un tel fichier n'est pas importé.",
                pt: "O arquivo tem mais de {0} MB - um arquivo assim não é importado.",
                ar: "حجم الملف أكبر من {0} ميغابايت - لا يُستورد مثل هذا الملف.",
                hi: "फ़ाइल {0} MB से बड़ी है - ऐसी फ़ाइल आयात नहीं होती।",
                bn: "ফাইলটি {0} MB-এর বেশি - এমন ফাইল আমদানি হয় না।",
                ur: "فائل {0} MB سے بڑی ہے - ایسی فائل امپورٹ نہیں ہوتی۔",
                zh: "文件大于 {0} MB - 不会导入这样的文件。");

            Add("Не найден маркер «{0}» - это не файл обмена CyrFlip.",
                en: "The marker “{0}” was not found - this is not a CyrFlip exchange file.",
                uk: "Не знайдено маркер «{0}» - це не файл обміну CyrFlip.",
                de: "Die Markierung „{0}“ wurde nicht gefunden - dies ist keine CyrFlip-Austauschdatei.",
                it: "Il marcatore «{0}» non è stato trovato - questo non è un file di scambio CyrFlip.",
                es: "No se encontró el marcador «{0}» - no es un archivo de intercambio de CyrFlip.",
                fr: "Le repère « {0} » est introuvable - ce n'est pas un fichier d'échange CyrFlip.",
                pt: "O marcador «{0}» não foi encontrado - este não é um arquivo de troca do CyrFlip.",
                ar: "لم يُعثر على العلامة «{0}» - هذا ليس ملف تبادل CyrFlip.",
                hi: "मार्कर «{0}» नहीं मिला - यह CyrFlip एक्सचेंज फ़ाइल नहीं है।",
                bn: "«{0}» চিহ্নটি পাওয়া যায়নি - এটি CyrFlip এক্সচেঞ্জ ফাইল নয়।",
                ur: "مارکر «{0}» نہیں ملا - یہ CyrFlip ایکسچینج فائل نہیں ہے۔",
                zh: "未找到标记“{0}” - 这不是 CyrFlip 交换文件。");

            Add("Строка {0}: {1}",
                en: "Line {0}: {1}", uk: "Рядок {0}: {1}", de: "Zeile {0}: {1}", it: "Riga {0}: {1}", es: "Línea {0}: {1}",
                fr: "Ligne {0} : {1}", pt: "Linha {0}: {1}", ar: "السطر {0}: {1}", hi: "पंक्ति {0}: {1}", bn: "লাইন {0}: {1}",
                ur: "سطر {0}: {1}", zh: "第 {0} 行：{1}");

            Add("и ещё {0}",
                en: "and {0} more", uk: "і ще {0}", de: "und {0} weitere", it: "e altri {0}", es: "y {0} más",
                fr: "et {0} de plus", pt: "e mais {0}", ar: "و{0} أخرى", hi: "और {0}", bn: "আরও {0}টি",
                ur: "اور {0} مزید", zh: "还有 {0} 个");

            Add("у блока нет текста в ограждении из обратных кавычек",
                en: "the block has no text inside a backtick fence", uk: "у блоку немає тексту в огородженні зі зворотних лапок",
                de: "der Block hat keinen Text in einem Backtick-Zaun", it: "il blocco non ha testo racchiuso tra backtick",
                es: "el bloque no tiene texto dentro de una valla de comillas invertidas", fr: "le bloc n'a pas de texte entre accents graves",
                pt: "o bloco não tem texto dentro de uma cerca de crases", ar: "لا تحتوي الكتلة على نص داخل سياج من علامات `",
                hi: "ब्लॉक में बैकटिक बाड़ के भीतर कोई टेक्स्ट नहीं है", bn: "ব্লকে ব্যাকটিক বেড়ার ভেতরে কোনো টেক্সট নেই",
                ur: "بلاک میں بیک ٹک باڑ کے اندر کوئی ٹیکسٹ نہیں", zh: "该块没有位于反引号围栏内的文本");

            Add("ограждение текста не закрыто - остаток файла не прочитан",
                en: "the text fence is never closed - the rest of the file was not read",
                uk: "огородження тексту не закрите - решту файлу не прочитано",
                de: "der Textzaun wird nie geschlossen - der Rest der Datei wurde nicht gelesen",
                it: "il recinto del testo non è chiuso - il resto del file non è stato letto",
                es: "la valla del texto no se cierra - el resto del archivo no se leyó",
                fr: "la clôture du texte n'est jamais fermée - le reste du fichier n'a pas été lu",
                pt: "a cerca do texto não foi fechada - o restante do arquivo não foi lido",
                ar: "سياج النص غير مغلق - لم تُقرأ بقية الملف",
                hi: "टेक्स्ट की बाड़ बंद नहीं है - फ़ाइल का बाकी हिस्सा पढ़ा नहीं गया",
                bn: "টেক্সটের বেড়া বন্ধ হয়নি - ফাইলের বাকি অংশ পড়া হয়নি",
                ur: "ٹیکسٹ کی باڑ بند نہیں - فائل کا باقی حصہ پڑھا نہیں گیا",
                zh: "文本围栏未闭合 - 文件其余部分未读取");

            Add("поле «{0}» отсутствует или записано неверно",
                en: "the “{0}” field is missing or malformed", uk: "поле «{0}» відсутнє або записане неправильно",
                de: "das Feld „{0}“ fehlt oder ist fehlerhaft", it: "il campo «{0}» manca o non è valido",
                es: "el campo «{0}» falta o es incorrecto", fr: "le champ « {0} » est absent ou mal formé",
                pt: "o campo «{0}» está ausente ou incorreto", ar: "الحقل «{0}» مفقود أو مكتوب بشكل غير صحيح",
                hi: "फ़ील्ड «{0}» नहीं है या गलत लिखा है", bn: "«{0}» ফিল্ডটি নেই বা ভুলভাবে লেখা",
                ur: "فیلڈ «{0}» موجود نہیں یا غلط لکھی گئی ہے", zh: "字段“{0}”缺失或格式错误");

            Add("текст не совпадает со своим Id - он изменён или повреждён",
                en: "the text does not match its Id - it was changed or damaged",
                uk: "текст не збігається зі своїм Id - його змінено або пошкоджено",
                de: "der Text passt nicht zu seiner Id - er wurde geändert oder beschädigt",
                it: "il testo non corrisponde al suo Id - è stato modificato o danneggiato",
                es: "el texto no coincide con su Id - se modificó o está dañado",
                fr: "le texte ne correspond pas à son Id - il a été modifié ou endommagé",
                pt: "o texto não corresponde ao seu Id - foi alterado ou danificado",
                ar: "النص لا يطابق الـ Id الخاص به - تم تغييره أو تلف",
                hi: "टेक्स्ट अपनी Id से मेल नहीं खाता - वह बदला गया है या खराब है",
                bn: "টেক্সট তার Id-এর সঙ্গে মেলে না - এটি বদলানো বা ক্ষতিগ্রস্ত",
                ur: "ٹیکسٹ اپنی Id سے میل نہیں کھاتا - اسے بدلا گیا ہے یا خراب ہے",
                zh: "文本与其 Id 不符 - 已被修改或损坏");

            Add("текст больше допустимого размера",
                en: "the text is larger than allowed", uk: "текст більший за допустимий розмір", de: "der Text ist größer als erlaubt",
                it: "il testo supera la dimensione consentita", es: "el texto supera el tamaño permitido",
                fr: "le texte dépasse la taille autorisée", pt: "o texto excede o tamanho permitido", ar: "النص أكبر من الحجم المسموح",
                hi: "टेक्स्ट अनुमत आकार से बड़ा है", bn: "টেক্সট অনুমোদিত আকারের চেয়ে বড়", ur: "ٹیکسٹ اجازت شدہ سائز سے بڑا ہے",
                zh: "文本超过允许的大小");

            Add("строка метаданных длиннее {0} символов",
                en: "a metadata line is longer than {0} characters", uk: "рядок метаданих довший за {0} символів",
                de: "eine Metadatenzeile ist länger als {0} Zeichen", it: "una riga di metadati supera {0} caratteri",
                es: "una línea de metadatos supera {0} caracteres", fr: "une ligne de métadonnées dépasse {0} caractères",
                pt: "uma linha de metadados tem mais de {0} caracteres", ar: "سطر بيانات وصفية أطول من {0} حرفًا",
                hi: "मेटाडेटा पंक्ति {0} वर्णों से लंबी है", bn: "মেটাডেটার লাইন {0} অক্ষরের চেয়ে দীর্ঘ",
                ur: "میٹا ڈیٹا کی سطر {0} حروف سے لمبی ہے", zh: "元数据行超过 {0} 个字符");

            Add("больше {0} объектов - остальные не импортированы",
                en: "more than {0} objects - the rest were not imported", uk: "понад {0} об'єктів - решту не імпортовано",
                de: "mehr als {0} Objekte - der Rest wurde nicht importiert", it: "più di {0} oggetti - i restanti non sono stati importati",
                es: "más de {0} objetos - el resto no se importó", fr: "plus de {0} objets - le reste n'a pas été importé",
                pt: "mais de {0} objetos - o restante não foi importado", ar: "أكثر من {0} عنصر - لم يُستورد الباقي",
                hi: "{0} से अधिक ऑब्जेक्ट - बाकी आयात नहीं हुए", bn: "{0}টির বেশি অবজেক্ট - বাকিগুলো আমদানি হয়নি",
                ur: "{0} سے زیادہ آبجیکٹس - باقی امپورٹ نہیں ہوئے", zh: "超过 {0} 个对象 - 其余未导入");

            // BusyDialog (ticket S0030 HT-2): shown while an import, an export or the notes replay runs on the pool.
            Add("Подождите, CyrFlip обрабатывает данные..",
                en: "Please wait, CyrFlip is processing the data..", uk: "Зачекайте, CyrFlip обробляє дані..",
                de: "Bitte warten, CyrFlip verarbeitet die Daten..", it: "Attendere, CyrFlip sta elaborando i dati..",
                es: "Espere, CyrFlip está procesando los datos..", fr: "Veuillez patienter, CyrFlip traite les données..",
                pt: "Aguarde, o CyrFlip está processando os dados..", ar: "يرجى الانتظار، يعالج CyrFlip البيانات..",
                hi: "कृपया प्रतीक्षा करें, CyrFlip डेटा संसाधित कर रहा है..", bn: "অনুগ্রহ করে অপেক্ষা করুন, CyrFlip ডেটা প্রক্রিয়া করছে..",
                ur: "براہ کرم انتظار کریں، CyrFlip ڈیٹا پر کارروائی کر رہا ہے..", zh: "请稍候，CyrFlip 正在处理数据..");
        }
    }
}
