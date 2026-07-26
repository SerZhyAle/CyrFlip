namespace CyrFlip
{
    /// <summary>Clipboard tab, the history manager strip, the search window and the tray balloons.</summary>
    internal static partial class Localization
    {
        private static void AddClipboardStrings()
        {
            Add("Буфер обмена",
                en: "Clipboard", uk: "Буфер обміну", de: "Zwischenablage", it: "Appunti",
                es: "Portapapeles", fr: "Presse-papiers", pt: "Área de transferência",
                ar: "الحافظة", hi: "क्लिपबोर्ड", bn: "ক্লিপবোর্ড", ur: "کلپ بورڈ", zh: "剪贴板");

            Add("История хранится только локально и шифруется Windows DPAPI для вашей учётной записи.",
                en: "History is stored locally only and encrypted with Windows DPAPI for your account.",
                uk: "Історія зберігається лише локально та шифрується Windows DPAPI для вашого облікового запису.",
                de: "Der Verlauf wird nur lokal gespeichert und mit Windows DPAPI für Ihr Konto verschlüsselt.",
                it: "La cronologia è salvata solo in locale e cifrata con Windows DPAPI per il tuo account.",
                es: "El historial se guarda solo localmente y se cifra con Windows DPAPI para tu cuenta.",
                fr: "L'historique est stocké uniquement en local et chiffré avec Windows DPAPI pour votre compte.",
                pt: "O histórico é armazenado apenas localmente e criptografado com o Windows DPAPI para a sua conta.",
                ar: "يُحفظ السجل محليًا فقط ويُشفَّر بواسطة Windows DPAPI لحسابك.",
                hi: "इतिहास केवल स्थानीय रूप से रखा जाता है और आपके खाते के लिए Windows DPAPI से एन्क्रिप्ट किया जाता है।",
                bn: "ইতিহাস কেবল স্থানীয়ভাবে সংরক্ষিত হয় এবং আপনার অ্যাকাউন্টের জন্য Windows DPAPI দিয়ে এনক্রিপ্ট করা হয়।",
                ur: "تاریخ صرف مقامی طور پر محفوظ ہوتی ہے اور آپ کے اکاؤنٹ کے لیے Windows DPAPI سے خفیہ کی جاتی ہے۔",
                zh: "历史仅保存在本地，并使用 Windows DPAPI 针对你的账户加密。");

            Add("Включить историю буфера",
                en: "Enable clipboard history", uk: "Увімкнути історію буфера",
                de: "Zwischenablage-Verlauf aktivieren", it: "Attiva la cronologia degli appunti",
                es: "Activar el historial del portapapeles", fr: "Activer l'historique du presse-papiers",
                pt: "Ativar o histórico da área de transferência", ar: "تفعيل سجل الحافظة",
                hi: "क्लिपबोर्ड इतिहास चालू करें", bn: "ক্লিপবোর্ড ইতিহাস চালু করুন",
                ur: "کلپ بورڈ تاریخ آن کریں", zh: "启用剪贴板历史");

            Add("CyrFlip сохраняет только Unicode-текст, скопированный после включения функции. Изображения, файлы и другие форматы не записываются.",
                en: "CyrFlip saves only Unicode text copied after this feature is enabled. Images, files and other formats are not stored.",
                uk: "CyrFlip зберігає лише Unicode-текст, скопійований після ввімкнення функції. Зображення, файли та інші формати не записуються.",
                de: "CyrFlip speichert nur Unicode-Text, der nach dem Aktivieren dieser Funktion kopiert wurde. Bilder, Dateien und andere Formate werden nicht gespeichert.",
                it: "CyrFlip salva solo testo Unicode copiato dopo l'attivazione della funzione. Immagini, file e altri formati non vengono registrati.",
                es: "CyrFlip guarda solo texto Unicode copiado después de activar la función. Las imágenes, los archivos y otros formatos no se registran.",
                fr: "CyrFlip n'enregistre que du texte Unicode copié après l'activation de la fonction. Les images, fichiers et autres formats ne sont pas conservés.",
                pt: "O CyrFlip salva apenas texto Unicode copiado depois que o recurso é ativado. Imagens, arquivos e outros formatos não são gravados.",
                ar: "يحفظ CyrFlip نص Unicode المنسوخ بعد تفعيل هذه الميزة فقط. أما الصور والملفات والتنسيقات الأخرى فلا تُحفظ.",
                hi: "CyrFlip केवल वह Unicode पाठ सहेजता है जो सुविधा चालू करने के बाद कॉपी किया गया हो। छवियाँ, फ़ाइलें और अन्य प्रारूप दर्ज नहीं होते।",
                bn: "সুবিধাটি চালু করার পরে কপি করা কেবল Unicode টেক্সটই CyrFlip সংরক্ষণ করে। ছবি, ফাইল ও অন্যান্য ফরম্যাট রাখা হয় না।",
                ur: "CyrFlip صرف وہ Unicode متن محفوظ کرتا ہے جو یہ خصوصیت آن کرنے کے بعد کاپی ہوا ہو۔ تصاویر، فائلیں اور دیگر فارمیٹس محفوظ نہیں ہوتے۔",
                zh: "CyrFlip 只保存启用该功能之后复制的 Unicode 文本。图片、文件及其他格式不会被记录。");

            Add("Приостановить захват истории",
                en: "Pause history capture", uk: "Призупинити захоплення історії",
                de: "Verlaufsaufzeichnung pausieren", it: "Sospendi la registrazione della cronologia",
                es: "Pausar la captura del historial", fr: "Suspendre la capture de l'historique",
                pt: "Pausar a captura do histórico", ar: "إيقاف تسجيل السجل مؤقتًا",
                hi: "इतिहास कैप्चर रोकें", bn: "ইতিহাস সংগ্রহ থামান",
                ur: "تاریخ محفوظ کرنا روکیں", zh: "暂停记录历史");

            Add("Временно прекращает захват новых копирований, не удаляя уже сохранённую историю. Полезно при работе с паролями и личными данными.",
                en: "Temporarily stops capturing new copies without deleting saved history. Useful while handling passwords or private data.",
                uk: "Тимчасово зупиняє захоплення нових копій, не видаляючи збережену історію. Корисно під час роботи з паролями чи приватними даними.",
                de: "Stoppt vorübergehend die Aufzeichnung neuer Kopien, ohne den gespeicherten Verlauf zu löschen. Praktisch beim Umgang mit Kennwörtern oder privaten Daten.",
                it: "Interrompe temporaneamente la registrazione delle nuove copie senza cancellare la cronologia salvata. Utile mentre gestisci password o dati personali.",
                es: "Detiene temporalmente la captura de nuevas copias sin borrar el historial guardado. Útil cuando manejas contraseñas o datos personales.",
                fr: "Interrompt temporairement la capture des nouvelles copies sans supprimer l'historique enregistré. Pratique quand vous manipulez des mots de passe ou des données personnelles.",
                pt: "Interrompe temporariamente a captura de novas cópias sem apagar o histórico salvo. Útil ao lidar com senhas ou dados pessoais.",
                ar: "يوقف مؤقتًا تسجيل عمليات النسخ الجديدة دون حذف السجل المحفوظ. مفيد أثناء التعامل مع كلمات المرور أو البيانات الشخصية.",
                hi: "सहेजे गए इतिहास को हटाए बिना नई कॉपी दर्ज करना अस्थायी रूप से रोक देता है। पासवर्ड या निजी डेटा के साथ काम करते समय उपयोगी।",
                bn: "সংরক্ষিত ইতিহাস মুছে না ফেলেই নতুন কপি সংগ্রহ সাময়িকভাবে বন্ধ করে। পাসওয়ার্ড বা ব্যক্তিগত তথ্য নিয়ে কাজ করার সময় সুবিধাজনক।",
                ur: "محفوظ شدہ تاریخ مٹائے بغیر نئی کاپیوں کا اندراج عارضی طور پر روک دیتا ہے۔ پاس ورڈ یا ذاتی ڈیٹا کے ساتھ کام کرتے وقت مفید۔",
                zh: "暂时停止记录新的复制内容，但不删除已保存的历史。处理密码或私密数据时很有用。");

            Add("Показывать окно менеджера буфера при запуске",
                en: "Show clipboard manager on startup", uk: "Показувати менеджер буфера під час запуску",
                de: "Zwischenablage-Manager beim Start anzeigen", it: "Mostra il gestore appunti all'avvio",
                es: "Mostrar el gestor del portapapeles al iniciar",
                fr: "Afficher le gestionnaire du presse-papiers au démarrage",
                pt: "Mostrar o gerenciador da área de transferência ao iniciar",
                ar: "إظهار مدير الحافظة عند بدء التشغيل", hi: "शुरू होने पर क्लिपबोर्ड प्रबंधक दिखाएँ",
                bn: "চালু হওয়ার সময় ক্লিপবোর্ড ম্যানেজার দেখান", ur: "شروع ہونے پر کلپ بورڈ مینیجر دکھائیں",
                zh: "启动时显示剪贴板管理器");

            Add("Запоминает, открыто ли окно менеджера буфера: если вы его закрыли, при следующем запуске оно останется закрытым (история всё равно ведётся в фоне).",
                en: "Remembers whether the clipboard manager window is open: if you closed it, it stays closed on the next launch (history is still captured in the background).",
                uk: "Запам'ятовує, чи відкрите вікно менеджера буфера: якщо ви його закрили, під час наступного запуску воно лишиться закритим (історія все одно ведеться у фоні).",
                de: "Merkt sich, ob das Fenster des Zwischenablage-Managers offen ist: Haben Sie es geschlossen, bleibt es beim nächsten Start geschlossen (der Verlauf wird trotzdem im Hintergrund geführt).",
                it: "Ricorda se la finestra del gestore appunti è aperta: se l'hai chiusa, resterà chiusa al prossimo avvio (la cronologia continua comunque in background).",
                es: "Recuerda si la ventana del gestor del portapapeles está abierta: si la cerraste, seguirá cerrada en el próximo inicio (el historial se sigue registrando en segundo plano).",
                fr: "Mémorise si la fenêtre du gestionnaire du presse-papiers est ouverte : si vous l'avez fermée, elle le restera au prochain démarrage (l'historique continue en arrière-plan).",
                pt: "Lembra se a janela do gerenciador da área de transferência está aberta: se você a fechou, ela continua fechada na próxima execução (o histórico continua sendo registrado em segundo plano).",
                ar: "يتذكّر ما إذا كانت نافذة مدير الحافظة مفتوحة: إذا أغلقتها ستبقى مغلقة عند التشغيل التالي (ويستمر تسجيل السجل في الخلفية).",
                hi: "याद रखता है कि क्लिपबोर्ड प्रबंधक की विंडो खुली है या नहीं: यदि आपने उसे बंद किया था, तो अगली बार भी बंद ही रहेगी (इतिहास फिर भी पृष्ठभूमि में दर्ज होता रहता है)।",
                bn: "ক্লিপবোর্ড ম্যানেজারের উইন্ডো খোলা আছে কি না মনে রাখে: আপনি বন্ধ করে থাকলে পরের বারও বন্ধই থাকবে (ইতিহাস তবুও ব্যাকগ্রাউন্ডে সংগ্রহ হয়)।",
                ur: "یاد رکھتا ہے کہ کلپ بورڈ مینیجر کی ونڈو کھلی ہے یا نہیں: اگر آپ نے بند کی تھی تو اگلی بار بھی بند رہے گی (تاریخ پھر بھی پس منظر میں محفوظ ہوتی رہتی ہے)۔",
                zh: "记住剪贴板管理器窗口是否打开：如果你关闭了它，下次启动时仍保持关闭（历史仍会在后台记录）。");

            Add("Прозрачность окна истории:",
                en: "History window transparency:", uk: "Прозорість вікна історії:",
                de: "Transparenz des Verlaufsfensters:", it: "Trasparenza della finestra cronologia:",
                es: "Transparencia de la ventana del historial:", fr: "Transparence de la fenêtre d'historique :",
                pt: "Transparência da janela do histórico:", ar: "شفافية نافذة السجل:",
                hi: "इतिहास विंडो की पारदर्शिता:", bn: "ইতিহাস উইন্ডোর স্বচ্ছতা:",
                ur: "تاریخ ونڈو کی شفافیت:", zh: "历史窗口透明度：");

            Add("Задаёт прозрачность плавающего окна истории от 30% до 100%. Значение применяется сразу.",
                en: "Sets the floating history window opacity from 30% to 100%. Applied immediately.",
                uk: "Задає прозорість плаваючого вікна історії від 30% до 100%. Застосовується одразу.",
                de: "Legt die Deckkraft des schwebenden Verlaufsfensters von 30 % bis 100 % fest. Wirkt sofort.",
                it: "Imposta l'opacità della finestra flottante della cronologia dal 30% al 100%. Si applica subito.",
                es: "Ajusta la opacidad de la ventana flotante del historial del 30% al 100%. Se aplica de inmediato.",
                fr: "Définit l'opacité de la fenêtre flottante d'historique de 30 % à 100 %. Effet immédiat.",
                pt: "Define a opacidade da janela flutuante do histórico de 30% a 100%. Aplicado imediatamente.",
                ar: "يضبط شفافية نافذة السجل العائمة من 30% إلى 100%. يُطبَّق فورًا.",
                hi: "तैरती इतिहास विंडो की अपारदर्शिता 30% से 100% तक सेट करता है। तुरंत लागू होता है।",
                bn: "ভাসমান ইতিহাস উইন্ডোর অস্বচ্ছতা ৩০% থেকে ১০০% পর্যন্ত নির্ধারণ করে। সঙ্গে সঙ্গে কার্যকর হয়।",
                ur: "تیرتی تاریخ ونڈو کی دھندلاہٹ 30% سے 100% تک مقرر کرتا ہے۔ فوراً لاگو ہوتا ہے۔",
                zh: "将浮动历史窗口的不透明度设为 30% 到 100%，立即生效。");

            Add("Поиск по истории",
                en: "Search history", uk: "Пошук в історії", de: "Verlauf durchsuchen",
                it: "Cerca nella cronologia", es: "Buscar en el historial", fr: "Rechercher dans l'historique",
                pt: "Pesquisar no histórico", ar: "البحث في السجل", hi: "इतिहास में खोजें",
                bn: "ইতিহাসে খুঁজুন", ur: "تاریخ میں تلاش کریں", zh: "搜索历史");

            Add("Открывает отдельное окно поиска по фрагменту текста. Для поиска нужно ввести не менее трёх символов.",
                en: "Opens a separate window to search by a text fragment. Enter at least three characters to search.",
                uk: "Відкриває окреме вікно для пошуку за фрагментом тексту. Для пошуку введіть щонайменше три символи.",
                de: "Öffnet ein eigenes Fenster für die Suche nach einem Textausschnitt. Geben Sie mindestens drei Zeichen ein.",
                it: "Apre una finestra separata per cercare un frammento di testo. Servono almeno tre caratteri.",
                es: "Abre una ventana aparte para buscar por un fragmento de texto. Escribe al menos tres caracteres.",
                fr: "Ouvre une fenêtre distincte pour rechercher un fragment de texte. Saisissez au moins trois caractères.",
                pt: "Abre uma janela separada para pesquisar por um trecho de texto. Digite pelo menos três caracteres.",
                ar: "يفتح نافذة منفصلة للبحث بجزء من النص. أدخل ثلاثة أحرف على الأقل للبحث.",
                hi: "पाठ के अंश से खोजने के लिए एक अलग विंडो खोलता है। खोज के लिए कम से कम तीन अक्षर दर्ज करें।",
                bn: "লেখার অংশ দিয়ে খোঁজার জন্য আলাদা উইন্ডো খোলে। খুঁজতে অন্তত তিনটি অক্ষর লিখুন।",
                ur: "متن کے ٹکڑے سے تلاش کے لیے الگ ونڈو کھولتا ہے۔ تلاش کے لیے کم از کم تین حروف لکھیں۔",
                zh: "打开一个独立窗口，按文本片段搜索。至少输入三个字符才能搜索。");

            Add("Очистить всю историю",
                en: "Clear all history", uk: "Очистити всю історію", de: "Gesamten Verlauf löschen",
                it: "Cancella tutta la cronologia", es: "Borrar todo el historial",
                fr: "Effacer tout l'historique", pt: "Limpar todo o histórico",
                ar: "مسح كل السجل", hi: "पूरा इतिहास मिटाएँ", bn: "সম্পূর্ণ ইতিহাস মুছুন",
                ur: "پوری تاریخ مٹائیں", zh: "清除全部历史");

            Add("Удаляет все записи из памяти и зашифрованного локального файла. Это действие нельзя отменить.",
                en: "Deletes every entry from memory and the encrypted local file. This cannot be undone.",
                uk: "Видаляє всі записи з пам'яті та зашифрованого локального файлу. Цю дію не можна скасувати.",
                de: "Löscht alle Einträge aus dem Speicher und der verschlüsselten lokalen Datei. Das lässt sich nicht rückgängig machen.",
                it: "Elimina tutte le voci dalla memoria e dal file locale cifrato. L'operazione non è reversibile.",
                es: "Elimina todas las entradas de la memoria y del archivo local cifrado. No se puede deshacer.",
                fr: "Supprime toutes les entrées de la mémoire et du fichier local chiffré. Action irréversible.",
                pt: "Exclui todas as entradas da memória e do arquivo local criptografado. Não é possível desfazer.",
                ar: "يحذف كل العناصر من الذاكرة ومن الملف المحلي المشفَّر. لا يمكن التراجع عن هذا الإجراء.",
                hi: "मेमोरी और एन्क्रिप्टेड स्थानीय फ़ाइल से सभी प्रविष्टियाँ हटा देता है। इसे पूर्ववत नहीं किया जा सकता।",
                bn: "মেমরি ও এনক্রিপ্ট করা স্থানীয় ফাইল থেকে সব এন্ট্রি মুছে ফেলে। এটি ফেরানো যায় না।",
                ur: "میموری اور خفیہ مقامی فائل سے تمام اندراجات حذف کر دیتا ہے۔ اسے واپس نہیں کیا جا سکتا۔",
                zh: "从内存和加密的本地文件中删除所有条目。此操作无法撤销。");

            Add("Удалить всю сохранённую историю буфера?",
                en: "Delete all saved clipboard history?", uk: "Видалити всю збережену історію буфера?",
                de: "Den gesamten gespeicherten Zwischenablage-Verlauf löschen?",
                it: "Eliminare tutta la cronologia degli appunti salvata?",
                es: "¿Borrar todo el historial guardado del portapapeles?",
                fr: "Supprimer tout l'historique du presse-papiers enregistré ?",
                pt: "Excluir todo o histórico salvo da área de transferência?",
                ar: "هل تريد حذف كل سجل الحافظة المحفوظ؟",
                hi: "क्या सहेजा गया पूरा क्लिपबोर्ड इतिहास हटा दें?",
                bn: "সংরক্ষিত সম্পূর্ণ ক্লিপবোর্ড ইতিহাস মুছে ফেলবেন?",
                ur: "کیا محفوظ شدہ پوری کلپ بورڈ تاریخ حذف کر دی جائے؟",
                zh: "要删除全部已保存的剪贴板历史吗？");

            // ---- Search window ----
            Add("Введите не менее 3 символов:",
                en: "Enter at least 3 characters:", uk: "Введіть щонайменше 3 символи:",
                de: "Mindestens 3 Zeichen eingeben:", it: "Inserisci almeno 3 caratteri:",
                es: "Escribe al menos 3 caracteres:", fr: "Saisissez au moins 3 caractères :",
                pt: "Digite pelo menos 3 caracteres:", ar: "أدخل 3 أحرف على الأقل:",
                hi: "कम से कम 3 अक्षर दर्ज करें:", bn: "অন্তত ৩টি অক্ষর লিখুন:",
                ur: "کم از کم 3 حروف لکھیں:", zh: "请输入至少 3 个字符：");

            Add("Введите минимум 3 символа для поиска по части текста.",
                en: "Enter at least 3 characters to search by text fragment.",
                uk: "Введіть щонайменше 3 символи для пошуку за фрагментом тексту.",
                de: "Geben Sie mindestens 3 Zeichen ein, um nach einem Textausschnitt zu suchen.",
                it: "Inserisci almeno 3 caratteri per cercare un frammento di testo.",
                es: "Escribe al menos 3 caracteres para buscar por fragmento de texto.",
                fr: "Saisissez au moins 3 caractères pour rechercher un fragment de texte.",
                pt: "Digite pelo menos 3 caracteres para pesquisar por trecho de texto.",
                ar: "أدخل 3 أحرف على الأقل للبحث بجزء من النص.",
                hi: "पाठ के अंश से खोजने के लिए कम से कम 3 अक्षर दर्ज करें।",
                bn: "লেখার অংশ দিয়ে খুঁজতে অন্তত ৩টি অক্ষর লিখুন।",
                ur: "متن کے ٹکڑے سے تلاش کے لیے کم از کم 3 حروف لکھیں۔",
                zh: "请输入至少 3 个字符，以按文本片段搜索。");

            Add("Совпадений не найдено.",
                en: "No matches found.", uk: "Збігів не знайдено.", de: "Keine Treffer gefunden.",
                it: "Nessuna corrispondenza trovata.", es: "No se encontraron coincidencias.",
                fr: "Aucun résultat trouvé.", pt: "Nenhuma correspondência encontrada.",
                ar: "لم يُعثر على أي تطابق.", hi: "कोई मिलान नहीं मिला।", bn: "কোনো মিল পাওয়া যায়নি।",
                ur: "کوئی مماثلت نہیں ملی۔", zh: "未找到匹配项。");

            Add("Найдено: {0}",
                en: "Found: {0}", uk: "Знайдено: {0}", de: "Gefunden: {0}", it: "Trovati: {0}",
                es: "Encontrados: {0}", fr: "Trouvés : {0}", pt: "Encontrados: {0}",
                ar: "النتائج: {0}", hi: "मिले: {0}", bn: "পাওয়া গেছে: {0}", ur: "ملے: {0}", zh: "找到：{0}");

            Add("Текст",
                en: "Text", uk: "Текст", de: "Text", it: "Testo", es: "Texto", fr: "Texte",
                pt: "Texto", ar: "النص", hi: "पाठ", bn: "লেখা", ur: "متن", zh: "文本");

            Add("Дата",
                en: "Date", uk: "Дата", de: "Datum", it: "Data", es: "Fecha", fr: "Date",
                pt: "Data", ar: "التاريخ", hi: "दिनांक", bn: "তারিখ", ur: "تاریخ", zh: "日期");

            Add("Источник",
                en: "Source", uk: "Джерело", de: "Quelle", it: "Origine", es: "Origen", fr: "Source",
                pt: "Origem", ar: "المصدر", hi: "स्रोत", bn: "উৎস", ur: "ماخذ", zh: "来源");

            Add("Закрыть",
                en: "Close", uk: "Закрити", de: "Schließen", it: "Chiudi", es: "Cerrar", fr: "Fermer",
                pt: "Fechar", ar: "إغلاق", hi: "बंद करें", bn: "বন্ধ করুন", ur: "بند کریں", zh: "关闭");

            Add("Вернуть в буфер",
                en: "Restore to clipboard", uk: "Повернути в буфер", de: "In die Zwischenablage zurücklegen",
                it: "Riporta negli appunti", es: "Devolver al portapapeles",
                fr: "Remettre dans le presse-papiers", pt: "Devolver à área de transferência",
                ar: "إعادة إلى الحافظة", hi: "क्लिपबोर्ड में वापस डालें",
                bn: "ক্লিপবোর্ডে ফেরত দিন", ur: "کلپ بورڈ میں واپس رکھیں", zh: "放回剪贴板");

            // ---- Tray balloons ----
            Add("Ничего не выделено. Я переворачиваю текст, а не воздух — сначала выделите что-нибудь.",
                en: "Nothing selected. I flip text, not thin air — highlight something first.",
                uk: "Нічого не виділено. Я перевертаю текст, а не повітря — спершу виділіть щось.",
                de: "Nichts ausgewählt. Ich wandle Text um, keine Luft — markieren Sie zuerst etwas.",
                it: "Nessuna selezione. Converto testo, non aria: seleziona prima qualcosa.",
                es: "No hay nada seleccionado. Convierto texto, no aire: selecciona algo primero.",
                fr: "Rien n'est sélectionné. Je convertis du texte, pas du vide — sélectionnez d'abord quelque chose.",
                pt: "Nada selecionado. Eu converto texto, não ar — selecione algo primeiro.",
                ar: "لا يوجد تحديد. أنا أحوّل النص لا الهواء — حدّد شيئًا أولاً.",
                hi: "कुछ भी चयनित नहीं है। मैं पाठ पलटता हूँ, हवा नहीं — पहले कुछ चुनिए।",
                bn: "কিছুই নির্বাচিত নয়। আমি লেখা রূপান্তর করি, বাতাস নয় — আগে কিছু নির্বাচন করুন।",
                ur: "کچھ منتخب نہیں ہے۔ میں متن بدلتا ہوں، ہوا نہیں — پہلے کچھ منتخب کریں۔",
                zh: "没有选中任何内容。我转换的是文本，不是空气——请先选中一些文字。");

            Add("Не удалось прочитать или заменить выделение. У буфера обмена были другие планы.",
                en: "Couldn't read or replace the selection. The clipboard had other plans.",
                uk: "Не вдалося прочитати або замінити виділення. У буфера обміну були інші плани.",
                de: "Die Auswahl konnte nicht gelesen oder ersetzt werden. Die Zwischenablage hatte andere Pläne.",
                it: "Impossibile leggere o sostituire la selezione. Gli appunti avevano altri programmi.",
                es: "No se pudo leer ni reemplazar la selección. El portapapeles tenía otros planes.",
                fr: "Impossible de lire ou de remplacer la sélection. Le presse-papiers avait d'autres projets.",
                pt: "Não foi possível ler ou substituir a seleção. A área de transferência tinha outros planos.",
                ar: "تعذّرت قراءة التحديد أو استبداله. يبدو أن للحافظة خططًا أخرى.",
                hi: "चयन को पढ़ा या बदला नहीं जा सका। क्लिपबोर्ड की योजनाएँ कुछ और थीं।",
                bn: "নির্বাচন পড়া বা বদলানো গেল না। ক্লিপবোর্ডের পরিকল্পনা ছিল অন্যরকম।",
                ur: "انتخاب پڑھا یا بدلا نہیں جا سکا۔ کلپ بورڈ کے ارادے کچھ اور تھے۔",
                zh: "无法读取或替换所选内容。剪贴板另有打算。");

            Add("Фрагмент слишком велик для истории (>128 КБ).",
                en: "Fragment is too large for history (>128 KB).",
                uk: "Фрагмент завеликий для історії (>128 КБ).",
                de: "Der Ausschnitt ist zu groß für den Verlauf (>128 KB).",
                it: "Il frammento è troppo grande per la cronologia (>128 KB).",
                es: "El fragmento es demasiado grande para el historial (>128 KB).",
                fr: "Le fragment est trop grand pour l'historique (>128 Ko).",
                pt: "O trecho é grande demais para o histórico (>128 KB).",
                ar: "الجزء أكبر من أن يُحفظ في السجل (أكثر من 128 كيلوبايت).",
                hi: "यह अंश इतिहास के लिए बहुत बड़ा है (>128 KB)।",
                bn: "অংশটি ইতিহাসের জন্য অনেক বড় (>১২৮ কিলোবাইট)।",
                ur: "یہ ٹکڑا تاریخ کے لیے بہت بڑا ہے (>128 کلوبائٹ)۔",
                zh: "该片段太大，无法存入历史（超过 128 KB）。");

            Add("Не удалось изменить автозапуск Windows:",
                en: "Couldn't update Windows startup:", uk: "Не вдалося змінити автозапуск Windows:",
                de: "Der Windows-Autostart konnte nicht geändert werden:",
                it: "Impossibile modificare l'avvio automatico di Windows:",
                es: "No se pudo cambiar el inicio automático de Windows:",
                fr: "Impossible de modifier le démarrage automatique de Windows :",
                pt: "Não foi possível alterar a inicialização do Windows:",
                ar: "تعذّر تغيير بدء تشغيل Windows:", hi: "Windows स्टार्टअप बदला नहीं जा सका:",
                bn: "Windows স্টার্টআপ পরিবর্তন করা গেল না:", ur: "Windows اسٹارٹ اپ تبدیل نہیں ہو سکا:",
                zh: "无法修改 Windows 开机启动：");
        }
    }
}
