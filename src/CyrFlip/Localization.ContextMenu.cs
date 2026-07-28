namespace CyrFlip
{
    /// <summary>CyrFlip's own context menu over the selected text: the menu items and its settings.</summary>
    internal static partial class Localization
    {
        private static void AddContextMenuStrings()
        {
            // "Копировать", "Вставить" and "Настройки" are already registered by the translator tab;
            // only the commands this menu adds are new.
            Add("Вырезать",
                en: "Cut", uk: "Вирізати", de: "Ausschneiden", it: "Taglia",
                es: "Cortar", fr: "Couper", pt: "Recortar",
                ar: "قص", hi: "काटें", bn: "কাটুন", ur: "کاٹیں", zh: "剪切");

            Add("Перевести на {0}",
                en: "Translate into {0}", uk: "Перекласти на {0}", de: "Übersetzen nach {0}",
                it: "Traduci in {0}", es: "Traducir al {0}", fr: "Traduire en {0}",
                pt: "Traduzir para {0}", ar: "الترجمة إلى {0}", hi: "{0} में अनुवाद करें",
                bn: "{0}-এ অনুবাদ করুন", ur: "{0} میں ترجمہ کریں", zh: "翻译成 {0}");

            Add("Своё контекстное меню над выделенным текстом",
                en: "CyrFlip's own context menu over the selected text",
                uk: "Власне контекстне меню над виділеним текстом",
                de: "Eigenes Kontextmenü über dem markierten Text",
                it: "Menu contestuale di CyrFlip sul testo selezionato",
                es: "Menú contextual propio sobre el texto seleccionado",
                fr: "Menu contextuel propre au texte sélectionné",
                pt: "Menu de contexto próprio sobre o texto selecionado",
                ar: "قائمة سياق خاصة بـ CyrFlip فوق النص المحدد",
                hi: "चयनित पाठ पर CyrFlip का अपना संदर्भ मेनू",
                bn: "নির্বাচিত লেখার উপর CyrFlip-এর নিজস্ব কনটেক্সট মেনু",
                ur: "منتخب متن پر CyrFlip کا اپنا سیاق مینو",
                zh: "在选中文本上显示 CyrFlip 自己的右键菜单");

            Add("Аккорд мыши открывает меню CyrFlip рядом с указателем: копировать, вырезать, вставить, конвертация раскладок, регистр, перевод, быстрый запуск. Родное меню приложения при этом не появляется. Пока флажок снят, CyrFlip вообще не следит за мышью.",
                en: "The mouse chord opens CyrFlip's menu next to the pointer: copy, cut, paste, layout conversion, letter case, translation, quick launch. The application's own menu does not appear. While the box is clear, CyrFlip does not watch the mouse at all.",
                uk: "Акорд миші відкриває меню CyrFlip поруч із вказівником: копіювати, вирізати, вставити, конвертація розкладок, регістр, переклад, швидкий запуск. Рідне меню застосунку при цьому не з'являється. Поки прапорець знято, CyrFlip взагалі не стежить за мишею.",
                de: "Der Maus-Akkord öffnet das Menü von CyrFlip neben dem Zeiger: Kopieren, Ausschneiden, Einfügen, Layout-Konvertierung, Groß- und Kleinschreibung, Übersetzung, Schnellstart. Das eigene Menü der Anwendung erscheint dabei nicht. Solange das Kästchen leer ist, beobachtet CyrFlip die Maus überhaupt nicht.",
                it: "La combinazione del mouse apre il menu di CyrFlip accanto al puntatore: copia, taglia, incolla, conversione dei layout, maiuscole e minuscole, traduzione, avvio rapido. Il menu dell'applicazione non compare. Finché la casella è deselezionata, CyrFlip non osserva affatto il mouse.",
                es: "La combinación del ratón abre el menú de CyrFlip junto al puntero: copiar, cortar, pegar, conversión de distribuciones, mayúsculas y minúsculas, traducción, inicio rápido. El menú propio de la aplicación no aparece. Mientras la casilla esté desmarcada, CyrFlip no vigila el ratón en absoluto.",
                fr: "La combinaison souris ouvre le menu de CyrFlip près du pointeur : copier, couper, coller, conversion de dispositions, casse, traduction, lancement rapide. Le menu propre à l'application n'apparaît pas. Tant que la case est décochée, CyrFlip ne surveille pas du tout la souris.",
                pt: "A combinação do mouse abre o menu do CyrFlip ao lado do ponteiro: copiar, recortar, colar, conversão de layouts, maiúsculas e minúsculas, tradução, início rápido. O menu da própria aplicação não aparece. Enquanto a caixa estiver desmarcada, o CyrFlip não observa o mouse.",
                ar: "تفتح تركيبة الفأرة قائمة CyrFlip بجوار المؤشر: نسخ، قص، لصق، تحويل تخطيطات لوحة المفاتيح، حالة الأحرف، الترجمة، التشغيل السريع. ولا تظهر قائمة التطبيق نفسه. وما دام المربع غير محدد، لا يراقب CyrFlip الفأرة إطلاقًا.",
                hi: "माउस संयोजन सूचक के पास CyrFlip का मेनू खोलता है: कॉपी, काटें, चिपकाएँ, लेआउट रूपांतरण, अक्षर आकार, अनुवाद, त्वरित प्रारंभ। एप्लिकेशन का अपना मेनू नहीं दिखता। जब तक यह बॉक्स खाली है, CyrFlip माउस पर बिल्कुल नज़र नहीं रखता।",
                bn: "মাউস কম্বিনেশন পয়েন্টারের পাশে CyrFlip-এর মেনু খোলে: কপি, কাটুন, পেস্ট, লেআউট রূপান্তর, অক্ষরের ছাঁদ, অনুবাদ, দ্রুত চালু। অ্যাপ্লিকেশনের নিজস্ব মেনু দেখা যায় না। বাক্সটি খালি থাকা পর্যন্ত CyrFlip মাউস একেবারেই পর্যবেক্ষণ করে না।",
                ur: "ماؤس کا مجموعہ پوائنٹر کے پاس CyrFlip کا مینو کھولتا ہے: کاپی، کاٹیں، پیسٹ، لے آؤٹ کی تبدیلی، حروف کی حالت، ترجمہ، فوری آغاز۔ ایپلیکیشن کا اپنا مینو ظاہر نہیں ہوتا۔ جب تک یہ خانہ خالی ہے، CyrFlip ماؤس پر بالکل نظر نہیں رکھتا۔",
                zh: "鼠标组合键在指针旁打开 CyrFlip 的菜单：复制、剪切、粘贴、键盘布局转换、大小写、翻译、快速启动。应用程序自带的菜单不会出现。未勾选时，CyrFlip 完全不监视鼠标。");

            Add("Аккорд мыши:",
                en: "Mouse chord:", uk: "Акорд миші:", de: "Maus-Akkord:", it: "Combinazione mouse:",
                es: "Combinación del ratón:", fr: "Combinaison souris :", pt: "Combinação do mouse:",
                ar: "تركيبة الفأرة:", hi: "माउस संयोजन:", bn: "মাউস কম্বিনেশন:",
                ur: "ماؤس کا مجموعہ:", zh: "鼠标组合键：");

            Add("Ctrl и правая кнопка ничем в Windows не заняты. Shift и правая кнопка отберут у Проводника расширенное меню, а средняя кнопка отберёт автоскролл и открытие ссылки в новой вкладке.",
                en: "Ctrl with the right button is claimed by nothing in Windows. Shift with the right button takes the extended menu away from File Explorer, and the middle button takes away autoscroll and opening a link in a new tab.",
                uk: "Ctrl і права кнопка ні для чого у Windows не зайняті. Shift і права кнопка відберуть у Провідника розширене меню, а середня кнопка відбере автопрокручування та відкриття посилання в новій вкладці.",
                de: "Strg mit der rechten Taste ist in Windows von nichts belegt. Umschalt mit der rechten Taste nimmt dem Explorer das erweiterte Menü, und die mittlere Taste nimmt den Autoscroll und das Öffnen eines Links in einem neuen Tab.",
                it: "Ctrl con il tasto destro non è occupato da nulla in Windows. Maiusc con il tasto destro toglie a Esplora file il menu esteso e il tasto centrale toglie lo scorrimento automatico e l'apertura di un collegamento in una nuova scheda.",
                es: "Ctrl con el botón derecho no está ocupado por nada en Windows. Mayús con el botón derecho le quita al Explorador el menú ampliado, y el botón central quita el desplazamiento automático y la apertura de un enlace en una pestaña nueva.",
                fr: "Ctrl avec le bouton droit n'est occupé par rien sous Windows. Maj avec le bouton droit retire à l'Explorateur son menu étendu, et le bouton du milieu retire le défilement automatique et l'ouverture d'un lien dans un nouvel onglet.",
                pt: "Ctrl com o botão direito não está ocupado por nada no Windows. Shift com o botão direito tira do Explorador de Arquivos o menu estendido, e o botão do meio tira a rolagem automática e a abertura de um link em uma nova guia.",
                ar: "لا يشغل Ctrl مع الزر الأيمن أي شيء في Windows. أما Shift مع الزر الأيمن فيسلب مستكشف الملفات قائمته الموسعة، والزر الأوسط يسلب التمرير التلقائي وفتح الرابط في علامة تبويب جديدة.",
                hi: "Ctrl के साथ दायाँ बटन Windows में किसी काम में नहीं लगा है। Shift के साथ दायाँ बटन File Explorer से विस्तारित मेनू छीन लेगा, और मध्य बटन ऑटो-स्क्रॉल तथा लिंक को नए टैब में खोलना छीन लेगा।",
                bn: "Ctrl সহ ডান বোতাম Windows-এ কোনো কাজে ব্যবহৃত হয় না। Shift সহ ডান বোতাম File Explorer থেকে বর্ধিত মেনু কেড়ে নেবে, আর মধ্য বোতাম কেড়ে নেবে অটো-স্ক্রল ও নতুন ট্যাবে লিংক খোলা।",
                ur: "Windows میں Ctrl کے ساتھ دایاں بٹن کسی کام کے لیے مختص نہیں۔ Shift کے ساتھ دایاں بٹن File Explorer سے توسیعی مینو چھین لے گا، اور درمیانی بٹن آٹو اسکرول اور نئے ٹیب میں لنک کھولنا چھین لے گا۔",
                zh: "Ctrl 加右键在 Windows 中没有任何占用。Shift 加右键会夺走文件资源管理器的扩展菜单，中键则会夺走自动滚动和在新标签页打开链接。");

            Add(MouseChord.RightButtonName,
                en: "right mouse button", uk: "права кнопка миші", de: "rechte Maustaste",
                it: "tasto destro del mouse", es: "botón derecho del ratón",
                fr: "bouton droit de la souris", pt: "botão direito do mouse",
                ar: "زر الفأرة الأيمن", hi: "दायाँ माउस बटन", bn: "ডান মাউস বোতাম",
                ur: "دایاں ماؤس بٹن", zh: "鼠标右键");

            Add(MouseChord.MiddleButtonName,
                en: "middle mouse button", uk: "середня кнопка миші", de: "mittlere Maustaste",
                it: "tasto centrale del mouse", es: "botón central del ratón",
                fr: "bouton du milieu de la souris", pt: "botão do meio do mouse",
                ar: "زر الفأرة الأوسط", hi: "मध्य माउस बटन", bn: "মধ্য মাউস বোতাম",
                ur: "درمیانی ماؤس بٹن", zh: "鼠标中键");

            Add("Не удалось перехватить мышь, контекстное меню не будет открываться.",
                en: "Could not hook the mouse; the context menu will not open.",
                uk: "Не вдалося перехопити мишу, контекстне меню не відкриватиметься.",
                de: "Die Maus konnte nicht abgefangen werden; das Kontextmenü lässt sich nicht öffnen.",
                it: "Non è stato possibile intercettare il mouse: il menu contestuale non si aprirà.",
                es: "No se pudo interceptar el ratón: el menú contextual no se abrirá.",
                fr: "Impossible d'intercepter la souris : le menu contextuel ne s'ouvrira pas.",
                pt: "Não foi possível interceptar o mouse: o menu de contexto não abrirá.",
                ar: "تعذّر اعتراض الفأرة، ولن تُفتح قائمة السياق.",
                hi: "माउस को इंटरसेप्ट नहीं किया जा सका, संदर्भ मेनू नहीं खुलेगा।",
                bn: "মাউস ইন্টারসেপ্ট করা যায়নি, কনটেক্সট মেনু খুলবে না।",
                ur: "ماؤس کو روکا نہیں جا سکا، سیاق مینو نہیں کھلے گا۔",
                zh: "无法挂钩鼠标，右键菜单将无法打开。");
        }
    }
}
