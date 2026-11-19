namespace CyrFlip
{
    /// <summary>
    /// The 13-language sentences of <c>screenshot window</c> and <c>screenshot region --annotate</c> (ticket S0130,
    /// APP-COMMANDS 0.3). Every other sentence the two verbs say is a key another file owns and this file never repeats:
    /// the window-naming refusals are the selection family's own keys, and the capture, save, clipboard, busy and crash
    /// answers are the wave-1 screenshot verbs' own. None of the four keys names the caller's path, pid or title.
    /// </summary>
    internal static partial class Localization
    {
        private static void AddAppCommandsScreenshotExtrasStrings()
        {
            Add("The named window is minimized or outside the screens, so there is nothing on the screen to capture. Restore it, then run the command again.",
                ru: "Указанное окно свёрнуто или находится за пределами экранов, поэтому на экране нечего снимать. Разверните его и повторите команду.",
                uk: "Вказане вікно згорнуте або перебуває за межами екранів, тому на екрані немає чого захоплювати. Розгорніть його та повторіть команду.",
                de: "Das genannte Fenster ist minimiert oder außerhalb der Bildschirme, daher gibt es auf dem Bildschirm nichts zu erfassen. Stellen Sie es wieder her und führen Sie den Befehl erneut aus.",
                it: "La finestra indicata è ridotta a icona o fuori dagli schermi, quindi non c'è nulla sullo schermo da acquisire. Ripristinala, poi esegui di nuovo il comando.",
                es: "La ventana indicada está minimizada o fuera de las pantallas, así que no hay nada en la pantalla que capturar. Restáurala y ejecuta el comando de nuevo.",
                fr: "La fenêtre indiquée est réduite ou en dehors des écrans, il n'y a donc rien à capturer à l'écran. Restaurez-la, puis relancez la commande.",
                pt: "A janela indicada está minimizada ou fora das telas, então não há nada na tela para capturar. Restaure-a e execute o comando novamente.",
                ar: "النافذة المحددة مصغّرة أو خارج الشاشات، فلا يوجد شيء على الشاشة لالتقاطه. استعدها، ثم شغّل الأمر مرة أخرى.",
                hi: "नामित विंडो छोटी की गई है या स्क्रीनों के बाहर है, इसलिए कैप्चर करने के लिए स्क्रीन पर कुछ नहीं है। इसे पुनर्स्थापित करें, फिर आदेश दोबारा चलाएँ।",
                bn: "নির্দিষ্ট উইন্ডোটি ছোট করা হয়েছে বা পর্দার বাইরে, তাই ধারণ করার মতো পর্দায় কিছু নেই। এটি পুনরুদ্ধার করুন, তারপর আবার কমান্ড চালান।",
                ur: "نامزد کردہ ونڈو چھوٹی کی گئی ہے یا اسکرینوں سے باہر ہے، اس لیے کیپچر کرنے کے لیے اسکرین پر کچھ نہیں ہے۔ اسے بحال کریں، پھر کمانڈ دوبارہ چلائیں۔",
                zh: "指定的窗口已最小化或在屏幕之外，因此屏幕上没有可捕获的内容。请还原它，然后重新运行命令。");
            Add("The capture was not finished in time. Nothing was saved.",
                ru: "Снимок не был сделан вовремя. Ничего не сохранено.",
                uk: "Знімок не було зроблено вчасно. Нічого не збережено.",
                de: "Die Aufnahme wurde nicht rechtzeitig abgeschlossen. Es wurde nichts gespeichert.",
                it: "L'acquisizione non è stata completata in tempo. Non è stato salvato nulla.",
                es: "La captura no terminó a tiempo. No se guardó nada.",
                fr: "La capture ne s'est pas terminée à temps. Rien n'a été enregistré.",
                pt: "A captura não foi concluída a tempo. Nada foi salvo.",
                ar: "لم يكتمل الالتقاط في الوقت المحدد. لم يتم حفظ أي شيء.",
                hi: "कैप्चर समय पर पूरा नहीं हुआ। कुछ नहीं सहेजा गया।",
                bn: "ধারণটি সময়মতো সম্পন্ন হয়নি। কিছুই সংরক্ষিত হয়নি।",
                ur: "کیپچر وقت پر مکمل نہیں ہوا۔ کچھ بھی محفوظ نہیں ہوا۔",
                zh: "捕获未及时完成。没有保存任何内容。");
            Add("The annotated capture was not finished in time. The selection and the editor were closed and nothing was saved.",
                ru: "Помеченный снимок не был завершён вовремя. Выделение и редактор закрыты, ничего не сохранено.",
                uk: "Знімок із позначками не було завершено вчасно. Виділення та редактор закрито, нічого не збережено.",
                de: "Die beschriftete Aufnahme wurde nicht rechtzeitig abgeschlossen. Die Auswahl und der Editor wurden geschlossen, es wurde nichts gespeichert.",
                it: "L'acquisizione annotata non è stata completata in tempo. La selezione e l'editor sono stati chiusi e non è stato salvato nulla.",
                es: "La captura anotada no terminó a tiempo. La selección y el editor se cerraron y no se guardó nada.",
                fr: "La capture annotée ne s'est pas terminée à temps. La sélection et l'éditeur ont été fermés et rien n'a été enregistré.",
                pt: "A captura anotada não foi concluída a tempo. A seleção e o editor foram fechados e nada foi salvo.",
                ar: "لم يكتمل الالتقاط المُعلَّق في الوقت المحدد. أُغلقت المحدد والمحرر ولم يتم حفظ أي شيء.",
                hi: "एनोटेटेड कैप्चर समय पर पूरा नहीं हुआ। चयन और संपादक बंद कर दिए गए, कुछ नहीं सहेजा गया।",
                bn: "চিহ্নিত ধারণটি সময়মতো সম্পন্ন হয়নি। নির্বাচন এবং সম্পাদক বন্ধ করা হয়েছে, কিছুই সংরক্ষিত হয়নি।",
                ur: "نشان زدہ کیپچر وقت پر مکمل نہیں ہوا۔ انتخاب اور ایڈیٹر بند کر دیے گئے، کچھ بھی محفوظ نہیں ہوا۔",
                zh: "批注的捕获未及时完成。选区和编辑器已关闭，未保存任何内容。");
            Add("The draw-on-capture editor is switched off. Turn it on in the settings of CyrFlip.",
                ru: "Редактор рисования на снимке выключен. Включите его в настройках CyrFlip.",
                uk: "Редактор малювання на знімку вимкнено. Увімкніть його в налаштуваннях CyrFlip.",
                de: "Der Editor zum Zeichnen auf der Aufnahme ist ausgeschaltet. Aktivieren Sie ihn in den Einstellungen von CyrFlip.",
                it: "L'editor per disegnare sull'acquisizione è spento. Attivalo nelle impostazioni di CyrFlip.",
                es: "El editor para dibujar sobre la captura está desactivado. Actívalo en los ajustes de CyrFlip.",
                fr: "L'éditeur de dessin sur la capture est désactivé. Activez-le dans les paramètres de CyrFlip.",
                pt: "O editor para desenhar sobre a captura está desativado. Ative-o nas configurações do CyrFlip.",
                ar: "محرر الرسم على الالتقاط متوقف. شغّله في إعدادات CyrFlip.",
                hi: "कैप्चर पर बनाने वाला संपादक बंद है। इसे CyrFlip की सेटिंग्स में चालू करें।",
                bn: "ধারণের ওপর আঁকার সম্পাদকটি বন্ধ আছে। CyrFlip-এর সেটিংসে এটি চালু করুন।",
                ur: "کیپچر پر ڈراؤ کرنے والا ایڈیٹر بند ہے۔ اسے CyrFlip کی ترتیبات میں آن کریں۔",
                zh: "截图绘制编辑器已关闭。请在 CyrFlip 的设置中启用它。");
        }
    }
}
