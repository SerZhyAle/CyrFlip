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
        }
    }
}
