namespace CyrFlip
{
    /// <summary>Hotkeys tab, the hotkey-capture dialog and the "chord already taken" warnings.</summary>
    internal static partial class Localization
    {
        private static void AddHotkeyStrings()
        {
            Add("Горячие клавиши",
                en: "Hotkeys", uk: "Гарячі клавіші", de: "Tastenkürzel", it: "Scorciatoie",
                es: "Atajos de teclado", fr: "Raccourcis clavier", pt: "Atalhos de teclado",
                ar: "اختصارات لوحة المفاتيح", hi: "शॉर्टकट कुंजियाँ", bn: "শর্টকাট কী",
                ur: "شارٹ کٹ کیز", zh: "快捷键");

            Add("Комбинации работают глобально, пока CyrFlip запущен в вашем сеансе Windows. Каждый хоткей можно включить или отключить отдельно.",
                en: "These shortcuts work globally while CyrFlip runs in your Windows session. Each hotkey can be enabled or disabled independently.",
                uk: "Ці комбінації працюють глобально, поки CyrFlip запущений у вашому сеансі Windows. Кожну гарячу клавішу можна ввімкнути чи вимкнути окремо.",
                de: "Diese Tastenkürzel gelten systemweit, solange CyrFlip in Ihrer Windows-Sitzung läuft. Jedes Kürzel lässt sich einzeln ein- oder ausschalten.",
                it: "Queste scorciatoie valgono a livello di sistema finché CyrFlip è in esecuzione nella tua sessione di Windows. Ogni scorciatoia può essere attivata o disattivata singolarmente.",
                es: "Estos atajos funcionan de forma global mientras CyrFlip se ejecuta en tu sesión de Windows. Cada atajo se puede activar o desactivar por separado.",
                fr: "Ces raccourcis fonctionnent globalement tant que CyrFlip s'exécute dans votre session Windows. Chaque raccourci peut être activé ou désactivé séparément.",
                pt: "Estes atalhos funcionam globalmente enquanto o CyrFlip estiver em execução na sua sessão do Windows. Cada atalho pode ser ativado ou desativado separadamente.",
                ar: "تعمل هذه الاختصارات على مستوى النظام ما دام CyrFlip يعمل في جلسة Windows الخاصة بك. ويمكن تفعيل كل اختصار أو تعطيله على حدة.",
                hi: "जब तक CyrFlip आपके Windows सत्र में चल रहा है, ये शॉर्टकट पूरे सिस्टम में काम करते हैं। हर शॉर्टकट अलग-अलग चालू या बंद किया जा सकता है।",
                bn: "আপনার Windows সেশনে CyrFlip চালু থাকা পর্যন্ত এই শর্টকাটগুলি সিস্টেমজুড়ে কাজ করে। প্রতিটি শর্টকাট আলাদাভাবে চালু বা বন্ধ করা যায়।",
                ur: "جب تک CyrFlip آپ کے Windows سیشن میں چل رہا ہے، یہ شارٹ کٹس پورے سسٹم میں کام کرتے ہیں۔ ہر شارٹ کٹ الگ سے آن یا آف کیا جا سکتا ہے۔",
                zh: "只要 CyrFlip 在你的 Windows 会话中运行，这些快捷键就全局有效。每个快捷键都可以单独启用或禁用。");

            // The hook watchdog's only voice: Windows drops a low-level hook that overran its
            // timeout and tells nobody, so a re-arm that keeps failing is the one thing the user has
            // to hear about - the chords are dead until CyrFlip restarts.
            Add("Windows не отдаёт перехват клавиатуры — горячие клавиши могут не работать. Помогает перезапуск CyrFlip.",
                en: "Windows will not grant the keyboard hook — the hotkeys may not work. Restarting CyrFlip usually helps.",
                uk: "Windows не віддає перехоплення клавіатури — гарячі клавіші можуть не працювати. Допомагає перезапуск CyrFlip.",
                de: "Windows gibt den Tastatur-Hook nicht frei — die Tastenkürzel funktionieren möglicherweise nicht. Ein Neustart von CyrFlip hilft meist.",
                it: "Windows non concede l'aggancio della tastiera: le scorciatoie potrebbero non funzionare. Di solito basta riavviare CyrFlip.",
                es: "Windows no concede el enganche del teclado: los atajos pueden no funcionar. Reiniciar CyrFlip suele ayudar.",
                fr: "Windows refuse le hook clavier : les raccourcis risquent de ne pas fonctionner. Redémarrer CyrFlip résout généralement le problème.",
                pt: "O Windows não concede o hook do teclado: os atalhos podem não funcionar. Reiniciar o CyrFlip costuma resolver.",
                ar: "لا يمنح Windows اعتراض لوحة المفاتيح — قد لا تعمل الاختصارات. عادةً ما تفيد إعادة تشغيل CyrFlip.",
                hi: "Windows कीबोर्ड हुक नहीं दे रहा — शॉर्टकट शायद काम न करें। आमतौर पर CyrFlip को दोबारा शुरू करने से मदद मिलती है।",
                bn: "Windows কীবোর্ড হুক দিচ্ছে না — শর্টকাট কাজ না-ও করতে পারে। সাধারণত CyrFlip পুনরায় চালু করলে কাজ হয়।",
                ur: "Windows کی بورڈ ہک نہیں دے رہا — شارٹ کٹس شاید کام نہ کریں۔ عام طور پر CyrFlip دوبارہ شروع کرنے سے مدد ملتی ہے۔",
                zh: "Windows 未授予键盘挂钩 — 快捷键可能失效。重启 CyrFlip 通常可以解决。");

            Add("Слушать глобальные горячие клавиши",
                en: "Listen for global hotkeys", uk: "Слухати глобальні гарячі клавіші",
                de: "Auf globale Tastenkürzel reagieren", it: "Ascolta le scorciatoie globali",
                es: "Escuchar los atajos globales", fr: "Écouter les raccourcis globaux",
                pt: "Ouvir os atalhos globais", ar: "الاستماع للاختصارات على مستوى النظام",
                hi: "वैश्विक शॉर्टकट सुनें", bn: "গ্লোবাল শর্টকাট শুনুন",
                ur: "عالمی شارٹ کٹس سنیں", zh: "监听全局快捷键");

            Add("Общий выключатель всех горячих клавиш. Когда снят, CyrFlip не перехватывает ни одной комбинации — клавиши проходят в приложение как обычно.",
                en: "Master switch for all hotkeys. When off, CyrFlip intercepts none of the chords — keys reach the app as usual.",
                uk: "Загальний вимикач усіх гарячих клавіш. Коли знято, CyrFlip не перехоплює жодної комбінації — клавіші потрапляють у застосунок як звичайно.",
                de: "Hauptschalter für alle Tastenkürzel. Ist er aus, fängt CyrFlip keine Kombination ab — die Tasten erreichen die Anwendung wie gewohnt.",
                it: "Interruttore generale di tutte le scorciatoie. Se disattivato, CyrFlip non intercetta nessuna combinazione: i tasti arrivano all'applicazione come sempre.",
                es: "Interruptor general de todos los atajos. Si está desactivado, CyrFlip no intercepta ninguna combinación: las teclas llegan a la aplicación como siempre.",
                fr: "Interrupteur général de tous les raccourcis. Désactivé, CyrFlip n'intercepte aucune combinaison : les touches parviennent normalement à l'application.",
                pt: "Interruptor geral de todos os atalhos. Quando desligado, o CyrFlip não intercepta nenhuma combinação: as teclas chegam ao aplicativo normalmente.",
                ar: "مفتاح رئيسي لجميع الاختصارات. عند إيقافه لا يعترض CyrFlip أي تركيبة، وتصل المفاتيح إلى التطبيق كالمعتاد.",
                hi: "सभी शॉर्टकट का मुख्य स्विच। बंद होने पर CyrFlip किसी भी संयोजन को नहीं रोकता — कुंजियाँ हमेशा की तरह ऐप तक पहुँचती हैं।",
                bn: "সব শর্টকাটের প্রধান সুইচ। বন্ধ থাকলে CyrFlip কোনো সংমিশ্রণ আটকায় না — কী-গুলি স্বাভাবিকভাবেই অ্যাপে পৌঁছায়।",
                ur: "تمام شارٹ کٹس کا مرکزی سوئچ۔ بند ہونے پر CyrFlip کوئی مجموعہ نہیں روکتا — کیز حسبِ معمول ایپ تک پہنچتی ہیں۔",
                zh: "所有快捷键的总开关。关闭后，CyrFlip 不会拦截任何组合键——按键将照常传递给应用。");

            Add("Исправить CapsLock",
                en: "Fix CapsLock", uk: "Виправити CapsLock", de: "CapsLock korrigieren",
                it: "Correggi CapsLock", es: "Corregir CapsLock", fr: "Corriger Verr. Maj.",
                pt: "Corrigir CapsLock", ar: "تصحيح CapsLock", hi: "CapsLock ठीक करें",
                bn: "CapsLock ঠিক করুন", ur: "CapsLock درست کریں", zh: "修正 CapsLock");

            Add("Меняет верхний и нижний регистр у выделенного текста. Удобно для случайно включённого CapsLock.",
                en: "Swaps upper and lower case in the selection; useful after accidentally enabling CapsLock.",
                uk: "Змінює верхній і нижній регістр виділеного тексту; зручно після випадково ввімкненого CapsLock.",
                de: "Vertauscht Groß- und Kleinschreibung in der Auswahl; hilfreich nach versehentlich aktiviertem CapsLock.",
                it: "Inverte maiuscole e minuscole nella selezione; utile dopo aver attivato CapsLock per errore.",
                es: "Intercambia mayúsculas y minúsculas en la selección; útil tras activar CapsLock sin querer.",
                fr: "Inverse majuscules et minuscules dans la sélection ; utile après un Verr. Maj. activé par mégarde.",
                pt: "Inverte maiúsculas e minúsculas na seleção; útil depois de ativar o CapsLock sem querer.",
                ar: "يبدّل الأحرف الكبيرة والصغيرة في التحديد؛ مفيد بعد تفعيل CapsLock بالخطأ.",
                hi: "चयनित पाठ के बड़े और छोटे अक्षर आपस में बदल देता है; गलती से CapsLock चालू रह जाने पर उपयोगी।",
                bn: "নির্বাচিত লেখার বড় ও ছোট হাতের অক্ষর অদলবদল করে; ভুলবশত CapsLock চালু থাকলে কাজে লাগে।",
                ur: "منتخب متن کے بڑے اور چھوٹے حروف آپس میں بدل دیتا ہے؛ غلطی سے CapsLock آن رہ جانے پر مفید۔",
                zh: "交换所选文本的大小写；不小心开着 CapsLock 时很有用。");

            Add("Менеджер буфера",
                en: "Clipboard manager", uk: "Менеджер буфера", de: "Zwischenablage-Manager",
                it: "Gestore appunti", es: "Gestor del portapapeles", fr: "Gestionnaire du presse-papiers",
                pt: "Gerenciador da área de transferência", ar: "مدير الحافظة", hi: "क्लिपबोर्ड प्रबंधक",
                bn: "ক্লিপবোর্ড ম্যানেজার", ur: "کلپ بورڈ مینیجر", zh: "剪贴板管理器");

            Add("Показывает или скрывает окно текстовой истории. Двум действиям CyrFlip нельзя назначить одну комбинацию.",
                en: "Shows or hides the text-history window. Two CyrFlip actions cannot share one shortcut.",
                uk: "Показує або ховає вікно текстової історії. Двом діям CyrFlip не можна призначити одну комбінацію.",
                de: "Blendet das Fenster mit dem Textverlauf ein oder aus. Zwei CyrFlip-Aktionen können sich kein Kürzel teilen.",
                it: "Mostra o nasconde la finestra della cronologia testi. Due azioni di CyrFlip non possono condividere una scorciatoia.",
                es: "Muestra u oculta la ventana del historial de texto. Dos acciones de CyrFlip no pueden compartir un atajo.",
                fr: "Affiche ou masque la fenêtre de l'historique de texte. Deux actions de CyrFlip ne peuvent pas partager un raccourci.",
                pt: "Mostra ou oculta a janela do histórico de texto. Duas ações do CyrFlip não podem compartilhar um atalho.",
                ar: "يعرض نافذة سجل النصوص أو يخفيها. لا يمكن لإجراءين في CyrFlip أن يتشاركا اختصارًا واحدًا.",
                hi: "पाठ-इतिहास विंडो दिखाता या छिपाता है। CyrFlip की दो क्रियाओं को एक ही शॉर्टकट नहीं दिया जा सकता।",
                bn: "টেক্সট ইতিহাসের উইন্ডো দেখায় বা লুকায়। CyrFlip-এর দুটি কাজকে একই শর্টকাট দেওয়া যায় না।",
                ur: "متن کی تاریخ کی ونڈو دکھاتا یا چھپاتا ہے۔ CyrFlip کے دو کاموں کو ایک ہی شارٹ کٹ نہیں دیا جا سکتا۔",
                zh: "显示或隐藏文本历史窗口。CyrFlip 的两个操作不能共用同一个快捷键。");

            Add("Уступать хоткеи удалённому рабочему столу (mstsc/msrdc)",
                en: "Yield hotkeys to the remote desktop (mstsc/msrdc)",
                uk: "Поступатися хоткеями віддаленому робочому столу (mstsc/msrdc)",
                de: "Tastenkürzel an den Remotedesktop abgeben (mstsc/msrdc)",
                it: "Cedi le scorciatoie al desktop remoto (mstsc/msrdc)",
                es: "Ceder los atajos al escritorio remoto (mstsc/msrdc)",
                fr: "Céder les raccourcis au Bureau à distance (mstsc/msrdc)",
                pt: "Ceder os atalhos à área de trabalho remota (mstsc/msrdc)",
                ar: "التنازل عن الاختصارات لسطح المكتب البعيد (mstsc/msrdc)",
                hi: "रिमोट डेस्कटॉप (mstsc/msrdc) को शॉर्टकट सौंपें",
                bn: "রিমোট ডেস্কটপকে (mstsc/msrdc) শর্টকাট ছেড়ে দিন",
                ur: "ریموٹ ڈیسک ٹاپ (mstsc/msrdc) کو شارٹ کٹس دے دیں",
                zh: "把快捷键让给远程桌面（mstsc/msrdc）");

            Add("Когда в фокусе окно клиента удалённого рабочего стола (mstsc/msrdc), CyrFlip не перехватывает хоткеи — клавиша уходит в удалённый сеанс, где её обработает CyrFlip на той машине. Включите, если утилита запущена на обеих сторонах RDP.",
                en: "When a remote-desktop client window (mstsc/msrdc) is focused, CyrFlip does not intercept the hotkeys — the key travels to the remote session where that machine's CyrFlip handles it. Enable this if the app runs on both ends of the RDP connection.",
                uk: "Коли у фокусі вікно клієнта віддаленого робочого столу (mstsc/msrdc), CyrFlip не перехоплює хоткеї — клавіша йде у віддалений сеанс, де її обробить CyrFlip на тій машині. Увімкніть, якщо застосунок запущено на обох боках RDP.",
                de: "Wenn ein Remotedesktop-Fenster (mstsc/msrdc) den Fokus hat, fängt CyrFlip die Kürzel nicht ab — die Taste gelangt in die Remotesitzung, wo das dortige CyrFlip sie verarbeitet. Aktivieren Sie das, wenn die App auf beiden Seiten der RDP-Verbindung läuft.",
                it: "Quando è attiva una finestra del client desktop remoto (mstsc/msrdc), CyrFlip non intercetta le scorciatoie: il tasto raggiunge la sessione remota, dove lo gestisce il CyrFlip di quella macchina. Attiva l'opzione se l'app è in esecuzione su entrambi i lati della connessione RDP.",
                es: "Cuando la ventana de un cliente de escritorio remoto (mstsc/msrdc) tiene el foco, CyrFlip no intercepta los atajos: la tecla llega a la sesión remota, donde la gestiona el CyrFlip de esa máquina. Actívalo si la aplicación se ejecuta en ambos extremos de la conexión RDP.",
                fr: "Lorsqu'une fenêtre de client Bureau à distance (mstsc/msrdc) a le focus, CyrFlip n'intercepte pas les raccourcis : la touche atteint la session distante, où le CyrFlip de cette machine la traite. Activez cette option si l'application tourne des deux côtés de la connexion RDP.",
                pt: "Quando a janela de um cliente de área de trabalho remota (mstsc/msrdc) está em foco, o CyrFlip não intercepta os atalhos: a tecla chega à sessão remota, onde o CyrFlip daquela máquina a processa. Ative isso se o aplicativo estiver em execução nas duas pontas da conexão RDP.",
                ar: "عندما تكون نافذة عميل سطح المكتب البعيد (mstsc/msrdc) في المقدمة، لا يعترض CyrFlip الاختصارات، بل ينتقل المفتاح إلى الجلسة البعيدة حيث يعالجه CyrFlip الموجود هناك. فعّل هذا الخيار إذا كان التطبيق يعمل على طرفَي اتصال RDP.",
                hi: "जब रिमोट डेस्कटॉप क्लाइंट (mstsc/msrdc) की विंडो सक्रिय हो, तो CyrFlip शॉर्टकट नहीं रोकता — कुंजी रिमोट सत्र तक जाती है, जहाँ उस मशीन का CyrFlip उसे संभालता है। यदि ऐप RDP कनेक्शन के दोनों सिरों पर चल रहा है तो इसे चालू करें।",
                bn: "রিমোট ডেস্কটপ ক্লায়েন্টের (mstsc/msrdc) উইন্ডো সক্রিয় থাকলে CyrFlip শর্টকাট আটকায় না — কী রিমোট সেশনে চলে যায়, যেখানে সেই মেশিনের CyrFlip তা সামলায়। RDP সংযোগের দুই প্রান্তেই অ্যাপ চললে এটি চালু করুন।",
                ur: "جب ریموٹ ڈیسک ٹاپ کلائنٹ (mstsc/msrdc) کی ونڈو فوکس میں ہو تو CyrFlip شارٹ کٹس نہیں روکتا — کی ریموٹ سیشن تک جاتی ہے جہاں وہاں کا CyrFlip اسے سنبھالتا ہے۔ اگر ایپ RDP کنکشن کے دونوں سروں پر چل رہی ہو تو یہ آن کریں۔",
                zh: "当远程桌面客户端窗口（mstsc/msrdc）处于前台时，CyrFlip 不拦截快捷键——按键会传到远程会话，由那台机器上的 CyrFlip 处理。如果两端都运行本程序，请启用此项。");

            Add("Здесь только эти два хоткея. Все комбинации, которые конвертируют текст из одной раскладки в другую — включая EN ⇄ RU на Ctrl+Shift+F12 — живут одной таблицей на вкладке «Конвертация раскладок».",
                en: "Only these two hotkeys live here. Every combination that converts text from one layout into another — EN ⇄ RU on Ctrl+Shift+F12 included — lives in a single table on the «Layout conversions» tab.",
                uk: "Тут лише ці два хоткеї. Усі сполучення, що перетворюють текст з однієї розкладки в іншу — разом із EN ⇄ RU на Ctrl+Shift+F12 — зібрані в одну таблицю на вкладці «Перетворення розкладок».",
                de: "Hier stehen nur diese beiden Kürzel. Alle Kombinationen, die Text von einem Layout in ein anderes umwandeln — auch EN ⇄ RU auf Strg+Umschalt+F12 — stehen gemeinsam in einer Tabelle auf der Registerkarte «Layout-Umwandlung».",
                it: "Qui ci sono solo queste due scorciatoie. Tutte le combinazioni che convertono il testo da un layout a un altro — compresa EN ⇄ RU su Ctrl+Maiusc+F12 — stanno in un'unica tabella nella scheda «Conversione layout».",
                es: "Aquí solo están estos dos atajos. Todas las combinaciones que convierten texto de una distribución a otra — incluida EN ⇄ RU en Ctrl+Mayús+F12 — están en una sola tabla en la pestaña «Conversión de distribuciones».",
                fr: "Seuls ces deux raccourcis se trouvent ici. Toutes les combinaisons qui convertissent du texte d'une disposition vers une autre — y compris EN ⇄ RU sur Ctrl+Maj+F12 — sont réunies dans un seul tableau, dans l'onglet «Conversion de dispositions».",
                pt: "Aqui ficam só estes dois atalhos. Todas as combinações que convertem texto de um layout para outro — inclusive EN ⇄ RU em Ctrl+Shift+F12 — ficam em uma única tabela na aba «Conversão de layouts».",
                ar: "لا يوجد هنا سوى هذين الاختصارين. أما كل التركيبات التي تحوّل النص من تخطيط إلى آخر — بما فيها EN ⇄ RU على Ctrl+Shift+F12 — فتوجد في جدول واحد ضمن تبويب «تحويل التخطيطات».",
                hi: "यहाँ केवल ये दो शॉर्टकट हैं। पाठ को एक लेआउट से दूसरे में बदलने वाले सभी संयोजन — Ctrl+Shift+F12 पर EN ⇄ RU सहित — «लेआउट रूपांतरण» टैब की एक ही तालिका में रहते हैं।",
                bn: "এখানে কেবল এই দুটি শর্টকাট। এক লেআউট থেকে অন্য লেআউটে লেখা রূপান্তর করে এমন সব সংমিশ্রণ — Ctrl+Shift+F12-এ EN ⇄ RU সহ — «লেআউট রূপান্তর» ট্যাবের একটিমাত্র টেবিলে থাকে।",
                ur: "یہاں صرف یہی دو شارٹ کٹس ہیں۔ متن کو ایک لے آؤٹ سے دوسرے میں بدلنے والے تمام مجموعے — بشمول Ctrl+Shift+F12 پر EN ⇄ RU — «لے آؤٹ تبدیلی» ٹیب کی ایک ہی فہرست میں ہوتے ہیں۔",
                zh: "这里只有这两个快捷键。所有把文本从一种布局转换成另一种布局的组合键——包括 Ctrl+Shift+F12 上的 EN ⇄ RU——都集中在《布局转换》选项卡的同一张表里。");

            Add("Изменить...",
                en: "Change...", uk: "Змінити...", de: "Ändern...", it: "Modifica...", es: "Cambiar...",
                fr: "Modifier...", pt: "Alterar...", ar: "تغيير...", hi: "बदलें...", bn: "বদলান...",
                ur: "تبدیل کریں...", zh: "更改...");

            // ---- HotkeyDialog ----
            Add("Новая комбинация (модификатор обязателен):",
                en: "New combo (a modifier is required):", uk: "Нова комбінація (модифікатор обов'язковий):",
                de: "Neue Kombination (Modifikatortaste erforderlich):",
                it: "Nuova combinazione (un modificatore è obbligatorio):",
                es: "Nueva combinación (se requiere un modificador):",
                fr: "Nouvelle combinaison (une touche de modification est obligatoire) :",
                pt: "Nova combinação (é obrigatório um modificador):",
                ar: "تركيبة جديدة (مفتاح تعديل مطلوب):", hi: "नया संयोजन (एक मॉडिफ़ायर आवश्यक है):",
                bn: "নতুন সংমিশ্রণ (একটি মডিফায়ার আবশ্যক):", ur: "نیا مجموعہ (ایک موڈیفائر لازمی ہے):",
                zh: "新组合键（必须包含修饰键）：");

            Add("Задать хоткей регистра",
                en: "Set case hotkey", uk: "Задати хоткей регістру", de: "Kürzel für Groß-/Kleinschreibung festlegen",
                it: "Imposta la scorciatoia delle maiuscole", es: "Definir el atajo de mayúsculas",
                fr: "Définir le raccourci de casse", pt: "Definir o atalho de maiúsculas",
                ar: "تعيين اختصار حالة الأحرف", hi: "केस शॉर्टकट सेट करें",
                bn: "কেস শর্টকাট নির্ধারণ", ur: "کیس کا شارٹ کٹ مقرر کریں", zh: "设置大小写快捷键");

            Add("Задать хоткей истории буфера",
                en: "Set clipboard history hotkey", uk: "Задати хоткей історії буфера",
                de: "Kürzel für den Zwischenablage-Verlauf festlegen",
                it: "Imposta la scorciatoia della cronologia appunti",
                es: "Definir el atajo del historial del portapapeles",
                fr: "Définir le raccourci de l'historique du presse-papiers",
                pt: "Definir o atalho do histórico da área de transferência",
                ar: "تعيين اختصار سجل الحافظة", hi: "क्लिपबोर्ड इतिहास शॉर्टकट सेट करें",
                bn: "ক্লিপবোর্ড ইতিহাসের শর্টকাট নির্ধারণ", ur: "کلپ بورڈ تاریخ کا شارٹ کٹ مقرر کریں",
                zh: "设置剪贴板历史快捷键");

            // ---- Chord clash ----
            Add("Эта комбинация уже занята {0}. Их нельзя объединять — выберите другую.",
                en: "That combination is already taken by {0}. They can't share — pick another one.",
                uk: "Цю комбінацію вже зайнято {0}. Їх не можна поєднувати — оберіть іншу.",
                de: "Diese Kombination ist bereits durch {0} belegt. Sie lässt sich nicht teilen — wählen Sie eine andere.",
                it: "Questa combinazione è già usata da {0}. Non può essere condivisa: scegline un'altra.",
                es: "Esa combinación ya la usa {0}. No se puede compartir: elige otra.",
                fr: "Cette combinaison est déjà utilisée par {0}. Elle ne peut pas être partagée — choisissez-en une autre.",
                pt: "Essa combinação já é usada por {0}. Não dá para compartilhar — escolha outra.",
                ar: "هذه التركيبة مستخدَمة بالفعل بواسطة {0}. لا يمكن مشاركتها — اختر تركيبة أخرى.",
                hi: "यह संयोजन पहले से {0} के पास है। इसे साझा नहीं किया जा सकता — कोई दूसरा चुनें।",
                bn: "এই সংমিশ্রণটি ইতিমধ্যে {0} ব্যবহার করছে। এটি ভাগ করা যায় না — অন্যটি বেছে নিন।",
                ur: "یہ مجموعہ پہلے ہی {0} کے پاس ہے۔ اسے بانٹا نہیں جا سکتا — کوئی دوسرا منتخب کریں۔",
                zh: "该组合键已被{0}占用。两者不能共用——请另选一个。");

            Add("хоткеем регистра",
                en: "the case-flip hotkey", uk: "хоткеєм регістру", de: "das Kürzel für Groß-/Kleinschreibung",
                it: "la scorciatoia delle maiuscole", es: "el atajo de mayúsculas",
                fr: "le raccourci de casse", pt: "o atalho de maiúsculas",
                ar: "اختصار حالة الأحرف", hi: "केस शॉर्टकट", bn: "কেস শর্টকাট",
                ur: "کیس کا شارٹ کٹ", zh: "大小写快捷键");

            Add("конвертацией раскладок",
                en: "a layout conversion", uk: "перетворенням розкладок", de: "eine Layout-Umwandlung",
                it: "una conversione di layout", es: "una conversión de distribución",
                fr: "une conversion de disposition", pt: "uma conversão de layout",
                ar: "تحويل تخطيط", hi: "एक लेआउट रूपांतरण", bn: "একটি লেআউট রূপান্তর",
                ur: "ایک لے آؤٹ تبدیلی", zh: "某个布局转换");

            Add("другим хоткеем CyrFlip",
                en: "another CyrFlip hotkey", uk: "іншим хоткеєм CyrFlip", de: "ein anderes CyrFlip-Kürzel",
                it: "un'altra scorciatoia di CyrFlip", es: "otro atajo de CyrFlip",
                fr: "un autre raccourci de CyrFlip", pt: "outro atalho do CyrFlip",
                ar: "اختصار آخر في CyrFlip", hi: "CyrFlip का कोई अन्य शॉर्टकट",
                bn: "CyrFlip-এর অন্য একটি শর্টকাট", ur: "CyrFlip کا کوئی اور شارٹ کٹ", zh: "CyrFlip 的另一个快捷键");

            // ---- Chords the capture dialog refuses (HotkeyRules, ticket S0004 KC-5) ----
            Add("Shift без Ctrl и Alt годится только для F1-F24: с другой клавишей такая комбинация мешала бы печатать и выделять текст.",
                en: "Shift without Ctrl or Alt only works with F1-F24: with any other key the combination would get in the way of typing and selecting text.",
                uk: "Shift без Ctrl і Alt годиться лише для F1-F24: з іншою клавішею така комбінація заважала б друкувати й виділяти текст.",
                de: "Shift ohne Strg oder Alt geht nur mit F1-F24: Mit jeder anderen Taste würde die Kombination beim Tippen und Markieren stören.",
                it: "Shift senza Ctrl o Alt funziona solo con F1-F24: con qualsiasi altro tasto la combinazione intralcerebbe la digitazione e la selezione del testo.",
                es: "Shift sin Ctrl ni Alt solo sirve con F1-F24: con cualquier otra tecla la combinación estorbaría al escribir y al seleccionar texto.",
                fr: "Maj sans Ctrl ni Alt ne fonctionne qu'avec F1-F24 : avec une autre touche, la combinaison gênerait la saisie et la sélection du texte.",
                pt: "Shift sem Ctrl ou Alt só funciona com F1-F24: com qualquer outra tecla a combinação atrapalharia a digitação e a seleção de texto.",
                ar: "لا يصلح Shift بدون Ctrl أو Alt إلا مع F1-F24: مع أي مفتاح آخر ستعيق هذه التركيبة الكتابة وتحديد النص.",
                hi: "Ctrl या Alt के बिना Shift केवल F1-F24 के साथ चलता है: किसी और कुंजी के साथ यह संयोजन टाइपिंग और टेक्स्ट चुनने में बाधा डालेगा।",
                bn: "Ctrl বা Alt ছাড়া Shift শুধু F1-F24-এর সঙ্গে চলে: অন্য কোনো কী-এর সঙ্গে এই সংমিশ্রণ টাইপ করা ও টেক্সট নির্বাচনে বাধা দেবে।",
                ur: "Ctrl یا Alt کے بغیر Shift صرف F1-F24 کے ساتھ چلتا ہے: کسی اور کلید کے ساتھ یہ مجموعہ ٹائپنگ اور متن منتخب کرنے میں رکاوٹ بنے گا۔",
                zh: "不带 Ctrl 或 Alt 的 Shift 只能与 F1-F24 搭配：与其他键组合会妨碍输入和选择文本。");

            Add("Это стандартная команда правки (копировать, вставить, отменить..) - её занимать нельзя.",
                en: "This is a standard editing command (copy, paste, undo..) - it can't be taken.",
                uk: "Це стандартна команда редагування (копіювати, вставити, скасувати..) - її займати не можна.",
                de: "Das ist ein Standard-Bearbeitungsbefehl (Kopieren, Einfügen, Rückgängig..) - er kann nicht belegt werden.",
                it: "È un comando di modifica standard (copia, incolla, annulla..) - non può essere usato.",
                es: "Es un comando de edición estándar (copiar, pegar, deshacer..) - no se puede ocupar.",
                fr: "C'est une commande d'édition standard (copier, coller, annuler..) - elle ne peut pas être prise.",
                pt: "É um comando de edição padrão (copiar, colar, desfazer..) - não pode ser usado.",
                ar: "هذا أمر تحرير قياسي (نسخ، لصق، تراجع..) - لا يمكن استخدامه.",
                hi: "यह एक मानक संपादन कमांड है (कॉपी, पेस्ट, पूर्ववत..) - इसे नहीं लिया जा सकता।",
                bn: "এটি একটি সাধারণ সম্পাদনা কমান্ড (কপি, পেস্ট, আনডু..) - এটি নেওয়া যাবে না।",
                ur: "یہ ترمیم کی ایک معیاری کمانڈ ہے (کاپی، پیسٹ، واپس..) - اسے نہیں لیا جا سکتا۔",
                zh: "这是标准编辑命令（复制、粘贴、撤销..）- 不能占用。");

            Add("На раскладке «{1}» эта комбинация печатает «{0}» - CyrFlip перехватывал бы этот символ.",
                en: "On the «{1}» layout this combination types «{0}» - CyrFlip would swallow that character.",
                uk: "На розкладці «{1}» ця комбінація друкує «{0}» - CyrFlip перехоплював би цей символ.",
                de: "Auf dem Layout «{1}» tippt diese Kombination «{0}» - CyrFlip würde dieses Zeichen abfangen.",
                it: "Nel layout «{1}» questa combinazione digita «{0}» - CyrFlip intercetterebbe quel carattere.",
                es: "En la distribución «{1}» esta combinación escribe «{0}» - CyrFlip interceptaría ese carácter.",
                fr: "Sur la disposition «{1}», cette combinaison tape «{0}» - CyrFlip intercepterait ce caractère.",
                pt: "No layout «{1}» esta combinação digita «{0}» - o CyrFlip interceptaria esse caractere.",
                ar: "في تخطيط «{1}» تكتب هذه التركيبة «{0}» - وسيعترض CyrFlip هذا الحرف.",
                hi: "«{1}» लेआउट पर यह संयोजन «{0}» टाइप करता है - CyrFlip यह अक्षर रोक लेगा।",
                bn: "«{1}» লেআউটে এই সংমিশ্রণ «{0}» টাইপ করে - CyrFlip অক্ষরটি আটকে দেবে।",
                ur: "«{1}» لے آؤٹ پر یہ مجموعہ «{0}» ٹائپ کرتا ہے - CyrFlip یہ حرف روک لے گا۔",
                zh: "在「{1}」布局中，这个组合键会输入「{0}」- CyrFlip 会拦截这个字符。");

            // ---- One owner per chord (ChordRegistry, ticket S0004 KC-6) ----
            Add("Комбинация {0} назначена в Windows для переключения на язык «{1}». Если её займёт CyrFlip, Windows её больше не получит. Всё равно назначить?",
                en: "{0} is assigned in Windows to switch to «{1}». If CyrFlip takes it, Windows will no longer receive it. Assign it anyway?",
                uk: "Комбінацію {0} призначено у Windows для перемикання на мову «{1}». Якщо її займе CyrFlip, Windows її більше не отримає. Однаково призначити?",
                de: "{0} ist in Windows dem Wechsel zu «{1}» zugewiesen. Übernimmt CyrFlip sie, erhält Windows sie nicht mehr. Trotzdem zuweisen?",
                it: "{0} è assegnata in Windows al passaggio a «{1}». Se la prende CyrFlip, Windows non la riceverà più. Assegnarla comunque?",
                es: "{0} está asignada en Windows para cambiar a «{1}». Si la ocupa CyrFlip, Windows dejará de recibirla. ¿Asignarla de todos modos?",
                fr: "{0} est attribuée dans Windows au passage à «{1}». Si CyrFlip la prend, Windows ne la recevra plus. L'attribuer quand même ?",
                pt: "{0} está atribuída no Windows para mudar para «{1}». Se o CyrFlip a usar, o Windows deixará de recebê-la. Atribuir mesmo assim?",
                ar: "التركيبة {0} مخصصة في Windows للتبديل إلى «{1}». إذا أخذها CyrFlip فلن يستقبلها Windows بعد الآن. هل تريد تعيينها على أي حال؟",
                hi: "{0} Windows में «{1}» पर स्विच करने के लिए निर्धारित है। अगर CyrFlip इसे ले लेता है तो Windows को यह नहीं मिलेगा। फिर भी निर्धारित करें?",
                bn: "{0} Windows-এ «{1}»-এ যাওয়ার জন্য নির্ধারিত। CyrFlip এটি নিলে Windows আর এটি পাবে না। তবুও নির্ধারণ করবেন?",
                ur: "{0} Windows میں «{1}» پر جانے کے لیے مقرر ہے۔ اگر CyrFlip اسے لے لے تو Windows کو یہ مزید نہیں ملے گا۔ پھر بھی مقرر کریں؟",
                zh: "{0} 在 Windows 中已分配为切换到「{1}」。如果被 CyrFlip 占用，Windows 将不再收到它。仍然分配吗？");

            Add("Комбинация {0} назначена сразу двум действиям: «{1}» и «{2}». Сработает только одно - смените одну из них.",
                en: "{0} is assigned to two actions at once: «{1}» and «{2}». Only one of them will run - change one of the combinations.",
                uk: "Комбінацію {0} призначено одразу двом діям: «{1}» і «{2}». Спрацює лише одна - змініть одну з комбінацій.",
                de: "{0} ist zwei Aktionen zugleich zugewiesen: «{1}» und «{2}». Nur eine davon wird ausgeführt - ändern Sie eine der Kombinationen.",
                it: "{0} è assegnata a due azioni insieme: «{1}» e «{2}». Ne verrà eseguita solo una - cambia una delle combinazioni.",
                es: "{0} está asignada a dos acciones a la vez: «{1}» y «{2}». Solo se ejecutará una - cambia una de las combinaciones.",
                fr: "{0} est attribuée à deux actions à la fois : «{1}» et «{2}». Une seule sera exécutée - modifiez l'une des combinaisons.",
                pt: "{0} está atribuída a duas ações ao mesmo tempo: «{1}» e «{2}». Só uma será executada - altere uma das combinações.",
                ar: "التركيبة {0} مخصصة لإجراءين معًا: «{1}» و«{2}». سيُنفَّذ واحد فقط - غيّر إحدى التركيبتين.",
                hi: "{0} एक साथ दो कार्यों को सौंपा गया है: «{1}» और «{2}»। केवल एक चलेगा - किसी एक संयोजन को बदलें।",
                bn: "{0} একসঙ্গে দুটি কাজে নির্ধারিত: «{1}» ও «{2}»। শুধু একটি চলবে - একটি সংমিশ্রণ বদলান।",
                ur: "{0} ایک ساتھ دو کاموں کو دیا گیا ہے: «{1}» اور «{2}»۔ صرف ایک چلے گا - کسی ایک مجموعے کو بدلیں۔",
                zh: "{0} 同时分配给了两个操作：「{1}」和「{2}」。只有一个会生效 - 请更改其中一个组合键。");

            Add("Удалить сохранённое сочетание для неустановленной раскладки?",
                en: "Delete saved shortcut for the uninstalled layout?",
                uk: "Видалити збережену комбінацію для невстановленої розкладки?",
                de: "Gespeicherte Tastenkombination für die nicht installierte Tastaturbelegung löschen?",
                it: "Eliminare la scorciatoia salvata per il layout non installato?",
                es: "¿Eliminar el acceso directo guardado para la distribución no instalada?",
                fr: "Supprimer le raccourci enregistré pour la disposition non installée ?",
                pt: "Excluir o atalho salvo para o layout não instalado?",
                ar: "هل تريد حذف الاختصار المحفوظ للتخطيط غير المثبت؟",
                hi: "क्या अनइंस्टॉल किए गए लेआउट के लिए सहेजा गया शॉर्टकट हटाएं?",
                bn: "আনইনস্টল করা লেআউটের সংরক্ষিত শর্টকাট কি মুছবেন?",
                ur: "کیا ان انسٹال شدہ لے آؤٹ کے لیے محفوظ کردہ شارٹ کٹ کو حذف کریں؟",
                zh: "是否删除未安装键盘布局的已保存快捷键？");
        }
    }
}
