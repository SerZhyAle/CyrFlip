namespace CyrFlip
{
    /// <summary>The theme selector on the General tab and the answers of <see cref="ConfirmDialog"/>.</summary>
    internal static partial class Localization
    {
        private static void AddThemeStrings()
        {
            Add("Тема:",
                en: "Theme:", uk: "Тема:", de: "Design:", it: "Tema:", es: "Tema:", fr: "Thème :",
                pt: "Tema:", ar: "السمة:", hi: "थीम:", bn: "থিম:", ur: "تھیم:", zh: "主题：");

            Add("Как в Windows",
                en: "Same as Windows", uk: "Як у Windows", de: "Wie Windows", it: "Come Windows",
                es: "Como Windows", fr: "Comme Windows", pt: "Igual ao Windows", ar: "مثل Windows",
                hi: "Windows जैसा", bn: "Windows-এর মতো", ur: "Windows کی طرح", zh: "跟随 Windows");

            Add("Светлая",
                en: "Light", uk: "Світла", de: "Hell", it: "Chiaro", es: "Claro", fr: "Clair",
                pt: "Claro", ar: "فاتح", hi: "हल्का", bn: "হালকা", ur: "ہلکا", zh: "浅色");

            Add("Тёмная",
                en: "Dark", uk: "Темна", de: "Dunkel", it: "Scuro", es: "Oscuro", fr: "Sombre",
                pt: "Escuro", ar: "داكن", hi: "गहरा", bn: "গাঢ়", ur: "گہرا", zh: "深色");

            Add("«Как в Windows» - CyrFlip светлый или тёмный вместе с Windows и переключается вслед за ней без перезапуска. Контрастные темы Windows всегда важнее этой настройки. Метка раскладки у курсора и каретки от темы не зависит: её цвет обозначает раскладку.",
                en: "“Same as Windows” - CyrFlip is light or dark with Windows and follows it when it switches, without a restart. Windows contrast themes always take precedence over this setting. The layout marker at the cursor and caret does not change with the theme: its colour names the layout.",
                uk: "«Як у Windows» - CyrFlip світлий або темний разом із Windows і перемикається слідом за нею без перезапуску. Контрастні теми Windows завжди важливіші за це налаштування. Мітка розкладки біля курсора та каретки від теми не залежить: її колір позначає розкладку.",
                de: "„Wie Windows“ - CyrFlip ist hell oder dunkel wie Windows und wechselt ohne Neustart mit. Kontrastdesigns von Windows haben immer Vorrang vor dieser Einstellung. Die Layoutmarke an Mauszeiger und Textcursor ändert sich mit dem Design nicht: Ihre Farbe steht für das Layout.",
                it: "«Come Windows» - CyrFlip è chiaro o scuro come Windows e lo segue quando cambia, senza riavvio. I temi a contrasto di Windows hanno sempre la precedenza su questa impostazione. L'indicatore del layout vicino al puntatore e al cursore di testo non cambia con il tema: il suo colore indica il layout.",
                es: "«Como Windows» - CyrFlip es claro u oscuro como Windows y lo sigue cuando cambia, sin reiniciar. Los temas de contraste de Windows siempre tienen prioridad sobre este ajuste. La marca de distribución junto al puntero y al cursor de texto no cambia con el tema: su color indica la distribución.",
                fr: "« Comme Windows » - CyrFlip est clair ou sombre comme Windows et le suit quand il change, sans redémarrage. Les thèmes de contraste de Windows l'emportent toujours sur ce réglage. Le repère de disposition près du pointeur et du curseur de texte ne change pas avec le thème : sa couleur désigne la disposition.",
                pt: "«Igual ao Windows» - o CyrFlip fica claro ou escuro com o Windows e o acompanha quando ele muda, sem reiniciar. Os temas de contraste do Windows sempre têm prioridade sobre esta configuração. A marca do layout junto ao ponteiro e ao cursor de texto não muda com o tema: a cor dela indica o layout.",
                ar: "«مثل Windows» - يكون CyrFlip فاتحًا أو داكنًا مع Windows ويتبعه عند التبديل دون إعادة تشغيل. سمات التباين في Windows لها الأولوية دائمًا على هذا الإعداد. علامة التخطيط بجوار المؤشر ومؤشر النص لا تتغير مع السمة: لونها يدل على التخطيط.",
                hi: "«Windows जैसा» - CyrFlip Windows के साथ हल्का या गहरा रहता है और बिना रीस्टार्ट के उसके साथ बदल जाता है। Windows की कॉन्ट्रास्ट थीम हमेशा इस सेटिंग से ऊपर रहती हैं। कर्सर और कैरेट के पास लेआउट का निशान थीम के साथ नहीं बदलता: उसका रंग लेआउट बताता है।",
                bn: "«Windows-এর মতো» - CyrFlip Windows-এর সঙ্গে হালকা বা গাঢ় থাকে এবং রিস্টার্ট ছাড়াই তার সঙ্গে বদলায়। Windows-এর কনট্রাস্ট থিম সবসময় এই সেটিংয়ের চেয়ে অগ্রাধিকার পায়। কার্সর ও ক্যারেটের পাশে লেআউটের চিহ্ন থিমের সঙ্গে বদলায় না: এর রং লেআউট বোঝায়।",
                ur: "«Windows کی طرح» - CyrFlip Windows کے ساتھ ہلکا یا گہرا رہتا ہے اور ری اسٹارٹ کے بغیر اس کے ساتھ بدل جاتا ہے۔ Windows کی کنٹراسٹ تھیمز ہمیشہ اس ترتیب پر مقدم ہیں۔ کرسر اور کیرٹ کے پاس لے آؤٹ کا نشان تھیم کے ساتھ نہیں بدلتا: اس کا رنگ لے آؤٹ بتاتا ہے۔",
                zh: "“跟随 Windows” - CyrFlip 随 Windows 显示浅色或深色，并在 Windows 切换时同步切换，无需重启。Windows 的对比度主题始终优先于此设置。光标和插入点旁的布局标记不随主题变化：它的颜色代表布局。");

            Add("Да",
                en: "Yes", uk: "Так", de: "Ja", it: "Sì", es: "Sí", fr: "Oui",
                pt: "Sim", ar: "نعم", hi: "हाँ", bn: "হ্যাঁ", ur: "ہاں", zh: "是");

            Add("Нет",
                en: "No", uk: "Ні", de: "Nein", it: "No", es: "No", fr: "Non",
                pt: "Não", ar: "لا", hi: "नहीं", bn: "না", ur: "نہیں", zh: "否");
        }
    }
}
