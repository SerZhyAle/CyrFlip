using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// The running copy's side of the layout and text verbs (ticket S0109). <c>layout current</c> and
    /// <c>layout switch</c> run on the UI thread - the indicator they read and poll belongs to it, and the tray click
    /// does the same - while the verbs that only compute (<c>text convert</c>, <c>text flip-case</c>, the labels of
    /// <c>layout list</c>) run on the pool: the hooks share the UI thread, and a text of 32768 characters should not
    /// hold it. None of them activates a window: a command leaves windows as it found them.
    /// </summary>
    internal sealed partial class CyrFlipContext
    {
        private Task<AppCommandResponse> RunLayoutTextCommand(AppCommandInvocation invocation)
        {
            var host = new IndicatorLayoutHost(_indicator);
            switch (invocation.Spec.Name)
            {
                case "layout list":
                    // The tracked window is read here, on the thread that owns it; the names come from the pool.
                    LayoutEntry? active = host.Active();
                    return Task.Run(() => AppCommandsLayoutText.LayoutList(host.Entries(), active, running: true, T));
                case "layout current":
                    return Done(AppCommandsLayoutText.LayoutCurrent(host.Active(), host.CapsLockOn(), T));
                case "layout switch":
                    return Done(AppCommandsLayoutText.LayoutSwitch(invocation, host, T));
                case "text convert":
                    return Task.Run(() => AppCommandsLayoutText.TextConvert(invocation, host.Entries(), AppCommandsLayoutText.ProductConverter, T));
                default:
                    return Task.Run(() => AppCommandsLayoutText.TextFlipCase(invocation, T));
            }
        }
    }
}
