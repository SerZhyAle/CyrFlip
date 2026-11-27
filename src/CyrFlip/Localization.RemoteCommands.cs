namespace CyrFlip
{
    /// <summary>
    /// The strings of External control (REMOTE-CONTROL target, tickets CYRFLP-0140 and CYRFLP-0142). The English,
    /// Russian and Ukrainian words of the window, of its Settings group and of the confirmation are the contract's
    /// (`REMOTE-CONTROL` rule 18, `WINDOW.md` section 6) and are not reworded here; the other ten languages translate
    /// the English.
    /// </summary>
    internal static partial class Localization
    {
        private static void AddRemoteCommandsStrings()
        {
            AddRemoteControlWindowStrings();
            AddRemoteControlOutcomeStrings();
            AddRemoteControlVerbStrings();

            Add("Listen to commands from my other devices",
                ru: "Принимать команды с моих других устройств", uk: "Приймати команди з моїх інших пристроїв",
                de: "Befehle von meinen anderen Geräten annehmen", it: "Accetta comandi dagli altri miei dispositivi",
                es: "Aceptar órdenes de mis otros dispositivos", fr: "Accepter les commandes de mes autres appareils",
                pt: "Aceitar comandos dos meus outros dispositivos", ar: "استقبال الأوامر من أجهزتي الأخرى",
                hi: "मेरे दूसरे डिवाइसों से आदेश स्वीकार करें", bn: "আমার অন্য ডিভাইস থেকে নির্দেশ গ্রহণ করুন",
                ur: "میرے دوسرے آلات سے احکام قبول کریں", zh: "接受来自我其他设备的命令");
            Add("Windows could not open the log file.",
                ru: "Windows не удалось открыть файл журнала.", uk: "Windows не вдалося відкрити файл журналу.",
                de: "Windows konnte die Protokolldatei nicht öffnen.", it: "Windows non è riuscito ad aprire il file di registro.",
                es: "Windows no pudo abrir el archivo de registro.", fr: "Windows n'a pas pu ouvrir le fichier journal.",
                pt: "O Windows não conseguiu abrir o arquivo de registro.", ar: "تعذّر على Windows فتح ملف السجل.",
                hi: "Windows लॉग फ़ाइल नहीं खोल सका।", bn: "Windows লগ ফাইলটি খুলতে পারেনি।",
                ur: "Windows لاگ فائل نہیں کھول سکا۔", zh: "Windows 无法打开日志文件。");
            Add("Every CyrFlip key combination is paused.",
                ru: "Все сочетания клавиш CyrFlip приостановлены.", uk: "Усі сполучення клавіш CyrFlip призупинено.",
                de: "Alle Tastenkombinationen von CyrFlip sind angehalten.", it: "Tutte le combinazioni di tasti di CyrFlip sono sospese.",
                es: "Todas las combinaciones de teclas de CyrFlip están en pausa.", fr: "Toutes les combinaisons de touches de CyrFlip sont suspendues.",
                pt: "Todas as combinações de teclas do CyrFlip estão pausadas.", ar: "أُوقفت كل مجموعات مفاتيح CyrFlip مؤقتًا.",
                hi: "CyrFlip के सभी कुंजी संयोजन रोक दिए गए हैं।", bn: "CyrFlip-এর সব কী-সমন্বয় থামানো হয়েছে।",
                ur: "CyrFlip کے تمام کلیدی امتزاج روک دیے گئے ہیں۔", zh: "CyrFlip 的所有组合键都已暂停。");
            Add("The CyrFlip key combinations are on again.",
                ru: "Сочетания клавиш CyrFlip снова включены.", uk: "Сполучення клавіш CyrFlip знову ввімкнено.",
                de: "Die Tastenkombinationen von CyrFlip sind wieder eingeschaltet.", it: "Le combinazioni di tasti di CyrFlip sono di nuovo attive.",
                es: "Las combinaciones de teclas de CyrFlip vuelven a estar activadas.", fr: "Les combinaisons de touches de CyrFlip sont de nouveau actives.",
                pt: "As combinações de teclas do CyrFlip estão ativadas de novo.", ar: "عادت مجموعات مفاتيح CyrFlip إلى العمل.",
                hi: "CyrFlip के कुंजी संयोजन फिर से चालू हैं।", bn: "CyrFlip-এর কী-সমন্বয় আবার চালু হয়েছে।",
                ur: "CyrFlip کے کلیدی امتزاج دوبارہ آن ہیں۔", zh: "CyrFlip 的组合键已重新开启。");
        }

        private static void AddRemoteControlWindowStrings()
        {
            Add("External control",
                ru: "Внешнее управление", uk: "Зовнішнє керування",
                de: "Externe Steuerung", it: "Controllo esterno",
                es: "Control externo", fr: "Contrôle externe",
                pt: "Controle externo", ar: "التحكم الخارجي",
                hi: "बाहरी नियंत्रण", bn: "বাহ্যিক নিয়ন্ত্রণ",
                ur: "بیرونی کنٹرول", zh: "外部控制");
            Add("Off until you turn it on. Only the devices you allow, and only what you allow.",
                ru: "Выключено, пока вы не включите. Только устройства, которые вы разрешили, и только то, что вы разрешили.",
                uk: "Вимкнено, доки ви не ввімкнете. Лише пристрої, які ви дозволили, і лише те, що ви дозволили.",
                de: "Aus, bis Sie es einschalten. Nur die Geräte, die Sie erlauben, und nur das, was Sie erlauben.",
                it: "Spento finché non lo accendi. Solo i dispositivi che consenti, e solo ciò che consenti.",
                es: "Desactivado hasta que lo actives. Solo los dispositivos que permitas, y solo lo que permitas.",
                fr: "Désactivé tant que vous ne l'activez pas. Seuls les appareils que vous autorisez, et seulement ce que vous autorisez.",
                pt: "Desligado até você ligar. Só os dispositivos que você permitir, e só o que você permitir.",
                ar: "مطفأ حتى تشغّله. الأجهزة التي تسمح بها فقط، وما تسمح به فقط.",
                hi: "जब तक आप चालू न करें, बंद है। केवल वे डिवाइस जिन्हें आप अनुमति दें, और केवल वही जिसकी आप अनुमति दें।",
                bn: "আপনি চালু না করা পর্যন্ত বন্ধ। শুধু আপনার অনুমতি দেওয়া ডিভাইস, আর শুধু আপনার অনুমতি দেওয়া কাজ।",
                ur: "جب تک آپ آن نہ کریں، بند ہے۔ صرف وہ آلات جن کی آپ اجازت دیں، اور صرف وہی جس کی آپ اجازت دیں۔",
                zh: "在您打开之前保持关闭。仅限您允许的设备，且仅限您允许的操作。");
            Add("Open External control",
                ru: "Открыть внешнее управление", uk: "Відкрити зовнішнє керування",
                de: "Externe Steuerung öffnen", it: "Apri controllo esterno",
                es: "Abrir control externo", fr: "Ouvrir le contrôle externe",
                pt: "Abrir controle externo", ar: "فتح التحكم الخارجي",
                hi: "बाहरी नियंत्रण खोलें", bn: "বাহ্যিক নিয়ন্ত্রণ খুলুন",
                ur: "بیرونی کنٹرول کھولیں", zh: "打开外部控制");
            Add("This device",
                ru: "Это устройство", uk: "Цей пристрій",
                de: "Dieses Gerät", it: "Questo dispositivo",
                es: "Este dispositivo", fr: "Cet appareil",
                pt: "Este dispositivo", ar: "هذا الجهاز",
                hi: "यह डिवाइस", bn: "এই ডিভাইস",
                ur: "یہ آلہ", zh: "此设备");
            Add("Off. Commands from other devices are not accepted.",
                ru: "Выключено. Команды с других устройств не принимаются.", uk: "Вимкнено. Команди з інших пристроїв не приймаються.",
                de: "Aus. Befehle von anderen Geräten werden nicht angenommen.", it: "Spento. I comandi da altri dispositivi non vengono accettati.",
                es: "Desactivado. No se aceptan órdenes de otros dispositivos.", fr: "Désactivé. Les commandes d'autres appareils ne sont pas acceptées.",
                pt: "Desligado. Comandos de outros dispositivos não são aceitos.", ar: "مطفأ. لا تُقبل الأوامر من الأجهزة الأخرى.",
                hi: "बंद। दूसरे डिवाइसों से आदेश स्वीकार नहीं किए जाते।", bn: "বন্ধ। অন্য ডিভাইস থেকে নির্দেশ গ্রহণ করা হয় না।",
                ur: "بند۔ دوسرے آلات سے احکام قبول نہیں کیے جاتے۔", zh: "已关闭。不接受来自其他设备的命令。");
            Add("No connected exchange server carries commands.",
                ru: "Ни один подключённый сервер обмена не передаёт команды.", uk: "Жоден під'єднаний сервер обміну не передає команди.",
                de: "Kein verbundener Austauschserver überträgt Befehle.", it: "Nessun server di scambio collegato trasmette comandi.",
                es: "Ningún servidor de intercambio conectado transmite órdenes.", fr: "Aucun serveur d'échange connecté ne transmet de commandes.",
                pt: "Nenhum servidor de troca conectado transmite comandos.", ar: "لا يحمل أي خادم تبادل متصل أوامر.",
                hi: "कोई भी जुड़ा हुआ एक्सचेंज सर्वर आदेश नहीं पहुँचाता।", bn: "কোনো সংযুক্ত এক্সচেঞ্জ সার্ভার নির্দেশ বহন করে না।",
                ur: "کوئی بھی منسلک ایکسچینج سرور احکام نہیں پہنچاتا۔", zh: "没有已连接的交换服务器传递命令。");
            Add("Open exchange settings",
                ru: "Открыть настройки обмена", uk: "Відкрити налаштування обміну",
                de: "Austauscheinstellungen öffnen", it: "Apri le impostazioni di scambio",
                es: "Abrir la configuración de intercambio", fr: "Ouvrir les paramètres d'échange",
                pt: "Abrir as configurações de troca", ar: "فتح إعدادات التبادل",
                hi: "एक्सचेंज सेटिंग्स खोलें", bn: "এক্সচেঞ্জ সেটিংস খুলুন",
                ur: "ایکسچینج کی ترتیبات کھولیں", zh: "打开交换设置");
            Add("Sign in to this account again to accept commands.",
                ru: "Войдите в этот аккаунт ещё раз, чтобы принимать команды.", uk: "Увійдіть у цей акаунт ще раз, щоб приймати команди.",
                de: "Melden Sie sich erneut bei diesem Konto an, um Befehle anzunehmen.", it: "Accedi di nuovo a questo account per accettare comandi.",
                es: "Vuelve a iniciar sesión en esta cuenta para aceptar órdenes.", fr: "Reconnectez-vous à ce compte pour accepter des commandes.",
                pt: "Entre novamente nesta conta para aceitar comandos.", ar: "سجّل الدخول إلى هذا الحساب مرة أخرى لقبول الأوامر.",
                hi: "आदेश स्वीकार करने के लिए इस खाते में फिर से साइन इन करें।", bn: "নির্দেশ গ্রহণ করতে এই অ্যাকাউন্টে আবার সাইন ইন করুন।",
                ur: "احکام قبول کرنے کے لیے اس اکاؤنٹ میں دوبارہ سائن ان کریں۔", zh: "请再次登录此账户以接受命令。");
            Add("On. The exchange server cannot be reached right now.",
                ru: "Включено. Сервер обмена сейчас недоступен.", uk: "Увімкнено. Сервер обміну зараз недоступний.",
                de: "Ein. Der Austauschserver ist gerade nicht erreichbar.", it: "Acceso. Il server di scambio non è raggiungibile in questo momento.",
                es: "Activado. El servidor de intercambio no está disponible ahora.", fr: "Activé. Le serveur d'échange est injoignable pour le moment.",
                pt: "Ligado. O servidor de troca não está acessível agora.", ar: "مشغّل. لا يمكن الوصول إلى خادم التبادل الآن.",
                hi: "चालू। एक्सचेंज सर्वर अभी उपलब्ध नहीं है।", bn: "চালু। এক্সচেঞ্জ সার্ভারে এখন পৌঁছানো যাচ্ছে না।",
                ur: "آن۔ ایکسچینج سرور ابھی دستیاب نہیں۔", zh: "已开启。目前无法连接交换服务器。");
            Add("On. The devices you allow can send commands now.",
                ru: "Включено. Разрешённые устройства могут отправлять команды.", uk: "Увімкнено. Дозволені пристрої можуть надсилати команди.",
                de: "Ein. Die Geräte, die Sie erlauben, können jetzt Befehle senden.", it: "Acceso. I dispositivi che consenti ora possono inviare comandi.",
                es: "Activado. Los dispositivos que permitas ya pueden enviar órdenes.", fr: "Activé. Les appareils que vous autorisez peuvent maintenant envoyer des commandes.",
                pt: "Ligado. Os dispositivos que você permitir já podem enviar comandos.", ar: "مشغّل. يمكن للأجهزة التي تسمح بها إرسال الأوامر الآن.",
                hi: "चालू। जिन डिवाइसों को आप अनुमति देते हैं, वे अब आदेश भेज सकते हैं।", bn: "চালু। আপনার অনুমতি দেওয়া ডিভাইস এখন নির্দেশ পাঠাতে পারে।",
                ur: "آن۔ جن آلات کی آپ اجازت دیں وہ اب احکام بھیج سکتے ہیں۔", zh: "已开启。您允许的设备现在可以发送命令。");
            Add("Waiting for your answer",
                ru: "Ждёт вашего ответа", uk: "Чекає на вашу відповідь",
                de: "Wartet auf Ihre Antwort", it: "In attesa della tua risposta",
                es: "Esperando tu respuesta", fr: "En attente de votre réponse",
                pt: "Aguardando sua resposta", ar: "بانتظار إجابتك",
                hi: "आपके उत्तर की प्रतीक्षा", bn: "আপনার উত্তরের অপেক্ষায়",
                ur: "آپ کے جواب کا انتظار", zh: "等待您的回答");
            Add("Who can control this device",
                ru: "Кто может управлять этим устройством", uk: "Хто може керувати цим пристроєм",
                de: "Wer dieses Gerät steuern darf", it: "Chi può controllare questo dispositivo",
                es: "Quién puede controlar este dispositivo", fr: "Qui peut contrôler cet appareil",
                pt: "Quem pode controlar este dispositivo", ar: "من يمكنه التحكم بهذا الجهاز",
                hi: "इस डिवाइस को कौन नियंत्रित कर सकता है", bn: "কে এই ডিভাইস নিয়ন্ত্রণ করতে পারে",
                ur: "اس آلے کو کون کنٹرول کر سکتا ہے", zh: "谁可以控制此设备");
            Add("Turn on listening to choose devices.",
                ru: "Включите приём команд, чтобы выбрать устройства.", uk: "Увімкніть приймання команд, щоб вибрати пристрої.",
                de: "Schalten Sie das Annehmen von Befehlen ein, um Geräte zu wählen.", it: "Attiva la ricezione dei comandi per scegliere i dispositivi.",
                es: "Activa la recepción de órdenes para elegir dispositivos.", fr: "Activez la réception des commandes pour choisir les appareils.",
                pt: "Ative o recebimento de comandos para escolher os dispositivos.", ar: "شغّل استقبال الأوامر لاختيار الأجهزة.",
                hi: "डिवाइस चुनने के लिए आदेश स्वीकार करना चालू करें।", bn: "ডিভাইস বাছতে নির্দেশ গ্রহণ চালু করুন।",
                ur: "آلات چننے کے لیے احکام قبول کرنا آن کریں۔", zh: "请打开接收命令以选择设备。");
            Add("No other device of your account can send commands yet. Sign in on it with the same account.",
                ru: "Пока ни одно другое устройство вашего аккаунта не может отправлять команды. Войдите на нём в тот же аккаунт.",
                uk: "Поки жоден інший пристрій вашого акаунта не може надсилати команди. Увійдіть на ньому в той самий акаунт.",
                de: "Noch kann kein anderes Gerät Ihres Kontos Befehle senden. Melden Sie sich dort mit demselben Konto an.",
                it: "Nessun altro dispositivo del tuo account può ancora inviare comandi. Accedi su di esso con lo stesso account.",
                es: "Aún ningún otro dispositivo de tu cuenta puede enviar órdenes. Inicia sesión en él con la misma cuenta.",
                fr: "Aucun autre appareil de votre compte ne peut encore envoyer de commandes. Connectez-vous dessus avec le même compte.",
                pt: "Nenhum outro dispositivo da sua conta pode enviar comandos ainda. Entre nele com a mesma conta.",
                ar: "لا يمكن لأي جهاز آخر في حسابك إرسال الأوامر بعد. سجّل الدخول عليه بالحساب نفسه.",
                hi: "आपके खाते का कोई दूसरा डिवाइस अभी आदेश नहीं भेज सकता। उस पर उसी खाते से साइन इन करें।",
                bn: "আপনার অ্যাকাউন্টের অন্য কোনো ডিভাইস এখনো নির্দেশ পাঠাতে পারে না। সেখানে একই অ্যাকাউন্টে সাইন ইন করুন।",
                ur: "آپ کے اکاؤنٹ کا کوئی دوسرا آلہ ابھی احکام نہیں بھیج سکتا۔ اس پر اسی اکاؤنٹ سے سائن ان کریں۔",
                zh: "您账户中的其他设备暂时还不能发送命令。请在该设备上用同一账户登录。");
            Add("Online",
                ru: "В сети", uk: "У мережі",
                de: "Online", it: "Online",
                es: "En línea", fr: "En ligne",
                pt: "Online", ar: "متصل",
                hi: "ऑनलाइन", bn: "অনলাইন",
                ur: "آن لائن", zh: "在线");
            Add("Offline since {0}",
                ru: "Не в сети с {0}", uk: "Не в мережі з {0}",
                de: "Offline seit {0}", it: "Offline da {0}",
                es: "Sin conexión desde {0}", fr: "Hors ligne depuis {0}",
                pt: "Offline desde {0}", ar: "غير متصل منذ {0}",
                hi: "{0} से ऑफ़लाइन", bn: "{0} থেকে অফলাইন",
                ur: "{0} سے آف لائن", zh: "自 {0} 起离线");
            Add("Allow this device",
                ru: "Разрешить это устройство", uk: "Дозволити цей пристрій",
                de: "Dieses Gerät erlauben", it: "Consenti questo dispositivo",
                es: "Permitir este dispositivo", fr: "Autoriser cet appareil",
                pt: "Permitir este dispositivo", ar: "السماح لهذا الجهاز",
                hi: "इस डिवाइस को अनुमति दें", bn: "এই ডিভাইসকে অনুমতি দিন",
                ur: "اس آلے کی اجازت دیں", zh: "允许此设备");
            Add("Safe commands",
                ru: "Безопасные команды", uk: "Безпечні команди",
                de: "Sichere Befehle", it: "Comandi sicuri",
                es: "Órdenes seguras", fr: "Commandes sûres",
                pt: "Comandos seguros", ar: "أوامر آمنة",
                hi: "सुरक्षित आदेश", bn: "নিরাপদ নির্দেশ",
                ur: "محفوظ احکام", zh: "安全命令");
            Add("Commands that read your data",
                ru: "Команды, которые читают ваши данные", uk: "Команди, що читають ваші дані",
                de: "Befehle, die Ihre Daten lesen", it: "Comandi che leggono i tuoi dati",
                es: "Órdenes que leen tus datos", fr: "Commandes qui lisent vos données",
                pt: "Comandos que leem seus dados", ar: "أوامر تقرأ بياناتك",
                hi: "आपका डेटा पढ़ने वाले आदेश", bn: "আপনার ডেটা পড়ে এমন নির্দেশ",
                ur: "آپ کا ڈیٹا پڑھنے والے احکام", zh: "读取您数据的命令");
            Add("Commands that change your data",
                ru: "Команды, которые изменяют ваши данные", uk: "Команди, що змінюють ваші дані",
                de: "Befehle, die Ihre Daten ändern", it: "Comandi che modificano i tuoi dati",
                es: "Órdenes que cambian tus datos", fr: "Commandes qui modifient vos données",
                pt: "Comandos que alteram seus dados", ar: "أوامر تغيّر بياناتك",
                hi: "आपका डेटा बदलने वाले आदेश", bn: "আপনার ডেটা বদলায় এমন নির্দেশ",
                ur: "آپ کا ڈیٹا بدلنے والے احکام", zh: "更改您数据的命令");
            Add("Commands that send your data off this device",
                ru: "Команды, которые отправляют ваши данные с этого устройства", uk: "Команди, що надсилають ваші дані з цього пристрою",
                de: "Befehle, die Ihre Daten von diesem Gerät senden", it: "Comandi che inviano i tuoi dati fuori da questo dispositivo",
                es: "Órdenes que envían tus datos fuera de este dispositivo", fr: "Commandes qui envoient vos données hors de cet appareil",
                pt: "Comandos que enviam seus dados para fora deste dispositivo", ar: "أوامر ترسل بياناتك خارج هذا الجهاز",
                hi: "आपका डेटा इस डिवाइस से बाहर भेजने वाले आदेश", bn: "আপনার ডেটা এই ডিভাইসের বাইরে পাঠায় এমন নির্দেশ",
                ur: "آپ کا ڈیٹا اس آلے سے باہر بھیجنے والے احکام", zh: "把您的数据发送到此设备之外的命令");
            Add("No access",
                ru: "Нет доступа", uk: "Немає доступу",
                de: "Kein Zugriff", it: "Nessun accesso",
                es: "Sin acceso", fr: "Aucun accès",
                pt: "Sem acesso", ar: "بلا وصول",
                hi: "कोई पहुँच नहीं", bn: "কোনো অনুমতি নেই",
                ur: "کوئی رسائی نہیں", zh: "无权限");
            Add("Ask each time",
                ru: "Спрашивать каждый раз", uk: "Питати щоразу",
                de: "Jedes Mal fragen", it: "Chiedi ogni volta",
                es: "Preguntar cada vez", fr: "Demander à chaque fois",
                pt: "Perguntar sempre", ar: "اسأل في كل مرة",
                hi: "हर बार पूछें", bn: "প্রতিবার জিজ্ঞেস করুন",
                ur: "ہر بار پوچھیں", zh: "每次询问");
            Add("Always allow",
                ru: "Разрешать всегда", uk: "Дозволяти завжди",
                de: "Immer erlauben", it: "Consenti sempre",
                es: "Permitir siempre", fr: "Toujours autoriser",
                pt: "Permitir sempre", ar: "اسمح دائمًا",
                hi: "हमेशा अनुमति दें", bn: "সবসময় অনুমতি দিন",
                ur: "ہمیشہ اجازت دیں", zh: "始终允许");
            Add("Always allow this kind?",
                ru: "Разрешать такие команды всегда?", uk: "Дозволяти такі команди завжди?",
                de: "Diese Art immer erlauben?", it: "Consentire sempre questo tipo?",
                es: "¿Permitir siempre este tipo?", fr: "Toujours autoriser ce type ?",
                pt: "Permitir sempre este tipo?", ar: "السماح دائمًا بهذا النوع؟",
                hi: "इस प्रकार को हमेशा अनुमति दें?", bn: "এই ধরনকে সবসময় অনুমতি দেবেন?",
                ur: "اس قسم کی ہمیشہ اجازت دیں؟", zh: "始终允许此类命令？");
            Add("{0} will run commands of this kind on this device without asking you.",
                ru: "{0} будет выполнять такие команды на этом устройстве, не спрашивая вас.",
                uk: "{0} виконуватиме такі команди на цьому пристрої, не питаючи вас.",
                de: "{0} führt Befehle dieser Art auf diesem Gerät aus, ohne Sie zu fragen.",
                it: "{0} eseguirà comandi di questo tipo su questo dispositivo senza chiedertelo.",
                es: "{0} ejecutará órdenes de este tipo en este dispositivo sin preguntarte.",
                fr: "{0} exécutera les commandes de ce type sur cet appareil sans vous demander.",
                pt: "{0} executará comandos deste tipo neste dispositivo sem perguntar a você.",
                ar: "سينفّذ {0} أوامر من هذا النوع على هذا الجهاز دون أن يسألك.",
                hi: "{0} इस प्रकार के आदेश इस डिवाइस पर आपसे पूछे बिना चलाएगा।",
                bn: "{0} এই ধরনের নির্দেশ এই ডিভাইসে আপনাকে জিজ্ঞেস না করেই চালাবে।",
                ur: "{0} اس قسم کے احکام اس آلے پر آپ سے پوچھے بغیر چلائے گا۔",
                zh: "{0} 将在此设备上运行此类命令而不询问您。");
            Add("This change was not saved. Try again.",
                ru: "Изменение не сохранено. Попробуйте ещё раз.", uk: "Зміну не збережено. Спробуйте ще раз.",
                de: "Diese Änderung wurde nicht gespeichert. Versuchen Sie es erneut.", it: "Questa modifica non è stata salvata. Riprova.",
                es: "Este cambio no se guardó. Inténtalo de nuevo.", fr: "Cette modification n'a pas été enregistrée. Réessayez.",
                pt: "Esta alteração não foi salva. Tente novamente.", ar: "لم يُحفظ هذا التغيير. حاول مرة أخرى.",
                hi: "यह बदलाव सहेजा नहीं गया। फिर से कोशिश करें।", bn: "এই পরিবর্তন সংরক্ষিত হয়নি। আবার চেষ্টা করুন।",
                ur: "یہ تبدیلی محفوظ نہیں ہوئی۔ دوبارہ کوشش کریں۔", zh: "此更改未保存。请重试。");
            Add("Commands this program accepts",
                ru: "Команды, которые принимает эта программа", uk: "Команди, які приймає ця програма",
                de: "Befehle, die dieses Programm annimmt", it: "Comandi che questo programma accetta",
                es: "Órdenes que acepta este programa", fr: "Commandes acceptées par ce programme",
                pt: "Comandos que este programa aceita", ar: "الأوامر التي يقبلها هذا البرنامج",
                hi: "यह प्रोग्राम कौन-से आदेश स्वीकार करता है", bn: "এই প্রোগ্রাম যে নির্দেশ গ্রহণ করে",
                ur: "یہ پروگرام کون سے احکام قبول کرتا ہے", zh: "此程序接受的命令");
            Add("Asks on this device every time",
                ru: "Каждый раз спрашивает на этом устройстве", uk: "Щоразу питає на цьому пристрої",
                de: "Fragt jedes Mal auf diesem Gerät", it: "Chiede ogni volta su questo dispositivo",
                es: "Pregunta cada vez en este dispositivo", fr: "Demande à chaque fois sur cet appareil",
                pt: "Pergunta sempre neste dispositivo", ar: "يسأل على هذا الجهاز في كل مرة",
                hi: "हर बार इस डिवाइस पर पूछता है", bn: "প্রতিবার এই ডিভাইসে জিজ্ঞেস করে",
                ur: "ہر بار اس آلے پر پوچھتا ہے", zh: "每次都在此设备上询问");
            Add("Recent requests",
                ru: "Последние запросы", uk: "Останні запити",
                de: "Letzte Anfragen", it: "Richieste recenti",
                es: "Peticiones recientes", fr: "Demandes récentes",
                pt: "Pedidos recentes", ar: "الطلبات الأخيرة",
                hi: "हाल के अनुरोध", bn: "সাম্প্রতিক অনুরোধ",
                ur: "حالیہ درخواستیں", zh: "最近的请求");
            Add("No requests yet.",
                ru: "Запросов пока не было.", uk: "Запитів поки не було.",
                de: "Noch keine Anfragen.", it: "Ancora nessuna richiesta.",
                es: "Aún no hay peticiones.", fr: "Aucune demande pour l'instant.",
                pt: "Nenhum pedido ainda.", ar: "لا طلبات بعد.",
                hi: "अभी तक कोई अनुरोध नहीं।", bn: "এখনো কোনো অনুরোধ নেই।",
                ur: "ابھی تک کوئی درخواست نہیں۔", zh: "尚无请求。");
            Add("Open the full log",
                ru: "Открыть весь журнал", uk: "Відкрити весь журнал",
                de: "Ganzes Protokoll öffnen", it: "Apri il registro completo",
                es: "Abrir el registro completo", fr: "Ouvrir tout le journal",
                pt: "Abrir o registro completo", ar: "فتح السجل كاملًا",
                hi: "पूरा लॉग खोलें", bn: "পুরো লগ খুলুন",
                ur: "پورا لاگ کھولیں", zh: "打开完整日志");
            Add("{0} asks {1}",
                ru: "{0} просит {1}", uk: "{0} просить {1}",
                de: "{0} fragt {1}", it: "{0} chiede a {1}",
                es: "{0} pide a {1}", fr: "{0} demande à {1}",
                pt: "{0} pede a {1}", ar: "{0} يطلب من {1}",
                hi: "{0} {1} से अनुरोध करता है", bn: "{0} {1}-কে অনুরোধ করছে",
                ur: "{0} {1} سے درخواست کرتا ہے", zh: "{0} 请求 {1}");
            Add("Kind: {0}",
                ru: "Вид: {0}", uk: "Вид: {0}",
                de: "Art: {0}", it: "Tipo: {0}",
                es: "Tipo: {0}", fr: "Type : {0}",
                pt: "Tipo: {0}", ar: "النوع: {0}",
                hi: "प्रकार: {0}", bn: "ধরন: {0}",
                ur: "قسم: {0}", zh: "类型：{0}");
            Add("Answer within {0} s. Without an answer the command is not run.",
                ru: "Ответьте в течение {0} с. Без ответа команда не выполняется.",
                uk: "Дайте відповідь протягом {0} с. Без відповіді команда не виконується.",
                de: "Antworten Sie innerhalb von {0} s. Ohne Antwort wird der Befehl nicht ausgeführt.",
                it: "Rispondi entro {0} s. Senza risposta il comando non viene eseguito.",
                es: "Responde en {0} s. Sin respuesta, la orden no se ejecuta.",
                fr: "Répondez dans les {0} s. Sans réponse, la commande n'est pas exécutée.",
                pt: "Responda em até {0} s. Sem resposta, o comando não é executado.",
                ar: "أجب خلال {0} ث. من دون إجابة لا يُنفَّذ الأمر.",
                hi: "{0} सेकंड में उत्तर दें। उत्तर के बिना आदेश नहीं चलता।",
                bn: "{0} সেকেন্ডের মধ্যে উত্তর দিন। উত্তর না দিলে নির্দেশ চলে না।",
                ur: "{0} سیکنڈ میں جواب دیں۔ جواب کے بغیر حکم نہیں چلتا۔",
                zh: "请在 {0} 秒内回答。没有回答，命令就不会运行。");
            Add("Allow",
                ru: "Разрешить", uk: "Дозволити",
                de: "Erlauben", it: "Consenti",
                es: "Permitir", fr: "Autoriser",
                pt: "Permitir", ar: "سماح",
                hi: "अनुमति दें", bn: "অনুমতি দিন",
                ur: "اجازت دیں", zh: "允许");
            Add("Decline",
                ru: "Отклонить", uk: "Відхилити",
                de: "Ablehnen", it: "Rifiuta",
                es: "Rechazar", fr: "Refuser",
                pt: "Recusar", ar: "رفض",
                hi: "अस्वीकार करें", bn: "প্রত্যাখ্যান করুন",
                ur: "مسترد کریں", zh: "拒绝");
            Add("unverified",
                ru: "не подтверждено", uk: "не підтверджено",
                de: "nicht bestätigt", it: "non verificato",
                es: "sin verificar", fr: "non vérifié",
                pt: "não verificado", ar: "غير مُتحقَّق منه",
                hi: "असत्यापित", bn: "অযাচাইকৃত",
                ur: "غیر تصدیق شدہ", zh: "未验证");
        }

        private static void AddRemoteControlOutcomeStrings()
        {
            Add("done", ru: "выполнено", uk: "виконано", de: "erledigt", it: "eseguito", es: "hecho", fr: "fait",
                pt: "feito", ar: "تم", hi: "पूरा हुआ", bn: "সম্পন্ন", ur: "مکمل", zh: "已完成");
            Add("accepted", ru: "принято", uk: "прийнято", de: "angenommen", it: "accettato", es: "aceptado", fr: "accepté",
                pt: "aceito", ar: "مقبول", hi: "स्वीकृत", bn: "গৃহীত", ur: "قبول کیا گیا", zh: "已接受");
            Add("busy", ru: "занято", uk: "зайнято", de: "beschäftigt", it: "occupato", es: "ocupado", fr: "occupé",
                pt: "ocupado", ar: "مشغول", hi: "व्यस्त", bn: "ব্যস্ত", ur: "مصروف", zh: "忙碌");
            Add("failed", ru: "не удалось", uk: "не вдалося", de: "fehlgeschlagen", it: "non riuscito", es: "falló", fr: "échec",
                pt: "falhou", ar: "فشل", hi: "विफल", bn: "ব্যর্থ", ur: "ناکام", zh: "失败");
            Add("not allowed", ru: "не разрешено", uk: "не дозволено", de: "nicht erlaubt", it: "non consentito", es: "no permitido", fr: "non autorisé",
                pt: "não permitido", ar: "غير مسموح", hi: "अनुमति नहीं", bn: "অনুমতি নেই", ur: "اجازت نہیں", zh: "不允许");
            Add("declined", ru: "отклонено", uk: "відхилено", de: "abgelehnt", it: "rifiutato", es: "rechazado", fr: "refusé",
                pt: "recusado", ar: "مرفوض", hi: "अस्वीकृत", bn: "প্রত্যাখ্যাত", ur: "مسترد", zh: "已拒绝");
            Add("too late", ru: "слишком поздно", uk: "надто пізно", de: "zu spät", it: "troppo tardi", es: "demasiado tarde", fr: "trop tard",
                pt: "tarde demais", ar: "متأخر جدًا", hi: "बहुत देर", bn: "অনেক দেরি", ur: "بہت دیر", zh: "太晚");
            Add("no such command", ru: "нет такой команды", uk: "немає такої команди", de: "kein solcher Befehl", it: "comando inesistente", es: "no existe esa orden", fr: "commande inconnue",
                pt: "comando inexistente", ar: "لا يوجد هذا الأمر", hi: "ऐसा कोई आदेश नहीं", bn: "এমন কোনো নির্দেশ নেই", ur: "ایسا کوئی حکم نہیں", zh: "没有这个命令");
            Add("wrong arguments", ru: "неверные аргументы", uk: "хибні аргументи", de: "falsche Argumente", it: "argomenti errati", es: "argumentos incorrectos", fr: "arguments incorrects",
                pt: "argumentos incorretos", ar: "وسائط خاطئة", hi: "गलत तर्क", bn: "ভুল আর্গুমেন্ট", ur: "غلط دلائل", zh: "参数错误");
            Add("could not be opened", ru: "не удалось открыть", uk: "не вдалося відкрити", de: "konnte nicht geöffnet werden", it: "non è stato possibile aprirlo", es: "no se pudo abrir", fr: "n'a pas pu être ouvert",
                pt: "não pôde ser aberto", ar: "تعذّر فتحه", hi: "खोला नहीं जा सका", bn: "খোলা যায়নি", ur: "کھولا نہیں جا سکا", zh: "无法打开");
        }

        private static void AddRemoteControlVerbStrings()
        {
            Add("List the commands it accepts",
                ru: "Перечислить принимаемые команды", uk: "Перелічити команди, які приймає",
                de: "Die angenommenen Befehle auflisten", it: "Elencare i comandi accettati",
                es: "Listar las órdenes que acepta", fr: "Lister les commandes acceptées",
                pt: "Listar os comandos aceitos", ar: "سرد الأوامر التي يقبلها",
                hi: "स्वीकार किए जाने वाले आदेश सूचीबद्ध करें", bn: "গ্রহণযোগ্য নির্দেশের তালিকা দিন",
                ur: "قبول کیے جانے والے احکام کی فہرست دیں", zh: "列出它接受的命令");
            Add("Tell the version",
                ru: "Сообщить версию", uk: "Повідомити версію",
                de: "Die Version nennen", it: "Indicare la versione",
                es: "Indicar la versión", fr: "Indiquer la version",
                pt: "Informar a versão", ar: "إخبار بالإصدار",
                hi: "संस्करण बताएं", bn: "সংস্করণ জানান",
                ur: "ورژن بتائیں", zh: "报告版本");
            Add("Tell whether it is ready",
                ru: "Сообщить, готов ли он", uk: "Повідомити, чи він готовий",
                de: "Sagen, ob es bereit ist", it: "Dire se è pronto",
                es: "Indicar si está listo", fr: "Dire s'il est prêt",
                pt: "Dizer se está pronto", ar: "إخبار ما إذا كان جاهزًا",
                hi: "बताएं कि यह तैयार है या नहीं", bn: "এটি প্রস্তুত কি না জানান",
                ur: "بتائیں کہ یہ تیار ہے یا نہیں", zh: "报告是否就绪");
            Add("Open the settings",
                ru: "Открыть настройки", uk: "Відкрити налаштування",
                de: "Die Einstellungen öffnen", it: "Aprire le impostazioni",
                es: "Abrir la configuración", fr: "Ouvrir les paramètres",
                pt: "Abrir as configurações", ar: "فتح الإعدادات",
                hi: "सेटिंग्स खोलें", bn: "সেটিংস খুলুন",
                ur: "ترتیبات کھولیں", zh: "打开设置");
            Add("Close CyrFlip",
                ru: "Закрыть CyrFlip", uk: "Закрити CyrFlip",
                de: "CyrFlip schließen", it: "Chiudere CyrFlip",
                es: "Cerrar CyrFlip", fr: "Fermer CyrFlip",
                pt: "Fechar o CyrFlip", ar: "إغلاق CyrFlip",
                hi: "CyrFlip बंद करें", bn: "CyrFlip বন্ধ করুন",
                ur: "CyrFlip بند کریں", zh: "关闭 CyrFlip");
            Add("List the keyboard layouts",
                ru: "Перечислить раскладки клавиатуры", uk: "Перелічити розкладки клавіатури",
                de: "Die Tastaturlayouts auflisten", it: "Elencare i layout di tastiera",
                es: "Listar las distribuciones de teclado", fr: "Lister les dispositions de clavier",
                pt: "Listar os layouts de teclado", ar: "سرد تخطيطات لوحة المفاتيح",
                hi: "कीबोर्ड लेआउट सूचीबद्ध करें", bn: "কিবোর্ড লেআউটের তালিকা দিন",
                ur: "کی بورڈ لے آؤٹس کی فہرست دیں", zh: "列出键盘布局");
            Add("Tell the current keyboard layout",
                ru: "Сообщить текущую раскладку", uk: "Повідомити поточну розкладку",
                de: "Das aktuelle Tastaturlayout nennen", it: "Indicare il layout di tastiera attuale",
                es: "Indicar la distribución de teclado actual", fr: "Indiquer la disposition de clavier actuelle",
                pt: "Informar o layout de teclado atual", ar: "إخبار بتخطيط لوحة المفاتيح الحالي",
                hi: "मौजूदा कीबोर्ड लेआउट बताएं", bn: "বর্তমান কিবোর্ড লেআউট জানান",
                ur: "موجودہ کی بورڈ لے آؤٹ بتائیں", zh: "报告当前键盘布局");
            Add("Pause or resume the key combinations",
                ru: "Приостановить или возобновить сочетания клавиш", uk: "Призупинити або відновити сполучення клавіш",
                de: "Die Tastenkombinationen anhalten oder fortsetzen", it: "Sospendere o riprendere le combinazioni di tasti",
                es: "Pausar o reanudar las combinaciones de teclas", fr: "Suspendre ou reprendre les combinaisons de touches",
                pt: "Pausar ou retomar as combinações de teclas", ar: "إيقاف مجموعات المفاتيح مؤقتًا أو استئنافها",
                hi: "कुंजी संयोजन रोकें या फिर से चालू करें", bn: "কী-সমন্বয় থামান বা আবার চালু করুন",
                ur: "کلیدی امتزاج روکیں یا دوبارہ چلائیں", zh: "暂停或恢复组合键");
            Add("Keep this PC awake",
                ru: "Не давать компьютеру уснуть", uk: "Не давати комп'ютеру заснути",
                de: "Diesen PC wach halten", it: "Tenere sveglio questo PC",
                es: "Mantener despierto este equipo", fr: "Garder ce PC éveillé",
                pt: "Manter este PC acordado", ar: "إبقاء هذا الحاسوب مستيقظًا",
                hi: "इस पीसी को जगाए रखें", bn: "এই পিসি জাগিয়ে রাখুন",
                ur: "اس پی سی کو جاگتا رکھیں", zh: "让这台电脑保持唤醒");
        }
    }
}
