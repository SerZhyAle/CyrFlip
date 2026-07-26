namespace CyrFlip
{
    /// <summary>The scenario launcher (absorbed OneClickRunner): tab, dialogs, tray submenu, Jump List, errors.</summary>
    internal static partial class Localization
    {
        private static void AddLauncherStrings()
        {
            // ---- Tab, master switch, table ----
            Add("Быстрый запуск",
                en: "Quick launch", uk: "Швидкий запуск", de: "Schnellstart", it: "Avvio rapido",
                es: "Inicio rápido", fr: "Lancement rapide", pt: "Início rápido", ar: "تشغيل سريع",
                hi: "त्वरित लॉन्च", bn: "দ্রুত চালু", ur: "فوری لانچ", zh: "快速启动");

            Add("Ваши программы, скрипты и загрузки yt-dlp: запуск из меню в трее, из этой таблицы, по глобальной комбинации и из Jump List значка CyrFlip на панели задач. Пока выключатель снят, CyrFlip ведёт себя как раньше — ни пункта в трее, ни задач в Jump List.",
                en: "Your programs, scripts and yt-dlp downloads: run them from the tray menu, from this table, with a global shortcut, or from the Jump List of the CyrFlip taskbar icon. While the switch is off, CyrFlip behaves exactly as before — no tray entry and no Jump List tasks.",
                uk: "Ваші програми, скрипти та завантаження yt-dlp: запуск із меню в треї, з цієї таблиці, за глобальною комбінацією та з Jump List значка CyrFlip на панелі завдань. Поки перемикач вимкнено, CyrFlip поводиться як раніше — ні пункту в треї, ні завдань у Jump List.",
                de: "Ihre Programme, Skripte und yt-dlp-Downloads: Start über das Tray-Menü, diese Tabelle, ein globales Tastenkürzel oder die Jump List des CyrFlip-Taskleistensymbols. Solange der Schalter aus ist, verhält sich CyrFlip wie zuvor — kein Tray-Eintrag, keine Jump-List-Aufgaben.",
                it: "I tuoi programmi, script e download yt-dlp: avvio dal menu nella tray, da questa tabella, con una scorciatoia globale o dalla Jump List dell'icona CyrFlip sulla barra delle applicazioni. Finché l'interruttore è disattivato, CyrFlip si comporta come prima: nessuna voce nella tray e nessuna attività nella Jump List.",
                es: "Sus programas, scripts y descargas de yt-dlp: ejecútelos desde el menú de la bandeja, desde esta tabla, con un atajo global o desde la Jump List del icono de CyrFlip en la barra de tareas. Mientras el interruptor esté apagado, CyrFlip se comporta como antes: sin entrada en la bandeja ni tareas en la Jump List.",
                fr: "Vos programmes, scripts et téléchargements yt-dlp : lancez-les depuis le menu de la zone de notification, depuis ce tableau, avec un raccourci global ou depuis la Jump List de l'icône CyrFlip dans la barre des tâches. Tant que l'interrupteur est désactivé, CyrFlip se comporte comme avant — aucune entrée dans la zone de notification, aucune tâche dans la Jump List.",
                pt: "Seus programas, scripts e downloads do yt-dlp: execute-os pelo menu da bandeja, por esta tabela, com um atalho global ou pela Jump List do ícone do CyrFlip na barra de tarefas. Enquanto o interruptor estiver desligado, o CyrFlip se comporta como antes — sem item na bandeja e sem tarefas na Jump List.",
                ar: "برامجك وسكربتاتك وتنزيلات yt-dlp: شغّلها من قائمة صينية النظام، أو من هذا الجدول، أو باختصار عام، أو من Jump List لأيقونة CyrFlip في شريط المهام. ما دام المفتاح مطفأً يتصرف CyrFlip كما كان — لا عنصر في الصينية ولا مهام في Jump List.",
                hi: "आपके प्रोग्राम, स्क्रिप्ट और yt-dlp डाउनलोड: इन्हें ट्रे मेनू से, इस तालिका से, ग्लोबल शॉर्टकट से या टास्कबार पर CyrFlip आइकन की Jump List से चलाएँ। जब तक स्विच बंद है, CyrFlip पहले जैसा ही व्यवहार करता है — न ट्रे में कोई पंक्ति, न Jump List में कार्य।",
                bn: "আপনার প্রোগ্রাম, স্ক্রিপ্ট ও yt-dlp ডাউনলোড: ট্রে মেনু থেকে, এই টেবিল থেকে, গ্লোবাল শর্টকাট দিয়ে বা টাস্কবারে CyrFlip আইকনের Jump List থেকে চালান। সুইচ বন্ধ থাকা পর্যন্ত CyrFlip আগের মতোই আচরণ করে — ট্রেতে কোনো আইটেম নেই, Jump List-এ কোনো কাজ নেই।",
                ur: "آپ کے پروگرام، اسکرپٹس اور yt-dlp ڈاؤن لوڈز: انہیں ٹرے مینو سے، اس جدول سے، عالمی شارٹ کٹ سے یا ٹاسک بار پر CyrFlip آئیکن کی Jump List سے چلائیں۔ جب تک سوئچ بند ہے، CyrFlip پہلے جیسا ہی رہتا ہے — نہ ٹرے میں کوئی اندراج، نہ Jump List میں کام۔",
                zh: "您的程序、脚本和 yt-dlp 下载：可从托盘菜单、此表格、全局快捷键或任务栏 CyrFlip 图标的 Jump List 启动。开关关闭时，CyrFlip 的行为与以前完全相同——托盘中没有该项，Jump List 中也没有任务。");

            Add("Включить быстрый запуск",
                en: "Enable quick launch", uk: "Увімкнути швидкий запуск", de: "Schnellstart aktivieren",
                it: "Abilita l'avvio rapido", es: "Activar el inicio rápido", fr: "Activer le lancement rapide",
                pt: "Ativar o início rápido", ar: "تفعيل التشغيل السريع", hi: "त्वरित लॉन्च चालू करें",
                bn: "দ্রুত চালু সক্রিয় করুন", ur: "فوری لانچ فعال کریں", zh: "启用快速启动");

            Add("Добавляет подменю сценариев в меню трея и задачи в Jump List панели задач (правый клик по значку CyrFlip на панели задач). Сценарии хранятся по одному XML-файлу и не удаляются при выключении.",
                en: "Adds a scenario submenu to the tray menu and tasks to the taskbar Jump List (right-click the CyrFlip taskbar icon). Scenarios are stored one XML file each and are kept when the feature is switched off.",
                uk: "Додає підменю сценаріїв у меню трея та завдання в Jump List панелі завдань (правий клік по значку CyrFlip на панелі завдань). Сценарії зберігаються по одному XML-файлу і не видаляються при вимкненні.",
                de: "Fügt dem Tray-Menü ein Szenario-Untermenü und der Jump List der Taskleiste Aufgaben hinzu (Rechtsklick auf das CyrFlip-Taskleistensymbol). Szenarien werden je in einer XML-Datei gespeichert und beim Abschalten nicht gelöscht.",
                it: "Aggiunge un sottomenu di scenari al menu della tray e attività alla Jump List della barra delle applicazioni (clic destro sull'icona CyrFlip). Gli scenari sono salvati in un file XML ciascuno e non vengono eliminati alla disattivazione.",
                es: "Agrega un submenú de escenarios al menú de la bandeja y tareas a la Jump List de la barra de tareas (clic derecho en el icono de CyrFlip). Cada escenario se guarda en un archivo XML y no se elimina al desactivar la función.",
                fr: "Ajoute un sous-menu de scénarios au menu de la zone de notification et des tâches à la Jump List de la barre des tâches (clic droit sur l'icône CyrFlip). Chaque scénario est stocké dans un fichier XML et n'est pas supprimé à la désactivation.",
                pt: "Adiciona um submenu de cenários ao menu da bandeja e tarefas à Jump List da barra de tarefas (clique com o botão direito no ícone do CyrFlip). Cada cenário fica em um arquivo XML e não é excluído ao desligar o recurso.",
                ar: "يضيف قائمة فرعية للسيناريوهات إلى قائمة الصينية ومهامَّ إلى Jump List في شريط المهام (انقر بزر الفأرة الأيمن على أيقونة CyrFlip). يُحفظ كل سيناريو في ملف XML ولا يُحذف عند إيقاف الميزة.",
                hi: "ट्रे मेनू में परिदृश्यों का उपमेनू और टास्कबार की Jump List में कार्य जोड़ता है (टास्कबार पर CyrFlip आइकन पर राइट-क्लिक करें)। हर परिदृश्य एक XML फ़ाइल में सहेजा जाता है और सुविधा बंद करने पर हटाया नहीं जाता।",
                bn: "ট্রে মেনুতে দৃশ্যপটের সাবমেনু এবং টাস্কবারের Jump List-এ কাজ যোগ করে (টাস্কবারে CyrFlip আইকনে ডান ক্লিক)। প্রতিটি দৃশ্যপট একটি XML ফাইলে থাকে এবং বৈশিষ্ট্য বন্ধ করলেও মুছে যায় না।",
                ur: "ٹرے مینو میں منظرناموں کا ذیلی مینو اور ٹاسک بار کی Jump List میں کام شامل کرتا ہے (ٹاسک بار پر CyrFlip آئیکن پر دایاں کلک)۔ ہر منظرنامہ ایک XML فائل میں محفوظ ہوتا ہے اور فیچر بند کرنے پر حذف نہیں ہوتا۔",
                zh: "在托盘菜单中添加场景子菜单，并在任务栏 Jump List 中添加任务（右键点击任务栏上的 CyrFlip 图标）。每个场景保存为一个 XML 文件，关闭该功能时不会被删除。");

            Add("Значок на панели задач: левый клик — список сценариев, правый — Jump List Windows.",
                en: "Taskbar button: left-click for the scenario list, right-click for the Windows Jump List.",
                uk: "Значок на панелі завдань: лівий клік — список сценаріїв, правий — Jump List Windows.",
                de: "Taskleistensymbol: Linksklick zeigt die Szenarioliste, Rechtsklick die Windows-Jump-List.",
                it: "Icona nella barra delle applicazioni: clic sinistro per l'elenco degli scenari, clic destro per la Jump List di Windows.",
                es: "Icono en la barra de tareas: clic izquierdo para la lista de escenarios, clic derecho para la Jump List de Windows.",
                fr: "Icône dans la barre des tâches : clic gauche pour la liste des scénarios, clic droit pour la Jump List de Windows.",
                pt: "Ícone na barra de tarefas: clique com o botão esquerdo abre a lista de cenários, com o direito abre a Jump List do Windows.",
                ar: "أيقونة شريط المهام: النقر بالزر الأيسر يعرض قائمة السيناريوهات، والنقر بالزر الأيمن يفتح Jump List في ويندوز.",
                hi: "टास्कबार आइकन: बायाँ क्लिक परिदृश्यों की सूची दिखाता है, दायाँ क्लिक Windows की Jump List खोलता है।",
                bn: "টাস্কবার আইকন: বাঁ ক্লিকে দৃশ্যপটের তালিকা, ডান ক্লিকে Windows-এর Jump List।",
                ur: "ٹاسک بار آئیکن: بائیں کلک سے منظرناموں کی فہرست، دائیں کلک سے ونڈوز کی Jump List۔",
                zh: "任务栏图标：左键点击显示场景列表，右键点击打开 Windows 的 Jump List。");

            Add("Поиск:",
                en: "Search:", uk: "Пошук:", de: "Suche:", it: "Cerca:", es: "Buscar:", fr: "Recherche :",
                pt: "Pesquisar:", ar: "بحث:", hi: "खोज:", bn: "খুঁজুন:", ur: "تلاش:", zh: "搜索：");

            Add("Имя",
                en: "Name", uk: "Ім'я", de: "Name", it: "Nome", es: "Nombre", fr: "Nom",
                pt: "Nome", ar: "الاسم", hi: "नाम", bn: "নাম", ur: "نام", zh: "名称");

            Add("Путь",
                en: "Path", uk: "Шлях", de: "Pfad", it: "Percorso", es: "Ruta", fr: "Chemin",
                pt: "Caminho", ar: "المسار", hi: "पथ", bn: "পথ", ur: "راستہ", zh: "路径");

            Add("Аргументы",
                en: "Arguments", uk: "Аргументи", de: "Argumente", it: "Argomenti", es: "Argumentos",
                fr: "Arguments", pt: "Argumentos", ar: "الوسائط", hi: "आर्ग्युमेंट", bn: "আর্গুমেন্ট",
                ur: "آرگومنٹس", zh: "参数");

            Add("Рабочая папка",
                en: "Working folder", uk: "Робоча папка", de: "Arbeitsordner", it: "Cartella di lavoro",
                es: "Carpeta de trabajo", fr: "Dossier de travail", pt: "Pasta de trabalho",
                ar: "مجلد العمل", hi: "कार्य फ़ोल्डर", bn: "কার্য ফোল্ডার", ur: "ورکنگ فولڈر", zh: "工作文件夹");

            Add("Админ",
                en: "Admin", uk: "Адмін", de: "Admin", it: "Admin", es: "Admin", fr: "Admin",
                pt: "Admin", ar: "مسؤول", hi: "एडमिन", bn: "অ্যাডমিন", ur: "ایڈمن", zh: "管理员");

            Add("Не прочитано файлов: {0}",
                en: "Unreadable files: {0}", uk: "Не прочитано файлів: {0}", de: "Nicht lesbare Dateien: {0}",
                it: "File non leggibili: {0}", es: "Archivos ilegibles: {0}", fr: "Fichiers illisibles : {0}",
                pt: "Arquivos ilegíveis: {0}", ar: "ملفات تعذّرت قراءتها: {0}", hi: "न पढ़ी गई फ़ाइलें: {0}",
                bn: "পড়া যায়নি এমন ফাইল: {0}", ur: "ناقابلِ مطالعہ فائلیں: {0}", zh: "无法读取的文件：{0}");

            // ---- Buttons and row actions ----
            Add("Добавить...",
                en: "Add...", uk: "Додати...", de: "Hinzufügen...", it: "Aggiungi...", es: "Agregar...",
                fr: "Ajouter...", pt: "Adicionar...", ar: "إضافة...", hi: "जोड़ें...", bn: "যোগ করুন...",
                ur: "شامل کریں...", zh: "添加...");

            Add("Запустить",
                en: "Run", uk: "Запустити", de: "Ausführen", it: "Esegui", es: "Ejecutar", fr: "Exécuter",
                pt: "Executar", ar: "تشغيل", hi: "चलाएँ", bn: "চালান", ur: "چلائیں", zh: "运行");

            Add("Клонировать",
                en: "Clone", uk: "Клонувати", de: "Duplizieren", it: "Clona", es: "Clonar", fr: "Cloner",
                pt: "Clonar", ar: "استنساخ", hi: "क्लोन करें", bn: "ক্লোন করুন", ur: "کلون کریں", zh: "克隆");

            Add("Экспорт...",
                en: "Export...", uk: "Експорт...", de: "Exportieren...", it: "Esporta...", es: "Exportar...",
                fr: "Exporter...", pt: "Exportar...", ar: "تصدير...", hi: "निर्यात...", bn: "রপ্তানি...",
                ur: "ایکسپورٹ...", zh: "导出...");

            Add("Импорт...",
                en: "Import...", uk: "Імпорт...", de: "Importieren...", it: "Importa...", es: "Importar...",
                fr: "Importer...", pt: "Importar...", ar: "استيراد...", hi: "आयात...", bn: "আমদানি...",
                ur: "امپورٹ...", zh: "导入...");

            Add("Импорт из OneClickRunner...",
                en: "Import from OneClickRunner...", uk: "Імпорт з OneClickRunner...",
                de: "Aus OneClickRunner importieren...", it: "Importa da OneClickRunner...",
                es: "Importar de OneClickRunner...", fr: "Importer depuis OneClickRunner...",
                pt: "Importar do OneClickRunner...", ar: "استيراد من OneClickRunner...",
                hi: "OneClickRunner से आयात करें...", bn: "OneClickRunner থেকে আমদানি...",
                ur: "OneClickRunner سے امپورٹ...", zh: "从 OneClickRunner 导入...");

            Add("Двойной клик или Enter — запуск, F2 — изменение, Delete — удаление. Импорт из OneClickRunner копирует сценарии и никогда не изменяет исходные файлы.",
                en: "Double-click or Enter runs, F2 edits, Delete removes. Importing from OneClickRunner copies the scenarios and never changes the source files.",
                uk: "Подвійний клік або Enter — запуск, F2 — змінення, Delete — видалення. Імпорт з OneClickRunner копіює сценарії і ніколи не змінює вихідні файли.",
                de: "Doppelklick oder Enter startet, F2 bearbeitet, Entf löscht. Der Import aus OneClickRunner kopiert die Szenarien und verändert die Quelldateien nie.",
                it: "Doppio clic o Invio esegue, F2 modifica, Canc elimina. L'importazione da OneClickRunner copia gli scenari e non modifica mai i file di origine.",
                es: "Doble clic o Enter ejecuta, F2 edita, Supr elimina. La importación desde OneClickRunner copia los escenarios y nunca cambia los archivos de origen.",
                fr: "Double-clic ou Entrée exécute, F2 modifie, Suppr supprime. L'import depuis OneClickRunner copie les scénarios et ne modifie jamais les fichiers d'origine.",
                pt: "Clique duplo ou Enter executa, F2 edita, Delete remove. A importação do OneClickRunner copia os cenários e nunca altera os arquivos de origem.",
                ar: "نقرة مزدوجة أو Enter للتشغيل، وF2 للتحرير، وDelete للحذف. الاستيراد من OneClickRunner ينسخ السيناريوهات ولا يغيّر الملفات الأصلية أبداً.",
                hi: "डबल-क्लिक या Enter चलाता है, F2 संपादित करता है, Delete हटाता है। OneClickRunner से आयात परिदृश्यों की प्रतिलिपि बनाता है और स्रोत फ़ाइलों को कभी नहीं बदलता।",
                bn: "ডাবল ক্লিক বা Enter চালায়, F2 সম্পাদনা করে, Delete মুছে দেয়। OneClickRunner থেকে আমদানি দৃশ্যপট কপি করে এবং উৎস ফাইল কখনও বদলায় না।",
                ur: "ڈبل کلک یا Enter چلاتا ہے، F2 ترمیم کرتا ہے، Delete حذف کرتا ہے۔ OneClickRunner سے امپورٹ منظرناموں کی نقل بناتا ہے اور اصل فائلوں کو کبھی نہیں بدلتا۔",
                zh: "双击或 Enter 运行，F2 编辑，Delete 删除。从 OneClickRunner 导入会复制场景，绝不会更改源文件。");

            Add("{0} (копия)",
                en: "{0} (copy)", uk: "{0} (копія)", de: "{0} (Kopie)", it: "{0} (copia)", es: "{0} (copia)",
                fr: "{0} (copie)", pt: "{0} (cópia)", ar: "{0} (نسخة)", hi: "{0} (प्रतिलिपि)",
                bn: "{0} (কপি)", ur: "{0} (نقل)", zh: "{0}（副本）");

            Add("Удалить сценарий «{0}»?",
                en: "Remove the scenario \"{0}\"?", uk: "Видалити сценарій «{0}»?",
                de: "Szenario \"{0}\" löschen?", it: "Rimuovere lo scenario \"{0}\"?",
                es: "¿Eliminar el escenario \"{0}\"?", fr: "Supprimer le scénario « {0} » ?",
                pt: "Remover o cenário \"{0}\"?", ar: "هل تريد حذف السيناريو «{0}»؟",
                hi: "परिदृश्य \"{0}\" हटाएँ?", bn: "দৃশ্যপট \"{0}\" মুছবেন?", ur: "منظرنامہ «{0}» حذف کریں؟",
                zh: "删除场景“{0}”？");

            // ---- Export / import ----
            Add("Экспортировать сценарий в XML",
                en: "Export scenario to XML", uk: "Експортувати сценарій у XML",
                de: "Szenario als XML exportieren", it: "Esporta scenario in XML",
                es: "Exportar escenario a XML", fr: "Exporter le scénario en XML",
                pt: "Exportar cenário para XML", ar: "تصدير السيناريو إلى XML",
                hi: "परिदृश्य को XML में निर्यात करें", bn: "দৃশ্যপট XML-এ রপ্তানি করুন",
                ur: "منظرنامہ XML میں ایکسپورٹ کریں", zh: "将场景导出为 XML");

            Add("Сценарий «{0}» экспортирован.",
                en: "Scenario \"{0}\" exported.", uk: "Сценарій «{0}» експортовано.",
                de: "Szenario \"{0}\" exportiert.", it: "Scenario \"{0}\" esportato.",
                es: "Escenario \"{0}\" exportado.", fr: "Scénario « {0} » exporté.",
                pt: "Cenário \"{0}\" exportado.", ar: "تم تصدير السيناريو «{0}».",
                hi: "परिदृश्य \"{0}\" निर्यात किया गया।", bn: "দৃশ্যপট \"{0}\" রপ্তানি হয়েছে।",
                ur: "منظرنامہ «{0}» ایکسپورٹ ہو گیا۔", zh: "场景“{0}”已导出。");

            Add("Не удалось экспортировать: {0}",
                en: "Export failed: {0}", uk: "Не вдалося експортувати: {0}",
                de: "Export fehlgeschlagen: {0}", it: "Esportazione non riuscita: {0}",
                es: "No se pudo exportar: {0}", fr: "Échec de l'export : {0}",
                pt: "Falha ao exportar: {0}", ar: "فشل التصدير: {0}", hi: "निर्यात विफल: {0}",
                bn: "রপ্তানি ব্যর্থ: {0}", ur: "ایکسپورٹ ناکام: {0}", zh: "导出失败：{0}");

            Add("Выберите XML-файл сценария",
                en: "Select a scenario XML file", uk: "Виберіть XML-файл сценарію",
                de: "Szenario-XML-Datei auswählen", it: "Seleziona un file XML di scenario",
                es: "Seleccione un archivo XML de escenario", fr: "Sélectionnez un fichier XML de scénario",
                pt: "Selecione um arquivo XML de cenário", ar: "اختر ملف XML للسيناريو",
                hi: "परिदृश्य की XML फ़ाइल चुनें", bn: "দৃশ্যপটের XML ফাইল নির্বাচন করুন",
                ur: "منظرنامے کی XML فائل منتخب کریں", zh: "选择场景 XML 文件");

            Add("Не удалось импортировать: {0}",
                en: "Import failed: {0}", uk: "Не вдалося імпортувати: {0}",
                de: "Import fehlgeschlagen: {0}", it: "Importazione non riuscita: {0}",
                es: "No se pudo importar: {0}", fr: "Échec de l'import : {0}",
                pt: "Falha ao importar: {0}", ar: "فشل الاستيراد: {0}", hi: "आयात विफल: {0}",
                bn: "আমদানি ব্যর্থ: {0}", ur: "امپورٹ ناکام: {0}", zh: "导入失败：{0}");

            Add("Импортирован сценарий «{0}».",
                en: "Imported scenario \"{0}\".", uk: "Імпортовано сценарій «{0}».",
                de: "Szenario \"{0}\" importiert.", it: "Scenario \"{0}\" importato.",
                es: "Escenario \"{0}\" importado.", fr: "Scénario « {0} » importé.",
                pt: "Cenário \"{0}\" importado.", ar: "تم استيراد السيناريو «{0}».",
                hi: "परिदृश्य \"{0}\" आयात किया गया।", bn: "দৃশ্যপট \"{0}\" আমদানি হয়েছে।",
                ur: "منظرنامہ «{0}» امپورٹ ہو گیا۔", zh: "已导入场景“{0}”。");

            Add("Его комбинация уже занята, поэтому не перенесена.",
                en: "Its shortcut is already taken, so it was not carried over.",
                uk: "Його сполучення вже зайняте, тому не перенесено.",
                de: "Sein Tastenkürzel ist bereits belegt und wurde daher nicht übernommen.",
                it: "La sua scorciatoia è già in uso, quindi non è stata importata.",
                es: "Su atajo ya está en uso, por lo que no se importó.",
                fr: "Son raccourci est déjà pris, il n'a donc pas été repris.",
                pt: "O atalho dele já está em uso, portanto não foi importado.",
                ar: "اختصاره مستخدم بالفعل، لذلك لم يُنقل.",
                hi: "इसका शॉर्टकट पहले से लिया जा चुका है, इसलिए वह नहीं लाया गया।",
                bn: "এর শর্টকাট আগেই ব্যবহৃত, তাই আনা হয়নি।",
                ur: "اس کا شارٹ کٹ پہلے سے مختص ہے، لہٰذا منتقل نہیں ہوا۔",
                zh: "它的快捷键已被占用，因此未一并导入。");

            Add("Сценарии OneClickRunner не найдены.",
                en: "No OneClickRunner scenarios were found.", uk: "Сценарії OneClickRunner не знайдено.",
                de: "Keine OneClickRunner-Szenarien gefunden.", it: "Nessuno scenario OneClickRunner trovato.",
                es: "No se encontraron escenarios de OneClickRunner.", fr: "Aucun scénario OneClickRunner trouvé.",
                pt: "Nenhum cenário do OneClickRunner foi encontrado.", ar: "لم يُعثر على سيناريوهات OneClickRunner.",
                hi: "OneClickRunner के परिदृश्य नहीं मिले।", bn: "OneClickRunner-এর কোনো দৃশ্যপট পাওয়া যায়নি।",
                ur: "OneClickRunner کے منظرنامے نہیں ملے۔", zh: "未找到 OneClickRunner 场景。");

            // ---- Migration (first enable and the explicit button) ----
            Add("Найдены сценарии OneClickRunner ({0} шт.). Перенести их в CyrFlip? Исходные файлы останутся без изменений.",
                en: "Found OneClickRunner scenarios ({0}). Bring them into CyrFlip? The source files stay untouched.",
                uk: "Знайдено сценарії OneClickRunner ({0} шт.). Перенести їх у CyrFlip? Вихідні файли залишаться без змін.",
                de: "OneClickRunner-Szenarien gefunden ({0}). In CyrFlip übernehmen? Die Quelldateien bleiben unverändert.",
                it: "Trovati scenari OneClickRunner ({0}). Portarli in CyrFlip? I file di origine restano intatti.",
                es: "Se encontraron escenarios de OneClickRunner ({0}). ¿Llevarlos a CyrFlip? Los archivos de origen no se modifican.",
                fr: "Scénarios OneClickRunner trouvés ({0}). Les importer dans CyrFlip ? Les fichiers d'origine restent intacts.",
                pt: "Cenários do OneClickRunner encontrados ({0}). Trazê-los para o CyrFlip? Os arquivos de origem permanecem intactos.",
                ar: "عُثر على سيناريوهات OneClickRunner ({0}). نقلها إلى CyrFlip؟ تبقى الملفات الأصلية دون تغيير.",
                hi: "OneClickRunner के परिदृश्य मिले ({0})। इन्हें CyrFlip में लाएँ? स्रोत फ़ाइलें ज्यों की त्यों रहेंगी।",
                bn: "OneClickRunner-এর দৃশ্যপট পাওয়া গেছে ({0}টি)। এগুলি CyrFlip-এ আনবেন? উৎস ফাইল অপরিবর্তিত থাকবে।",
                ur: "OneClickRunner کے منظرنامے ملے ({0})۔ انہیں CyrFlip میں لائیں؟ اصل فائلیں جوں کی توں رہیں گی۔",
                zh: "发现 OneClickRunner 场景（{0} 个）。要导入 CyrFlip 吗？源文件保持不变。");

            Add("Перенесено сценариев: {0}.",
                en: "Scenarios imported: {0}.", uk: "Перенесено сценаріїв: {0}.",
                de: "Übernommene Szenarien: {0}.", it: "Scenari importati: {0}.",
                es: "Escenarios importados: {0}.", fr: "Scénarios importés : {0}.",
                pt: "Cenários importados: {0}.", ar: "السيناريوهات المنقولة: {0}.",
                hi: "आयातित परिदृश्य: {0}।", bn: "আমদানি করা দৃশ্যপট: {0}টি।",
                ur: "امپورٹ شدہ منظرنامے: {0}۔", zh: "已导入场景：{0} 个。");

            Add("Пропущено повреждённых файлов: {0}.",
                en: "Corrupt files skipped: {0}.", uk: "Пропущено пошкоджених файлів: {0}.",
                de: "Übersprungene defekte Dateien: {0}.", it: "File danneggiati saltati: {0}.",
                es: "Archivos dañados omitidos: {0}.", fr: "Fichiers corrompus ignorés : {0}.",
                pt: "Arquivos corrompidos ignorados: {0}.", ar: "الملفات التالفة المتخطاة: {0}.",
                hi: "छोड़ी गई क्षतिग्रस्त फ़ाइलें: {0}।", bn: "বাদ দেওয়া নষ্ট ফাইল: {0}টি।",
                ur: "چھوڑی گئی خراب فائلیں: {0}۔", zh: "已跳过损坏文件：{0} 个。");

            Add("Из-за совпадения идентификаторов назначены новые: {0}.",
                en: "New ids assigned because of collisions: {0}.", uk: "Через збіг ідентифікаторів призначено нові: {0}.",
                de: "Wegen Kollisionen neue IDs vergeben: {0}.", it: "Nuovi id assegnati per collisioni: {0}.",
                es: "Nuevos id asignados por colisiones: {0}.", fr: "Nouveaux identifiants attribués pour cause de collision : {0}.",
                pt: "Novos ids atribuídos por colisões: {0}.", ar: "معرّفات جديدة بسبب التطابق: {0}.",
                hi: "टकराव के कारण नए id दिए गए: {0}।", bn: "সংঘর্ষের কারণে নতুন আইডি: {0}টি।",
                ur: "ٹکراؤ کی وجہ سے نئے شناختی نمبر: {0}۔", zh: "因冲突而分配新 ID：{0} 个。");

            Add("Калькулятор",
                en: "Calculator", uk: "Калькулятор", de: "Rechner", it: "Calcolatrice", es: "Calculadora",
                fr: "Calculatrice", pt: "Calculadora", ar: "الحاسبة", hi: "कैलकुलेटर", bn: "ক্যালকুলেটর",
                ur: "کیلکولیٹر", zh: "计算器");

            // ---- Tray submenu and Jump List tasks ----
            Add("Управление сценариями...",
                en: "Manage scenarios...", uk: "Керування сценаріями...", de: "Szenarien verwalten...",
                it: "Gestisci scenari...", es: "Administrar escenarios...", fr: "Gérer les scénarios...",
                pt: "Gerenciar cenários...", ar: "إدارة السيناريوهات...", hi: "परिदृश्य प्रबंधित करें...",
                bn: "দৃশ্যপট পরিচালনা...", ur: "منظرناموں کا نظم...", zh: "管理场景...");

            Add("Выход из CyrFlip",
                en: "Exit CyrFlip", uk: "Вихід з CyrFlip", de: "CyrFlip beenden", it: "Esci da CyrFlip",
                es: "Salir de CyrFlip", fr: "Quitter CyrFlip", pt: "Sair do CyrFlip", ar: "إنهاء CyrFlip",
                hi: "CyrFlip से बाहर निकलें", bn: "CyrFlip থেকে প্রস্থান", ur: "CyrFlip بند کریں", zh: "退出 CyrFlip");

            Add("сценарием быстрого запуска",
                en: "a quick-launch scenario", uk: "сценарієм швидкого запуску",
                de: "einem Schnellstart-Szenario", it: "uno scenario di avvio rapido",
                es: "un escenario de inicio rápido", fr: "un scénario de lancement rapide",
                pt: "um cenário de início rápido", ar: "سيناريو تشغيل سريع",
                hi: "त्वरित लॉन्च परिदृश्य द्वारा", bn: "দ্রুত চালুর দৃশ্যপট দ্বারা",
                ur: "فوری لانچ منظرنامے سے", zh: "快速启动场景");

            // ---- Launch errors ----
            Add("Не удалось запустить «{0}»: {1}",
                en: "Couldn't start \"{0}\": {1}", uk: "Не вдалося запустити «{0}»: {1}",
                de: "\"{0}\" konnte nicht gestartet werden: {1}", it: "Impossibile avviare \"{0}\": {1}",
                es: "No se pudo iniciar \"{0}\": {1}", fr: "Impossible de lancer « {0} » : {1}",
                pt: "Não foi possível iniciar \"{0}\": {1}", ar: "تعذّر تشغيل «{0}»: {1}",
                hi: "\"{0}\" शुरू नहीं हो सका: {1}", bn: "\"{0}\" চালু করা যায়নি: {1}",
                ur: "«{0}» شروع نہیں ہو سکا: {1}", zh: "无法启动“{0}”：{1}");

            Add("У сценария не задан путь.",
                en: "The scenario has no path set.", uk: "У сценарію не задано шлях.",
                de: "Für das Szenario ist kein Pfad festgelegt.", it: "Lo scenario non ha un percorso impostato.",
                es: "El escenario no tiene una ruta establecida.", fr: "Le scénario n'a pas de chemin défini.",
                pt: "O cenário não tem um caminho definido.", ar: "لم يُحدَّد مسار لهذا السيناريو.",
                hi: "परिदृश्य के लिए कोई पथ निर्धारित नहीं है।", bn: "দৃশ্যপটের জন্য কোনো পথ নির্ধারিত নেই।",
                ur: "منظرنامے کے لیے کوئی راستہ متعین نہیں۔", zh: "该场景未设置路径。");

            Add("Файл не найден: {0}",
                en: "File not found: {0}", uk: "Файл не знайдено: {0}", de: "Datei nicht gefunden: {0}",
                it: "File non trovato: {0}", es: "Archivo no encontrado: {0}", fr: "Fichier introuvable : {0}",
                pt: "Arquivo não encontrado: {0}", ar: "الملف غير موجود: {0}", hi: "फ़ाइल नहीं मिली: {0}",
                bn: "ফাইল পাওয়া যায়নি: {0}", ur: "فائل نہیں ملی: {0}", zh: "找不到文件：{0}");

            Add("«{0}» не найден ни как файл, ни в PATH.",
                en: "\"{0}\" was not found as a file or on PATH.", uk: "«{0}» не знайдено ні як файл, ні в PATH.",
                de: "\"{0}\" wurde weder als Datei noch im PATH gefunden.", it: "\"{0}\" non trovato né come file né nel PATH.",
                es: "\"{0}\" no se encontró como archivo ni en PATH.", fr: "« {0} » introuvable comme fichier ou dans le PATH.",
                pt: "\"{0}\" não foi encontrado como arquivo nem no PATH.", ar: "لم يُعثر على «{0}» كملف ولا في PATH.",
                hi: "\"{0}\" न फ़ाइल के रूप में मिला, न PATH में।", bn: "\"{0}\" ফাইল হিসেবে বা PATH-এ পাওয়া যায়নি।",
                ur: "«{0}» نہ فائل کے طور پر ملا نہ PATH میں۔", zh: "“{0}”既不是文件，也不在 PATH 中。");

            Add("Сценарию yt-dlp нужен запрос ссылки, недоступный в этом контексте.",
                en: "A yt-dlp scenario needs the link prompt, which is unavailable in this context.",
                uk: "Сценарію yt-dlp потрібен запит посилання, недоступний у цьому контексті.",
                de: "Ein yt-dlp-Szenario benötigt die Link-Abfrage, die in diesem Kontext nicht verfügbar ist.",
                it: "Uno scenario yt-dlp richiede la richiesta del link, non disponibile in questo contesto.",
                es: "Un escenario de yt-dlp necesita la solicitud de enlace, no disponible en este contexto.",
                fr: "Un scénario yt-dlp nécessite l'invite de lien, indisponible dans ce contexte.",
                pt: "Um cenário do yt-dlp precisa da solicitação de link, indisponível neste contexto.",
                ar: "يحتاج سيناريو yt-dlp إلى مطالبة الرابط، وهي غير متاحة في هذا السياق.",
                hi: "yt-dlp परिदृश्य को लिंक प्रॉम्प्ट चाहिए, जो इस संदर्भ में उपलब्ध नहीं है।",
                bn: "yt-dlp দৃশ্যপটের জন্য লিংক প্রম্পট দরকার, যা এই প্রসঙ্গে নেই।",
                ur: "yt-dlp منظرنامے کو لنک پرامپٹ درکار ہے، جو اس سیاق میں دستیاب نہیں۔",
                zh: "yt-dlp 场景需要链接输入框，但当前环境不可用。");

            Add("Ссылка содержит недопустимые символы (кавычки или управляющие).",
                en: "The link contains characters that are not allowed (quotes or control characters).",
                uk: "Посилання містить неприпустимі символи (лапки або керівні).",
                de: "Der Link enthält unzulässige Zeichen (Anführungs- oder Steuerzeichen).",
                it: "Il link contiene caratteri non consentiti (virgolette o caratteri di controllo).",
                es: "El enlace contiene caracteres no permitidos (comillas o caracteres de control).",
                fr: "Le lien contient des caractères interdits (guillemets ou caractères de contrôle).",
                pt: "O link contém caracteres não permitidos (aspas ou caracteres de controle).",
                ar: "يحتوي الرابط على أحرف غير مسموح بها (علامات اقتباس أو أحرف تحكم).",
                hi: "लिंक में अमान्य वर्ण हैं (उद्धरण चिह्न या कंट्रोल वर्ण)।",
                bn: "লিংকে অননুমোদিত অক্ষর আছে (উদ্ধৃতি বা কন্ট্রোল অক্ষর)।",
                ur: "لنک میں ناجائز حروف ہیں (اقتباس یا کنٹرول حروف)۔",
                zh: "链接包含不允许的字符（引号或控制字符）。");

            Add("yt-dlp не найден в PATH. Установите его, чтобы команда yt-dlp работала в терминале.",
                en: "yt-dlp was not found on PATH. Install it so the yt-dlp command works in a terminal.",
                uk: "yt-dlp не знайдено в PATH. Встановіть його, щоб команда yt-dlp працювала в терміналі.",
                de: "yt-dlp wurde nicht im PATH gefunden. Installieren Sie es, damit der Befehl yt-dlp im Terminal funktioniert.",
                it: "yt-dlp non trovato nel PATH. Installalo perché il comando yt-dlp funzioni nel terminale.",
                es: "yt-dlp no se encontró en PATH. Instálelo para que el comando yt-dlp funcione en una terminal.",
                fr: "yt-dlp introuvable dans le PATH. Installez-le pour que la commande yt-dlp fonctionne dans un terminal.",
                pt: "yt-dlp não foi encontrado no PATH. Instale-o para que o comando yt-dlp funcione no terminal.",
                ar: "لم يُعثر على yt-dlp في PATH. ثبّته حتى يعمل أمر yt-dlp في الطرفية.",
                hi: "yt-dlp PATH में नहीं मिला। इसे इंस्टॉल करें ताकि टर्मिनल में yt-dlp कमांड चले।",
                bn: "yt-dlp PATH-এ পাওয়া যায়নি। এটি ইনস্টল করুন যাতে টার্মিনালে yt-dlp কমান্ড চলে।",
                ur: "yt-dlp PATH میں نہیں ملا۔ اسے انسٹال کریں تاکہ ٹرمینل میں yt-dlp کمانڈ چلے۔",
                zh: "在 PATH 中找不到 yt-dlp。请安装它，使 yt-dlp 命令可在终端中运行。");

            Add("Не удалось использовать папку загрузки «{0}»: {1}",
                en: "Cannot use the download folder \"{0}\": {1}", uk: "Не вдалося використати папку завантаження «{0}»: {1}",
                de: "Download-Ordner \"{0}\" kann nicht verwendet werden: {1}", it: "Impossibile usare la cartella di download \"{0}\": {1}",
                es: "No se puede usar la carpeta de descarga \"{0}\": {1}", fr: "Impossible d'utiliser le dossier de téléchargement « {0} » : {1}",
                pt: "Não é possível usar a pasta de download \"{0}\": {1}", ar: "تعذّر استخدام مجلد التنزيل «{0}»: {1}",
                hi: "डाउनलोड फ़ोल्डर \"{0}\" उपयोग नहीं हो सका: {1}", bn: "ডাউনলোড ফোল্ডার \"{0}\" ব্যবহার করা যায়নি: {1}",
                ur: "ڈاؤن لوڈ فولڈر «{0}» استعمال نہیں ہو سکا: {1}", zh: "无法使用下载文件夹“{0}”：{1}");

            Add("Сценарий не найден — обновите Jump List, открыв CyrFlip.",
                en: "The scenario was not found — open CyrFlip to refresh the Jump List.",
                uk: "Сценарій не знайдено — оновіть Jump List, відкривши CyrFlip.",
                de: "Das Szenario wurde nicht gefunden — öffnen Sie CyrFlip, um die Jump List zu aktualisieren.",
                it: "Scenario non trovato — apri CyrFlip per aggiornare la Jump List.",
                es: "No se encontró el escenario; abra CyrFlip para actualizar la Jump List.",
                fr: "Scénario introuvable — ouvrez CyrFlip pour actualiser la Jump List.",
                pt: "O cenário não foi encontrado — abra o CyrFlip para atualizar a Jump List.",
                ar: "لم يُعثر على السيناريو — افتح CyrFlip لتحديث Jump List.",
                hi: "परिदृश्य नहीं मिला — Jump List ताज़ा करने के लिए CyrFlip खोलें।",
                bn: "দৃশ্যপট পাওয়া যায়নি — Jump List হালনাগাদ করতে CyrFlip খুলুন।",
                ur: "منظرنامہ نہیں ملا — Jump List تازہ کرنے کے لیے CyrFlip کھولیں۔",
                zh: "未找到该场景——请打开 CyrFlip 以刷新 Jump List。");

            // ---- yt-dlp link dialog ----
            Add("Загрузка yt-dlp",
                en: "yt-dlp download", uk: "Завантаження yt-dlp", de: "yt-dlp-Download", it: "Download yt-dlp",
                es: "Descarga de yt-dlp", fr: "Téléchargement yt-dlp", pt: "Download do yt-dlp",
                ar: "تنزيل yt-dlp", hi: "yt-dlp डाउनलोड", bn: "yt-dlp ডাউনলোড", ur: "yt-dlp ڈاؤن لوڈ", zh: "yt-dlp 下载");

            Add("Ссылка для скачивания:",
                en: "Link to download:", uk: "Посилання для завантаження:", de: "Link zum Herunterladen:",
                it: "Link da scaricare:", es: "Enlace para descargar:", fr: "Lien à télécharger :",
                pt: "Link para baixar:", ar: "رابط التنزيل:", hi: "डाउनलोड करने का लिंक:",
                bn: "ডাউনলোডের লিংক:", ur: "ڈاؤن لوڈ کا لنک:", zh: "下载链接：");

            Add("Начать",
                en: "Start", uk: "Почати", de: "Starten", it: "Avvia", es: "Iniciar", fr: "Démarrer",
                pt: "Iniciar", ar: "بدء", hi: "शुरू करें", bn: "শুরু করুন", ur: "شروع کریں", zh: "开始");

            // ---- Scenario add/edit dialog ----
            Add("Новый сценарий",
                en: "New scenario", uk: "Новий сценарій", de: "Neues Szenario", it: "Nuovo scenario",
                es: "Nuevo escenario", fr: "Nouveau scénario", pt: "Novo cenário", ar: "سيناريو جديد",
                hi: "नया परिदृश्य", bn: "নতুন দৃশ্যপট", ur: "نیا منظرنامہ", zh: "新建场景");

            Add("Изменить сценарий",
                en: "Edit scenario", uk: "Змінити сценарій", de: "Szenario bearbeiten", it: "Modifica scenario",
                es: "Editar escenario", fr: "Modifier le scénario", pt: "Editar cenário", ar: "تحرير السيناريو",
                hi: "परिदृश्य संपादित करें", bn: "দৃশ্যপট সম্পাদনা", ur: "منظرنامہ ترمیم کریں", zh: "编辑场景");

            Add("Тип:",
                en: "Type:", uk: "Тип:", de: "Typ:", it: "Tipo:", es: "Tipo:", fr: "Type :",
                pt: "Tipo:", ar: "النوع:", hi: "प्रकार:", bn: "ধরন:", ur: "قسم:", zh: "类型：");

            Add("Имя:",
                en: "Name:", uk: "Ім'я:", de: "Name:", it: "Nome:", es: "Nombre:", fr: "Nom :",
                pt: "Nome:", ar: "الاسم:", hi: "नाम:", bn: "নাম:", ur: "نام:", zh: "名称：");

            Add("Программа или скрипт",
                en: "Program or script", uk: "Програма або скрипт", de: "Programm oder Skript",
                it: "Programma o script", es: "Programa o script", fr: "Programme ou script",
                pt: "Programa ou script", ar: "برنامج أو سكربت", hi: "प्रोग्राम या स्क्रिप्ट",
                bn: "প্রোগ্রাম বা স্ক্রিপ্ট", ur: "پروگرام یا اسکرپٹ", zh: "程序或脚本");

            Add("yt-dlp: скачать по ссылке",
                en: "yt-dlp: download a link", uk: "yt-dlp: завантажити за посиланням",
                de: "yt-dlp: Link herunterladen", it: "yt-dlp: scarica da link",
                es: "yt-dlp: descargar un enlace", fr: "yt-dlp : télécharger un lien",
                pt: "yt-dlp: baixar um link", ar: "yt-dlp: تنزيل رابط", hi: "yt-dlp: लिंक डाउनलोड करें",
                bn: "yt-dlp: লিংক ডাউনলোড", ur: "yt-dlp: لنک ڈاؤن لوڈ کریں", zh: "yt-dlp：下载链接");

            Add("Путь:",
                en: "Path:", uk: "Шлях:", de: "Pfad:", it: "Percorso:", es: "Ruta:", fr: "Chemin :",
                pt: "Caminho:", ar: "المسار:", hi: "पथ:", bn: "পথ:", ur: "راستہ:", zh: "路径：");

            Add("Аргументы:",
                en: "Arguments:", uk: "Аргументи:", de: "Argumente:", it: "Argomenti:", es: "Argumentos:",
                fr: "Arguments :", pt: "Argumentos:", ar: "الوسائط:", hi: "आर्ग्युमेंट:", bn: "আর্গুমেন্ট:",
                ur: "آرگومنٹس:", zh: "参数：");

            Add("Рабочая папка:",
                en: "Working folder:", uk: "Робоча папка:", de: "Arbeitsordner:", it: "Cartella di lavoro:",
                es: "Carpeta de trabajo:", fr: "Dossier de travail :", pt: "Pasta de trabalho:",
                ar: "مجلد العمل:", hi: "कार्य फ़ोल्डर:", bn: "কার্য ফোল্ডার:", ur: "ورکنگ فولڈر:", zh: "工作文件夹：");

            Add("Обзор...",
                en: "Browse...", uk: "Огляд...", de: "Durchsuchen...", it: "Sfoglia...", es: "Examinar...",
                fr: "Parcourir...", pt: "Procurar...", ar: "استعراض...", hi: "ब्राउज़ करें...",
                bn: "ব্রাউজ...", ur: "براؤز...", zh: "浏览...");

            Add("Запускать от имени администратора",
                en: "Run as administrator", uk: "Запускати від імені адміністратора",
                de: "Als Administrator ausführen", it: "Esegui come amministratore",
                es: "Ejecutar como administrador", fr: "Exécuter en tant qu'administrateur",
                pt: "Executar como administrador", ar: "تشغيل كمسؤول", hi: "प्रशासक के रूप में चलाएँ",
                bn: "প্রশাসক হিসেবে চালান", ur: "بطور منتظم چلائیں", zh: "以管理员身份运行");

            Add("Папка загрузки (пусто = Загрузки):",
                en: "Download folder (empty = Downloads):", uk: "Папка завантаження (порожньо = Завантаження):",
                de: "Download-Ordner (leer = Downloads):", it: "Cartella di download (vuoto = Download):",
                es: "Carpeta de descarga (vacío = Descargas):", fr: "Dossier de téléchargement (vide = Téléchargements) :",
                pt: "Pasta de download (vazio = Downloads):", ar: "مجلد التنزيل (فارغ = التنزيلات):",
                hi: "डाउनलोड फ़ोल्डर (खाली = Downloads):", bn: "ডাউনলোড ফোল্ডার (খালি = Downloads):",
                ur: "ڈاؤن لوڈ فولڈر (خالی = Downloads):", zh: "下载文件夹（留空 = 下载）：");

            Add("Доп. параметры yt-dlp:",
                en: "Extra yt-dlp options:", uk: "Дод. параметри yt-dlp:", de: "Zusätzliche yt-dlp-Optionen:",
                it: "Opzioni yt-dlp aggiuntive:", es: "Opciones extra de yt-dlp:", fr: "Options yt-dlp supplémentaires :",
                pt: "Opções extras do yt-dlp:", ar: "خيارات yt-dlp إضافية:", hi: "अतिरिक्त yt-dlp विकल्प:",
                bn: "অতিরিক্ত yt-dlp অপশন:", ur: "اضافی yt-dlp اختیارات:", zh: "额外的 yt-dlp 选项：");

            Add("Ссылка запрашивается при каждом запуске. Программа yt-dlp должна быть доступна в PATH.",
                en: "The link is asked for on every run. The yt-dlp program must be available on PATH.",
                uk: "Посилання запитується при кожному запуску. Програма yt-dlp має бути доступна в PATH.",
                de: "Der Link wird bei jedem Start abgefragt. Das Programm yt-dlp muss im PATH verfügbar sein.",
                it: "Il link viene richiesto a ogni avvio. Il programma yt-dlp deve essere disponibile nel PATH.",
                es: "El enlace se solicita en cada ejecución. El programa yt-dlp debe estar disponible en PATH.",
                fr: "Le lien est demandé à chaque exécution. Le programme yt-dlp doit être disponible dans le PATH.",
                pt: "O link é solicitado a cada execução. O programa yt-dlp deve estar disponível no PATH.",
                ar: "يُطلب الرابط عند كل تشغيل. يجب أن يكون برنامج yt-dlp متاحاً في PATH.",
                hi: "हर बार चलाने पर लिंक पूछा जाता है। yt-dlp प्रोग्राम PATH में उपलब्ध होना चाहिए।",
                bn: "প্রতিবার চালানোর সময় লিংক চাওয়া হয়। yt-dlp প্রোগ্রাম PATH-এ থাকতে হবে।",
                ur: "ہر بار چلانے پر لنک پوچھا جاتا ہے۔ yt-dlp پروگرام PATH میں دستیاب ہونا چاہیے۔",
                zh: "每次运行时都会询问链接。yt-dlp 程序必须在 PATH 中可用。");

            Add("Комбинация запуска сценария",
                en: "Scenario launch shortcut", uk: "Комбінація запуску сценарію",
                de: "Startkürzel des Szenarios", it: "Scorciatoia di avvio dello scenario",
                es: "Atajo de inicio del escenario", fr: "Raccourci de lancement du scénario",
                pt: "Atalho de início do cenário", ar: "اختصار تشغيل السيناريو",
                hi: "परिदृश्य लॉन्च शॉर्टकट", bn: "দৃশ্যপট চালুর শর্টকাট",
                ur: "منظرنامہ لانچ شارٹ کٹ", zh: "场景启动快捷键");

            Add("Выберите программу или скрипт",
                en: "Select a program or script", uk: "Виберіть програму або скрипт",
                de: "Programm oder Skript auswählen", it: "Seleziona un programma o script",
                es: "Seleccione un programa o script", fr: "Sélectionnez un programme ou un script",
                pt: "Selecione um programa ou script", ar: "اختر برنامجاً أو سكربتاً",
                hi: "प्रोग्राम या स्क्रिप्ट चुनें", bn: "প্রোগ্রাম বা স্ক্রিপ্ট নির্বাচন করুন",
                ur: "پروگرام یا اسکرپٹ منتخب کریں", zh: "选择程序或脚本");

            Add("Выберите рабочую папку",
                en: "Select the working folder", uk: "Виберіть робочу папку", de: "Arbeitsordner auswählen",
                it: "Seleziona la cartella di lavoro", es: "Seleccione la carpeta de trabajo",
                fr: "Sélectionnez le dossier de travail", pt: "Selecione a pasta de trabalho",
                ar: "اختر مجلد العمل", hi: "कार्य फ़ोल्डर चुनें", bn: "কার্য ফোল্ডার নির্বাচন করুন",
                ur: "ورکنگ فولڈر منتخب کریں", zh: "选择工作文件夹");

            Add("Выберите папку загрузки",
                en: "Select the download folder", uk: "Виберіть папку завантаження",
                de: "Download-Ordner auswählen", it: "Seleziona la cartella di download",
                es: "Seleccione la carpeta de descarga", fr: "Sélectionnez le dossier de téléchargement",
                pt: "Selecione a pasta de download", ar: "اختر مجلد التنزيل", hi: "डाउनलोड फ़ोल्डर चुनें",
                bn: "ডাউনলোড ফোল্ডার নির্বাচন করুন", ur: "ڈاؤن لوڈ فولڈر منتخب کریں", zh: "选择下载文件夹");

            Add("Укажите имя сценария.",
                en: "Enter a scenario name.", uk: "Вкажіть ім'я сценарію.", de: "Geben Sie einen Szenarionamen an.",
                it: "Specifica un nome per lo scenario.", es: "Indique un nombre para el escenario.",
                fr: "Indiquez un nom de scénario.", pt: "Informe um nome para o cenário.",
                ar: "أدخل اسم السيناريو.", hi: "परिदृश्य का नाम दर्ज करें।", bn: "দৃশ্যপটের নাম দিন।",
                ur: "منظرنامے کا نام درج کریں۔", zh: "请输入场景名称。");

            Add("Укажите путь к программе или скрипту.",
                en: "Enter the path to a program or script.", uk: "Вкажіть шлях до програми або скрипту.",
                de: "Geben Sie den Pfad zu einem Programm oder Skript an.", it: "Specifica il percorso di un programma o script.",
                es: "Indique la ruta de un programa o script.", fr: "Indiquez le chemin d'un programme ou d'un script.",
                pt: "Informe o caminho de um programa ou script.", ar: "أدخل مسار برنامج أو سكربت.",
                hi: "प्रोग्राम या स्क्रिप्ट का पथ दर्ज करें।", bn: "প্রোগ্রাম বা স্ক্রিপ্টের পথ দিন।",
                ur: "پروگرام یا اسکرپٹ کا راستہ درج کریں۔", zh: "请输入程序或脚本的路径。");
        }
    }
}
