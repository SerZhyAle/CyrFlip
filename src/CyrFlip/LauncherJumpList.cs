using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The taskbar Jump List, via hand-rolled Shell COM (<c>ICustomDestinationList</c> +
    /// <c>IShellLinkW</c> + <c>IObjectCollection</c>) - the WinForms/net48 answer to WPF's
    /// <c>JumpList</c>, with no new dependency (the same approach as <see cref="UiaCaretCom"/> /
    /// <see cref="Ia2Caret"/>; the vtable order below is load-bearing). One task per scenario
    /// (<c>/launcher-run:{guid}</c>) plus "Manage scenarios" and "Exit CyrFlip"; the standard
    /// Recent/Frequent categories are never shown. No explicit AppUserModelID is set (v0.2 §14.2):
    /// portable rides the exe-path identity exactly as OneClickRunner shipped, MSIX gets the package
    /// identity automatically.
    ///
    /// Everything is guarded: a Shell refusal (or a policy-disabled taskbar) degrades to "no jump
    /// list", never to a crash. Call on the UI (STA) thread.
    /// </summary>
    internal static class LauncherJumpList
    {
        /// <summary>What one Jump List task should look like - kept as pure data so tests can cover the mapping.</summary>
        internal readonly struct TaskSpec
        {
            public readonly string Title;
            public readonly string Arguments;
            public readonly string Description;
            public readonly string IconPath;
            public readonly int IconIndex;
            public TaskSpec(string title, string arguments, string description, string iconPath, int iconIndex)
            {
                Title = title; Arguments = arguments; Description = description; IconPath = iconPath; IconIndex = iconIndex;
            }
        }

        /// <summary>
        /// The full ordered task list for the current scenarios: one task per scenario in stored
        /// order, then Manage scenarios and Exit (spec §8.2). Pure - no COM, no shell.
        /// </summary>
        internal static List<TaskSpec> BuildTasks(IEnumerable<LauncherScenario> scenarios,
            string exePath, Func<string, string> translate)
        {
            var tasks = new List<TaskSpec>();
            foreach (LauncherScenario scenario in scenarios)
            {
                LauncherIcon icon = LauncherIconResolver.Resolve(scenario);
                tasks.Add(new TaskSpec(scenario.Name, LauncherIpc.RunPrefix + scenario.Id.ToString("D"),
                    scenario.Name, icon.Path, icon.Index));
            }
            tasks.Add(new TaskSpec(translate("Управление сценариями..."), LauncherIpc.SettingsCommand,
                translate("Управление сценариями..."), exePath, 0));
            tasks.Add(new TaskSpec(translate("Выход из CyrFlip"), LauncherIpc.ExitCommand,
                translate("Выход из CyrFlip"), exePath, 0));
            return tasks;
        }

        /// <summary>Replace the Jump List with the given tasks. False when the shell refused.</summary>
        [HandleProcessCorruptedStateExceptions]
        public static bool Apply(List<TaskSpec> tasks)
        {
            try
            {
                string exePath = Application.ExecutablePath;
                var list = (ICustomDestinationList)new CDestinationList();
                try
                {
                    Guid iidObjectArray = typeof(IObjectArray).GUID;
                    list.BeginList(out uint _, ref iidObjectArray, out object removed);
                    if (removed != null) Marshal.ReleaseComObject(removed);

                    var collection = (IObjectCollection)new CEnumerableObjectCollection();
                    // The collection AddRefs every link it takes, so ours are released as soon as they
                    // are handed over - otherwise each rebuild (any scenario edit, any language change)
                    // leaves a shell-link RCW behind until the next GC.
                    var links = new List<IShellLinkW>();
                    try
                    {
                        foreach (TaskSpec task in tasks)
                        {
                            IShellLinkW link = MakeShellLink(exePath, task);
                            links.Add(link);
                            collection.AddObject(link);
                        }

                        list.AddUserTasks((IObjectArray)collection);
                        list.CommitList();
                    }
                    finally
                    {
                        foreach (IShellLinkW link in links)
                        {
                            try { Marshal.ReleaseComObject(link); } catch { /* best effort */ }
                        }
                        Marshal.ReleaseComObject(collection);
                    }
                }
                catch
                {
                    try { list.AbortList(); } catch { }
                    throw;
                }
                finally { Marshal.ReleaseComObject(list); }
                return true;
            }
            catch (Exception ex)
            {
                LauncherLog.Log("JumpList: apply failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Remove the app's Jump List entirely (the launcher was disabled - spec §4).</summary>
        [HandleProcessCorruptedStateExceptions]
        public static bool Clear()
        {
            try
            {
                var list = (ICustomDestinationList)new CDestinationList();
                try { list.DeleteList(null); }
                finally { Marshal.ReleaseComObject(list); }
                return true;
            }
            catch (Exception ex)
            {
                LauncherLog.Log("JumpList: clear failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>An IShellLink that starts this exe with the task's arguments, titled via the property store.</summary>
        private static IShellLinkW MakeShellLink(string exePath, TaskSpec task)
        {
            var link = (IShellLinkW)new CShellLink();
            link.SetPath(exePath);
            link.SetArguments(task.Arguments);
            link.SetDescription(task.Description);
            if (!string.IsNullOrEmpty(task.IconPath))
                link.SetIconLocation(task.IconPath, task.IconIndex);

            // The visible task caption is PKEY_Title on the link's property store - a link without
            // it shows up blank in the Jump List.
            var store = (IPropertyStore)link;
            var titleKey = new PropertyKey(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
            PropVariant title = PropVariant.FromString(task.Title);
            try
            {
                store.SetValue(ref titleKey, ref title);
                store.Commit();
            }
            finally { title.Clear(); }
            return link;
        }

        // ---- COM interop. Method order matches shobjidl.h exactly - a wrong slot silently calls
        // ---- the wrong method (see the CLAUDE.md "COM vtable gotchas" note). ----

        [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
        private class CDestinationList { }

        [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
        private class CEnumerableObjectCollection { }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICustomDestinationList
        {
            void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void BeginList(out uint pcMinSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);
            void AppendKnownCategory(int category);
            void AddUserTasks(IObjectArray poa);
            void CommitList();
            void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? pszAppID);
            void AbortList();
        }

        [ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectArray
        {
            void GetCount(out uint pcObjects);
            void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        }

        [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectCollection
        {
            // IObjectArray's two methods occupy the first slots.
            void GetCount(out uint pcObjects);
            void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            void AddObject([MarshalAs(UnmanagedType.Interface)] object pvObject);
            void AddFromArray(IObjectArray poaSource);
            void RemoveObjectAt(uint uiIndex);
            void Clear();
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PropertyKey pkey);
            void GetValue(ref PropertyKey key, out PropVariant pv);
            void SetValue(ref PropertyKey key, ref PropVariant pv);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;
            public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
        }

        /// <summary>Minimal PROPVARIANT: only VT_LPWSTR is ever stored (the task title).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort VarType;
            public ushort Reserved1, Reserved2, Reserved3;
            public IntPtr Pointer;
            public IntPtr Pointer2;

            private const ushort VT_LPWSTR = 31;

            public static PropVariant FromString(string value) => new PropVariant
            {
                VarType = VT_LPWSTR,
                Pointer = Marshal.StringToCoTaskMemUni(value),
            };

            public void Clear()
            {
                PropVariantClear(ref this);
            }

            [DllImport("ole32.dll")]
            private static extern int PropVariantClear(ref PropVariant pvar);
        }
    }
}
