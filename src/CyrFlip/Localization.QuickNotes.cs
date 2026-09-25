namespace CyrFlip
{
    /// <summary>
    /// The quick notes: the window, the tray entry, the context-menu command and the settings tab.
    /// Strings already registered elsewhere ("Текст", "Копировать", "Удалить", "Настройки",
    /// "Экспорт...", "Найдено: {0}", "Изменить...") are not repeated here - the key is the Russian
    /// source string, so a second registration would simply overwrite the first.
    /// </summary>
    internal static partial class Localization
    {
        private static void AddQuickNotesStrings()
        {
            Add("Быстрые заметки",
                en: "Quick notes", uk: "Швидкі нотатки", de: "Schnellnotizen", it: "Note rapide",
                es: "Notas rápidas", fr: "Notes rapides", pt: "Notas rápidas",
                ar: "ملاحظات سريعة", hi: "त्वरित नोट्स", bn: "দ্রুত নোট", ur: "فوری نوٹس", zh: "快速笔记");

            Add("Заметка",
                en: "Note", uk: "Нотатка", de: "Notiz", it: "Nota",
                es: "Nota", fr: "Note", pt: "Nota",
                ar: "ملاحظة", hi: "नोट", bn: "নোট", ur: "نوٹ", zh: "笔记");

            Add("Список",
                en: "List", uk: "Список", de: "Liste", it: "Elenco",
                es: "Lista", fr: "Liste", pt: "Lista",
                ar: "قائمة", hi: "सूची", bn: "তালিকা", ur: "فہرست", zh: "清单");

            Add("Пункт",
                en: "Item", uk: "Пункт", de: "Eintrag", it: "Voce",
                es: "Elemento", fr: "Élément", pt: "Item",
                ar: "عنصر", hi: "आइटम", bn: "আইটেম", ur: "آئٹم", zh: "条目");

            Add("Создана",
                en: "Created", uk: "Створено", de: "Erstellt", it: "Creata",
                es: "Creada", fr: "Créée", pt: "Criada",
                ar: "أُنشئت", hi: "बनाई गई", bn: "তৈরি", ur: "بنائی گئی", zh: "创建于");

            Add("Имя (необязательно)",
                en: "Name (optional)", uk: "Ім'я (необов'язково)", de: "Name (optional)", it: "Nome (facoltativo)",
                es: "Nombre (opcional)", fr: "Nom (facultatif)", pt: "Nome (opcional)",
                ar: "الاسم (اختياري)", hi: "नाम (वैकल्पिक)", bn: "নাম (ঐচ্ছিক)", ur: "نام (اختیاری)", zh: "名称（可选）");

            Add("Добавить пункт",
                en: "Add item", uk: "Додати пункт", de: "Eintrag hinzufügen", it: "Aggiungi voce",
                es: "Añadir elemento", fr: "Ajouter un élément", pt: "Adicionar item",
                ar: "إضافة عنصر", hi: "आइटम जोड़ें", bn: "আইটেম যোগ করুন", ur: "آئٹم شامل کریں", zh: "添加条目");

            Add("Удалить пункт",
                en: "Remove item", uk: "Видалити пункт", de: "Eintrag entfernen", it: "Rimuovi voce",
                es: "Eliminar elemento", fr: "Supprimer l'élément", pt: "Remover item",
                ar: "حذف العنصر", hi: "आइटम हटाएँ", bn: "আইটেম মুছুন", ur: "آئٹم حذف کریں", zh: "删除条目");

            Add("Копировать для Google Keep",
                en: "Copy for Google Keep", uk: "Копіювати для Google Keep", de: "Für Google Keep kopieren",
                it: "Copia per Google Keep", es: "Copiar para Google Keep", fr: "Copier pour Google Keep",
                pt: "Copiar para o Google Keep", ar: "نسخ لـ Google Keep", hi: "Google Keep के लिए कॉपी करें",
                bn: "Google Keep-এর জন্য কপি করুন", ur: "Google Keep کے لیے کاپی کریں", zh: "复制到 Google Keep");

            Add("Заметок: {0}",
                en: "Notes: {0}", uk: "Нотаток: {0}", de: "Notizen: {0}", it: "Note: {0}",
                es: "Notas: {0}", fr: "Notes : {0}", pt: "Notas: {0}",
                ar: "الملاحظات: {0}", hi: "नोट्स: {0}", bn: "নোট: {0}", ur: "نوٹس: {0}", zh: "笔记数：{0}");

            Add("(пусто)",
                en: "(empty)", uk: "(порожньо)", de: "(leer)", it: "(vuota)",
                es: "(vacía)", fr: "(vide)", pt: "(vazia)",
                ar: "(فارغة)", hi: "(खाली)", bn: "(খালি)", ur: "(خالی)", zh: "（空）");

            Add("Новая заметка",
                en: "New note", uk: "Нова нотатка", de: "Neue Notiz", it: "Nuova nota",
                es: "Nota nueva", fr: "Nouvelle note", pt: "Nota nova",
                ar: "ملاحظة جديدة", hi: "नया नोट", bn: "নতুন নোট", ur: "نیا نوٹ", zh: "新建笔记");

            Add("Создана {0}, изменена {1}",
                en: "Created {0}, changed {1}", uk: "Створено {0}, змінено {1}",
                de: "Erstellt {0}, geändert {1}", it: "Creata il {0}, modificata il {1}",
                es: "Creada el {0}, modificada el {1}", fr: "Créée le {0}, modifiée le {1}",
                pt: "Criada em {0}, alterada em {1}",
                ar: "أُنشئت في {0}، عُدِّلت في {1}", hi: "{0} को बनाई गई, {1} को बदली गई",
                bn: "{0}-এ তৈরি, {1}-এ পরিবর্তিত", ur: "{0} کو بنائی گئی، {1} کو تبدیل ہوئی",
                zh: "创建于 {0}，修改于 {1}");

            Add("Фрагмент больше {0} КБ и в заметку не помещается.",
                en: "The fragment is larger than {0} KB and does not fit in a note.",
                uk: "Фрагмент більший за {0} КБ і в нотатку не вміщується.",
                de: "Der Ausschnitt ist größer als {0} KB und passt nicht in eine Notiz.",
                it: "Il frammento supera {0} KB e non entra in una nota.",
                es: "El fragmento supera {0} KB y no cabe en una nota.",
                fr: "Le fragment dépasse {0} Ko et ne tient pas dans une note.",
                pt: "O fragmento é maior que {0} KB e não cabe em uma nota.",
                ar: "المقتطف أكبر من {0} كيلوبايت ولا يتسع في ملاحظة.",
                hi: "यह अंश {0} KB से बड़ा है और नोट में नहीं समाता।",
                bn: "অংশটি {0} KB-এর চেয়ে বড় এবং নোটে আঁটে না।",
                ur: "یہ ٹکڑا {0} KB سے بڑا ہے اور نوٹ میں نہیں سماتا۔",
                zh: "该片段超过 {0} KB，放不进一条笔记。");

            Add("Отметки выполнения при переходе к тексту не сохранятся. Продолжить?",
                en: "The tick marks cannot survive the switch to text. Continue?",
                uk: "Позначки виконання під час переходу до тексту не збережуться. Продовжити?",
                de: "Die Häkchen gehen beim Wechsel zu Text verloren. Fortfahren?",
                it: "Le spunte non sopravvivono al passaggio a testo. Continuare?",
                es: "Las marcas de verificación no sobreviven al cambio a texto. ¿Continuar?",
                fr: "Les cases cochées ne survivront pas au passage en texte. Continuer ?",
                pt: "As marcas de conclusão não sobrevivem à mudança para texto. Continuar?",
                ar: "لن تبقى علامات الإنجاز عند التحويل إلى نص. هل تريد المتابعة؟",
                hi: "पाठ में बदलने पर पूर्णता के निशान नहीं बचेंगे। जारी रखें?",
                bn: "লেখায় বদলালে টিক চিহ্নগুলি থাকবে না। চালিয়ে যাবেন?",
                ur: "متن میں تبدیل کرنے پر مکمل ہونے کے نشان باقی نہیں رہیں گے۔ جاری رکھیں؟",
                zh: "切换为文本后，勾选标记将无法保留。要继续吗？");

            Add("Пункты списка не хранят переносы строк: текст вернётся с обычными переводами строк. Продолжить?",
                en: "List items hold no line endings: the text will come back with plain newlines. Continue?",
                uk: "Пункти списку не зберігають символи кінця рядка: текст повернеться зі звичайними переводами рядків. Продовжити?",
                de: "Listeneinträge speichern keine Zeilenenden: Der Text kommt mit einfachen Zeilenumbrüchen zurück. Fortfahren?",
                it: "Le voci dell'elenco non conservano i fine riga: il testo tornerà con semplici a capo. Continuare?",
                es: "Los elementos de la lista no guardan finales de línea: el texto volverá con saltos de línea simples. ¿Continuar?",
                fr: "Les éléments de liste ne conservent pas les fins de ligne : le texte reviendra avec de simples sauts de ligne. Continuer ?",
                pt: "Os itens da lista não guardam finais de linha: o texto voltará com quebras de linha simples. Continuar?",
                ar: "لا تحتفظ عناصر القائمة بنهايات الأسطر: سيعود النص بفواصل أسطر عادية. هل تريد المتابعة؟",
                hi: "सूची की मदें पंक्ति-अंत नहीं रखतीं: पाठ सामान्य न्यूलाइन के साथ लौटेगा। जारी रखें?",
                bn: "তালিকার আইটেম লাইন-শেষ ধরে রাখে না: লেখা সাধারণ নিউলাইন নিয়ে ফিরবে। চালিয়ে যাবেন?",
                ur: "فہرست کے آئٹمز سطر کے اختتام محفوظ نہیں رکھتے: متن عام نئی سطروں کے ساتھ واپس آئے گا۔ جاری رکھیں؟",
                zh: "清单条目不保存换行符：文本将以普通换行返回。要继续吗？");

            Add("Текст скопирован. Создайте заметку в Google Keep и вставьте его.",
                en: "The text is on the clipboard. Create a note in Google Keep and paste it there.",
                uk: "Текст скопійовано. Створіть нотатку в Google Keep і вставте його.",
                de: "Der Text ist in der Zwischenablage. Erstellen Sie eine Notiz in Google Keep und fügen Sie ihn ein.",
                it: "Il testo è negli appunti. Crea una nota in Google Keep e incollalo.",
                es: "El texto está en el portapapeles. Cree una nota en Google Keep y péguelo.",
                fr: "Le texte est dans le presse-papiers. Créez une note dans Google Keep et collez-le.",
                pt: "O texto está na área de transferência. Crie uma nota no Google Keep e cole-o.",
                ar: "النص في الحافظة. أنشئ ملاحظة في Google Keep والصقه فيها.",
                hi: "पाठ क्लिपबोर्ड में है। Google Keep में नोट बनाएँ और उसे चिपकाएँ।",
                bn: "লেখাটি ক্লিপবোর্ডে আছে। Google Keep-এ একটি নোট তৈরি করে পেস্ট করুন।",
                ur: "متن کلپ بورڈ میں ہے۔ Google Keep میں نوٹ بنائیں اور اسے پیسٹ کریں۔",
                zh: "文本已复制。请在 Google Keep 中新建一条笔记并粘贴。");

            Add("Отметки выполнения в Google Keep не переносятся.",
                en: "The tick marks do not travel to Google Keep.",
                uk: "Позначки виконання до Google Keep не переносяться.",
                de: "Die Häkchen werden nicht nach Google Keep übertragen.",
                it: "Le spunte non vengono trasferite in Google Keep.",
                es: "Las marcas de verificación no se transfieren a Google Keep.",
                fr: "Les cases cochées ne sont pas transférées vers Google Keep.",
                pt: "As marcas de conclusão não são transferidas para o Google Keep.",
                ar: "لا تُنقل علامات الإنجاز إلى Google Keep.",
                hi: "पूर्णता के निशान Google Keep में नहीं जाते।",
                bn: "টিক চিহ্নগুলি Google Keep-এ যায় না।",
                ur: "مکمل ہونے کے نشان Google Keep میں منتقل نہیں ہوتے۔",
                zh: "勾选标记不会带到 Google Keep。");

            Add("Открыть Google Keep в браузере?",
                en: "Open Google Keep in the browser?", uk: "Відкрити Google Keep у браузері?",
                de: "Google Keep im Browser öffnen?", it: "Aprire Google Keep nel browser?",
                es: "¿Abrir Google Keep en el navegador?", fr: "Ouvrir Google Keep dans le navigateur ?",
                pt: "Abrir o Google Keep no navegador?", ar: "هل تريد فتح Google Keep في المتصفح؟",
                hi: "ब्राउज़र में Google Keep खोलें?", bn: "ব্রাউজারে Google Keep খুলবেন?",
                ur: "براؤزر میں Google Keep کھولیں؟", zh: "在浏览器中打开 Google Keep？");

            Add("Экспорт заметки",
                en: "Export the note", uk: "Експорт нотатки", de: "Notiz exportieren", it: "Esporta la nota",
                es: "Exportar la nota", fr: "Exporter la note", pt: "Exportar a nota",
                ar: "تصدير الملاحظة", hi: "नोट निर्यात करें", bn: "নোট রপ্তানি করুন", ur: "نوٹ برآمد کریں", zh: "导出笔记");

            Add("Экспорт всех заметок",
                en: "Export every note", uk: "Експорт усіх нотаток", de: "Alle Notizen exportieren",
                it: "Esporta tutte le note", es: "Exportar todas las notas", fr: "Exporter toutes les notes",
                pt: "Exportar todas as notas", ar: "تصدير كل الملاحظات", hi: "सभी नोट्स निर्यात करें",
                bn: "সব নোট রপ্তানি করুন", ur: "تمام نوٹس برآمد کریں", zh: "导出全部笔记");

            Add("Текстовый файл",
                en: "Text file", uk: "Текстовий файл", de: "Textdatei", it: "File di testo",
                es: "Archivo de texto", fr: "Fichier texte", pt: "Arquivo de texto",
                ar: "ملف نصي", hi: "पाठ फ़ाइल", bn: "টেক্সট ফাইল", ur: "متنی فائل", zh: "文本文件");

            Add("Заметка сохранена: {0}",
                en: "Note saved: {0}", uk: "Нотатку збережено: {0}", de: "Notiz gespeichert: {0}",
                it: "Nota salvata: {0}", es: "Nota guardada: {0}", fr: "Note enregistrée : {0}",
                pt: "Nota salva: {0}", ar: "تم حفظ الملاحظة: {0}", hi: "नोट सहेजा गया: {0}",
                bn: "নোট সংরক্ষিত: {0}", ur: "نوٹ محفوظ ہو گیا: {0}", zh: "笔记已保存：{0}");

            Add("Заметки сохранены: {0}",
                en: "Notes saved: {0}", uk: "Нотатки збережено: {0}", de: "Notizen gespeichert: {0}",
                it: "Note salvate: {0}", es: "Notas guardadas: {0}", fr: "Notes enregistrées : {0}",
                pt: "Notas salvas: {0}", ar: "تم حفظ الملاحظات: {0}", hi: "नोट्स सहेजे गए: {0}",
                bn: "নোট সংরক্ষিত: {0}", ur: "نوٹس محفوظ ہو گئے: {0}", zh: "笔记已保存：{0}");

            Add("Этот файл не защищён DPAPI — его прочитает любой, у кого есть доступ к папке.",
                en: "This file is not protected by DPAPI - anyone with access to the folder can read it.",
                uk: "Цей файл не захищений DPAPI — його прочитає будь-хто, хто має доступ до теки.",
                de: "Diese Datei ist nicht durch DPAPI geschützt - jeder mit Zugriff auf den Ordner kann sie lesen.",
                it: "Questo file non è protetto da DPAPI: può leggerlo chiunque abbia accesso alla cartella.",
                es: "Este archivo no está protegido por DPAPI: puede leerlo cualquiera que tenga acceso a la carpeta.",
                fr: "Ce fichier n'est pas protégé par DPAPI : toute personne ayant accès au dossier peut le lire.",
                pt: "Este arquivo não é protegido por DPAPI: qualquer pessoa com acesso à pasta pode lê-lo.",
                ar: "هذا الملف غير محمي بـ DPAPI، ويمكن لأي شخص لديه وصول إلى المجلد قراءته.",
                hi: "यह फ़ाइल DPAPI से सुरक्षित नहीं है — फ़ोल्डर तक पहुँच रखने वाला कोई भी इसे पढ़ सकता है।",
                bn: "এই ফাইলটি DPAPI দিয়ে সুরক্ষিত নয় — ফোল্ডারে প্রবেশাধিকার থাকা যে কেউ এটি পড়তে পারবে।",
                ur: "یہ فائل DPAPI سے محفوظ نہیں ہے — فولڈر تک رسائی رکھنے والا کوئی بھی اسے پڑھ سکتا ہے۔",
                zh: "该文件不受 DPAPI 保护——任何能访问该文件夹的人都能读取它。");

            Add("Не удалось сохранить файл: {0}",
                en: "Could not save the file: {0}", uk: "Не вдалося зберегти файл: {0}",
                de: "Die Datei konnte nicht gespeichert werden: {0}", it: "Impossibile salvare il file: {0}",
                es: "No se pudo guardar el archivo: {0}", fr: "Impossible d'enregistrer le fichier : {0}",
                pt: "Não foi possível salvar o arquivo: {0}", ar: "تعذر حفظ الملف: {0}",
                hi: "फ़ाइल सहेजी नहीं जा सकी: {0}", bn: "ফাইল সংরক্ষণ করা যায়নি: {0}",
                ur: "فائل محفوظ نہیں ہو سکی: {0}", zh: "无法保存文件：{0}");

            Add("Удалить эту заметку? Отменить удаление нельзя.",
                en: "Delete this note? There is no undo.",
                uk: "Видалити цю нотатку? Скасувати видалення не можна.",
                de: "Diese Notiz löschen? Es gibt kein Rückgängig.",
                it: "Eliminare questa nota? Non è possibile annullare.",
                es: "¿Eliminar esta nota? No se puede deshacer.",
                fr: "Supprimer cette note ? Il n'y a pas d'annulation.",
                pt: "Excluir esta nota? Não há como desfazer.",
                ar: "هل تريد حذف هذه الملاحظة؟ لا يمكن التراجع.",
                hi: "यह नोट हटाएँ? पूर्ववत करना संभव नहीं है।",
                bn: "এই নোটটি মুছবেন? পূর্বাবস্থায় ফেরানো যাবে না।",
                ur: "یہ نوٹ حذف کریں؟ واپسی ممکن نہیں۔",
                zh: "删除这条笔记？无法撤销。");

            Add("Удалить все быстрые заметки, журнал и резервную копию? Отменить это нельзя.",
                en: "Delete every quick note, the journal and its backup? There is no undo.",
                uk: "Видалити всі швидкі нотатки, журнал і резервну копію? Скасувати це не можна.",
                de: "Alle Schnellnotizen, das Journal und dessen Sicherung löschen? Es gibt kein Rückgängig.",
                it: "Eliminare tutte le note rapide, il registro e la sua copia di riserva? Non è possibile annullare.",
                es: "¿Eliminar todas las notas rápidas, el diario y su copia de seguridad? No se puede deshacer.",
                fr: "Supprimer toutes les notes rapides, le journal et sa sauvegarde ? Il n'y a pas d'annulation.",
                pt: "Excluir todas as notas rápidas, o diário e sua cópia de segurança? Não há como desfazer.",
                ar: "هل تريد حذف كل الملاحظات السريعة والسجل ونسخته الاحتياطية؟ لا يمكن التراجع.",
                hi: "सभी त्वरित नोट्स, जर्नल और उसका बैकअप हटाएँ? पूर्ववत करना संभव नहीं है।",
                bn: "সব দ্রুত নোট, জার্নাল ও তার ব্যাকআপ মুছবেন? পূর্বাবস্থায় ফেরানো যাবে না।",
                ur: "تمام فوری نوٹس، جرنل اور اس کا بیک اپ حذف کریں؟ واپسی ممکن نہیں۔",
                zh: "删除全部快速笔记、日志及其备份？无法撤销。");

            Add("Сохранить выделение в быстрые заметки",
                en: "Save the selection to quick notes",
                uk: "Зберегти виділене у швидкі нотатки",
                de: "Auswahl in den Schnellnotizen speichern",
                it: "Salva la selezione nelle note rapide",
                es: "Guardar la selección en las notas rápidas",
                fr: "Enregistrer la sélection dans les notes rapides",
                pt: "Salvar a seleção nas notas rápidas",
                ar: "حفظ التحديد في الملاحظات السريعة",
                hi: "चयन को त्वरित नोट्स में सहेजें",
                bn: "নির্বাচনটি দ্রুত নোটে সংরক্ষণ করুন",
                ur: "منتخب متن کو فوری نوٹس میں محفوظ کریں",
                zh: "把选中的文本存入快速笔记");

            Add("Задать хоткей быстрых заметок",
                en: "Set the quick-notes hotkey", uk: "Задати хоткей швидких нотаток",
                de: "Tastenkombination für Schnellnotizen festlegen", it: "Imposta la combinazione per le note rapide",
                es: "Definir la combinación de las notas rápidas", fr: "Définir le raccourci des notes rapides",
                pt: "Definir o atalho das notas rápidas", ar: "تعيين اختصار الملاحظات السريعة",
                hi: "त्वरित नोट्स की हॉटकी तय करें", bn: "দ্রুত নোটের হটকি নির্ধারণ করুন",
                ur: "فوری نوٹس کی ہاٹ کی مقرر کریں", zh: "设置快速笔记的快捷键");

            Add("быстрые заметки",
                en: "quick notes", uk: "швидкі нотатки", de: "Schnellnotizen", it: "note rapide",
                es: "notas rápidas", fr: "notes rapides", pt: "notas rápidas",
                ar: "ملاحظات سريعة", hi: "त्वरित नोट्स", bn: "দ্রুত নোট", ur: "فوری نوٹس", zh: "快速笔记");

            Add("быстрыми заметками",
                en: "the quick notes", uk: "швидкими нотатками", de: "den Schnellnotizen", it: "le note rapide",
                es: "las notas rápidas", fr: "les notes rapides", pt: "as notas rápidas",
                ar: "الملاحظات السريعة", hi: "त्वरित नोट्स", bn: "দ্রুত নোট", ur: "فوری نوٹس", zh: "快速笔记");

            Add("Загрузка заметок..",
                en: "Loading notes..", uk: "Завантаження нотаток..", de: "Notizen werden geladen..",
                it: "Caricamento delle note..", es: "Cargando notas..", fr: "Chargement des notes..",
                pt: "Carregando notas..", ar: "جارٍ تحميل الملاحظات..", hi: "नोट्स लोड हो रहे हैं..",
                bn: "নোট লোড হচ্ছে..", ur: "نوٹس لوڈ ہو رہے ہیں..", zh: "正在加载笔记..");

            Add("Заметка не сохранена на диск - подробности в журнале диагностики.",
                en: "The note was not saved to disk - see the diagnostic log for details.",
                uk: "Нотатку не збережено на диск - подробиці в журналі діагностики.",
                de: "Die Notiz wurde nicht auf dem Datenträger gespeichert - Details im Diagnoseprotokoll.",
                it: "La nota non è stata salvata su disco - dettagli nel registro di diagnostica.",
                es: "La nota no se guardó en el disco - detalles en el registro de diagnóstico.",
                fr: "La note n'a pas été enregistrée sur le disque - détails dans le journal de diagnostic.",
                pt: "A nota não foi salva no disco - detalhes no log de diagnóstico.",
                ar: "لم تُحفظ الملاحظة على القرص - التفاصيل في سجل التشخيص.",
                hi: "नोट डिस्क पर सहेजा नहीं गया - विवरण निदान लॉग में है।",
                bn: "নোটটি ডিস্কে সংরক্ষিত হয়নি - বিস্তারিত ডায়াগনস্টিক লগে।",
                ur: "نوٹ ڈسک پر محفوظ نہیں ہوا - تفصیل تشخیصی لاگ میں ہے۔",
                zh: "笔记未保存到磁盘 - 详情见诊断日志。");

            Add("Журнал быстрых заметок не найден - заметки восстановлены из резервной копии.",
                en: "The quick-notes journal was missing - the notes were restored from its backup.",
                uk: "Журнал швидких нотаток не знайдено - нотатки відновлено з резервної копії.",
                de: "Das Schnellnotiz-Journal fehlte - die Notizen wurden aus der Sicherung wiederhergestellt.",
                it: "Il registro delle note rapide mancava - le note sono state ripristinate dalla copia di riserva.",
                es: "Faltaba el diario de notas rápidas - las notas se restauraron desde la copia de seguridad.",
                fr: "Le journal des notes rapides était introuvable - les notes ont été restaurées depuis la sauvegarde.",
                pt: "O diário de notas rápidas não foi encontrado - as notas foram restauradas da cópia de segurança.",
                ar: "سجل الملاحظات السريعة مفقود - استُعيدت الملاحظات من النسخة الاحتياطية.",
                hi: "त्वरित नोट्स का जर्नल नहीं मिला - नोट्स बैकअप से पुनर्स्थापित किए गए।",
                bn: "দ্রুত নোটের জার্নাল পাওয়া যায়নি - নোটগুলি ব্যাকআপ থেকে পুনরুদ্ধার করা হয়েছে।",
                ur: "فوری نوٹس کا جرنل نہیں ملا - نوٹس بیک اپ سے بحال کر دیے گئے۔",
                zh: "未找到快速笔记日志 - 已从备份中恢复笔记。");

            Add("Не прочитано записей быстрых заметок: {0}. Остальные заметки на месте.",
                en: "Unreadable quick-note records: {0}. Every other note is there.",
                uk: "Не прочитано записів швидких нотаток: {0}. Решта нотаток на місці.",
                de: "Unlesbare Schnellnotiz-Einträge: {0}. Alle anderen Notizen sind da.",
                it: "Record di note rapide illeggibili: {0}. Tutte le altre note ci sono.",
                es: "Registros de notas rápidas ilegibles: {0}. El resto de las notas están.",
                fr: "Enregistrements de notes rapides illisibles : {0}. Toutes les autres notes sont là.",
                pt: "Registros de notas rápidas ilegíveis: {0}. As demais notas estão lá.",
                ar: "سجلات ملاحظات سريعة غير مقروءة: {0}. بقية الملاحظات موجودة.",
                hi: "अपठनीय त्वरित-नोट रिकॉर्ड: {0}. बाकी सभी नोट्स सुरक्षित हैं।",
                bn: "পড়া যায়নি এমন দ্রুত-নোট রেকর্ড: {0}। বাকি সব নোট ঠিক আছে।",
                ur: "ناقابلِ مطالعہ فوری نوٹ ریکارڈ: {0}۔ باقی تمام نوٹس موجود ہیں۔",
                zh: "无法读取的快速笔记记录：{0} 条。其余笔记都在。");

            Add("Заметки хранятся только на этом компьютере, в зашифрованном DPAPI файле вашей учётной записи Windows. CyrFlip не отправляет их в сеть и ничем их не индексирует.",
                en: "Notes live only on this computer, in a file encrypted with the DPAPI of your Windows account. CyrFlip never sends them anywhere and indexes them with nothing.",
                uk: "Нотатки зберігаються лише на цьому комп'ютері, у зашифрованому DPAPI файлі вашого облікового запису Windows. CyrFlip не надсилає їх у мережу і нічим їх не індексує.",
                de: "Notizen liegen nur auf diesem Computer, in einer mit der DPAPI Ihres Windows-Kontos verschlüsselten Datei. CyrFlip sendet sie nirgendwohin und indiziert sie mit nichts.",
                it: "Le note restano solo su questo computer, in un file cifrato con la DPAPI del tuo account Windows. CyrFlip non le invia da nessuna parte e non le indicizza con nulla.",
                es: "Las notas solo están en este equipo, en un archivo cifrado con la DPAPI de su cuenta de Windows. CyrFlip no las envía a ninguna parte ni las indexa con nada.",
                fr: "Les notes ne se trouvent que sur cet ordinateur, dans un fichier chiffré par la DPAPI de votre compte Windows. CyrFlip ne les envoie nulle part et ne les indexe avec rien.",
                pt: "As notas ficam apenas neste computador, em um arquivo criptografado com a DPAPI da sua conta do Windows. O CyrFlip não as envia a lugar nenhum nem as indexa com nada.",
                ar: "تبقى الملاحظات على هذا الحاسوب فقط، في ملف مشفَّر بـ DPAPI الخاص بحساب Windows لديك. لا يرسلها CyrFlip إلى أي مكان ولا يفهرسها بأي أداة.",
                hi: "नोट्स केवल इसी कंप्यूटर पर रहते हैं, आपके Windows खाते की DPAPI से एन्क्रिप्ट की गई फ़ाइल में। CyrFlip उन्हें कहीं नहीं भेजता और किसी चीज़ से अनुक्रमित नहीं करता।",
                bn: "নোটগুলি কেবল এই কম্পিউটারেই থাকে, আপনার Windows অ্যাকাউন্টের DPAPI দিয়ে এনক্রিপ্ট করা একটি ফাইলে। CyrFlip সেগুলি কোথাও পাঠায় না এবং কিছু দিয়েই ইনডেক্স করে না।",
                ur: "نوٹس صرف اسی کمپیوٹر پر رہتے ہیں، آپ کے Windows اکاؤنٹ کی DPAPI سے خفیہ کردہ فائل میں۔ CyrFlip انہیں کہیں نہیں بھیجتا اور کسی چیز سے فہرست بند نہیں کرتا۔",
                zh: "笔记只保存在这台电脑上，存于用你的 Windows 账户 DPAPI 加密的文件中。CyrFlip 不会把它们发往任何地方，也不用任何方式为它们建立索引。");

            Add("Это не хранилище секретов: не сохраняйте здесь пароли, боевые токены и приватные ключи.",
                en: "This is not a secret store: do not keep passwords, production tokens or private keys here.",
                uk: "Це не сховище секретів: не зберігайте тут паролі, бойові токени та приватні ключі.",
                de: "Dies ist kein Geheimnisspeicher: Bewahren Sie hier keine Passwörter, Produktionstoken oder privaten Schlüssel auf.",
                it: "Questo non è un archivio di segreti: non conservare qui password, token di produzione o chiavi private.",
                es: "Esto no es un almacén de secretos: no guarde aquí contraseñas, tokens de producción ni claves privadas.",
                fr: "Ceci n'est pas un coffre à secrets : n'y conservez ni mots de passe, ni jetons de production, ni clés privées.",
                pt: "Isto não é um cofre de segredos: não guarde aqui senhas, tokens de produção ou chaves privadas.",
                ar: "هذا ليس مخزنًا للأسرار: لا تحفظ هنا كلمات المرور أو رموز الإنتاج أو المفاتيح الخاصة.",
                hi: "यह गोपनीय जानकारी का भंडार नहीं है: यहाँ पासवर्ड, उत्पादन टोकन और निजी कुंजियाँ न रखें।",
                bn: "এটি গোপন তথ্যের ভাণ্ডার নয়: এখানে পাসওয়ার্ড, প্রোডাকশন টোকেন বা প্রাইভেট কি রাখবেন না।",
                ur: "یہ رازوں کا ذخیرہ نہیں ہے: یہاں پاس ورڈ، پروڈکشن ٹوکن اور نجی کلیدیں محفوظ نہ کریں۔",
                zh: "这不是密钥保管库：不要在这里保存密码、生产环境令牌和私钥。");

            Add("Заметки хранятся только на этом компьютере и шифруются DPAPI. Не храните здесь пароли, боевые токены и приватные ключи.",
                en: "Notes live only on this computer and are encrypted with DPAPI. Do not keep passwords, production tokens or private keys here.",
                uk: "Нотатки зберігаються лише на цьому комп'ютері й шифруються DPAPI. Не зберігайте тут паролі, бойові токени та приватні ключі.",
                de: "Notizen liegen nur auf diesem Computer und sind mit DPAPI verschlüsselt. Bewahren Sie hier keine Passwörter, Produktionstoken oder privaten Schlüssel auf.",
                it: "Le note restano solo su questo computer e sono cifrate con DPAPI. Non conservare qui password, token di produzione o chiavi private.",
                es: "Las notas solo están en este equipo y se cifran con DPAPI. No guarde aquí contraseñas, tokens de producción ni claves privadas.",
                fr: "Les notes ne se trouvent que sur cet ordinateur et sont chiffrées par DPAPI. N'y conservez ni mots de passe, ni jetons de production, ni clés privées.",
                pt: "As notas ficam apenas neste computador e são criptografadas com DPAPI. Não guarde aqui senhas, tokens de produção ou chaves privadas.",
                ar: "تبقى الملاحظات على هذا الحاسوب فقط وتُشفَّر بـ DPAPI. لا تحفظ هنا كلمات المرور أو رموز الإنتاج أو المفاتيح الخاصة.",
                hi: "नोट्स केवल इसी कंप्यूटर पर रहते हैं और DPAPI से एन्क्रिप्ट होते हैं। यहाँ पासवर्ड, उत्पादन टोकन और निजी कुंजियाँ न रखें।",
                bn: "নোটগুলি কেবল এই কম্পিউটারেই থাকে এবং DPAPI দিয়ে এনক্রিপ্ট করা হয়। এখানে পাসওয়ার্ড, প্রোডাকশন টোকেন বা প্রাইভেট কি রাখবেন না।",
                ur: "نوٹس صرف اسی کمپیوٹر پر رہتے ہیں اور DPAPI سے خفیہ کیے جاتے ہیں۔ یہاں پاس ورڈ، پروڈکشن ٹوکن اور نجی کلیدیں محفوظ نہ کریں۔",
                zh: "笔记只保存在这台电脑上，并由 DPAPI 加密。不要在这里保存密码、生产环境令牌和私钥。");

            // ---- The settings tab ----

            Add("Маленький локальный блокнот: по хоткею открыть, бросить фрагмент кода или мысль, закрыть. Текст хранится как есть — без разметки, подсветки и автоформатирования, — поэтому его можно вставить обратно в код без единого изменения.",
                en: "A small local notepad: open it with a chord, drop in a fragment of code or a thought, close it. The text is stored exactly as it is - no markup, no highlighting, no auto-formatting - so it can be pasted back into the code without a single change.",
                uk: "Маленький локальний блокнот: за хоткеєм відкрити, кинути фрагмент коду чи думку, закрити. Текст зберігається як є — без розмітки, підсвічування й автоформатування, — тому його можна вставити назад у код без жодної зміни.",
                de: "Ein kleiner lokaler Notizblock: per Tastenkombination öffnen, einen Codeausschnitt oder Gedanken hineinwerfen, schließen. Der Text wird unverändert gespeichert - ohne Markup, Hervorhebung und Autoformatierung -, sodass er ohne eine einzige Änderung zurück in den Code eingefügt werden kann.",
                it: "Un piccolo blocco note locale: apri con una combinazione, butta dentro un frammento di codice o un pensiero, chiudi. Il testo è conservato così com'è - senza markup, evidenziazione o formattazione automatica - quindi può essere incollato di nuovo nel codice senza una sola modifica.",
                es: "Un pequeño bloc de notas local: ábralo con una combinación, suelte un fragmento de código o una idea, ciérrelo. El texto se guarda tal cual - sin marcado, resaltado ni formato automático -, por lo que puede pegarse de vuelta en el código sin un solo cambio.",
                fr: "Un petit bloc-notes local : ouvrez-le par un raccourci, jetez-y un fragment de code ou une idée, refermez-le. Le texte est conservé tel quel - sans balisage, coloration ni mise en forme automatique - et peut donc être recollé dans le code sans la moindre modification.",
                pt: "Um pequeno bloco de notas local: abra com um atalho, jogue lá um trecho de código ou uma ideia, feche. O texto é guardado exatamente como está - sem marcação, realce ou formatação automática -, então pode ser colado de volta no código sem uma única alteração.",
                ar: "مفكرة محلية صغيرة: افتحها باختصار، وألقِ فيها مقتطف شيفرة أو فكرة، ثم أغلقها. يُحفظ النص كما هو تمامًا - بلا ترميز ولا تلوين ولا تنسيق تلقائي - فيمكن لصقه في الشيفرة مرة أخرى دون أي تغيير.",
                hi: "एक छोटा स्थानीय नोटपैड: हॉटकी से खोलें, कोड का अंश या कोई विचार डालें, बंद करें। पाठ जैसा है वैसा ही सहेजा जाता है - बिना मार्कअप, हाइलाइटिंग और स्वतः-स्वरूपण के - इसलिए उसे बिना एक भी बदलाव के कोड में वापस चिपकाया जा सकता है।",
                bn: "একটি ছোট স্থানীয় নোটপ্যাড: হটকি দিয়ে খুলুন, কোডের অংশ বা একটি ভাবনা রাখুন, বন্ধ করুন। লেখা যেমন আছে ঠিক তেমনই রাখা হয় - মার্কআপ, হাইলাইট বা স্বয়ংক্রিয় বিন্যাস ছাড়াই - তাই এটি একটিও পরিবর্তন ছাড়াই কোডে ফিরিয়ে পেস্ট করা যায়।",
                ur: "ایک چھوٹی مقامی نوٹ بک: ہاٹ کی سے کھولیں، کوڈ کا ٹکڑا یا کوئی خیال ڈالیں، بند کر دیں۔ متن جوں کا توں محفوظ ہوتا ہے - بغیر مارک اپ، ہائی لائٹنگ اور خودکار فارمیٹنگ کے - اس لیے اسے بغیر کسی تبدیلی کے کوڈ میں واپس چسپاں کیا جا سکتا ہے۔",
                zh: "一个小小的本地记事本：用快捷键打开，扔进一段代码或一个想法，关掉。文本原样保存——没有标记、没有语法着色、没有自动排版——因此可以一字不差地贴回代码里。");

            Add("Включить быстрые заметки",
                en: "Enable the quick notes", uk: "Увімкнути швидкі нотатки", de: "Schnellnotizen aktivieren",
                it: "Attiva le note rapide", es: "Activar las notas rápidas", fr: "Activer les notes rapides",
                pt: "Ativar as notas rápidas", ar: "تفعيل الملاحظات السريعة", hi: "त्वरित नोट्स चालू करें",
                bn: "দ্রুত নোট চালু করুন", ur: "فوری نوٹس فعال کریں", zh: "启用快速笔记");

            Add("Пока выключено, CyrFlip не читает и не создаёт файл заметок, в трее нет пункта заметок и комбинация не назначена. Уже сохранённые заметки остаются на диске.",
                en: "While it is off, CyrFlip neither reads nor creates the notes file, the tray has no notes entry and the chord is not bound. Notes already saved stay on disk.",
                uk: "Поки вимкнено, CyrFlip не читає і не створює файл нотаток, у треї немає пункту нотаток, а комбінація не призначена. Уже збережені нотатки залишаються на диску.",
                de: "Solange es aus ist, liest und erstellt CyrFlip die Notizdatei nicht, das Infobereichsmenü hat keinen Notizeintrag und die Tastenkombination ist nicht belegt. Bereits gespeicherte Notizen bleiben auf der Festplatte.",
                it: "Finché è disattivato, CyrFlip non legge né crea il file delle note, l'area di notifica non ha la voce note e la combinazione non è assegnata. Le note già salvate restano sul disco.",
                es: "Mientras está desactivado, CyrFlip no lee ni crea el archivo de notas, la bandeja no tiene entrada de notas y la combinación no está asignada. Las notas ya guardadas permanecen en el disco.",
                fr: "Tant que c'est désactivé, CyrFlip ne lit ni ne crée le fichier des notes, la zone de notification n'a pas d'entrée notes et le raccourci n'est pas attribué. Les notes déjà enregistrées restent sur le disque.",
                pt: "Enquanto estiver desligado, o CyrFlip não lê nem cria o arquivo de notas, a bandeja não tem item de notas e o atalho não está atribuído. As notas já salvas permanecem no disco.",
                ar: "ما دام معطلًا، لا يقرأ CyrFlip ملف الملاحظات ولا ينشئه، ولا يوجد عنصر ملاحظات في منطقة الإشعارات، والاختصار غير معيَّن. تبقى الملاحظات المحفوظة مسبقًا على القرص.",
                hi: "जब तक यह बंद है, CyrFlip नोट्स फ़ाइल न पढ़ता है न बनाता है, ट्रे में नोट्स की प्रविष्टि नहीं होती और संयोजन नियत नहीं होता। पहले सहेजे गए नोट्स डिस्क पर बने रहते हैं।",
                bn: "বন্ধ থাকা অবস্থায় CyrFlip নোট ফাইল পড়েও না, তৈরিও করে না, ট্রেতে নোটের কোনো এন্ট্রি থাকে না এবং কম্বিনেশনও বরাদ্দ হয় না। আগে সংরক্ষিত নোট ডিস্কে থেকে যায়।",
                ur: "جب تک یہ بند ہے، CyrFlip نوٹس فائل نہ پڑھتا ہے نہ بناتا ہے، ٹرے میں نوٹس کا اندراج نہیں ہوتا اور مجموعہ مقرر نہیں ہوتا۔ پہلے سے محفوظ نوٹس ڈسک پر باقی رہتے ہیں۔",
                zh: "关闭时，CyrFlip 既不读取也不创建笔记文件，托盘中没有笔记项，快捷键也不会绑定。已保存的笔记仍留在磁盘上。");

            Add("Как открывать",
                en: "How to open it", uk: "Як відкривати", de: "Wie man sie öffnet", it: "Come aprirle",
                es: "Cómo abrirlas", fr: "Comment les ouvrir", pt: "Como abrir",
                ar: "كيفية الفتح", hi: "कैसे खोलें", bn: "কীভাবে খুলবেন", ur: "کیسے کھولیں", zh: "如何打开");

            Add("Сразу создаёт новую заметку и ставит курсор в поле текста — записать мысль можно, ничего больше не нажимая. Одну комбинацию нельзя отдать двум действиям CyrFlip.",
                en: "Creates a new note at once and puts the caret in the body - the thought can be written down without pressing anything else. One chord cannot be given to two CyrFlip actions.",
                uk: "Одразу створює нову нотатку й ставить курсор у поле тексту — записати думку можна, більше нічого не натискаючи. Одну комбінацію не можна віддати двом діям CyrFlip.",
                de: "Erstellt sofort eine neue Notiz und setzt die Schreibmarke in den Text - der Gedanke lässt sich festhalten, ohne noch etwas zu drücken. Eine Kombination kann nicht zwei CyrFlip-Aktionen gehören.",
                it: "Crea subito una nuova nota e mette il cursore nel testo: il pensiero si annota senza premere altro. Una combinazione non può appartenere a due azioni di CyrFlip.",
                es: "Crea al instante una nota nueva y coloca el cursor en el texto: la idea se anota sin pulsar nada más. Una combinación no puede pertenecer a dos acciones de CyrFlip.",
                fr: "Crée aussitôt une nouvelle note et place le curseur dans le texte : l'idée s'écrit sans rien appuyer d'autre. Une combinaison ne peut pas appartenir à deux actions de CyrFlip.",
                pt: "Cria de imediato uma nota nova e põe o cursor no texto: a ideia se anota sem apertar mais nada. Uma combinação não pode pertencer a duas ações do CyrFlip.",
                ar: "ينشئ ملاحظة جديدة فورًا ويضع المؤشر في النص، فتُدوَّن الفكرة دون الضغط على أي شيء آخر. لا يمكن إسناد التركيبة نفسها إلى إجراءين في CyrFlip.",
                hi: "तुरंत नया नोट बनाता है और कर्सर पाठ में रख देता है — विचार लिखने के लिए और कुछ दबाने की ज़रूरत नहीं। एक ही संयोजन CyrFlip की दो क्रियाओं को नहीं दिया जा सकता।",
                bn: "সঙ্গে সঙ্গে নতুন নোট তৈরি করে কার্সার লেখার ঘরে রাখে — ভাবনাটি লিখতে আর কিছু চাপতে হয় না। একটি কম্বিনেশন CyrFlip-এর দুটি কাজে দেওয়া যায় না।",
                ur: "فوراً نیا نوٹ بناتا ہے اور کرسر متن میں رکھ دیتا ہے — خیال لکھنے کے لیے مزید کچھ دبانے کی ضرورت نہیں۔ ایک مجموعہ CyrFlip کے دو کاموں کو نہیں دیا جا سکتا۔",
                zh: "立即新建一条笔记并把光标放进正文——不用再按别的就能把想法写下来。同一个组合键不能同时分配给两个 CyrFlip 动作。");

            Add("Открыть заметки",
                en: "Open the notes", uk: "Відкрити нотатки", de: "Notizen öffnen", it: "Apri le note",
                es: "Abrir las notas", fr: "Ouvrir les notes", pt: "Abrir as notas",
                ar: "فتح الملاحظات", hi: "नोट्स खोलें", bn: "নোট খুলুন", ur: "نوٹس کھولیں", zh: "打开笔记");

            Add("То же, что пункт «Быстрые заметки» в меню трея: открывает список без создания новой заметки.",
                en: "The same as the \"Quick notes\" entry in the tray menu: it opens the list without creating a new note.",
                uk: "Те саме, що пункт «Швидкі нотатки» в меню трея: відкриває список, не створюючи нову нотатку.",
                de: "Dasselbe wie der Eintrag „Schnellnotizen“ im Infobereichsmenü: Er öffnet die Liste, ohne eine neue Notiz anzulegen.",
                it: "Come la voce «Note rapide» nel menu dell'area di notifica: apre l'elenco senza creare una nota nuova.",
                es: "Lo mismo que la entrada «Notas rápidas» del menú de la bandeja: abre la lista sin crear una nota nueva.",
                fr: "Comme l'entrée « Notes rapides » du menu de la zone de notification : ouvre la liste sans créer de nouvelle note.",
                pt: "O mesmo que o item «Notas rápidas» no menu da bandeja: abre a lista sem criar uma nota nova.",
                ar: "مثل عنصر «ملاحظات سريعة» في قائمة منطقة الإشعارات: يفتح القائمة دون إنشاء ملاحظة جديدة.",
                hi: "ट्रे मेनू की «त्वरित नोट्स» प्रविष्टि के समान: नया नोट बनाए बिना सूची खोलता है।",
                bn: "ট্রে মেনুর «দ্রুত নোট» এন্ট্রির মতোই: নতুন নোট না বানিয়ে তালিকা খোলে।",
                ur: "ٹرے مینو کے «فوری نوٹس» اندراج جیسا: نیا نوٹ بنائے بغیر فہرست کھولتا ہے۔",
                zh: "与托盘菜单中的「快速笔记」相同：打开列表，不新建笔记。");

            Add("Редактор",
                en: "Editor", uk: "Редактор", de: "Editor", it: "Editor",
                es: "Editor", fr: "Éditeur", pt: "Editor",
                ar: "المحرر", hi: "संपादक", bn: "সম্পাদক", ur: "ایڈیٹر", zh: "编辑器");

            Add("Переносить длинные строки в редакторе",
                en: "Wrap long lines in the editor", uk: "Переносити довгі рядки в редакторі",
                de: "Lange Zeilen im Editor umbrechen", it: "Manda a capo le righe lunghe nell'editor",
                es: "Ajustar las líneas largas en el editor", fr: "Renvoyer les longues lignes à la ligne dans l'éditeur",
                pt: "Quebrar linhas longas no editor", ar: "التفاف الأسطر الطويلة في المحرر",
                hi: "संपादक में लंबी पंक्तियाँ लपेटें", bn: "সম্পাদকে লম্বা লাইন মুড়ে দিন",
                ur: "ایڈیٹر میں لمبی سطریں لپیٹیں", zh: "在编辑器中自动换行");

            Add("По умолчанию выключено: тело заметки чаще код, чем проза, а перенесённую строку кода приходится мысленно собирать обратно. Включите, если пишете здесь текст.",
                en: "Off by default: the body of a note is more often code than prose, and a wrapped line of code is one you have to mentally un-wrap. Turn it on if you write prose here.",
                uk: "Типово вимкнено: тіло нотатки частіше код, ніж проза, а перенесений рядок коду доводиться подумки збирати назад. Увімкніть, якщо пишете тут текст.",
                de: "Standardmäßig aus: Der Inhalt einer Notiz ist häufiger Code als Prosa, und eine umbrochene Codezeile muss man im Kopf wieder zusammensetzen. Schalten Sie es ein, wenn Sie hier Text schreiben.",
                it: "Disattivato per impostazione predefinita: il corpo di una nota è più spesso codice che prosa, e una riga di codice mandata a capo va ricomposta a mente. Attivalo se qui scrivi testo.",
                es: "Desactivado de forma predeterminada: el cuerpo de una nota es más a menudo código que prosa, y una línea de código ajustada hay que recomponerla mentalmente. Actívelo si aquí escribe texto.",
                fr: "Désactivé par défaut : le corps d'une note est plus souvent du code que de la prose, et une ligne de code renvoyée à la ligne doit être reconstituée mentalement. Activez-le si vous écrivez du texte ici.",
                pt: "Desligado por padrão: o corpo de uma nota é mais vezes código do que prosa, e uma linha de código quebrada precisa ser remontada mentalmente. Ligue se você escreve texto aqui.",
                ar: "معطَّل افتراضيًا: متن الملاحظة شيفرة أكثر مما هو نثر، وسطر الشيفرة الملتف يلزم إعادة تجميعه ذهنيًا. فعِّله إن كنت تكتب هنا نصًا.",
                hi: "डिफ़ॉल्ट रूप से बंद: नोट का मुख्य भाग गद्य से ज़्यादा अक्सर कोड होता है, और लपेटी गई कोड-पंक्ति को मन में जोड़ना पड़ता है। यदि आप यहाँ गद्य लिखते हैं तो इसे चालू करें।",
                bn: "ডিফল্টে বন্ধ: নোটের মূল অংশ গদ্যের চেয়ে বেশি বার কোড হয়, আর মুড়ে যাওয়া কোডের লাইন মনে মনে জোড়া লাগাতে হয়। এখানে গদ্য লিখলে চালু করুন।",
                ur: "بطور طے شدہ بند: نوٹ کا متن نثر سے زیادہ اکثر کوڈ ہوتا ہے، اور لپٹی ہوئی کوڈ کی سطر ذہن میں دوبارہ جوڑنی پڑتی ہے۔ اگر آپ یہاں نثر لکھتے ہیں تو اسے فعال کریں۔",
                zh: "默认关闭：笔记正文多半是代码而非散文，而折行的代码要在脑子里再拼回去。若你在这里写文字，请打开它。");

            Add("Экспорт и хранение",
                en: "Export and storage", uk: "Експорт і зберігання", de: "Export und Speicherung",
                it: "Esportazione e archiviazione", es: "Exportación y almacenamiento", fr: "Export et stockage",
                pt: "Exportação e armazenamento", ar: "التصدير والتخزين", hi: "निर्यात और भंडारण",
                bn: "রপ্তানি ও সংরক্ষণ", ur: "برآمد اور ذخیرہ", zh: "导出与存储");

            Add("Добавлять даты при экспорте",
                en: "Add the dates when exporting", uk: "Додавати дати під час експорту",
                de: "Beim Export die Daten hinzufügen", it: "Aggiungi le date all'esportazione",
                es: "Añadir las fechas al exportar", fr: "Ajouter les dates lors de l'export",
                pt: "Adicionar as datas ao exportar", ar: "إضافة التواريخ عند التصدير",
                hi: "निर्यात करते समय तिथियाँ जोड़ें", bn: "রপ্তানির সময় তারিখ যোগ করুন",
                ur: "برآمد کرتے وقت تاریخیں شامل کریں", zh: "导出时附上日期");

            Add("По умолчанию в экспорт идут только имя и текст: так фрагмент кода вставляется обратно без правок. С флажком к каждой заметке добавляется дата создания.",
                en: "By default only the name and the text are exported, so a fragment of code pastes back without edits. With the box ticked, each note also carries its creation date.",
                uk: "Типово в експорт ідуть лише ім'я та текст: так фрагмент коду вставляється назад без правок. З прапорцем до кожної нотатки додається дата створення.",
                de: "Standardmäßig werden nur Name und Text exportiert, damit ein Codeausschnitt ohne Änderungen wieder eingefügt werden kann. Mit gesetztem Häkchen trägt jede Notiz zusätzlich ihr Erstellungsdatum.",
                it: "Per impostazione predefinita si esportano solo il nome e il testo, così un frammento di codice si reincolla senza modifiche. Con la casella spuntata ogni nota porta anche la data di creazione.",
                es: "De forma predeterminada solo se exportan el nombre y el texto, así un fragmento de código se pega de vuelta sin cambios. Con la casilla marcada, cada nota lleva además su fecha de creación.",
                fr: "Par défaut, seuls le nom et le texte sont exportés, afin qu'un fragment de code se recolle sans retouche. Case cochée, chaque note porte aussi sa date de création.",
                pt: "Por padrão só o nome e o texto são exportados, assim um trecho de código cola de volta sem edições. Com a caixa marcada, cada nota leva também a data de criação.",
                ar: "افتراضيًا يُصدَّر الاسم والنص فقط، فيُلصَق مقتطف الشيفرة مجددًا دون تعديل. وبتحديد المربع تحمل كل ملاحظة أيضًا تاريخ إنشائها.",
                hi: "डिफ़ॉल्ट रूप से केवल नाम और पाठ निर्यात होते हैं, ताकि कोड का अंश बिना संपादन के वापस चिपके। बॉक्स चुनने पर हर नोट के साथ उसकी निर्माण तिथि भी जाती है।",
                bn: "ডিফল্টে কেবল নাম ও লেখা রপ্তানি হয়, যাতে কোডের অংশ সম্পাদনা ছাড়াই ফিরিয়ে পেস্ট করা যায়। বাক্সে টিক দিলে প্রতিটি নোটের সঙ্গে তার তৈরির তারিখও যায়।",
                ur: "بطور طے شدہ صرف نام اور متن برآمد ہوتے ہیں، تاکہ کوڈ کا ٹکڑا بغیر ترمیم کے واپس چسپاں ہو۔ خانہ منتخب کرنے پر ہر نوٹ کے ساتھ اس کی تخلیق کی تاریخ بھی جاتی ہے۔",
                zh: "默认只导出名称和正文，这样代码片段可以原样贴回。勾选后，每条笔记还会附上创建日期。");

            Add("Экспортировать все заметки в Markdown...",
                en: "Export every note to Markdown...", uk: "Експортувати всі нотатки в Markdown...",
                de: "Alle Notizen nach Markdown exportieren...", it: "Esporta tutte le note in Markdown...",
                es: "Exportar todas las notas a Markdown...", fr: "Exporter toutes les notes en Markdown...",
                pt: "Exportar todas as notas para Markdown...", ar: "تصدير كل الملاحظات إلى Markdown...",
                hi: "सभी नोट्स Markdown में निर्यात करें...", bn: "সব নোট Markdown-এ রপ্তানি করুন...",
                ur: "تمام نوٹس Markdown میں برآمد کریں...", zh: "把全部笔记导出为 Markdown...");

            Add("Один файл со всеми заметками по порядку. Экспортированный файл уже не защищён DPAPI — его прочитает любой, у кого есть доступ к папке.",
                en: "One file with every note in order. The exported file is no longer protected by DPAPI - anyone with access to the folder can read it.",
                uk: "Один файл з усіма нотатками по порядку. Експортований файл уже не захищений DPAPI — його прочитає будь-хто, хто має доступ до теки.",
                de: "Eine Datei mit allen Notizen der Reihe nach. Die exportierte Datei ist nicht mehr durch DPAPI geschützt - jeder mit Zugriff auf den Ordner kann sie lesen.",
                it: "Un solo file con tutte le note in ordine. Il file esportato non è più protetto da DPAPI: può leggerlo chiunque abbia accesso alla cartella.",
                es: "Un archivo con todas las notas en orden. El archivo exportado ya no está protegido por DPAPI: puede leerlo cualquiera que tenga acceso a la carpeta.",
                fr: "Un seul fichier contenant toutes les notes dans l'ordre. Le fichier exporté n'est plus protégé par DPAPI : toute personne ayant accès au dossier peut le lire.",
                pt: "Um arquivo com todas as notas em ordem. O arquivo exportado já não é protegido por DPAPI: qualquer pessoa com acesso à pasta pode lê-lo.",
                ar: "ملف واحد يضم كل الملاحظات بالترتيب. الملف المُصدَّر لم يعد محميًا بـ DPAPI، ويمكن لأي شخص لديه وصول إلى المجلد قراءته.",
                hi: "क्रम में सभी नोट्स वाली एक फ़ाइल। निर्यात की गई फ़ाइल अब DPAPI से सुरक्षित नहीं है — फ़ोल्डर तक पहुँच रखने वाला कोई भी उसे पढ़ सकता है।",
                bn: "ক্রমানুসারে সব নোট নিয়ে একটি ফাইল। রপ্তানি করা ফাইল আর DPAPI দিয়ে সুরক্ষিত নয় — ফোল্ডারে প্রবেশাধিকার থাকা যে কেউ এটি পড়তে পারবে।",
                ur: "ترتیب سے تمام نوٹس پر مشتمل ایک فائل۔ برآمد شدہ فائل اب DPAPI سے محفوظ نہیں — فولڈر تک رسائی رکھنے والا کوئی بھی اسے پڑھ سکتا ہے۔",
                zh: "一个文件，按顺序包含全部笔记。导出的文件不再受 DPAPI 保护——任何能访问该文件夹的人都能读取它。");

            Add("Удалить все быстрые заметки",
                en: "Delete every quick note", uk: "Видалити всі швидкі нотатки",
                de: "Alle Schnellnotizen löschen", it: "Elimina tutte le note rapide",
                es: "Eliminar todas las notas rápidas", fr: "Supprimer toutes les notes rapides",
                pt: "Excluir todas as notas rápidas", ar: "حذف كل الملاحظات السريعة",
                hi: "सभी त्वरित नोट्स हटाएँ", bn: "সব দ্রুত নোট মুছুন", ur: "تمام فوری نوٹس حذف کریں", zh: "删除全部快速笔记");

            Add("Удаляет все заметки, журнал и его резервную копию. Отменить это нельзя.",
                en: "Deletes every note, the journal and its backup. There is no undo.",
                uk: "Видаляє всі нотатки, журнал і його резервну копію. Скасувати це не можна.",
                de: "Löscht alle Notizen, das Journal und dessen Sicherung. Es gibt kein Rückgängig.",
                it: "Elimina tutte le note, il registro e la sua copia di riserva. Non è possibile annullare.",
                es: "Elimina todas las notas, el diario y su copia de seguridad. No se puede deshacer.",
                fr: "Supprime toutes les notes, le journal et sa sauvegarde. Il n'y a pas d'annulation.",
                pt: "Exclui todas as notas, o diário e sua cópia de segurança. Não há como desfazer.",
                ar: "يحذف كل الملاحظات والسجل ونسخته الاحتياطية. لا يمكن التراجع.",
                hi: "सभी नोट्स, जर्नल और उसका बैकअप हटा देता है। पूर्ववत करना संभव नहीं है।",
                bn: "সব নোট, জার্নাল ও তার ব্যাকআপ মুছে দেয়। পূর্বাবস্থায় ফেরানো যাবে না।",
                ur: "تمام نوٹس، جرنل اور اس کا بیک اپ حذف کر دیتا ہے۔ واپسی ممکن نہیں۔",
                zh: "删除全部笔记、日志及其备份。无法撤销。");

            Add("Заметки лежат только на этом компьютере и шифруются Windows DPAPI для вашей учётной записи. CyrFlip не отправляет их в сеть, не индексирует их поиском Windows и не превращает записи истории буфера в заметки — для этого есть отдельная команда. Это не хранилище секретов: не сохраняйте здесь пароли, боевые токены и приватные ключи.",
                en: "Notes lie only on this computer and are encrypted with Windows DPAPI for your account. CyrFlip does not send them anywhere, does not index them with Windows Search and does not turn clipboard-history entries into notes - there is a separate command for that. This is not a secret store: do not keep passwords, production tokens or private keys here.",
                uk: "Нотатки лежать лише на цьому комп'ютері й шифруються Windows DPAPI для вашого облікового запису. CyrFlip не надсилає їх у мережу, не індексує їх пошуком Windows і не перетворює записи історії буфера на нотатки — для цього є окрема команда. Це не сховище секретів: не зберігайте тут паролі, бойові токени та приватні ключі.",
                de: "Notizen liegen nur auf diesem Computer und werden mit der Windows-DPAPI für Ihr Konto verschlüsselt. CyrFlip sendet sie nirgendwohin, indiziert sie nicht mit der Windows-Suche und macht aus Einträgen des Zwischenablageverlaufs keine Notizen - dafür gibt es einen eigenen Befehl. Dies ist kein Geheimnisspeicher: Bewahren Sie hier keine Passwörter, Produktionstoken oder privaten Schlüssel auf.",
                it: "Le note restano solo su questo computer e sono cifrate con la DPAPI di Windows per il tuo account. CyrFlip non le invia da nessuna parte, non le indicizza con la ricerca di Windows e non trasforma le voci della cronologia degli appunti in note: per questo c'è un comando a parte. Questo non è un archivio di segreti: non conservare qui password, token di produzione o chiavi private.",
                es: "Las notas están solo en este equipo y se cifran con la DPAPI de Windows para su cuenta. CyrFlip no las envía a ninguna parte, no las indexa con la búsqueda de Windows y no convierte las entradas del historial del portapapeles en notas: para eso hay un comando aparte. Esto no es un almacén de secretos: no guarde aquí contraseñas, tokens de producción ni claves privadas.",
                fr: "Les notes ne se trouvent que sur cet ordinateur et sont chiffrées par la DPAPI de Windows pour votre compte. CyrFlip ne les envoie nulle part, ne les indexe pas avec la recherche Windows et ne transforme pas les entrées de l'historique du presse-papiers en notes : il existe une commande distincte pour cela. Ceci n'est pas un coffre à secrets : n'y conservez ni mots de passe, ni jetons de production, ni clés privées.",
                pt: "As notas ficam apenas neste computador e são criptografadas com a DPAPI do Windows para a sua conta. O CyrFlip não as envia a lugar nenhum, não as indexa com a pesquisa do Windows e não transforma entradas do histórico da área de transferência em notas - há um comando separado para isso. Isto não é um cofre de segredos: não guarde aqui senhas, tokens de produção ou chaves privadas.",
                ar: "تبقى الملاحظات على هذا الحاسوب فقط وتُشفَّر بـ DPAPI في Windows لحسابك. لا يرسلها CyrFlip إلى أي مكان، ولا يفهرسها ببحث Windows، ولا يحوّل عناصر سجل الحافظة إلى ملاحظات - لذلك أمر منفصل. هذا ليس مخزنًا للأسرار: لا تحفظ هنا كلمات المرور أو رموز الإنتاج أو المفاتيح الخاصة.",
                hi: "नोट्स केवल इसी कंप्यूटर पर रहते हैं और आपके खाते के लिए Windows DPAPI से एन्क्रिप्ट होते हैं। CyrFlip उन्हें कहीं नहीं भेजता, Windows खोज से अनुक्रमित नहीं करता और क्लिपबोर्ड इतिहास की प्रविष्टियों को नोट्स में नहीं बदलता — उसके लिए अलग आदेश है। यह गोपनीय जानकारी का भंडार नहीं है: यहाँ पासवर्ड, उत्पादन टोकन और निजी कुंजियाँ न रखें।",
                bn: "নোটগুলি কেবল এই কম্পিউটারেই থাকে এবং আপনার অ্যাকাউন্টের জন্য Windows DPAPI দিয়ে এনক্রিপ্ট করা হয়। CyrFlip সেগুলি কোথাও পাঠায় না, Windows অনুসন্ধানে ইনডেক্স করে না এবং ক্লিপবোর্ড ইতিহাসের এন্ট্রিকে নোটে পরিণত করে না — তার জন্য আলাদা কমান্ড আছে। এটি গোপন তথ্যের ভাণ্ডার নয়: এখানে পাসওয়ার্ড, প্রোডাকশন টোকেন বা প্রাইভেট কি রাখবেন না।",
                ur: "نوٹس صرف اسی کمپیوٹر پر رہتے ہیں اور آپ کے اکاؤنٹ کے لیے Windows DPAPI سے خفیہ کیے جاتے ہیں۔ CyrFlip انہیں کہیں نہیں بھیجتا، Windows تلاش سے فہرست بند نہیں کرتا اور کلپ بورڈ تاریخ کے اندراجات کو نوٹس میں تبدیل نہیں کرتا — اس کے لیے الگ کمانڈ ہے۔ یہ رازوں کا ذخیرہ نہیں ہے: یہاں پاس ورڈ، پروڈکشن ٹوکن اور نجی کلیدیں محفوظ نہ کریں۔",
                zh: "笔记只存放在这台电脑上，并用你账户的 Windows DPAPI 加密。CyrFlip 不会把它们发往任何地方，不用 Windows 搜索为其建立索引，也不会把剪贴板历史条目变成笔记——那有单独的命令。这不是密钥保管库：不要在这里保存密码、生产环境令牌和私钥。");
        }
    }
}
