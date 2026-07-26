namespace CyrFlip
{
    /// <summary>The "Языки Windows" tab: keyboard layouts, the cycle chord and Windows' own language hotkeys.</summary>
    internal static partial class Localization
    {
        private static void AddWindowsLanguageStrings()
        {
            Add("Языки Windows",
                en: "Windows languages", uk: "Мови Windows", de: "Windows-Sprachen", it: "Lingue di Windows",
                es: "Idiomas de Windows", fr: "Langues Windows", pt: "Idiomas do Windows",
                ar: "لغات Windows", hi: "Windows की भाषाएँ", bn: "Windows-এর ভাষা",
                ur: "Windows کی زبانیں", zh: "Windows 语言");

            Add("Всё, что раньше требовало раздела «Язык и регион» в параметрах Windows: раскладки клавиатуры, порядок между ними и сочетания переключения. Раскладки уже входят в состав Windows — ничего не скачивается. Установка языка интерфейса (перевод самой Windows) остаётся за кнопкой «Открыть настройки Windows».",
                en: "Everything that used to need the Windows «Language & region» pane: keyboard layouts, their order, and the switch shortcuts. The layouts already ship with Windows — nothing is downloaded. Installing a display language (translating Windows itself) stays behind the «Open Windows settings» button.",
                uk: "Усе, що раніше вимагало розділу «Мова й регіон» у параметрах Windows: розкладки клавіатури, їхній порядок і сполучення перемикання. Розкладки вже входять до складу Windows — нічого не завантажується. Встановлення мови інтерфейсу (переклад самої Windows) лишається за кнопкою «Відкрити налаштування Windows».",
                de: "Alles, wofür früher der Windows-Bereich «Sprache und Region» nötig war: Tastaturlayouts, ihre Reihenfolge und die Umschaltkürzel. Die Layouts sind bereits Teil von Windows — es wird nichts heruntergeladen. Das Installieren einer Anzeigesprache (die Übersetzung von Windows selbst) bleibt hinter der Schaltfläche «Windows-Einstellungen öffnen».",
                it: "Tutto ciò che prima richiedeva la sezione «Lingua e area geografica» di Windows: i layout di tastiera, il loro ordine e le scorciatoie di commutazione. I layout sono già inclusi in Windows: non si scarica nulla. L'installazione di una lingua di visualizzazione (la traduzione di Windows stesso) resta dietro il pulsante «Apri le impostazioni di Windows».",
                es: "Todo lo que antes requería el panel «Idioma y región» de Windows: distribuciones de teclado, su orden y los atajos de cambio. Las distribuciones ya vienen con Windows: no se descarga nada. Instalar un idioma de presentación (traducir el propio Windows) sigue estando tras el botón «Abrir la configuración de Windows».",
                fr: "Tout ce qui exigeait auparavant le volet «Langue et région» de Windows : dispositions de clavier, leur ordre et les raccourcis de bascule. Les dispositions sont déjà livrées avec Windows — rien n'est téléchargé. L'installation d'une langue d'affichage (la traduction de Windows lui-même) reste derrière le bouton «Ouvrir les paramètres Windows».",
                pt: "Tudo o que antes exigia o painel «Idioma e região» do Windows: layouts de teclado, sua ordem e os atalhos de troca. Os layouts já vêm com o Windows — nada é baixado. Instalar um idioma de exibição (traduzir o próprio Windows) continua atrás do botão «Abrir as configurações do Windows».",
                ar: "كل ما كان يتطلب سابقًا صفحة «اللغة والمنطقة» في إعدادات Windows: تخطيطات لوحة المفاتيح وترتيبها واختصارات التبديل. التخطيطات مضمّنة في Windows أصلًا — ولا يُنزَّل أي شيء. أما تثبيت لغة العرض (ترجمة Windows نفسه) فيبقى خلف زر «فتح إعدادات Windows».",
                hi: "वह सब जो पहले Windows के «भाषा और क्षेत्र» पैनल में करना पड़ता था: कीबोर्ड लेआउट, उनका क्रम और स्विच करने के शॉर्टकट। लेआउट पहले से Windows के साथ आते हैं — कुछ भी डाउनलोड नहीं होता। डिस्प्ले भाषा स्थापित करना (यानी स्वयं Windows का अनुवाद) «Windows सेटिंग्स खोलें» बटन के पीछे ही रहता है।",
                bn: "যা কিছু আগে Windows-এর «ভাষা ও অঞ্চল» পাতায় করতে হতো: কীবোর্ড লেআউট, তাদের ক্রম এবং পরিবর্তনের শর্টকাট। লেআউটগুলি আগে থেকেই Windows-এর সঙ্গে আসে — কিছুই ডাউনলোড হয় না। ডিসপ্লে ভাষা ইনস্টল করা (অর্থাৎ Windows-এর নিজের অনুবাদ) «Windows সেটিংস খুলুন» বোতামের পিছনেই থাকে।",
                ur: "وہ سب کچھ جو پہلے Windows کے «زبان اور علاقہ» صفحے میں کرنا پڑتا تھا: کی بورڈ لے آؤٹ، اُن کی ترتیب اور تبدیلی کے شارٹ کٹس۔ لے آؤٹ پہلے ہی Windows کے ساتھ آتے ہیں — کچھ بھی ڈاؤن لوڈ نہیں ہوتا۔ ڈسپلے زبان نصب کرنا (یعنی خود Windows کا ترجمہ) «Windows کی ترتیبات کھولیں» بٹن کے پیچھے ہی رہتا ہے۔",
                zh: "以前必须在 Windows 的《语言和区域》面板里做的一切：键盘布局、它们的顺序以及切换快捷键。这些布局本来就随 Windows 提供——不会下载任何内容。安装显示语言（即翻译 Windows 本身）仍然在《打开 Windows 设置》按钮之后。");

            Add("Версия из Microsoft Store работает в контейнере, поэтому Windows может перенаправить запись в реестр внутрь пакета. Если раскладка или сочетание не подхватились даже после повторного входа в систему, задайте их в настройках Windows кнопкой внизу.",
                en: "The Microsoft Store build runs in a container, so Windows may redirect the registry write into the package. If a layout or shortcut is not picked up even after signing out and back in, set it in the Windows settings using the button below.",
                uk: "Версія з Microsoft Store працює в контейнері, тому Windows може перенаправити запис у реєстр усередину пакета. Якщо розкладка або сполучення не підхопилися навіть після повторного входу в систему, задайте їх у налаштуваннях Windows кнопкою внизу.",
                de: "Die Version aus dem Microsoft Store läuft in einem Container, daher kann Windows den Registrierungsschreibvorgang in das Paket umleiten. Wird ein Layout oder Kürzel selbst nach einer erneuten Anmeldung nicht übernommen, legen Sie es über die Schaltfläche unten in den Windows-Einstellungen fest.",
                it: "La versione dal Microsoft Store gira in un container, quindi Windows può reindirizzare la scrittura nel registro dentro il pacchetto. Se un layout o una scorciatoia non vengono applicati nemmeno dopo aver rieseguito l'accesso, impostali nelle impostazioni di Windows con il pulsante in basso.",
                es: "La versión de Microsoft Store se ejecuta en un contenedor, por lo que Windows puede redirigir la escritura del registro dentro del paquete. Si una distribución o un atajo no se aplican ni siquiera tras cerrar e iniciar sesión de nuevo, configúralos en la configuración de Windows con el botón de abajo.",
                fr: "La version du Microsoft Store s'exécute dans un conteneur : Windows peut donc rediriger l'écriture du registre à l'intérieur du paquet. Si une disposition ou un raccourci n'est pas pris en compte même après une reconnexion, définissez-le dans les paramètres Windows via le bouton ci-dessous.",
                pt: "A versão da Microsoft Store roda em um contêiner, então o Windows pode redirecionar a gravação no registro para dentro do pacote. Se um layout ou atalho não for aplicado nem depois de sair e entrar de novo, defina-o nas configurações do Windows pelo botão abaixo.",
                ar: "تعمل نسخة Microsoft Store داخل حاوية، لذلك قد يعيد Windows توجيه الكتابة في السجل إلى داخل الحزمة. وإذا لم يُطبَّق تخطيط أو اختصار حتى بعد تسجيل الخروج والدخول من جديد، فاضبطه في إعدادات Windows بالزر أدناه.",
                hi: "Microsoft Store वाला संस्करण एक कंटेनर में चलता है, इसलिए Windows रजिस्ट्री लेखन को पैकेज के भीतर पुनर्निर्देशित कर सकता है। यदि साइन आउट करके फिर साइन इन करने पर भी लेआउट या शॉर्टकट लागू न हो, तो नीचे दिए बटन से Windows सेटिंग्स में उसे सेट करें।",
                bn: "Microsoft Store-এর সংস্করণ একটি কনটেইনারে চলে, তাই Windows রেজিস্ট্রিতে লেখা প্যাকেজের ভিতরে পুনর্নির্দেশ করতে পারে। সাইন আউট করে আবার সাইন ইন করার পরেও লেআউট বা শর্টকাট কার্যকর না হলে নিচের বোতাম দিয়ে Windows সেটিংসে তা নির্ধারণ করুন।",
                ur: "Microsoft Store والا ورژن ایک کنٹینر میں چلتا ہے، اس لیے Windows رجسٹری کی لکھائی کو پیکیج کے اندر منتقل کر سکتا ہے۔ اگر سائن آؤٹ کر کے دوبارہ سائن اِن کرنے کے بعد بھی لے آؤٹ یا شارٹ کٹ لاگو نہ ہو تو نیچے دیے بٹن سے Windows کی ترتیبات میں مقرر کریں۔",
                zh: "Microsoft Store 版本运行在容器中，因此 Windows 可能会把注册表写入重定向到程序包内部。如果注销并重新登录后布局或快捷键仍未生效，请用下面的按钮在 Windows 设置中进行设置。");

            Add("Открыть настройки Windows",
                en: "Open Windows settings", uk: "Відкрити налаштування Windows", de: "Windows-Einstellungen öffnen",
                it: "Apri le impostazioni di Windows", es: "Abrir la configuración de Windows",
                fr: "Ouvrir les paramètres Windows", pt: "Abrir as configurações do Windows",
                ar: "فتح إعدادات Windows", hi: "Windows सेटिंग्स खोलें", bn: "Windows সেটিংস খুলুন",
                ur: "Windows کی ترتیبات کھولیں", zh: "打开 Windows 设置");

            Add("Раскладки клавиатуры",
                en: "Keyboard layouts", uk: "Розкладки клавіатури", de: "Tastaturlayouts",
                it: "Layout di tastiera", es: "Distribuciones de teclado", fr: "Dispositions de clavier",
                pt: "Layouts de teclado", ar: "تخطيطات لوحة المفاتيح", hi: "कीबोर्ड लेआउट",
                bn: "কীবোর্ড লেআউট", ur: "کی بورڈ لے آؤٹ", zh: "键盘布局");

            Add("Добавить раскладку...",
                en: "Add layout...", uk: "Додати розкладку...", de: "Layout hinzufügen...",
                it: "Aggiungi layout...", es: "Añadir distribución...", fr: "Ajouter une disposition...",
                pt: "Adicionar layout...", ar: "إضافة تخطيط...", hi: "लेआउट जोड़ें...",
                bn: "লেআউট যোগ করুন...", ur: "لے آؤٹ شامل کریں...", zh: "添加布局...");

            Add("Добавить раскладку",
                en: "Add layout", uk: "Додати розкладку", de: "Layout hinzufügen",
                it: "Aggiungi layout", es: "Añadir distribución", fr: "Ajouter une disposition",
                pt: "Adicionar layout", ar: "إضافة تخطيط", hi: "लेआउट जोड़ें",
                bn: "লেআউট যোগ করুন", ur: "لے آؤٹ شامل کریں", zh: "添加布局");

            Add("Добавить популярные языки",
                en: "Add popular languages", uk: "Додати популярні мови", de: "Beliebte Sprachen hinzufügen",
                it: "Aggiungi le lingue più diffuse", es: "Añadir los idiomas más usados",
                fr: "Ajouter les langues les plus courantes", pt: "Adicionar os idiomas mais usados",
                ar: "إضافة اللغات الأكثر انتشارًا", hi: "लोकप्रिय भाषाएँ जोड़ें",
                bn: "জনপ্রিয় ভাষা যোগ করুন", ur: "مقبول زبانیں شامل کریں", zh: "添加常用语言");

            Add("English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian и Ukrainian. Для языков с популярными вариантами (например, US International, Spanish Latin American) используйте «Добавить раскладку...».",
                en: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian and Ukrainian. For popular variants (for example US International and Spanish Latin American), use «Add layout...».",
                uk: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian та Ukrainian. Для популярних варіантів (наприклад US International і Spanish Latin American) скористайтеся «Додати розкладку...».",
                de: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian und Ukrainian. Für beliebte Varianten (etwa US International oder Spanish Latin American) verwenden Sie «Layout hinzufügen...».",
                it: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian e Ukrainian. Per le varianti più diffuse (ad esempio US International e Spanish Latin American) usa «Aggiungi layout...».",
                es: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian y Ukrainian. Para las variantes más habituales (por ejemplo US International o Spanish Latin American), usa «Añadir distribución...».",
                fr: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian et Ukrainian. Pour les variantes courantes (par exemple US International ou Spanish Latin American), utilisez «Ajouter une disposition...».",
                pt: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian e Ukrainian. Para as variantes mais comuns (por exemplo US International e Spanish Latin American), use «Adicionar layout...».",
                ar: "‏English وChinese وHindi وSpanish وFrench وArabic وBengali وPortuguese وRussian وUrdu وGerman وItalian وUkrainian. وللمتغيّرات الشائعة (مثل US International و Spanish Latin American) استخدم «إضافة تخطيط...».",
                hi: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian और Ukrainian। लोकप्रिय रूपांतरों (जैसे US International या Spanish Latin American) के लिए «लेआउट जोड़ें...» का उपयोग करें।",
                bn: "English, Chinese, Hindi, Spanish, French, Arabic, Bengali, Portuguese, Russian, Urdu, German, Italian ও Ukrainian। জনপ্রিয় সংস্করণের (যেমন US International বা Spanish Latin American) জন্য «লেআউট যোগ করুন...» ব্যবহার করুন।",
                ur: "‏English، Chinese، Hindi، Spanish، French، Arabic، Bengali، Portuguese، Russian، Urdu، German، Italian اور Ukrainian۔ مقبول اقسام (مثلاً US International یا Spanish Latin American) کے لیے «لے آؤٹ شامل کریں...» استعمال کریں۔",
                zh: "English、Chinese、Hindi、Spanish、French、Arabic、Bengali、Portuguese、Russian、Urdu、German、Italian 和 Ukrainian。若需要常见变体（例如 US International 或 Spanish Latin American），请使用《添加布局...》。");

            Add("Введите язык или название раскладки для фильтра",
                en: "Type a language or layout name to filter",
                uk: "Введіть мову або назву розкладки для фільтра",
                de: "Sprache oder Layoutnamen zum Filtern eingeben",
                it: "Digita una lingua o il nome di un layout per filtrare",
                es: "Escribe un idioma o el nombre de una distribución para filtrar",
                fr: "Saisissez une langue ou le nom d'une disposition pour filtrer",
                pt: "Digite um idioma ou o nome de um layout para filtrar",
                ar: "اكتب اسم لغة أو تخطيط للتصفية",
                hi: "फ़िल्टर करने के लिए भाषा या लेआउट का नाम लिखें",
                bn: "ফিল্টার করতে ভাষা বা লেআউটের নাম লিখুন",
                ur: "فلٹر کے لیے زبان یا لے آؤٹ کا نام لکھیں",
                zh: "输入语言或布局名称以筛选");

            Add("Добавить",
                en: "Add", uk: "Додати", de: "Hinzufügen", it: "Aggiungi", es: "Añadir", fr: "Ajouter",
                pt: "Adicionar", ar: "إضافة", hi: "जोड़ें", bn: "যোগ করুন", ur: "شامل کریں", zh: "添加");

            Add("Отмена",
                en: "Cancel", uk: "Скасувати", de: "Abbrechen", it: "Annulla", es: "Cancelar",
                fr: "Annuler", pt: "Cancelar", ar: "إلغاء", hi: "रद्द करें", bn: "বাতিল",
                ur: "منسوخ کریں", zh: "取消");

            Add("По умолчанию",
                en: "Set default", uk: "За замовчуванням", de: "Als Standard", it: "Predefinito",
                es: "Predeterminada", fr: "Par défaut", pt: "Padrão", ar: "تعيين كافتراضي",
                hi: "डिफ़ॉल्ट बनाएँ", bn: "ডিফল্ট করুন", ur: "ڈیفالٹ بنائیں", zh: "设为默认");

            Add("Удалить",
                en: "Delete", uk: "Видалити", de: "Entfernen", it: "Elimina", es: "Eliminar",
                fr: "Supprimer", pt: "Excluir", ar: "حذف", hi: "हटाएँ", bn: "মুছুন",
                ur: "حذف کریں", zh: "删除");

            Add("Удалить раскладку «{0}» из Windows?",
                en: "Remove the «{0}» layout from Windows?", uk: "Видалити розкладку «{0}» з Windows?",
                de: "Das Layout «{0}» aus Windows entfernen?", it: "Rimuovere il layout «{0}» da Windows?",
                es: "¿Quitar la distribución «{0}» de Windows?", fr: "Supprimer la disposition «{0}» de Windows ?",
                pt: "Remover o layout «{0}» do Windows?", ar: "هل تريد إزالة التخطيط «{0}» من Windows؟",
                hi: "क्या «{0}» लेआउट को Windows से हटाएँ?", bn: "«{0}» লেআউটটি Windows থেকে সরাবেন?",
                ur: "کیا «{0}» لے آؤٹ کو Windows سے ہٹا دیا جائے؟", zh: "要从 Windows 中移除「{0}」布局吗？");

            Add("Windows должна оставить хотя бы одну раскладку — эту удалить нельзя.",
                en: "Windows must keep at least one layout — this one cannot be removed.",
                uk: "Windows має залишити принаймні одну розкладку — цю видалити не можна.",
                de: "Windows muss mindestens ein Layout behalten — dieses lässt sich nicht entfernen.",
                it: "Windows deve mantenere almeno un layout: questo non può essere rimosso.",
                es: "Windows debe conservar al menos una distribución: esta no se puede quitar.",
                fr: "Windows doit conserver au moins une disposition — celle-ci ne peut pas être supprimée.",
                pt: "O Windows precisa manter ao menos um layout — este não pode ser removido.",
                ar: "يجب أن يحتفظ Windows بتخطيط واحد على الأقل — لا يمكن إزالة هذا التخطيط.",
                hi: "Windows को कम से कम एक लेआउट रखना ही होगा — इसे हटाया नहीं जा सकता।",
                bn: "Windows-কে অন্তত একটি লেআউট রাখতেই হবে — এটি সরানো যাবে না।",
                ur: "Windows کو کم از کم ایک لے آؤٹ رکھنا ہی ہوگا — اسے ہٹایا نہیں جا سکتا۔",
                zh: "Windows 必须至少保留一个布局——该布局无法移除。");

            Add("Изменение применено к раскладкам Windows. Обычно оно вступает в силу сразу; если что-то выглядит не так, выйдите из Windows и войдите снова.",
                en: "The change has been applied to the Windows layouts. It usually takes effect immediately; if something looks off, sign out of Windows and back in.",
                uk: "Зміну застосовано до розкладок Windows. Зазвичай вона діє одразу; якщо щось виглядає не так, вийдіть із Windows і увійдіть знову.",
                de: "Die Änderung wurde auf die Windows-Layouts angewendet. Sie wirkt normalerweise sofort; sieht etwas falsch aus, melden Sie sich bei Windows ab und wieder an.",
                it: "La modifica è stata applicata ai layout di Windows. Di solito ha effetto subito; se qualcosa non torna, esci da Windows e rientra.",
                es: "El cambio se ha aplicado a las distribuciones de Windows. Suele surtir efecto de inmediato; si algo no cuadra, cierra la sesión de Windows y vuelve a iniciarla.",
                fr: "La modification a été appliquée aux dispositions Windows. Elle prend généralement effet immédiatement ; si quelque chose semble incorrect, déconnectez-vous de Windows puis reconnectez-vous.",
                pt: "A alteração foi aplicada aos layouts do Windows. Normalmente tem efeito imediato; se algo parecer errado, saia do Windows e entre de novo.",
                ar: "طُبِّق التغيير على تخطيطات Windows. وعادةً ما يسري فورًا؛ فإذا بدا شيء غير صحيح، سجّل الخروج من Windows ثم ادخل مجددًا.",
                hi: "बदलाव Windows के लेआउट पर लागू कर दिया गया है। यह आमतौर पर तुरंत असर करता है; कुछ गड़बड़ लगे तो Windows से साइन आउट करके फिर साइन इन करें।",
                bn: "পরিবর্তনটি Windows-এর লেআউটে প্রয়োগ করা হয়েছে। সাধারণত সঙ্গে সঙ্গে কার্যকর হয়; কিছু ভুল মনে হলে Windows থেকে সাইন আউট করে আবার সাইন ইন করুন।",
                ur: "تبدیلی Windows کے لے آؤٹس پر لاگو کر دی گئی ہے۔ عام طور پر یہ فوراً اثر کرتی ہے؛ اگر کچھ غلط لگے تو Windows سے سائن آؤٹ کر کے دوبارہ سائن اِن کریں۔",
                zh: "更改已应用到 Windows 的布局。通常会立即生效；如果看起来不对，请注销 Windows 后重新登录。");

            Add("Вернуть раскладки как было",
                en: "Restore layouts", uk: "Повернути розкладки", de: "Layouts zurücksetzen",
                it: "Ripristina i layout", es: "Restaurar las distribuciones",
                fr: "Restaurer les dispositions", pt: "Restaurar os layouts",
                ar: "استعادة التخطيطات", hi: "लेआउट पुनर्स्थापित करें",
                bn: "লেআউট পুনরুদ্ধার করুন", ur: "لے آؤٹ بحال کریں", zh: "还原布局");

            Add("Вернуть раскладки Windows в исходное состояние?",
                en: "Restore the Windows layouts to their original state?",
                uk: "Повернути розкладки Windows до початкового стану?",
                de: "Die Windows-Layouts in den ursprünglichen Zustand zurücksetzen?",
                it: "Ripristinare i layout di Windows allo stato originale?",
                es: "¿Restaurar las distribuciones de Windows a su estado original?",
                fr: "Restaurer les dispositions Windows à leur état d'origine ?",
                pt: "Restaurar os layouts do Windows ao estado original?",
                ar: "هل تريد استعادة تخطيطات Windows إلى حالتها الأصلية؟",
                hi: "क्या Windows के लेआउट को उनकी मूल स्थिति में लौटाएँ?",
                bn: "Windows-এর লেআউটগুলি কি তাদের আসল অবস্থায় ফেরানো হবে?",
                ur: "کیا Windows کے لے آؤٹس کو اصل حالت میں واپس لایا جائے؟",
                zh: "要把 Windows 的布局还原到最初状态吗？");

            Add("Переключение по кругу",
                en: "Cycle switch", uk: "Перемикання по колу", de: "Reihum umschalten",
                it: "Commutazione ciclica", es: "Cambio cíclico", fr: "Bascule cyclique",
                pt: "Troca cíclica", ar: "التبديل الدوري", hi: "चक्रीय स्विच",
                bn: "চক্রাকার পরিবর্তন", ur: "چکری تبدیلی", zh: "循环切换");

            Add("Сочетание для перебора языков:",
                en: "Chord to cycle languages:", uk: "Сполучення для перебору мов:",
                de: "Kürzel zum Durchschalten der Sprachen:", it: "Scorciatoia per scorrere le lingue:",
                es: "Atajo para recorrer los idiomas:", fr: "Raccourci pour faire défiler les langues :",
                pt: "Atalho para percorrer os idiomas:", ar: "اختصار التنقل بين اللغات:",
                hi: "भाषाओं में चक्र लगाने का शॉर्टकट:", bn: "ভাষা বদলানোর শর্টকাট:",
                ur: "زبانیں بدلنے کا شارٹ کٹ:", zh: "循环切换语言的快捷键：");

            Add("Одно сочетание перебирает установленные языки по кругу. Это штатная настройка Windows (Alt+Shift, Ctrl+Shift или «`»); «—» отключает перебор.",
                en: "One chord cycles through the installed languages. This is the built-in Windows setting (Alt+Shift, Ctrl+Shift or «`»); «—» turns cycling off.",
                uk: "Одне сполучення перебирає встановлені мови по колу. Це штатне налаштування Windows (Alt+Shift, Ctrl+Shift або «`»); «—» вимикає перебір.",
                de: "Ein Kürzel schaltet der Reihe nach durch die installierten Sprachen. Das ist die eingebaute Windows-Einstellung (Alt+Umschalt, Strg+Umschalt oder «`»); «—» schaltet das Durchschalten ab.",
                it: "Una sola scorciatoia scorre in ciclo le lingue installate. È l'impostazione integrata di Windows (Alt+Maiusc, Ctrl+Maiusc o «`»); «—» disattiva il ciclo.",
                es: "Un solo atajo recorre en ciclo los idiomas instalados. Es la opción integrada de Windows (Alt+Mayús, Ctrl+Mayús o «`»); «—» desactiva el ciclo.",
                fr: "Un seul raccourci fait défiler les langues installées. C'est le réglage intégré de Windows (Alt+Maj, Ctrl+Maj ou «`») ; «—» désactive le défilement.",
                pt: "Um único atalho percorre em ciclo os idiomas instalados. É a configuração nativa do Windows (Alt+Shift, Ctrl+Shift ou «`»); «—» desliga o ciclo.",
                ar: "اختصار واحد يتنقّل بين اللغات المثبَّتة بالتناوب. هذا إعداد Windows الأصلي (Alt+Shift أو Ctrl+Shift أو «`»)، و«—» يوقف التنقّل.",
                hi: "एक ही शॉर्टकट स्थापित भाषाओं में चक्र लगाता है। यह Windows की अपनी सेटिंग है (Alt+Shift, Ctrl+Shift या «`»); «—» चक्र बंद कर देता है।",
                bn: "একটি শর্টকাটই ইনস্টল করা ভাষাগুলির মধ্যে চক্রাকারে বদলায়। এটি Windows-এর নিজস্ব সেটিং (Alt+Shift, Ctrl+Shift বা «`»); «—» চক্র বন্ধ করে দেয়।",
                ur: "ایک ہی شارٹ کٹ نصب زبانوں میں باری باری بدلتا ہے۔ یہ Windows کی اپنی ترتیب ہے (Alt+Shift، Ctrl+Shift یا «`»)؛ «—» اس چکر کو بند کر دیتا ہے۔",
                zh: "一个快捷键在已安装的语言之间循环切换。这是 Windows 自带的设置（Alt+Shift、Ctrl+Shift 或「`」）；选择「—」则关闭循环。");

            Add("Прямые сочетания на язык",
                en: "Direct per-language shortcuts", uk: "Прямі сполучення на мову",
                de: "Direkte Kürzel je Sprache", it: "Scorciatoie dirette per lingua",
                es: "Atajos directos por idioma", fr: "Raccourcis directs par langue",
                pt: "Atalhos diretos por idioma", ar: "اختصارات مباشرة لكل لغة",
                hi: "प्रति भाषा सीधे शॉर्टकट", bn: "প্রতি ভাষার সরাসরি শর্টকাট",
                ur: "ہر زبان کے لیے براہِ راست شارٹ کٹ", zh: "各语言的直达快捷键");

            Add("Эти сочетания обрабатывает сама Windows: они работают, даже когда CyrFlip закрыт. Комбинацию вы выбираете сами — в отличие от штатного окна, здесь не только Ctrl+Shift+цифра.",
                en: "Windows itself handles these shortcuts: they work even when CyrFlip is closed. You choose the combination yourself — unlike the built-in dialog, it is not limited to Ctrl+Shift+digit.",
                uk: "Ці сполучення обробляє сама Windows: вони працюють навіть коли CyrFlip закрито. Комбінацію ви обираєте самі — на відміну від штатного вікна, тут не лише Ctrl+Shift+цифра.",
                de: "Diese Kürzel verarbeitet Windows selbst: Sie funktionieren auch, wenn CyrFlip geschlossen ist. Die Kombination wählen Sie frei — anders als im Windows-Dialog ist sie nicht auf Strg+Umschalt+Ziffer beschränkt.",
                it: "Queste scorciatoie le gestisce Windows stesso: funzionano anche con CyrFlip chiuso. La combinazione la scegli tu — a differenza della finestra di sistema, non è limitata a Ctrl+Maiusc+cifra.",
                es: "Estos atajos los gestiona el propio Windows: funcionan incluso con CyrFlip cerrado. La combinación la eliges tú; a diferencia del cuadro de diálogo del sistema, no se limita a Ctrl+Mayús+dígito.",
                fr: "Ces raccourcis sont gérés par Windows lui-même : ils fonctionnent même quand CyrFlip est fermé. Vous choisissez librement la combinaison — contrairement à la boîte de dialogue système, elle ne se limite pas à Ctrl+Maj+chiffre.",
                pt: "Estes atalhos são tratados pelo próprio Windows: funcionam mesmo com o CyrFlip fechado. Você escolhe a combinação — ao contrário da caixa de diálogo nativa, ela não se limita a Ctrl+Shift+dígito.",
                ar: "يتولى Windows نفسه معالجة هذه الاختصارات: فهي تعمل حتى عندما يكون CyrFlip مغلقًا. وأنت من يختار التركيبة — وعلى خلاف نافذة Windows الأصلية، لا تقتصر على Ctrl+Shift+رقم.",
                hi: "इन शॉर्टकट को Windows स्वयं संभालता है: ये तब भी काम करते हैं जब CyrFlip बंद हो। संयोजन आप स्वयं चुनते हैं — Windows के अपने संवाद के विपरीत, यह केवल Ctrl+Shift+अंक तक सीमित नहीं है।",
                bn: "এই শর্টকাটগুলি Windows নিজেই সামলায়: CyrFlip বন্ধ থাকলেও এগুলি কাজ করে। সংমিশ্রণ আপনি নিজেই বেছে নেন — Windows-এর নিজস্ব ডায়ালগের মতো এটি কেবল Ctrl+Shift+সংখ্যায় সীমাবদ্ধ নয়।",
                ur: "ان شارٹ کٹس کو Windows خود سنبھالتا ہے: یہ اُس وقت بھی کام کرتے ہیں جب CyrFlip بند ہو۔ مجموعہ آپ خود منتخب کرتے ہیں — Windows کے اپنے ڈائیلاگ کے برعکس یہ صرف Ctrl+Shift+ہندسہ تک محدود نہیں۔",
                zh: "这些快捷键由 Windows 自己处理：即使关闭 CyrFlip 也依然有效。组合由你自行决定——不像系统自带对话框那样只能用 Ctrl+Shift+数字。");

            Add("Не назначено",
                en: "Not assigned", uk: "Не призначено", de: "Nicht zugewiesen", it: "Non assegnata",
                es: "Sin asignar", fr: "Non attribué", pt: "Não atribuído", ar: "غير معيَّن",
                hi: "निर्धारित नहीं", bn: "নির্ধারিত নয়", ur: "مقرر نہیں", zh: "未分配");

            Add("Задать...",
                en: "Assign...", uk: "Призначити...", de: "Zuweisen...", it: "Assegna...",
                es: "Asignar...", fr: "Attribuer...", pt: "Atribuir...", ar: "تعيين...",
                hi: "निर्धारित करें...", bn: "নির্ধারণ করুন...", ur: "مقرر کریں...", zh: "分配...");

            Add("Очистить",
                en: "Clear", uk: "Очистити", de: "Löschen", it: "Cancella", es: "Borrar",
                fr: "Effacer", pt: "Limpar", ar: "مسح", hi: "साफ़ करें", bn: "মুছে ফেলুন",
                ur: "صاف کریں", zh: "清除");

            Add("{0} → раскладка {1} (не установлена)",
                en: "{0} → layout {1} (not installed)", uk: "{0} → розкладка {1} (не встановлена)",
                de: "{0} → Layout {1} (nicht installiert)", it: "{0} → layout {1} (non installato)",
                es: "{0} → distribución {1} (no instalada)", fr: "{0} → disposition {1} (non installée)",
                pt: "{0} → layout {1} (não instalado)", ar: "{0} ← التخطيط {1} (غير مثبَّت)",
                hi: "{0} → लेआउट {1} (स्थापित नहीं)", bn: "{0} → লেআউট {1} (ইনস্টল করা নেই)",
                ur: "{0} ← لے آؤٹ {1} (نصب نہیں)", zh: "{0} → 布局 {1}（未安装）");

            Add("Сочетание для переключения на язык",
                en: "Shortcut for switching to this language", uk: "Сполучення для перемикання на цю мову",
                de: "Kürzel zum Wechsel in diese Sprache", it: "Scorciatoia per passare a questa lingua",
                es: "Atajo para cambiar a este idioma", fr: "Raccourci pour basculer vers cette langue",
                pt: "Atalho para mudar para este idioma", ar: "اختصار التبديل إلى هذه اللغة",
                hi: "इस भाषा पर स्विच करने का शॉर्टकट", bn: "এই ভাষায় যাওয়ার শর্টকাট",
                ur: "اس زبان پر جانے کا شارٹ کٹ", zh: "切换到该语言的快捷键");

            Add("Комбинация {0} уже занята горячей клавишей CyrFlip «{1}» — Windows её не получит.",
                en: "The {0} combination is already used by the CyrFlip hotkey «{1}» — Windows would never receive it.",
                uk: "Комбінацію {0} вже зайнято гарячою клавішею CyrFlip «{1}» — Windows її не отримає.",
                de: "Die Kombination {0} wird bereits vom CyrFlip-Kürzel «{1}» belegt — Windows würde sie nie erhalten.",
                it: "La combinazione {0} è già usata dalla scorciatoia di CyrFlip «{1}»: Windows non la riceverebbe mai.",
                es: "La combinación {0} ya la usa el atajo de CyrFlip «{1}»: Windows nunca la recibiría.",
                fr: "La combinaison {0} est déjà utilisée par le raccourci CyrFlip «{1}» — Windows ne la recevrait jamais.",
                pt: "A combinação {0} já é usada pelo atalho do CyrFlip «{1}» — o Windows nunca a receberia.",
                ar: "التركيبة {0} مستخدَمة بالفعل باختصار CyrFlip «{1}» — ولن تصل إلى Windows أبدًا.",
                hi: "संयोजन {0} पहले से CyrFlip के शॉर्टकट «{1}» के पास है — Windows तक यह कभी नहीं पहुँचेगा।",
                bn: "{0} সংমিশ্রণটি ইতিমধ্যে CyrFlip-এর «{1}» শর্টকাট ব্যবহার করছে — Windows এটি কখনোই পাবে না।",
                ur: "مجموعہ {0} پہلے ہی CyrFlip کے شارٹ کٹ «{1}» کے پاس ہے — Windows تک یہ کبھی نہیں پہنچے گا۔",
                zh: "组合键 {0} 已被 CyrFlip 的快捷键「{1}」占用——Windows 永远收不到它。");

            Add("Комбинация {0} уже назначена языку «{1}».",
                en: "The {0} combination is already assigned to «{1}».",
                uk: "Комбінацію {0} вже призначено мові «{1}».",
                de: "Die Kombination {0} ist bereits der Sprache «{1}» zugewiesen.",
                it: "La combinazione {0} è già assegnata alla lingua «{1}».",
                es: "La combinación {0} ya está asignada al idioma «{1}».",
                fr: "La combinaison {0} est déjà attribuée à la langue «{1}».",
                pt: "A combinação {0} já está atribuída ao idioma «{1}».",
                ar: "التركيبة {0} معيَّنة بالفعل للغة «{1}».",
                hi: "संयोजन {0} पहले से भाषा «{1}» को दिया गया है।",
                bn: "{0} সংমিশ্রণটি ইতিমধ্যে «{1}» ভাষার জন্য নির্ধারিত।",
                ur: "مجموعہ {0} پہلے ہی زبان «{1}» کو دیا گیا ہے۔",
                zh: "组合键 {0} 已分配给语言「{1}」。");

            Add("В Windows не осталось свободных слотов для языковых сочетаний.",
                en: "Windows has no free slots left for language shortcuts.",
                uk: "У Windows не лишилося вільних слотів для мовних сполучень.",
                de: "In Windows sind keine freien Plätze für Sprachkürzel mehr vorhanden.",
                it: "In Windows non restano slot liberi per le scorciatoie di lingua.",
                es: "A Windows no le quedan huecos libres para atajos de idioma.",
                fr: "Windows n'a plus d'emplacements libres pour les raccourcis de langue.",
                pt: "O Windows não tem mais espaços livres para atalhos de idioma.",
                ar: "لم تعد هناك خانات فارغة في Windows لاختصارات اللغات.",
                hi: "Windows में भाषा शॉर्टकट के लिए कोई खाली स्थान नहीं बचा।",
                bn: "Windows-এ ভাষার শর্টকাটের জন্য আর কোনো ফাঁকা স্লট নেই।",
                ur: "Windows میں زبان کے شارٹ کٹس کے لیے کوئی خالی جگہ باقی نہیں۔",
                zh: "Windows 中已没有可用于语言快捷键的空位。");

            Add("Не удалось записать сочетание в реестр Windows.",
                en: "Could not write the shortcut to the Windows registry.",
                uk: "Не вдалося записати сполучення до реєстру Windows.",
                de: "Das Kürzel konnte nicht in die Windows-Registrierung geschrieben werden.",
                it: "Impossibile scrivere la scorciatoia nel registro di Windows.",
                es: "No se pudo escribir el atajo en el registro de Windows.",
                fr: "Impossible d'écrire le raccourci dans le registre Windows.",
                pt: "Não foi possível gravar o atalho no registro do Windows.",
                ar: "تعذّرت كتابة الاختصار في سجل Windows.",
                hi: "शॉर्टकट को Windows रजिस्ट्री में लिखा नहीं जा सका।",
                bn: "শর্টকাটটি Windows রেজিস্ট্রিতে লেখা গেল না।",
                ur: "شارٹ کٹ کو Windows رجسٹری میں لکھا نہیں جا سکا۔",
                zh: "无法把快捷键写入 Windows 注册表。");

            Add("Сочетание записано в настройки Windows — обрабатывать его будет система, а не CyrFlip. Если оно не сработало сразу, выйдите из Windows и войдите снова.",
                en: "The shortcut has been written to the Windows settings — the system will handle it, not CyrFlip. If it does not work right away, sign out of Windows and back in.",
                uk: "Сполучення записано в налаштування Windows — його оброблятиме система, а не CyrFlip. Якщо воно не спрацювало одразу, вийдіть із Windows і увійдіть знову.",
                de: "Das Kürzel wurde in die Windows-Einstellungen geschrieben — es wird vom System verarbeitet, nicht von CyrFlip. Wirkt es nicht sofort, melden Sie sich bei Windows ab und wieder an.",
                it: "La scorciatoia è stata scritta nelle impostazioni di Windows: la gestirà il sistema, non CyrFlip. Se non funziona subito, esci da Windows e rientra.",
                es: "El atajo se ha escrito en la configuración de Windows: lo gestionará el sistema, no CyrFlip. Si no funciona enseguida, cierra la sesión de Windows y vuelve a iniciarla.",
                fr: "Le raccourci a été écrit dans les paramètres Windows — c'est le système qui le traitera, pas CyrFlip. S'il ne fonctionne pas tout de suite, déconnectez-vous de Windows puis reconnectez-vous.",
                pt: "O atalho foi gravado nas configurações do Windows — quem o processa é o sistema, não o CyrFlip. Se não funcionar de imediato, saia do Windows e entre novamente.",
                ar: "كُتب الاختصار في إعدادات Windows — وسيتولى النظام معالجته لا CyrFlip. وإذا لم يعمل فورًا، سجّل الخروج من Windows ثم ادخل من جديد.",
                hi: "शॉर्टकट Windows की सेटिंग्स में लिख दिया गया है — इसे सिस्टम संभालेगा, CyrFlip नहीं। यदि यह तुरंत काम न करे, तो Windows से साइन आउट करके फिर साइन इन करें।",
                bn: "শর্টকাটটি Windows-এর সেটিংসে লেখা হয়েছে — এটি সিস্টেম সামলাবে, CyrFlip নয়। সঙ্গে সঙ্গে কাজ না করলে Windows থেকে সাইন আউট করে আবার সাইন ইন করুন।",
                ur: "شارٹ کٹ Windows کی ترتیبات میں لکھ دیا گیا ہے — اسے سسٹم سنبھالے گا، CyrFlip نہیں۔ اگر یہ فوراً کام نہ کرے تو Windows سے سائن آؤٹ کر کے دوبارہ سائن اِن کریں۔",
                zh: "快捷键已写入 Windows 设置——由系统处理，而不是 CyrFlip。如果没有立即生效，请注销 Windows 后重新登录。");

            Add("Вернуть хоткеи как было",
                en: "Restore hotkeys", uk: "Повернути хоткеї", de: "Kürzel zurücksetzen",
                it: "Ripristina le scorciatoie", es: "Restaurar los atajos", fr: "Restaurer les raccourcis",
                pt: "Restaurar os atalhos", ar: "استعادة الاختصارات", hi: "शॉर्टकट पुनर्स्थापित करें",
                bn: "শর্টকাট পুনরুদ্ধার করুন", ur: "شارٹ کٹس بحال کریں", zh: "还原快捷键");

            Add("Возвращает языковые сочетания Windows в то состояние, в котором они были до первого изменения из CyrFlip.",
                en: "Returns the Windows language shortcuts to the state they were in before CyrFlip first changed them.",
                uk: "Повертає мовні сполучення Windows до стану, у якому вони були до першої зміни з CyrFlip.",
                de: "Setzt die Windows-Sprachkürzel auf den Stand vor der ersten Änderung durch CyrFlip zurück.",
                it: "Riporta le scorciatoie di lingua di Windows allo stato precedente alla prima modifica fatta da CyrFlip.",
                es: "Devuelve los atajos de idioma de Windows al estado que tenían antes de que CyrFlip los cambiara por primera vez.",
                fr: "Rétablit les raccourcis de langue Windows dans l'état où ils étaient avant la première modification par CyrFlip.",
                pt: "Devolve os atalhos de idioma do Windows ao estado em que estavam antes da primeira alteração feita pelo CyrFlip.",
                ar: "يعيد اختصارات لغات Windows إلى الحالة التي كانت عليها قبل أول تعديل أجراه CyrFlip.",
                hi: "Windows के भाषा शॉर्टकट को उस स्थिति में लौटाता है जिसमें वे CyrFlip द्वारा पहली बार बदले जाने से पहले थे।",
                bn: "CyrFlip প্রথমবার বদলানোর আগে Windows-এর ভাষা শর্টকাটগুলি যে অবস্থায় ছিল, সেই অবস্থায় ফিরিয়ে দেয়।",
                ur: "Windows کے زبان شارٹ کٹس کو اُس حالت میں واپس لاتا ہے جس میں وہ CyrFlip کی پہلی تبدیلی سے پہلے تھے۔",
                zh: "把 Windows 的语言快捷键恢复到 CyrFlip 首次修改之前的状态。");

            Add("Вернуть языковые сочетания Windows в исходное состояние?",
                en: "Restore the Windows language shortcuts to their original state?",
                uk: "Повернути мовні сполучення Windows до початкового стану?",
                de: "Die Windows-Sprachkürzel in den ursprünglichen Zustand zurücksetzen?",
                it: "Ripristinare le scorciatoie di lingua di Windows allo stato originale?",
                es: "¿Restaurar los atajos de idioma de Windows a su estado original?",
                fr: "Restaurer les raccourcis de langue Windows à leur état d'origine ?",
                pt: "Restaurar os atalhos de idioma do Windows ao estado original?",
                ar: "هل تريد استعادة اختصارات لغات Windows إلى حالتها الأصلية؟",
                hi: "क्या Windows के भाषा शॉर्टकट उनकी मूल स्थिति में लौटाएँ?",
                bn: "Windows-এর ভাষা শর্টকাটগুলি কি আসল অবস্থায় ফেরানো হবে?",
                ur: "کیا Windows کے زبان شارٹ کٹس اصل حالت میں واپس لائے جائیں؟",
                zh: "要把 Windows 的语言快捷键还原到最初状态吗？");
        }
    }
}
