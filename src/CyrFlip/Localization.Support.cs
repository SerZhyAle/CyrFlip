namespace CyrFlip
{
    /// <summary>
    /// "Send logs to the author": the About-tab button, the pre-send dialog and the two honest
    /// answers for when the machine has no mail client that takes attachments.
    /// </summary>
    internal static partial class Localization
    {
        private static void AddSupportStrings()
        {
            Add("Отправить логи автору..",
                en: "Send logs to the author..", uk: "Надіслати логи автору..",
                de: "Protokolle an den Autor senden..", it: "Invia i log all'autore..",
                es: "Enviar los registros al autor..", fr: "Envoyer les journaux à l'auteur..",
                pt: "Enviar os logs ao autor..", ar: "إرسال السجلات إلى المؤلف..",
                hi: "लॉग लेखक को भेजें..", bn: "লগ লেখকের কাছে পাঠান..",
                ur: "لاگز مصنف کو بھیجیں..", zh: "将日志发送给作者..");

            Add("Собирает логи CyrFlip в один архив и открывает письмо автору с этим вложением. Письмо отправляете вы сами — CyrFlip ничего не передаёт в сеть. История буфера обмена в архив не попадает.",
                en: "Collects CyrFlip's logs into one archive and opens a message to the author with it attached. You send the message yourself - CyrFlip transmits nothing over the network. Clipboard history never goes into the archive.",
                uk: "Збирає логи CyrFlip в один архів і відкриває лист до автора з цим вкладенням. Лист надсилаєте ви самі - CyrFlip нічого не передає в мережу. Історія буфера обміну до архіву не потрапляє.",
                de: "Fasst die Protokolle von CyrFlip in einem Archiv zusammen und öffnet eine Nachricht an den Autor mit diesem Anhang. Gesendet wird sie von Ihnen - CyrFlip überträgt nichts ins Netz. Der Zwischenablage-Verlauf kommt nie ins Archiv.",
                it: "Raccoglie i log di CyrFlip in un unico archivio e apre un messaggio all'autore con quell'allegato. Il messaggio lo invia lei - CyrFlip non trasmette nulla in rete. La cronologia degli appunti non finisce mai nell'archivio.",
                es: "Reúne los registros de CyrFlip en un solo archivo comprimido y abre un mensaje al autor con ese adjunto. El mensaje lo envía usted - CyrFlip no transmite nada por la red. El historial del portapapeles nunca entra en el archivo.",
                fr: "Rassemble les journaux de CyrFlip dans une archive et ouvre un message à l'auteur avec cette pièce jointe. C'est vous qui l'envoyez - CyrFlip ne transmet rien sur le réseau. L'historique du presse-papiers n'y figure jamais.",
                pt: "Reúne os logs do CyrFlip em um único arquivo e abre uma mensagem ao autor com esse anexo. Você mesmo envia a mensagem - o CyrFlip não transmite nada pela rede. O histórico da área de transferência nunca entra no arquivo.",
                ar: "يجمع سجلات CyrFlip في أرشيف واحد ويفتح رسالة إلى المؤلف مع هذا المرفق. أنت من يرسل الرسالة - CyrFlip لا ينقل أي شيء عبر الشبكة. سجل الحافظة لا يدخل الأرشيف أبدًا.",
                hi: "CyrFlip के लॉग को एक संग्रह में इकट्ठा करता है और उसे संलग्न करके लेखक के लिए संदेश खोलता है। संदेश आप स्वयं भेजते हैं - CyrFlip नेटवर्क पर कुछ नहीं भेजता। क्लिपबोर्ड इतिहास संग्रह में कभी शामिल नहीं होता।",
                bn: "CyrFlip-এর লগ একটি আর্কাইভে জড়ো করে এবং সেটি সংযুক্ত করে লেখকের জন্য একটি বার্তা খোলে। বার্তা আপনি নিজেই পাঠান - CyrFlip নেটওয়ার্কে কিছুই পাঠায় না। ক্লিপবোর্ডের ইতিহাস আর্কাইভে কখনও যায় না।",
                ur: "CyrFlip کے لاگز کو ایک آرکائیو میں جمع کرتا ہے اور اسے منسلک کر کے مصنف کے لیے پیغام کھولتا ہے۔ پیغام آپ خود بھیجتے ہیں - CyrFlip نیٹ ورک پر کچھ نہیں بھیجتا۔ کلپ بورڈ کی تاریخ آرکائیو میں کبھی شامل نہیں ہوتی۔",
                zh: "把 CyrFlip 的日志打包成一个压缩包，并打开一封已附加该文件的给作者的邮件。邮件由您自己发送 - CyrFlip 不会向网络传输任何内容。剪贴板历史绝不会进入压缩包。");

            Add("Логи для автора",
                en: "Logs for the author", uk: "Логи для автора", de: "Protokolle für den Autor",
                it: "Log per l'autore", es: "Registros para el autor", fr: "Journaux pour l'auteur",
                pt: "Logs para o autor", ar: "سجلات للمؤلف", hi: "लेखक के लिए लॉग",
                bn: "লেখকের জন্য লগ", ur: "مصنف کے لیے لاگز", zh: "发送给作者的日志");

            Add("Архив с логами собран:",
                en: "The log archive is ready:", uk: "Архів із логами створено:",
                de: "Das Protokollarchiv ist fertig:", it: "L'archivio dei log è pronto:",
                es: "El archivo con los registros está listo:", fr: "L'archive des journaux est prête :",
                pt: "O arquivo com os logs está pronto:", ar: "أرشيف السجلات جاهز:",
                hi: "लॉग का संग्रह तैयार है:", bn: "লগের আর্কাইভ তৈরি:",
                ur: "لاگز کا آرکائیو تیار ہے:", zh: "日志压缩包已生成：");

            Add("Файл",
                en: "File", uk: "Файл", de: "Datei", it: "File", es: "Archivo", fr: "Fichier",
                pt: "Arquivo", ar: "ملف", hi: "फ़ाइल", bn: "ফাইল", ur: "فائل", zh: "文件");

            Add("Размер",
                en: "Size", uk: "Розмір", de: "Größe", it: "Dimensione", es: "Tamaño", fr: "Taille",
                pt: "Tamanho", ar: "الحجم", hi: "आकार", bn: "আকার", ur: "سائز", zh: "大小");

            Add("Что внутри",
                en: "What it holds", uk: "Що всередині", de: "Inhalt", it: "Contenuto", es: "Contenido",
                fr: "Contenu", pt: "Conteúdo", ar: "المحتوى", hi: "इसमें क्या है", bn: "ভেতরে কী আছে",
                ur: "اس میں کیا ہے", zh: "包含内容");

            // What each collected file holds - SupportBundle.Contents, one line per file (S0010 TD-1).
            Add("версия, Windows, раскладки, включённые функции и настройки CyrFlip",
                en: "version, Windows, layouts, enabled features and CyrFlip settings",
                uk: "версія, Windows, розкладки, увімкнені функції та налаштування CyrFlip",
                de: "Version, Windows, Tastaturlayouts, aktive Funktionen und CyrFlip-Einstellungen",
                it: "versione, Windows, layout, funzioni attive e impostazioni di CyrFlip",
                es: "versión, Windows, distribuciones, funciones activas y ajustes de CyrFlip",
                fr: "version, Windows, dispositions, fonctions activées et réglages de CyrFlip",
                pt: "versão, Windows, layouts, recursos ativos e configurações do CyrFlip",
                ar: "الإصدار وWindows والتخطيطات والميزات المفعّلة وإعدادات CyrFlip",
                hi: "संस्करण, Windows, लेआउट, चालू सुविधाएँ और CyrFlip सेटिंग्स",
                bn: "সংস্করণ, Windows, লেআউট, চালু বৈশিষ্ট্য ও CyrFlip-এর সেটিংস",
                ur: "ورژن، Windows، لے آؤٹس، فعال خصوصیات اور CyrFlip کی ترتیبات",
                zh: "版本、Windows、键盘布局、已启用的功能和 CyrFlip 设置");

            Add("версия, Windows и счётчики в виде строк ключ=значение - без текста",
                en: "version, Windows and counters as key=value lines - no text",
                uk: "версія, Windows і лічильники у вигляді рядків ключ=значення - без тексту",
                de: "Version, Windows und Zähler als Zeilen Schlüssel=Wert - ohne Text",
                it: "versione, Windows e contatori come righe chiave=valore - senza testo",
                es: "versión, Windows y contadores como líneas clave=valor - sin texto",
                fr: "version, Windows et compteurs en lignes clé=valeur - sans texte",
                pt: "versão, Windows e contadores em linhas chave=valor - sem texto",
                ar: "الإصدار وWindows والعدّادات في أسطر مفتاح=قيمة - بلا نصوص",
                hi: "संस्करण, Windows और गिनतियाँ key=value पंक्तियों में - कोई टेक्स्ट नहीं",
                bn: "সংস্করণ, Windows ও গণনা key=value লাইনে - কোনো লেখা নেই",
                ur: "ورژن، Windows اور گنتیاں key=value سطروں میں - کوئی متن نہیں",
                zh: "版本、Windows 和计数，以 key=value 行记录 - 不含文本");

            Add("запуски сценариев: имя, тип, результат - без путей и аргументов",
                en: "scenario launches: name, kind, outcome - no paths or arguments",
                uk: "запуски сценаріїв: назва, тип, результат - без шляхів і аргументів",
                de: "Szenariostarts: Name, Art, Ergebnis - ohne Pfade und Argumente",
                it: "avvii degli scenari: nome, tipo, esito - senza percorsi né argomenti",
                es: "inicios de escenarios: nombre, tipo, resultado - sin rutas ni argumentos",
                fr: "lancements de scénarios : nom, type, résultat - sans chemins ni arguments",
                pt: "execuções de cenários: nome, tipo, resultado - sem caminhos nem argumentos",
                ar: "تشغيل السيناريوهات: الاسم والنوع والنتيجة - بلا مسارات ولا وسائط",
                hi: "परिदृश्य चलाना: नाम, प्रकार, परिणाम - पथ और तर्क नहीं",
                bn: "দৃশ্যপট চালু: নাম, ধরন, ফলাফল - পথ ও আর্গুমেন্ট ছাড়া",
                ur: "منظرنامے چلانا: نام، قسم، نتیجہ - راستوں اور آرگیومنٹس کے بغیر",
                zh: "场景启动：名称、类型、结果 - 不含路径和参数");

            Add("контекстное меню: команды, классы окон и числа - без текста",
                en: "context menu: commands, window classes and numbers - no text",
                uk: "контекстне меню: команди, класи вікон і числа - без тексту",
                de: "Kontextmenü: Befehle, Fensterklassen und Zahlen - kein Text",
                it: "menu contestuale: comandi, classi di finestra e numeri - nessun testo",
                es: "menú contextual: comandos, clases de ventana y números - sin texto",
                fr: "menu contextuel : commandes, classes de fenêtre et nombres - aucun texte",
                pt: "menu de contexto: comandos, classes de janela e números - sem texto",
                ar: "القائمة السياقية: الأوامر وفئات النوافذ والأرقام - بلا نص",
                hi: "संदर्भ मेनू: कमांड, विंडो क्लास और संख्याएँ - कोई पाठ नहीं",
                bn: "কনটেক্সট মেনু: কমান্ড, উইন্ডো ক্লাস ও সংখ্যা - কোনো লেখা নয়",
                ur: "سیاقی مینو: کمانڈز، ونڈو کلاسز اور اعداد - کوئی متن نہیں",
                zh: "右键菜单：命令、窗口类名和数字 - 不含文本");

            Add("переводы: языки, модель, длины и исход - без текста",
                en: "translations: languages, model, lengths and outcome - no text",
                uk: "переклади: мови, модель, довжини і результат - без тексту",
                de: "Übersetzungen: Sprachen, Modell, Längen und Ergebnis - kein Text",
                it: "traduzioni: lingue, modello, lunghezze ed esito - nessun testo",
                es: "traducciones: idiomas, modelo, longitudes y resultado - sin texto",
                fr: "traductions : langues, modèle, longueurs et résultat - aucun texte",
                pt: "traduções: idiomas, modelo, tamanhos e resultado - sem texto",
                ar: "الترجمات: اللغات والنموذج والأطوال والنتيجة - بلا نص",
                hi: "अनुवाद: भाषाएँ, मॉडल, लंबाई और परिणाम - कोई पाठ नहीं",
                bn: "অনুবাদ: ভাষা, মডেল, দৈর্ঘ্য ও ফলাফল - কোনো লেখা নয়",
                ur: "ترجمے: زبانیں، ماڈل، لمبائیاں اور نتیجہ - کوئی متن نہیں",
                zh: "翻译：语言、模型、长度和结果 - 不含文本");

            Add("быстрые заметки: только счётчики",
                en: "quick notes: counts only", uk: "швидкі нотатки: лише лічильники",
                de: "Schnellnotizen: nur Zähler", it: "note rapide: solo conteggi",
                es: "notas rápidas: solo recuentos", fr: "notes rapides : compteurs uniquement",
                pt: "notas rápidas: apenas contagens", ar: "الملاحظات السريعة: أعداد فقط",
                hi: "त्वरित नोट्स: केवल गिनती", bn: "দ্রুত নোট: শুধু গণনা",
                ur: "فوری نوٹس: صرف گنتی", zh: "快速笔记：仅计数");

            Add("история буфера: только счётчики",
                en: "clipboard history: counts only", uk: "історія буфера: лише лічильники",
                de: "Zwischenablage-Verlauf: nur Zähler", it: "cronologia appunti: solo conteggi",
                es: "historial del portapapeles: solo recuentos", fr: "historique du presse-papiers : compteurs uniquement",
                pt: "histórico da área de transferência: apenas contagens", ar: "سجل الحافظة: أعداد فقط",
                hi: "क्लिपबोर्ड इतिहास: केवल गिनती", bn: "ক্লিপবোর্ডের ইতিহাস: শুধু গণনা",
                ur: "کلپ بورڈ کی تاریخ: صرف گنتی", zh: "剪贴板历史：仅计数");

            Add("конвертации выделения: длины и имена процессов - без текста",
                en: "selection conversions: lengths and process names - no text",
                uk: "конвертації виділення: довжини й імена процесів - без тексту",
                de: "Umwandlungen der Auswahl: Längen und Prozessnamen - kein Text",
                it: "conversioni della selezione: lunghezze e nomi dei processi - nessun testo",
                es: "conversiones de la selección: longitudes y nombres de procesos - sin texto",
                fr: "conversions de la sélection : longueurs et noms de processus - aucun texte",
                pt: "conversões da seleção: tamanhos e nomes de processos - sem texto",
                ar: "تحويلات التحديد: الأطوال وأسماء العمليات - بلا نص",
                hi: "चयन रूपांतरण: लंबाई और प्रोसेस के नाम - कोई पाठ नहीं",
                bn: "নির্বাচনের রূপান্তর: দৈর্ঘ্য ও প্রসেসের নাম - কোনো লেখা নয়",
                ur: "انتخاب کی تبدیلیاں: لمبائیاں اور پروسیس کے نام - کوئی متن نہیں",
                zh: "选区转换：长度和进程名 - 不含文本");

            Add("снимки экрана: исход сохранения файла - без путей",
                en: "screen captures: the file save outcome - no paths",
                uk: "знімки екрана: підсумок збереження файлу - без шляхів",
                de: "Bildschirmaufnahmen: Ausgang des Dateispeicherns - keine Pfade",
                it: "acquisizioni dello schermo: esito del salvataggio del file - nessun percorso",
                es: "capturas de pantalla: resultado del guardado del archivo - sin rutas",
                fr: "captures d'écran : issue de l'enregistrement du fichier - sans chemins",
                pt: "capturas de tela: resultado do salvamento do arquivo - sem caminhos",
                ar: "لقاطات الشاشة: نتيجة حفظ الملف - بلا مسارات",
                hi: "स्क्रीन कैप्चर: फ़ाइल सहेजने का परिणाम - पथ नहीं",
                bn: "স্ক্রিন ক্যাপচার: ফাইল সংরক্ষণের ফল - পথ নেই",
                ur: "اسکرین کیپچر: فائل محفوظ کرنے کا نتیجہ - راستے نہیں",
                zh: "屏幕捕获：文件保存结果 - 不含路径");

            Add("диагностика каретки: классы окон и процессы - заголовки только длиной",
                en: "caret diagnostics: window classes and processes - titles only as a length",
                uk: "діагностика каретки: класи вікон і процеси - заголовки лише довжиною",
                de: "Caret-Diagnose: Fensterklassen und Prozesse - Titel nur als Länge",
                it: "diagnostica del cursore: classi di finestra e processi - titoli solo come lunghezza",
                es: "diagnóstico del cursor: clases de ventana y procesos - títulos solo como longitud",
                fr: "diagnostic du curseur : classes de fenêtre et processus - titres réduits à leur longueur",
                pt: "diagnóstico do cursor: classes de janela e processos - títulos só como tamanho",
                ar: "تشخيص المؤشر النصي: فئات النوافذ والعمليات - العناوين كطول فقط",
                hi: "कैरेट निदान: विंडो क्लास और प्रोसेस - शीर्षक केवल लंबाई के रूप में",
                bn: "ক্যারেট ডায়াগনস্টিক: উইন্ডো ক্লাস ও প্রসেস - শিরোনাম শুধু দৈর্ঘ্য হিসেবে",
                ur: "کیرٹ کی تشخیص: ونڈو کلاسز اور پروسیسز - عنوان صرف لمبائی کے طور پر",
                zh: "光标诊断：窗口类名和进程 - 标题只记录长度");

            Add("код текущей раскладки",
                en: "the current layout's code", uk: "код поточної розкладки",
                de: "Kürzel des aktuellen Layouts", it: "codice del layout attuale",
                es: "código de la distribución actual", fr: "code de la disposition actuelle",
                pt: "código do layout atual", ar: "رمز التخطيط الحالي",
                hi: "मौजूदा लेआउट का कोड", bn: "বর্তমান লেআউটের কোড",
                ur: "موجودہ لے آؤٹ کا کوڈ", zh: "当前布局的代码");

            Add("обрезан — сохранён только конец файла",
                en: "truncated - only the tail was kept", uk: "обрізаний - збережено лише кінець файлу",
                de: "gekürzt - nur das Ende der Datei", it: "troncato - conservata solo la parte finale",
                es: "recortado - solo se conservó el final", fr: "tronqué - seule la fin a été conservée",
                pt: "truncado - só o final foi mantido", ar: "مقتطع - تم الاحتفاظ بنهاية الملف فقط",
                hi: "काटा गया - केवल फ़ाइल का अंत रखा गया",
                bn: "কাটা হয়েছে - শুধু ফাইলের শেষ অংশ রাখা হয়েছে",
                ur: "کاٹا گیا - صرف فائل کا آخری حصہ رکھا گیا", zh: "已截断 - 仅保留文件末尾");

            Add("не вошёл — превышен общий размер",
                en: "left out - the total size limit was reached", uk: "не увійшов - перевищено загальний розмір",
                de: "nicht enthalten - Gesamtgröße überschritten", it: "escluso - superata la dimensione totale",
                es: "excluido - se superó el tamaño total", fr: "exclu - taille totale dépassée",
                pt: "não incluído - tamanho total excedido", ar: "غير مُضمَّن - تم تجاوز الحجم الإجمالي",
                hi: "शामिल नहीं - कुल आकार की सीमा पार", bn: "যোগ করা হয়নি - মোট আকারের সীমা ছাড়িয়েছে",
                ur: "شامل نہیں - کل سائز کی حد سے زیادہ", zh: "未包含 - 超出总大小上限");

            Add("Письмо отправляете вы сами - CyrFlip ничего не передаёт в сеть. История буфера обмена и быстрые заметки в архив не включены; пути и аргументы сценариев, заголовки окон и выделенный текст в логи не пишутся. Внутри логов встречаются пути к файлам - привычные уже без имени вашей учётной записи Windows, но непривычный путь его может сохранить.",
                en: "You send the message yourself - CyrFlip transmits nothing over the network. Clipboard history and quick notes are not part of the archive; scenario paths and arguments, window titles and selected text are never written to the logs. The logs do contain file paths - the usual ones no longer carry your Windows account name, but one in an unusual shape still can.",
                uk: "Лист надсилаєте ви самі - CyrFlip нічого не передає в мережу. Історія буфера обміну і швидкі нотатки до архіву не входять; шляхи й аргументи сценаріїв, заголовки вікон і виділений текст у логи не пишуться. У логах є шляхи до файлів - звичні вже без імені вашого облікового запису Windows, але непривичний шлях може його зберегти.",
                de: "Gesendet wird die Nachricht von Ihnen - CyrFlip überträgt nichts ins Netz. Zwischenablage-Verlauf und Schnellnotizen sind nicht Teil des Archivs; Szenariopfade und -argumente, Fenstertitel und markierter Text werden nie protokolliert. In den Protokollen stehen Dateipfade - in den üblichen steht Ihr Windows-Kontoname nicht mehr, doch ein ungewöhnlich geformter Pfad kann ihn noch enthalten.",
                it: "Il messaggio lo invia lei - CyrFlip non trasmette nulla in rete. La cronologia degli appunti e le note rapide non fanno parte dell'archivio; percorsi e argomenti degli scenari, titoli delle finestre e testo selezionato non vengono mai scritti nei log. Nei log ci sono percorsi di file - in quelli usuali il nome del suo account Windows non compare più, ma un percorso di forma insolita può ancora contenerlo.",
                es: "El mensaje lo envía usted - CyrFlip no transmite nada por la red. El historial del portapapeles y las notas rápidas no forman parte del archivo; las rutas y argumentos de los escenarios, los títulos de ventana y el texto seleccionado nunca se escriben en los registros. En los registros hay rutas de archivos - en las habituales ya no aparece el nombre de su cuenta de Windows, pero una ruta con una forma inusual todavía puede contenerlo.",
                fr: "C'est vous qui envoyez le message - CyrFlip ne transmet rien sur le réseau. L'historique du presse-papiers et les notes rapides ne font pas partie de l'archive ; les chemins et arguments des scénarios, les titres de fenêtre et le texte sélectionné ne sont jamais écrits dans les journaux. Les journaux contiennent des chemins de fichiers - dans les cas habituels le nom de votre compte Windows n'y figure plus, mais un chemin d'une forme inhabituelle peut encore le porter.",
                pt: "Você mesmo envia a mensagem - o CyrFlip não transmite nada pela rede. O histórico da área de transferência e as notas rápidas não fazem parte do arquivo; caminhos e argumentos de cenários, títulos de janelas e texto selecionado nunca são gravados nos logs. Os logs contêm caminhos de arquivos - nos comuns o nome da sua conta do Windows já não aparece, mas um caminho em formato incomum ainda pode contê-lo.",
                ar: "أنت من يرسل الرسالة - CyrFlip لا ينقل أي شيء عبر الشبكة. سجل الحافظة والملاحظات السريعة ليسا جزءًا من الأرشيف؛ ولا تُكتب في السجلات مسارات السيناريوهات ووسائطها ولا عناوين النوافذ ولا النص المحدد. تحتوي السجلات على مسارات ملفات - في المعتاد منها لم يعد اسم حساب Windows الخاص بك يظهر، لكن المسار ذا الشكل غير المعتاد قد يحتفظ به.",
                hi: "संदेश आप स्वयं भेजते हैं - CyrFlip नेटवर्क पर कुछ नहीं भेजता। क्लिपबोर्ड इतिहास और त्वरित नोट्स संग्रह का हिस्सा नहीं हैं; परिदृश्यों के पथ और तर्क, विंडो शीर्षक और चुना गया पाठ लॉग में कभी नहीं लिखे जाते। लॉग में फ़ाइल पथ होते हैं - सामान्य पथों में आपके Windows खाते का नाम अब नहीं रहता, पर असामान्य रूप का पथ उसे अब भी रख सकता है।",
                bn: "বার্তা আপনি নিজেই পাঠান - CyrFlip নেটওয়ার্কে কিছুই পাঠায় না। ক্লিপবোর্ডের ইতিহাস ও দ্রুত নোট আর্কাইভে নেই; দৃশ্যপটের পথ ও আর্গুমেন্ট, উইন্ডোর শিরোনাম এবং নির্বাচিত লেখা কখনও লগে লেখা হয় না। লগে ফাইলের পথ থাকে - সাধারণ পথে আপনার Windows অ্যাকাউন্টের নাম আর থাকে না, তবে অস্বাভাবিক আকৃতির পথ তা এখনও রাখতে পারে।",
                ur: "پیغام آپ خود بھیجتے ہیں - CyrFlip نیٹ ورک پر کچھ نہیں بھیجتا۔ کلپ بورڈ کی تاریخ اور فوری نوٹس آرکائیو کا حصہ نہیں؛ منظرناموں کے راستے اور آرگیومنٹس، ونڈو کے عنوان اور منتخب متن کبھی لاگز میں نہیں لکھے جاتے۔ لاگز میں فائلوں کے راستے ہوتے ہیں - عام راستوں میں آپ کے Windows اکاؤنٹ کا نام اب نہیں ہوتا، لیکن غیر معمولی شکل کا راستہ اسے اب بھی رکھ سکتا ہے۔",
                zh: "邮件由您自己发送 - CyrFlip 不会向网络传输任何内容。剪贴板历史和快速笔记不在压缩包内；场景的路径和参数、窗口标题以及选中的文本从不写入日志。日志中含有文件路径 - 常见形式的路径已不再带有您的 Windows 账户名，但形式少见的路径仍可能保留它。");

            Add("Создать письмо",
                en: "Create the message", uk: "Створити лист", de: "Nachricht erstellen",
                it: "Crea il messaggio", es: "Crear el mensaje", fr: "Créer le message",
                pt: "Criar a mensagem", ar: "إنشاء الرسالة", hi: "संदेश बनाएँ",
                bn: "বার্তা তৈরি করুন", ur: "پیغام بنائیں", zh: "创建邮件");

            Add("Открыть папку с архивом",
                en: "Open the archive folder", uk: "Відкрити папку з архівом",
                de: "Archivordner öffnen", it: "Apri la cartella dell'archivio",
                es: "Abrir la carpeta del archivo", fr: "Ouvrir le dossier de l'archive",
                pt: "Abrir a pasta do arquivo", ar: "فتح مجلد الأرشيف",
                hi: "संग्रह का फ़ोल्डर खोलें", bn: "আর্কাইভের ফোল্ডার খুলুন",
                ur: "آرکائیو کا فولڈر کھولیں", zh: "打开压缩包所在文件夹");

            Add("Не удалось собрать архив с логами:",
                en: "Could not build the log archive:", uk: "Не вдалося створити архів із логами:",
                de: "Das Protokollarchiv konnte nicht erstellt werden:",
                it: "Non è stato possibile creare l'archivio dei log:",
                es: "No se pudo crear el archivo con los registros:",
                fr: "Impossible de créer l'archive des journaux :",
                pt: "Não foi possível criar o arquivo com os logs:",
                ar: "تعذّر إنشاء أرشيف السجلات:", hi: "लॉग का संग्रह नहीं बनाया जा सका:",
                bn: "লগের আর্কাইভ তৈরি করা যায়নি:", ur: "لاگز کا آرکائیو نہیں بنایا جا سکا:",
                zh: "无法生成日志压缩包：");

            Add("Ваша почтовая программа не принимает вложение из ссылки. Письмо открыто, а архив выделен в проводнике — перетащите его в письмо перед отправкой.",
                en: "Your mail program does not accept an attachment from a link. The message is open and the archive is selected in Explorer - drag it into the message before sending.",
                uk: "Ваша поштова програма не приймає вкладення з посилання. Лист відкрито, а архів виділено в проводнику - перетягніть його в лист перед надсиланням.",
                de: "Ihr Mailprogramm nimmt keinen Anhang aus einem Link an. Die Nachricht ist offen und das Archiv im Explorer markiert - ziehen Sie es vor dem Senden in die Nachricht.",
                it: "Il suo programma di posta non accetta un allegato da un link. Il messaggio è aperto e l'archivio è selezionato in Esplora file: lo trascini nel messaggio prima di inviarlo.",
                es: "Su programa de correo no acepta un adjunto desde un enlace. El mensaje está abierto y el archivo está seleccionado en el Explorador: arrástrelo al mensaje antes de enviarlo.",
                fr: "Votre logiciel de messagerie n'accepte pas de pièce jointe issue d'un lien. Le message est ouvert et l'archive est sélectionnée dans l'Explorateur : glissez-la dans le message avant l'envoi.",
                pt: "Seu programa de e-mail não aceita anexo a partir de um link. A mensagem está aberta e o arquivo está selecionado no Explorador: arraste-o para a mensagem antes de enviar.",
                ar: "برنامج البريد لديك لا يقبل مرفقًا من رابط. الرسالة مفتوحة والأرشيف محدَّد في مستكشف الملفات - اسحبه إلى الرسالة قبل الإرسال.",
                hi: "आपका मेल प्रोग्राम लिंक से अनुलग्नक स्वीकार नहीं करता। संदेश खुला है और संग्रह Explorer में चुना हुआ है - भेजने से पहले उसे संदेश में खींचें।",
                bn: "আপনার মেইল প্রোগ্রাম লিঙ্ক থেকে সংযুক্তি নেয় না। বার্তাটি খোলা আছে এবং আর্কাইভটি Explorer-এ নির্বাচিত - পাঠানোর আগে সেটি বার্তায় টেনে দিন।",
                ur: "آپ کا میل پروگرام لنک سے منسلکہ قبول نہیں کرتا۔ پیغام کھلا ہے اور آرکائیو Explorer میں منتخب ہے - بھیجنے سے پہلے اسے پیغام میں کھینچ کر ڈالیں۔",
                zh: "您的邮件程序不接受来自链接的附件。邮件已打开，压缩包已在资源管理器中选中 - 发送前请把它拖入邮件。");

            Add("Не удалось открыть почтовую программу. Отправьте архив вручную на адрес:",
                en: "The mail program could not be opened. Please send the archive by hand to:",
                uk: "Не вдалося відкрити поштову програму. Надішліть архів вручну на адресу:",
                de: "Das Mailprogramm konnte nicht geöffnet werden. Senden Sie das Archiv bitte manuell an:",
                it: "Non è stato possibile aprire il programma di posta. Invii l'archivio manualmente a:",
                es: "No se pudo abrir el programa de correo. Envíe el archivo manualmente a:",
                fr: "Impossible d'ouvrir le logiciel de messagerie. Envoyez l'archive manuellement à :",
                pt: "Não foi possível abrir o programa de e-mail. Envie o arquivo manualmente para:",
                ar: "تعذّر فتح برنامج البريد. أرسل الأرشيف يدويًا إلى:",
                hi: "मेल प्रोग्राम नहीं खुल सका। संग्रह को स्वयं इस पते पर भेजें:",
                bn: "মেইল প্রোগ্রাম খোলা যায়নি। আর্কাইভটি নিজে এই ঠিকানায় পাঠান:",
                ur: "میل پروگرام نہیں کھل سکا۔ آرکائیو خود اس پتے پر بھیجیں:",
                zh: "无法打开邮件程序。请手动将压缩包发送至：");
        }
    }
}
