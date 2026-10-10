namespace CyrFlip
{
    /// <summary>
    /// Strings of the "Все комбинации" settings page (ticket S0056): every chord in one list, its state,
    /// and the reset. Wording follows the existing "Горячие клавиши" page ("hotkeys").
    /// </summary>
    internal static partial class Localization
    {
        private static void AddHotkeysOverviewStrings()
        {
            Add("Все комбинации",
                en: "All hotkeys", uk: "Усі комбінації", de: "Alle Tastenkürzel", it: "Tutte le scorciatoie",
                es: "Todos los atajos", fr: "Tous les raccourcis", pt: "Todos os atalhos",
                ar: "كل الاختصارات", hi: "सभी शॉर्टकट", bn: "সব শর্টকাট", ur: "تمام شارٹ کٹس", zh: "全部快捷键");

            Add("Все комбинации CyrFlip в одном списке: что делает каждая, действует ли она сейчас и почему нет.",
                en: "Every CyrFlip hotkey in one list: what each one does, whether it works right now and, if not, why.",
                uk: "Усі комбінації CyrFlip в одному списку: що робить кожна, чи діє вона зараз і чому ні.",
                de: "Alle CyrFlip-Tastenkürzel in einer Liste: was jedes tut, ob es gerade wirkt und, wenn nicht, warum.",
                it: "Tutte le scorciatoie di CyrFlip in un elenco: cosa fa ciascuna, se è attiva ora e, se no, perché.",
                es: "Todos los atajos de CyrFlip en una lista: qué hace cada uno, si funciona ahora y, si no, por qué.",
                fr: "Tous les raccourcis de CyrFlip dans une liste : ce que fait chacun, s'il agit en ce moment et, sinon, pourquoi.",
                pt: "Todos os atalhos do CyrFlip numa lista: o que cada um faz, se está ativo agora e, se não, porquê.",
                ar: "كل اختصارات CyrFlip في قائمة واحدة: ما يفعله كل منها، وهل يعمل الآن، وإن لم يعمل فلماذا.",
                hi: "CyrFlip के सभी शॉर्टकट एक सूची में: हर एक क्या करता है, क्या वह अभी काम कर रहा है, और नहीं तो क्यों।",
                bn: "CyrFlip-এর সব শর্টকাট এক তালিকায়: প্রতিটি কী করে, এখন কাজ করছে কি না, আর না করলে কেন।",
                ur: "CyrFlip کے تمام شارٹ کٹس ایک فہرست میں: ہر ایک کیا کرتا ہے، آیا ابھی کام کر رہا ہے، اور اگر نہیں تو کیوں۔",
                zh: "CyrFlip 的全部快捷键集中在一个列表里：各自的作用、此刻是否生效，以及未生效的原因。");

            Add("Тот же выключатель, что на странице «Горячие клавиши»: пока он снят, ни одна комбинация CyrFlip не действует.",
                en: "The same switch as on the “Hotkeys” page: while it is off, no CyrFlip hotkey works.",
                uk: "Той самий перемикач, що на сторінці «Гарячі клавіші»: поки його знято, жодна комбінація CyrFlip не діє.",
                de: "Derselbe Schalter wie auf der Seite „Tastenkürzel“: Ist er aus, wirkt kein CyrFlip-Tastenkürzel.",
                it: "Lo stesso interruttore della pagina «Scorciatoie»: finché è disattivato, nessuna scorciatoia di CyrFlip funziona.",
                es: "El mismo interruptor que en la página «Atajos de teclado»: mientras esté apagado, ningún atajo de CyrFlip funciona.",
                fr: "Le même interrupteur que sur la page « Raccourcis clavier » : tant qu'il est coupé, aucun raccourci de CyrFlip n'agit.",
                pt: "O mesmo interruptor da página «Atalhos de teclado»: enquanto estiver desligado, nenhum atalho do CyrFlip funciona.",
                ar: "هو المفتاح نفسه الموجود في صفحة «اختصارات لوحة المفاتيح»: ما دام معطّلًا لا يعمل أي اختصار في CyrFlip.",
                hi: "वही स्विच जो «शॉर्टकट कुंजियाँ» पृष्ठ पर है: जब तक यह बंद है, CyrFlip का कोई शॉर्टकट काम नहीं करता।",
                bn: "«শর্টকাট কী» পৃষ্ঠার একই সুইচ: এটি বন্ধ থাকলে CyrFlip-এর কোনো শর্টকাট কাজ করে না।",
                ur: "وہی سوئچ جو «شارٹ کٹ کیز» صفحے پر ہے: جب تک یہ بند ہے، CyrFlip کا کوئی شارٹ کٹ کام نہیں کرتا۔",
                zh: "与“快捷键”页面上的开关相同：关闭时，CyrFlip 的任何快捷键都不会生效。");

            Add("Сбросить все...",
                en: "Reset all...", uk: "Скинути всі...", de: "Alle zurücksetzen...", it: "Ripristina tutte...",
                es: "Restablecer todos...", fr: "Tout réinitialiser...", pt: "Repor todos...",
                ar: "إعادة تعيين الكل...", hi: "सभी रीसेट करें...", bn: "সব রিসেট করুন...", ur: "سب ری سیٹ کریں...", zh: "全部重置...");

            Add("Возвращает комбинации и их выключатели к тем, с которыми CyrFlip поставляется. Модули, строки таблиц и сценарии не удаляются и не выключаются.",
                en: "Puts the hotkeys and their switches back to what CyrFlip ships with. Modules, table rows and scenarios are not deleted or switched off.",
                uk: "Повертає комбінації та їхні перемикачі до тих, з якими постачається CyrFlip. Модулі, рядки таблиць і сценарії не видаляються й не вимикаються.",
                de: "Setzt die Tastenkürzel und ihre Schalter auf den Auslieferungszustand von CyrFlip zurück. Module, Tabellenzeilen und Szenarien werden weder gelöscht noch ausgeschaltet.",
                it: "Riporta le scorciatoie e i relativi interruttori ai valori con cui CyrFlip viene fornito. Moduli, righe delle tabelle e scenari non vengono eliminati né disattivati.",
                es: "Devuelve los atajos y sus interruptores a como llegan con CyrFlip. Los módulos, las filas de las tablas y los escenarios no se eliminan ni se apagan.",
                fr: "Rétablit les raccourcis et leurs interrupteurs tels que CyrFlip les livre. Les modules, les lignes des tableaux et les scénarios ne sont ni supprimés ni désactivés.",
                pt: "Repõe os atalhos e os respetivos interruptores como o CyrFlip os traz de origem. Módulos, linhas das tabelas e cenários não são eliminados nem desligados.",
                ar: "تعيد الاختصارات ومفاتيحها إلى ما يأتي به CyrFlip افتراضيًا. لا تُحذف الوحدات ولا صفوف الجداول ولا السيناريوهات ولا تُعطَّل.",
                hi: "शॉर्टकट और उनके स्विच को वैसा कर देता है जैसा CyrFlip के साथ आता है। मॉड्यूल, तालिका की पंक्तियाँ और परिदृश्य न हटाए जाते हैं, न बंद किए जाते हैं।",
                bn: "শর্টকাট ও তাদের সুইচ CyrFlip-এর সঙ্গে আসা অবস্থায় ফিরিয়ে আনে। মডিউল, টেবিলের সারি ও দৃশ্যপট মোছা বা বন্ধ করা হয় না।",
                ur: "شارٹ کٹس اور ان کے سوئچ کو اس حالت میں لوٹاتا ہے جس میں CyrFlip آتا ہے۔ ماڈیولز، جدول کی قطاریں اور منظرنامے نہ حذف ہوتے ہیں نہ بند۔",
                zh: "把快捷键及其开关恢复为 CyrFlip 出厂时的状态。模块、表格行和方案不会被删除或关闭。");

            Add("Основные комбинации",
                en: "Main hotkeys", uk: "Основні комбінації", de: "Wichtigste Tastenkürzel", it: "Scorciatoie principali",
                es: "Atajos principales", fr: "Raccourcis principaux", pt: "Atalhos principais",
                ar: "الاختصارات الأساسية", hi: "मुख्य शॉर्टकट", bn: "প্রধান শর্টকাট", ur: "بنیادی شارٹ کٹس", zh: "主要快捷键");

            Add("Меню над выделением",
                en: "Menu over a selection", uk: "Меню над виділенням", de: "Menü über der Auswahl", it: "Menu sulla selezione",
                es: "Menú sobre la selección", fr: "Menu sur la sélection", pt: "Menu sobre a seleção",
                ar: "قائمة فوق التحديد", hi: "चयन के ऊपर मेनू", bn: "নির্বাচনের উপর মেনু", ur: "انتخاب پر مینو", zh: "选区上的菜单");

            Add("Переключение раскладок Windows",
                en: "Windows layout switch", uk: "Перемикання розкладок Windows", de: "Windows-Layoutwechsel", it: "Cambio layout di Windows",
                es: "Cambio de distribución de Windows", fr: "Changement de disposition Windows", pt: "Troca de layout do Windows",
                ar: "تبديل تخطيط Windows", hi: "Windows लेआउट बदलना", bn: "Windows লেআউট বদল", ur: "Windows لے آؤٹ تبدیلی", zh: "Windows 布局切换");

            Add("выключено",
                en: "off", uk: "вимкнено", de: "aus", it: "disattivata", es: "desactivado", fr: "désactivé", pt: "desligado",
                ar: "متوقف", hi: "बंद", bn: "বন্ধ", ur: "بند", zh: "已关闭");

            Add("модуль выключен",
                en: "module is off", uk: "модуль вимкнено", de: "Modul ist aus", it: "modulo disattivato", es: "módulo apagado",
                fr: "module désactivé", pt: "módulo desligado", ar: "الوحدة متوقفة", hi: "मॉड्यूल बंद है", bn: "মডিউল বন্ধ",
                ur: "ماڈیول بند ہے", zh: "模块已关闭");

            Add("не назначена",
                en: "not assigned", uk: "не призначено", de: "nicht zugewiesen", it: "non assegnata", es: "sin asignar",
                fr: "non attribué", pt: "não atribuído", ar: "غير معيّن", hi: "असाइन नहीं", bn: "নির্ধারিত নয়",
                ur: "تفویض نہیں", zh: "未分配");

            Add("не распознана",
                en: "not recognised", uk: "не розпізнано", de: "nicht erkannt", it: "non riconosciuta", es: "no reconocida",
                fr: "non reconnu", pt: "não reconhecido", ar: "غير معروف", hi: "पहचानी नहीं गई", bn: "চেনা যায়নি",
                ur: "پہچانا نہیں گیا", zh: "无法识别");

            Add("не заполнена",
                en: "incomplete", uk: "не заповнено", de: "unvollständig", it: "incompleta", es: "incompleta",
                fr: "incomplète", pt: "incompleta", ar: "غير مكتمل", hi: "अधूरी", bn: "অসম্পূর্ণ",
                ur: "نامکمل", zh: "未填完整");

            Add("совпадает с: {0}",
                en: "same as: {0}", uk: "збігається з: {0}", de: "wie: {0}", it: "uguale a: {0}", es: "igual que: {0}",
                fr: "identique à : {0}", pt: "igual a: {0}", ar: "تطابق: {0}", hi: "इसके समान: {0}", bn: "একই: {0}",
                ur: "برابر: {0}", zh: "与以下冲突：{0}");

            Add("Здесь пока нет ни одной комбинации.",
                en: "There are no hotkeys here yet.", uk: "Тут поки немає жодної комбінації.",
                de: "Hier gibt es noch kein Tastenkürzel.", it: "Qui non c'è ancora nessuna scorciatoia.",
                es: "Aquí todavía no hay ningún atajo.", fr: "Il n'y a encore aucun raccourci ici.",
                pt: "Ainda não há nenhum atalho aqui.", ar: "لا توجد اختصارات هنا بعد.",
                hi: "यहाँ अभी कोई शॉर्टकट नहीं है।", bn: "এখানে এখনও কোনো শর্টকাট নেই।",
                ur: "یہاں ابھی کوئی شارٹ کٹ نہیں ہے۔", zh: "这里还没有快捷键。");

            Add("Горячие клавиши выключены: ни одна комбинация CyrFlip сейчас не действует.",
                en: "Hotkeys are off: no CyrFlip hotkey works right now.",
                uk: "Гарячі клавіші вимкнено: жодна комбінація CyrFlip зараз не діє.",
                de: "Tastenkürzel sind aus: Zurzeit wirkt kein CyrFlip-Tastenkürzel.",
                it: "Scorciatoie disattivate: al momento nessuna scorciatoia di CyrFlip funziona.",
                es: "Atajos desactivados: ahora mismo ningún atajo de CyrFlip funciona.",
                fr: "Raccourcis désactivés : aucun raccourci de CyrFlip n'agit pour le moment.",
                pt: "Atalhos desligados: neste momento nenhum atalho do CyrFlip funciona.",
                ar: "الاختصارات متوقفة: لا يعمل أي اختصار في CyrFlip الآن.",
                hi: "शॉर्टकट बंद हैं: अभी CyrFlip का कोई शॉर्टकट काम नहीं कर रहा।",
                bn: "শর্টকাট বন্ধ: এই মুহূর্তে CyrFlip-এর কোনো শর্টকাট কাজ করছে না।",
                ur: "شارٹ کٹس بند ہیں: اس وقت CyrFlip کا کوئی شارٹ کٹ کام نہیں کر رہا۔",
                zh: "快捷键已关闭：目前 CyrFlip 的任何快捷键都不会生效。");

            Add("Пока в фокусе клиент удалённого рабочего стола (mstsc/msrdc), комбинации уходят в удалённый сеанс.",
                en: "While a remote desktop client (mstsc/msrdc) has the focus, hotkeys go to the remote session.",
                uk: "Поки в фокусі клієнт віддаленого робочого стола (mstsc/msrdc), комбінації йдуть у віддалений сеанс.",
                de: "Solange ein Remotedesktop-Client (mstsc/msrdc) den Fokus hat, gehen die Tastenkürzel an die Remotesitzung.",
                it: "Finché il focus è su un client desktop remoto (mstsc/msrdc), le scorciatoie vanno alla sessione remota.",
                es: "Mientras un cliente de escritorio remoto (mstsc/msrdc) tiene el foco, los atajos van a la sesión remota.",
                fr: "Tant qu'un client Bureau à distance (mstsc/msrdc) a le focus, les raccourcis vont à la session distante.",
                pt: "Enquanto um cliente de ambiente de trabalho remoto (mstsc/msrdc) tiver o foco, os atalhos seguem para a sessão remota.",
                ar: "ما دام عميل سطح المكتب البعيد (mstsc/msrdc) في المقدمة، تُرسل الاختصارات إلى الجلسة البعيدة.",
                hi: "जब तक रिमोट डेस्कटॉप क्लाइंट (mstsc/msrdc) फ़ोकस में है, शॉर्टकट रिमोट सत्र में चले जाते हैं।",
                bn: "রিমোট ডেস্কটপ ক্লায়েন্ট (mstsc/msrdc) ফোকাসে থাকলে শর্টকাট রিমোট সেশনে চলে যায়।",
                ur: "جب تک ریموٹ ڈیسک ٹاپ کلائنٹ (mstsc/msrdc) فوکس میں ہے، شارٹ کٹس ریموٹ سیشن میں چلے جاتے ہیں۔",
                zh: "远程桌面客户端 (mstsc/msrdc) 处于焦点时，快捷键会交给远程会话。");

            Add("Эти комбинации ведёт сама Windows, не CyrFlip: здесь они только показаны, чтобы было видно, что занято.",
                en: "Windows handles these hotkeys, not CyrFlip: they are only shown here so you can see what is taken.",
                uk: "Ці комбінації веде сама Windows, а не CyrFlip: тут вони лише показані, щоб було видно, що зайнято.",
                de: "Diese Tastenkürzel verwaltet Windows selbst, nicht CyrFlip: Sie werden hier nur angezeigt, damit man sieht, was belegt ist.",
                it: "Queste scorciatoie sono gestite da Windows, non da CyrFlip: sono mostrate solo per far vedere cosa è occupato.",
                es: "Estos atajos los gestiona Windows, no CyrFlip: aquí solo se muestran para ver qué está ocupado.",
                fr: "Ces raccourcis sont gérés par Windows, pas par CyrFlip : ils ne sont affichés ici que pour voir ce qui est pris.",
                pt: "Estes atalhos são geridos pelo Windows, não pelo CyrFlip: aparecem aqui apenas para se ver o que está ocupado.",
                ar: "هذه الاختصارات يديرها Windows نفسه وليس CyrFlip؛ وتُعرض هنا فقط ليظهر ما هو مشغول.",
                hi: "इन शॉर्टकट को Windows स्वयं संभालता है, CyrFlip नहीं: ये यहाँ केवल यह दिखाने के लिए हैं कि क्या व्यस्त है।",
                bn: "এই শর্টকাটগুলো Windows নিজেই সামলায়, CyrFlip নয়: কোনটি দখলে আছে তা দেখাতেই এখানে দেখানো হয়েছে।",
                ur: "ان شارٹ کٹس کو Windows خود چلاتا ہے، CyrFlip نہیں: یہاں انہیں صرف یہ دکھانے کے لیے ظاہر کیا گیا ہے کہ کیا مصروف ہے۔",
                zh: "这些快捷键由 Windows 自己处理，而不是 CyrFlip：此处仅列出，方便查看哪些已被占用。");

            Add("Открыть «Языки Windows»",
                en: "Open “Windows languages”", uk: "Відкрити «Мови Windows»", de: "„Windows-Sprachen“ öffnen",
                it: "Apri «Lingue di Windows»", es: "Abrir «Idiomas de Windows»", fr: "Ouvrir « Langues Windows »",
                pt: "Abrir «Idiomas do Windows»", ar: "فتح «لغات Windows»", hi: "«Windows की भाषाएँ» खोलें",
                bn: "«Windows-এর ভাষা» খুলুন", ur: "«Windows کی زبانیں» کھولیں", zh: "打开“Windows 语言”");

            Add("Комбинация: {0}",
                en: "Hotkey: {0}", uk: "Комбінація: {0}", de: "Tastenkürzel: {0}", it: "Scorciatoia: {0}", es: "Atajo: {0}",
                fr: "Raccourci : {0}", pt: "Atalho: {0}", ar: "الاختصار: {0}", hi: "शॉर्टकट: {0}", bn: "শর্টকাট: {0}",
                ur: "شارٹ کٹ: {0}", zh: "快捷键：{0}");

            Add("Все комбинации уже такие, как при установке.",
                en: "All hotkeys are already as they were when installed.",
                uk: "Усі комбінації вже такі, як після встановлення.",
                de: "Alle Tastenkürzel sind bereits wie bei der Installation.",
                it: "Tutte le scorciatoie sono già come all'installazione.",
                es: "Todos los atajos ya están como en la instalación.",
                fr: "Tous les raccourcis sont déjà tels qu'à l'installation.",
                pt: "Todos os atalhos já estão como na instalação.",
                ar: "جميع الاختصارات هي بالفعل كما كانت عند التثبيت.",
                hi: "सभी शॉर्टकट पहले से वैसे ही हैं जैसे इंस्टॉल के समय थे।",
                bn: "সব শর্টকাট ইতিমধ্যে ইনস্টলের সময়ের মতোই আছে।",
                ur: "تمام شارٹ کٹس پہلے ہی ویسے ہیں جیسے انسٹال کے وقت تھے۔",
                zh: "所有快捷键已经是安装时的状态。");

            Add("Вернуть комбинации к тем, с которыми CyrFlip поставляется?",
                en: "Put the hotkeys back to what CyrFlip ships with?",
                uk: "Повернути комбінації до тих, з якими постачається CyrFlip?",
                de: "Tastenkürzel auf den Auslieferungszustand von CyrFlip zurücksetzen?",
                it: "Riportare le scorciatoie a quelle con cui viene fornito CyrFlip?",
                es: "¿Devolver los atajos a como llegan con CyrFlip?",
                fr: "Rétablir les raccourcis tels que CyrFlip les livre ?",
                pt: "Repor os atalhos como o CyrFlip os traz de origem?",
                ar: "إعادة الاختصارات إلى ما يأتي به CyrFlip افتراضيًا؟",
                hi: "शॉर्टकट को वैसा कर दें जैसा CyrFlip के साथ आता है?",
                bn: "শর্টকাটগুলো CyrFlip-এর সঙ্গে আসা অবস্থায় ফিরিয়ে আনবেন?",
                ur: "کیا شارٹ کٹس کو اس حالت میں لوٹایا جائے جس میں CyrFlip آتا ہے؟",
                zh: "要把快捷键恢复为 CyrFlip 出厂时的状态吗？");

            Add("Будет изменено: {0}. Уже такие, как при установке: {1}.",
                en: "Will change: {0}. Already as installed: {1}.",
                uk: "Буде змінено: {0}. Уже такі, як після встановлення: {1}.",
                de: "Wird geändert: {0}. Bereits wie bei der Installation: {1}.",
                it: "Verranno modificate: {0}. Già come all'installazione: {1}.",
                es: "Se cambiarán: {0}. Ya como en la instalación: {1}.",
                fr: "À modifier : {0}. Déjà tels qu'à l'installation : {1}.",
                pt: "Serão alterados: {0}. Já como na instalação: {1}.",
                ar: "سيتغير: {0}. كما في التثبيت بالفعل: {1}.",
                hi: "बदलेंगे: {0}। पहले से इंस्टॉल जैसे: {1}।",
                bn: "বদলাবে: {0}। ইতিমধ্যে ইনস্টলের মতো: {1}।",
                ur: "تبدیل ہوں گے: {0}۔ پہلے ہی انسٹال جیسے: {1}۔",
                zh: "将更改：{0}。已是安装时状态：{1}。");

            Add("Строки таблиц и сценарии не удаляются; у тех, что поставлялись без комбинации, она будет снята.",
                en: "Table rows and scenarios are not deleted; those that shipped without a hotkey will have theirs cleared.",
                uk: "Рядки таблиць і сценарії не видаляються; у тих, що постачалися без комбінації, її буде знято.",
                de: "Tabellenzeilen und Szenarien werden nicht gelöscht; bei denen, die ohne Tastenkürzel geliefert wurden, wird es entfernt.",
                it: "Le righe delle tabelle e gli scenari non vengono eliminati; a quelli forniti senza scorciatoia verrà tolta.",
                es: "Las filas de las tablas y los escenarios no se eliminan; a los que llegaron sin atajo se les quitará.",
                fr: "Les lignes des tableaux et les scénarios ne sont pas supprimés ; celles livrées sans raccourci perdront le leur.",
                pt: "As linhas das tabelas e os cenários não são eliminados; os que vieram sem atalho ficarão sem ele.",
                ar: "لا تُحذف صفوف الجداول ولا السيناريوهات؛ وتُزال الاختصارات من التي جاءت بلا اختصار.",
                hi: "तालिका की पंक्तियाँ और परिदृश्य नहीं हटाए जाते; जो बिना शॉर्टकट के आए थे, उनका शॉर्टकट हटा दिया जाएगा।",
                bn: "টেবিলের সারি ও দৃশ্যপট মোছা হয় না; যেগুলো শর্টকাট ছাড়া এসেছিল তাদের শর্টকাট সরানো হবে।",
                ur: "جدول کی قطاریں اور منظرنامے حذف نہیں ہوتے؛ جو بغیر شارٹ کٹ کے آئے تھے ان کا شارٹ کٹ ہٹا دیا جائے گا۔",
                zh: "表格行和方案不会被删除；出厂时没有快捷键的，其快捷键将被清除。");

            Add("Общий выключатель горячих клавиш будет включён.",
                en: "The master hotkey switch will be turned on.",
                uk: "Загальний перемикач гарячих клавіш буде ввімкнено.",
                de: "Der Hauptschalter für Tastenkürzel wird eingeschaltet.",
                it: "L'interruttore generale delle scorciatoie verrà attivato.",
                es: "El interruptor general de atajos se activará.",
                fr: "L'interrupteur général des raccourcis sera activé.",
                pt: "O interruptor geral dos atalhos será ligado.",
                ar: "سيتم تشغيل المفتاح العام للاختصارات.",
                hi: "शॉर्टकट का मुख्य स्विच चालू कर दिया जाएगा।",
                bn: "শর্টকাটের মূল সুইচ চালু করা হবে।",
                ur: "شارٹ کٹس کا مرکزی سوئچ آن کر دیا جائے گا۔",
                zh: "将打开快捷键总开关。");

            Add("Не будут изменены:",
                en: "Will not be changed:", uk: "Не буде змінено:", de: "Werden nicht geändert:", it: "Non verranno modificate:",
                es: "No se cambiarán:", fr: "Ne seront pas modifiés :", pt: "Não serão alterados:",
                ar: "لن يتغير:", hi: "नहीं बदलेंगे:", bn: "বদলাবে না:", ur: "تبدیل نہیں ہوں گے:", zh: "不会更改：");

            Add("«{0}»: комбинация {1} занята действием «{2}».",
                en: "“{0}”: the hotkey {1} is taken by “{2}”.",
                uk: "«{0}»: комбінацію {1} зайнято дією «{2}».",
                de: "„{0}“: Das Tastenkürzel {1} ist durch „{2}“ belegt.",
                it: "«{0}»: la scorciatoia {1} è occupata da «{2}».",
                es: "«{0}»: el atajo {1} está ocupado por «{2}».",
                fr: "« {0} » : le raccourci {1} est pris par « {2} ».",
                pt: "«{0}»: o atalho {1} está ocupado por «{2}».",
                ar: "«{0}»: الاختصار {1} مشغول بواسطة «{2}».",
                hi: "«{0}»: शॉर्टकट {1} «{2}» के पास है।",
                bn: "«{0}»: শর্টকাট {1} «{2}»-এর দখলে আছে।",
                ur: "«{0}»: شارٹ کٹ {1} «{2}» کے پاس ہے۔",
                zh: "“{0}”：快捷键 {1} 已被“{2}”占用。");

            Add("«{0}»: строка изменилась, пока шёл сброс, - не тронута.",
                en: "“{0}”: the row changed during the reset, so it was left alone.",
                uk: "«{0}»: рядок змінився під час скидання, тому його не чіпали.",
                de: "„{0}“: Die Zeile hat sich während des Zurücksetzens geändert und blieb unberührt.",
                it: "«{0}»: la riga è cambiata durante il ripristino ed è stata lasciata com'è.",
                es: "«{0}»: la fila cambió durante el restablecimiento y se dejó como estaba.",
                fr: "« {0} » : la ligne a changé pendant la réinitialisation et n'a pas été touchée.",
                pt: "«{0}»: a linha mudou durante a reposição e foi deixada como estava.",
                ar: "«{0}»: تغيّر الصف أثناء إعادة التعيين فلم يُمَسّ.",
                hi: "«{0}»: रीसेट के दौरान पंक्ति बदल गई, इसलिए उसे छुआ नहीं गया।",
                bn: "«{0}»: রিসেটের সময় সারিটি বদলে গেছে, তাই এটিকে স্পর্শ করা হয়নি।",
                ur: "«{0}»: ری سیٹ کے دوران قطار بدل گئی، اس لیے اسے چھوا نہیں گیا۔",
                zh: "“{0}”：重置期间该行已变化，因此未改动。");

            Add("«{0}»: строки больше нет.",
                en: "“{0}”: the row no longer exists.", uk: "«{0}»: рядка вже немає.", de: "„{0}“: Die Zeile gibt es nicht mehr.",
                it: "«{0}»: la riga non esiste più.", es: "«{0}»: la fila ya no existe.", fr: "« {0} » : la ligne n'existe plus.",
                pt: "«{0}»: a linha já não existe.", ar: "«{0}»: الصف لم يعد موجودًا.", hi: "«{0}»: वह पंक्ति अब नहीं है।",
                bn: "«{0}»: সারিটি আর নেই।", ur: "«{0}»: وہ قطار اب موجود نہیں۔", zh: "“{0}”：该行已不存在。");

            Add("«{0}»: заводская комбинация занята в Windows (язык «{1}»). Если продолжить, Windows её больше не получит.",
                en: "“{0}”: the shipped hotkey is used by Windows (language “{1}”). If you continue, Windows will no longer receive it.",
                uk: "«{0}»: заводська комбінація зайнята в Windows (мова «{1}»). Якщо продовжити, Windows її більше не отримає.",
                de: "„{0}“: Das ausgelieferte Tastenkürzel ist in Windows belegt (Sprache „{1}“). Wenn Sie fortfahren, erhält Windows es nicht mehr.",
                it: "«{0}»: la scorciatoia di fabbrica è usata da Windows (lingua «{1}»). Se continui, Windows non la riceverà più.",
                es: "«{0}»: el atajo de fábrica lo usa Windows (idioma «{1}»). Si continúa, Windows ya no lo recibirá.",
                fr: "« {0} » : le raccourci d'origine est utilisé par Windows (langue « {1} »). Si vous continuez, Windows ne le recevra plus.",
                pt: "«{0}»: o atalho de origem é usado pelo Windows (idioma «{1}»). Se continuar, o Windows deixará de o receber.",
                ar: "«{0}»: الاختصار الافتراضي مستخدم في Windows (اللغة «{1}»). إذا تابعت فلن يتلقاه Windows بعد الآن.",
                hi: "«{0}»: मूल शॉर्टकट Windows में उपयोग में है (भाषा «{1}»)। आगे बढ़ने पर Windows को वह नहीं मिलेगा।",
                bn: "«{0}»: মূল শর্টকাটটি Windows-এ ব্যবহৃত (ভাষা «{1}»)। এগোলে Windows আর এটি পাবে না।",
                ur: "«{0}»: اصل شارٹ کٹ Windows میں استعمال ہو رہا ہے (زبان «{1}»)۔ آگے بڑھنے پر Windows اسے مزید نہیں پائے گا۔",
                zh: "“{0}”：出厂快捷键已被 Windows 使用（语言“{1}”）。继续的话，Windows 将不再收到它。");

            Add("Сброшено: {0}. Не сброшено: {1}.",
                en: "Reset: {0}. Not reset: {1}.", uk: "Скинуто: {0}. Не скинуто: {1}.", de: "Zurückgesetzt: {0}. Nicht zurückgesetzt: {1}.",
                it: "Ripristinate: {0}. Non ripristinate: {1}.", es: "Restablecidos: {0}. Sin restablecer: {1}.",
                fr: "Réinitialisés : {0}. Non réinitialisés : {1}.", pt: "Repostos: {0}. Não repostos: {1}.",
                ar: "أُعيد تعيين: {0}. لم يُعد تعيين: {1}.", hi: "रीसेट: {0}। रीसेट नहीं: {1}।", bn: "রিসেট হয়েছে: {0}। হয়নি: {1}।",
                ur: "ری سیٹ ہوئے: {0}۔ ری سیٹ نہیں ہوئے: {1}۔", zh: "已重置：{0}。未重置：{1}。");

            Add("Общий выключатель горячих клавиш снова включён.",
                en: "The master hotkey switch is on again.",
                uk: "Загальний перемикач гарячих клавіш знову ввімкнено.",
                de: "Der Hauptschalter für Tastenkürzel ist wieder eingeschaltet.",
                it: "L'interruttore generale delle scorciatoie è di nuovo attivo.",
                es: "El interruptor general de atajos vuelve a estar activado.",
                fr: "L'interrupteur général des raccourcis est de nouveau activé.",
                pt: "O interruptor geral dos atalhos voltou a estar ligado.",
                ar: "عاد المفتاح العام للاختصارات إلى التشغيل.",
                hi: "शॉर्टकट का मुख्य स्विच फिर से चालू है।",
                bn: "শর্টকাটের মূল সুইচ আবার চালু।",
                ur: "شارٹ کٹس کا مرکزی سوئچ دوبارہ آن ہے۔",
                zh: "快捷键总开关已重新打开。");
        }
    }
}
