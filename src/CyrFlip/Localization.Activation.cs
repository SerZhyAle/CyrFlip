namespace CyrFlip
{
    /// <summary>
    /// Strings of the process-level surfaces that are not a settings page: what a second launch says when
    /// the running copy did not answer (APP-ACTIVATION rule 7C) and the busy window's cancel state
    /// (APP-BEHAVIOUR rule 3, ticket S0060 P7).
    /// </summary>
    internal static partial class Localization
    {
        private static void AddActivationStrings()
        {
            // The busy window after "Cancel" was pressed: the work may take a moment to notice the request.
            Add("Cancelling..",
                ru: "Отмена..",
                uk: "Скасування..",
                de: "Wird abgebrochen..",
                it: "Annullamento..",
                es: "Cancelando..",
                fr: "Annulation..",
                pt: "A cancelar..",
                ar: "جارٍ الإلغاء..",
                hi: "रद्द किया जा रहा है..",
                bn: "বাতিল করা হচ্ছে..",
                ur: "منسوخ کیا جا رہا ہے..",
                zh: "正在取消..");

            // The quick-notes export was cancelled from the busy window (an outcome, not a failure).
            Add("Export cancelled. No file was written.",
                ru: "Экспорт отменён. Файл не записан.",
                uk: "Експорт скасовано. Файл не записано.",
                de: "Export abgebrochen. Es wurde keine Datei geschrieben.",
                it: "Esportazione annullata. Nessun file è stato scritto.",
                es: "Exportación cancelada. No se escribió ningún archivo.",
                fr: "Exportation annulée. Aucun fichier n'a été écrit.",
                pt: "Exportação cancelada. Nenhum ficheiro foi escrito.",
                ar: "تم إلغاء التصدير. لم يُكتب أي ملف.",
                hi: "निर्यात रद्द किया गया। कोई फ़ाइल नहीं लिखी गई।",
                bn: "রপ্তানি বাতিল করা হয়েছে। কোনো ফাইল লেখা হয়নি।",
                ur: "برآمد منسوخ کر دی گئی۔ کوئی فائل نہیں لکھی گئی۔",
                zh: "导出已取消。未写入任何文件。");

            // A plain second launch asked the running copy to show itself and nobody answered in time.
            Add("CyrFlip is already running but is not answering. Check the notification area, or end it in Task Manager.",
                ru: "CyrFlip уже запущен, но не отвечает. Проверьте область уведомлений или завершите его в диспетчере задач.",
                uk: "CyrFlip уже запущено, але він не відповідає. Перевірте область сповіщень або завершіть його в диспетчері завдань.",
                de: "CyrFlip läuft bereits, antwortet aber nicht. Prüfen Sie den Infobereich oder beenden Sie es im Task-Manager.",
                it: "CyrFlip è già in esecuzione ma non risponde. Controlla l'area di notifica o terminalo da Gestione attività.",
                es: "CyrFlip ya se está ejecutando, pero no responde. Revisa el área de notificaciones o finalízalo en el Administrador de tareas.",
                fr: "CyrFlip est déjà en cours d'exécution, mais ne répond pas. Vérifiez la zone de notification ou terminez-le dans le Gestionnaire des tâches.",
                pt: "O CyrFlip já está em execução, mas não responde. Verifique a área de notificação ou termine-o no Gestor de Tarefas.",
                ar: "CyrFlip قيد التشغيل بالفعل لكنه لا يستجيب. تحقق من منطقة الإشعارات أو أنهِه من إدارة المهام.",
                hi: "CyrFlip पहले से चल रहा है, लेकिन जवाब नहीं दे रहा। सूचना क्षेत्र देखें, या टास्क मैनेजर में उसे समाप्त करें।",
                bn: "CyrFlip আগে থেকেই চলছে, কিন্তু সাড়া দিচ্ছে না। বিজ্ঞপ্তি এলাকা দেখুন, অথবা টাস্ক ম্যানেজারে এটি বন্ধ করুন।",
                ur: "CyrFlip پہلے ہی چل رہا ہے مگر جواب نہیں دے رہا۔ اطلاعات کا علاقہ دیکھیں، یا ٹاسک مینیجر میں اسے بند کریں۔",
                zh: "CyrFlip 已在运行，但没有响应。请检查通知区域，或在任务管理器中结束它。");
        }
    }
}
