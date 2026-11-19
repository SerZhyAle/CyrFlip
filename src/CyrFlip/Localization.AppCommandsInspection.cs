namespace CyrFlip
{
    /// <summary>
    /// The 13-language sentences of the four read-only verbs (ticket S0111, APP-COMMANDS 0.2). The rest of what they say is
    /// the sentences the earlier verbs already carry: the folder that does not exist, the file that exists, the scenario
    /// launcher switched off, the command that failed and the path Windows does not accept.
    /// </summary>
    internal static partial class Localization
    {
        private static void AddAppCommandsInspectionStrings()
        {
            Add("Scenarios listed: {0}.",
                ru: "Сценариев в списке: {0}.",
                uk: "Сценаріїв у списку: {0}.",
                de: "Aufgelistete Szenarien: {0}.",
                it: "Scenari elencati: {0}.",
                es: "Escenarios listados: {0}.",
                fr: "Scénarios listés : {0}.",
                pt: "Cenários listados: {0}.",
                ar: "السيناريوهات المعروضة: {0}.",
                hi: "सूची में परिदृश्य: {0}।",
                bn: "তালিকায় পরিস্থিতি: {0}।",
                ur: "فہرست میں منظرنامے: {0}۔",
                zh: "已列出场景：{0} 个。");
            Add("Chords listed: {0}.",
                ru: "Сочетаний клавиш в списке: {0}.",
                uk: "Комбінацій клавіш у списку: {0}.",
                de: "Aufgelistete Tastenkombinationen: {0}.",
                it: "Combinazioni di tasti elencate: {0}.",
                es: "Combinaciones de teclas listadas: {0}.",
                fr: "Combinaisons de touches listées : {0}.",
                pt: "Combinações de teclas listadas: {0}.",
                ar: "مجموعات المفاتيح المعروضة: {0}.",
                hi: "सूची में की-संयोजन: {0}।",
                bn: "তালিকায় কী-সংমিশ্রণ: {0}।",
                ur: "فہرست میں کی کمبی نیشن: {0}۔",
                zh: "已列出组合键：{0} 个。");
            Add("Wrote the log bundle to {0}.",
                ru: "Пакет журналов записан в {0}.",
                uk: "Пакет журналів записано до {0}.",
                de: "Das Protokollpaket wurde nach {0} geschrieben.",
                it: "Pacchetto dei log scritto in {0}.",
                es: "El paquete de registros se escribió en {0}.",
                fr: "Le paquet de journaux a été écrit dans {0}.",
                pt: "O pacote de registos foi gravado em {0}.",
                ar: "كُتبت حزمة السجلات إلى {0}.",
                hi: "लॉग पैकेज {0} में लिखा गया।",
                bn: "লগ প্যাকেজ {0}-এ লেখা হয়েছে।",
                ur: "لاگ پیکیج {0} میں لکھ دیا گیا۔",
                zh: "日志包已写入 {0}。");
            Add("The disk is full. The log bundle was not saved.",
                ru: "Диск заполнен. Пакет журналов не сохранён.",
                uk: "Диск заповнено. Пакет журналів не збережено.",
                de: "Die Festplatte ist voll. Das Protokollpaket wurde nicht gespeichert.",
                it: "Il disco è pieno. Il pacchetto dei log non è stato salvato.",
                es: "El disco está lleno. El paquete de registros no se guardó.",
                fr: "Le disque est plein. Le paquet de journaux n'a pas été enregistré.",
                pt: "O disco está cheio. O pacote de registos não foi guardado.",
                ar: "القرص ممتلئ. لم تُحفظ حزمة السجلات.",
                hi: "डिस्क भर गई है। लॉग पैकेज सहेजा नहीं गया।",
                bn: "ডিস্ক ভরে গেছে। লগ প্যাকেজ সংরক্ষিত হয়নি।",
                ur: "ڈسک بھر گئی ہے۔ لاگ پیکیج محفوظ نہیں ہوا۔",
                zh: "磁盘已满。日志包未保存。");
            Add("The log bundle could not be written. Nothing was saved.",
                ru: "Пакет журналов не удалось записать. Ничего не сохранено.",
                uk: "Пакет журналів не вдалося записати. Нічого не збережено.",
                de: "Das Protokollpaket konnte nicht geschrieben werden. Nichts wurde gespeichert.",
                it: "Non è stato possibile scrivere il pacchetto dei log. Non è stato salvato nulla.",
                es: "No se pudo escribir el paquete de registros. No se guardó nada.",
                fr: "Le paquet de journaux n'a pas pu être écrit. Rien n'a été enregistré.",
                pt: "Não foi possível gravar o pacote de registos. Nada foi guardado.",
                ar: "تعذّر كتابة حزمة السجلات. لم يُحفظ شيء.",
                hi: "लॉग पैकेज नहीं लिखा जा सका। कुछ भी सहेजा नहीं गया।",
                bn: "লগ প্যাকেজ লেখা যায়নি। কিছুই সংরক্ষিত হয়নি।",
                ur: "لاگ پیکیج نہیں لکھا جا سکا۔ کچھ بھی محفوظ نہیں ہوا۔",
                zh: "无法写入日志包。什么都没有保存。");
            Add("--out must be a file name, an existing folder, or a path ending in a backslash.",
                ru: "--out должен быть именем файла, существующей папкой или путём, оканчивающимся обратной косой чертой.",
                uk: "--out має бути іменем файлу, наявною папкою або шляхом, що закінчується оберненою скісною рискою.",
                de: "--out muss ein Dateiname, ein vorhandener Ordner oder ein Pfad mit einem abschließenden Backslash sein.",
                it: "--out deve essere un nome di file, una cartella esistente o un percorso che termina con una barra rovesciata.",
                es: "--out debe ser un nombre de archivo, una carpeta existente o una ruta que termine en barra invertida.",
                fr: "--out doit être un nom de fichier, un dossier existant ou un chemin se terminant par une barre oblique inverse.",
                pt: "--out deve ser um nome de ficheiro, uma pasta existente ou um caminho que termine em barra invertida.",
                ar: "يجب أن يكون --out اسم ملف أو مجلدًا موجودًا أو مسارًا ينتهي بشرطة مائلة عكسية.",
                hi: "--out एक फ़ाइल नाम, मौजूदा फ़ोल्डर या बैकस्लैश पर समाप्त होने वाला पथ होना चाहिए।",
                bn: "--out একটি ফাইলের নাম, বিদ্যমান একটি ফোল্ডার, বা ব্যাকস্ল্যাশে শেষ হওয়া একটি পথ হতে হবে।",
                ur: "--out فائل کا نام، موجودہ فولڈر، یا بیک سلیش پر ختم ہونے والا راستہ ہونا چاہیے۔",
                zh: "--out 必须是文件名、已存在的文件夹，或以反斜杠结尾的路径。");
        }
    }
}
