namespace CyrFlip
{
    /// <summary>Tray menu items and the short fragments that make up the tray tooltip.</summary>
    internal static partial class Localization
    {
        private static void AddTrayStrings()
        {
            Add("Показать историю",
                en: "Show history", uk: "Показати історію", de: "Verlauf anzeigen",
                it: "Mostra la cronologia", es: "Mostrar el historial", fr: "Afficher l'historique",
                pt: "Mostrar o histórico", ar: "إظهار السجل", hi: "इतिहास दिखाएँ",
                bn: "ইতিহাস দেখান", ur: "تاریخ دکھائیں", zh: "显示历史");

            Add("Скрыть историю",
                en: "Hide history", uk: "Сховати історію", de: "Verlauf ausblenden",
                it: "Nascondi la cronologia", es: "Ocultar el historial", fr: "Masquer l'historique",
                pt: "Ocultar o histórico", ar: "إخفاء السجل", hi: "इतिहास छिपाएँ",
                bn: "ইতিহাস লুকান", ur: "تاریخ چھپائیں", zh: "隐藏历史");

            Add("История буфера",
                en: "Clipboard history", uk: "Історія буфера", de: "Zwischenablage-Verlauf",
                it: "Cronologia appunti", es: "Historial del portapapeles", fr: "Historique du presse-papiers",
                pt: "Histórico da área de transferência", ar: "سجل الحافظة", hi: "क्लिपबोर्ड इतिहास",
                bn: "ক্লিপবোর্ড ইতিহাস", ur: "کلپ بورڈ تاریخ", zh: "剪贴板历史");

            Add("Курсор: индикатор раскладки",
                en: "Cursor: layout indicator", uk: "Курсор: індикатор розкладки",
                de: "Cursor: Layout-Anzeige", it: "Cursore: indicatore del layout",
                es: "Cursor: indicador de distribución", fr: "Curseur : indicateur de disposition",
                pt: "Cursor: indicador de layout", ar: "المؤشر: مبيّن التخطيط",
                hi: "कर्सर: लेआउट संकेतक", bn: "কার্সর: লেআউট নির্দেশক",
                ur: "کرسر: لے آؤٹ اشارہ", zh: "光标：布局指示");

            Add("Каретка: метка раскладки",
                en: "Caret: overlay label", uk: "Каретка: мітка розкладки",
                de: "Schreibmarke: Layout-Markierung", it: "Cursore di testo: etichetta del layout",
                es: "Cursor de texto: marca de distribución", fr: "Point d'insertion : marqueur de disposition",
                pt: "Cursor de texto: marca de layout", ar: "مؤشر الكتابة: علامة التخطيط",
                hi: "कैरेट: लेआउट का चिह्न", bn: "ক্যারেট: লেআউটের চিহ্ন",
                ur: "کیریٹ: لے آؤٹ کا نشان", zh: "插入点：布局标记");

            Add("Каретка: стиль точки",
                en: "Caret: dot style", uk: "Каретка: стиль крапки", de: "Schreibmarke: Punkt-Stil",
                it: "Cursore di testo: stile a punto", es: "Cursor de texto: estilo de punto",
                fr: "Point d'insertion : style point", pt: "Cursor de texto: estilo de ponto",
                ar: "مؤشر الكتابة: نمط النقطة", hi: "कैरेट: बिंदु शैली",
                bn: "ক্যারেট: বিন্দু ধরন", ur: "کیریٹ: نقطہ انداز", zh: "插入点：圆点样式");

            Add("Настройки...",
                en: "Settings...", uk: "Налаштування...", de: "Einstellungen...", it: "Impostazioni...",
                es: "Configuración...", fr: "Paramètres...", pt: "Configurações...",
                ar: "الإعدادات...", hi: "सेटिंग्स...", bn: "সেটিংস...", ur: "ترتیبات...", zh: "设置...");

            Add("Выход",
                en: "Exit", uk: "Вихід", de: "Beenden", it: "Esci", es: "Salir", fr: "Quitter",
                pt: "Sair", ar: "خروج", hi: "बाहर निकलें", bn: "প্রস্থান", ur: "باہر نکلیں", zh: "退出");

            // ---- Tooltip fragments (kept short: the shell tooltip is capped at 127 characters) ----
            Add("горячие клавиши выключены",
                en: "hotkeys are off", uk: "гарячі клавіші вимкнені", de: "Kürzel sind aus",
                it: "scorciatoie disattivate", es: "atajos desactivados", fr: "raccourcis désactivés",
                pt: "atalhos desligados", ar: "الاختصارات متوقفة", hi: "शॉर्टकट बंद हैं",
                bn: "শর্টকাট বন্ধ", ur: "شارٹ کٹس بند ہیں", zh: "快捷键已关闭");

            Add("регистр",
                en: "case", uk: "регістр", de: "Schreibung", it: "maiuscole", es: "mayúsculas",
                fr: "casse", pt: "maiúsculas", ar: "حالة الأحرف", hi: "केस", bn: "কেস",
                ur: "کیس", zh: "大小写");

            Add("менеджер буфера",
                en: "clipboard manager", uk: "менеджер буфера", de: "Zwischenablage",
                it: "gestore appunti", es: "portapapeles", fr: "presse-papiers",
                pt: "área de transferência", ar: "مدير الحافظة", hi: "क्लिपबोर्ड प्रबंधक",
                bn: "ক্লিপবোর্ড ম্যানেজার", ur: "کلپ بورڈ مینیجر", zh: "剪贴板管理器");

            Add("экран не гаснет",
                en: "screen stays on", uk: "екран не гасне", de: "Bildschirm bleibt an",
                it: "schermo sempre acceso", es: "pantalla siempre encendida", fr: "écran maintenu allumé",
                pt: "tela sempre ligada", ar: "الشاشة تبقى مضاءة", hi: "स्क्रीन चालू रहती है",
                bn: "স্ক্রিন চালু থাকে", ur: "اسکرین آن رہتی ہے", zh: "屏幕保持常亮");

            Add("не даёт засыпать",
                en: "keeping awake", uk: "не дає засинати", de: "hält wach", it: "impedisce la sospensione",
                es: "evita la suspensión", fr: "empêche la veille", pt: "impede a suspensão",
                ar: "يمنع السكون", hi: "स्लीप रोक रहा है", bn: "ঘুমাতে দিচ্ছে না",
                ur: "سلیپ روک رہا ہے", zh: "阻止睡眠");

            // ---- Single click on the tray icon: next layout in the Windows rotation ----
            Add("Переключать не на что: в Windows установлена только одна раскладка.",
                en: "Nothing to switch to: Windows has only one keyboard layout installed.",
                uk: "Перемикати нема на що: у Windows встановлено лише одну розкладку.",
                de: "Es gibt nichts zum Wechseln: In Windows ist nur ein Tastaturlayout installiert.",
                it: "Non c'è nulla su cui passare: Windows ha un solo layout di tastiera installato.",
                es: "No hay a qué cambiar: Windows solo tiene una distribución de teclado instalada.",
                fr: "Rien vers quoi basculer : Windows n'a qu'une seule disposition de clavier installée.",
                pt: "Não há para onde alternar: o Windows tem apenas um layout de teclado instalado.",
                ar: "لا يوجد ما يمكن التبديل إليه: لا يوجد في Windows سوى تخطيط لوحة مفاتيح واحد.",
                hi: "बदलने के लिए कुछ नहीं है: Windows में केवल एक ही कीबोर्ड लेआउट स्थापित है।",
                bn: "পাল্টানোর মতো কিছু নেই: Windows-এ কেবল একটিই কীবোর্ড লেআউট ইনস্টল করা আছে।",
                ur: "بدلنے کے لیے کچھ نہیں: Windows میں صرف ایک ہی کی بورڈ لے آؤٹ نصب ہے۔",
                zh: "无法切换：Windows 中只安装了一种键盘布局。");
        }
    }
}
