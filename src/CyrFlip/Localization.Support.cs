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

            Add("Примечание",
                en: "Note", uk: "Примітка", de: "Hinweis", it: "Nota", es: "Nota", fr: "Remarque",
                pt: "Observação", ar: "ملاحظة", hi: "टिप्पणी", bn: "মন্তব্য", ur: "نوٹ", zh: "备注");

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

            Add("Письмо отправляете вы сами — CyrFlip ничего не передаёт в сеть. История буфера обмена в архив не включена. Внутри логов встречаются пути к файлам, а в них — имя вашей учётной записи Windows.",
                en: "You send the message yourself - CyrFlip transmits nothing over the network. Clipboard history is not part of the archive. The logs do contain file paths, and those carry your Windows account name.",
                uk: "Лист надсилаєте ви самі - CyrFlip нічого не передає в мережу. Історія буфера обміну до архіву не входить. У логах є шляхи до файлів, а в них - ім'я вашого облікового запису Windows.",
                de: "Gesendet wird die Nachricht von Ihnen - CyrFlip überträgt nichts ins Netz. Der Zwischenablage-Verlauf ist nicht Teil des Archivs. In den Protokollen stehen Dateipfade, und darin steht Ihr Windows-Kontoname.",
                it: "Il messaggio lo invia lei - CyrFlip non trasmette nulla in rete. La cronologia degli appunti non fa parte dell'archivio. Nei log ci sono percorsi di file, e in essi il nome del suo account Windows.",
                es: "El mensaje lo envía usted - CyrFlip no transmite nada por la red. El historial del portapapeles no forma parte del archivo. En los registros hay rutas de archivos y, en ellas, el nombre de su cuenta de Windows.",
                fr: "C'est vous qui envoyez le message - CyrFlip ne transmet rien sur le réseau. L'historique du presse-papiers ne fait pas partie de l'archive. Les journaux contiennent des chemins de fichiers, donc le nom de votre compte Windows.",
                pt: "Você mesmo envia a mensagem - o CyrFlip não transmite nada pela rede. O histórico da área de transferência não faz parte do arquivo. Os logs contêm caminhos de arquivos e, neles, o nome da sua conta do Windows.",
                ar: "أنت من يرسل الرسالة - CyrFlip لا ينقل أي شيء عبر الشبكة. سجل الحافظة ليس جزءًا من الأرشيف. تحتوي السجلات على مسارات ملفات، وفيها اسم حساب Windows الخاص بك.",
                hi: "संदेश आप स्वयं भेजते हैं - CyrFlip नेटवर्क पर कुछ नहीं भेजता। क्लिपबोर्ड इतिहास संग्रह का हिस्सा नहीं है। लॉग में फ़ाइल पथ होते हैं, और उनमें आपके Windows खाते का नाम होता है।",
                bn: "বার্তা আপনি নিজেই পাঠান - CyrFlip নেটওয়ার্কে কিছুই পাঠায় না। ক্লিপবোর্ডের ইতিহাস আর্কাইভে নেই। লগে ফাইলের পথ থাকে, আর তাতে আপনার Windows অ্যাকাউন্টের নাম থাকে।",
                ur: "پیغام آپ خود بھیجتے ہیں - CyrFlip نیٹ ورک پر کچھ نہیں بھیجتا۔ کلپ بورڈ کی تاریخ آرکائیو کا حصہ نہیں۔ لاگز میں فائلوں کے راستے ہوتے ہیں، اور اُن میں آپ کے Windows اکاؤنٹ کا نام ہوتا ہے۔",
                zh: "邮件由您自己发送 - CyrFlip 不会向网络传输任何内容。剪贴板历史不在压缩包内。日志中含有文件路径，其中会出现您的 Windows 账户名。");

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
