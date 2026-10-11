using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32;

namespace CyrFlip
{
    // The Windows input stores share this seam. An override flows only through the current
    // execution context, so fault tests never edit the user's Windows input settings.
    internal interface IRuntimeRegistryKey : IDisposable
    {
        IRuntimeRegistryKey? OpenSubKey(string path, bool writable = false);
        IRuntimeRegistryKey CreateSubKey(string path);
        string[] GetValueNames();
        string[] GetSubKeyNames();
        object? GetValue(string name, object? fallback = null, RegistryValueOptions options = RegistryValueOptions.None);
        RegistryValueKind GetValueKind(string name);
        void SetValue(string name, object value, RegistryValueKind kind);
        void DeleteValue(string name, bool throwOnMissingValue = false);
        void DeleteSubKey(string path, bool throwOnMissingSubKey = false);
        void DeleteSubKeyTree(string path, bool throwOnMissingSubKey = false);
    }

    internal static class RuntimeRegistry
    {
        internal static readonly AsyncLocal<IRuntimeRegistryKey?> UserOverride = new AsyncLocal<IRuntimeRegistryKey?>();
        internal static IRuntimeRegistryKey User => UserOverride.Value ?? new Key(Registry.CurrentUser);
        internal static IRuntimeRegistryKey Machine => new Key(Registry.LocalMachine);

        private sealed class Key : IRuntimeRegistryKey
        {
            private readonly RegistryKey _key;
            public Key(RegistryKey key) => _key = key;
            public IRuntimeRegistryKey? OpenSubKey(string path, bool writable = false)
            {
                RegistryKey? key = _key.OpenSubKey(path, writable);
                return key == null ? null : new Key(key);
            }
            public IRuntimeRegistryKey CreateSubKey(string path) => new Key(_key.CreateSubKey(path)
                ?? throw new InvalidOperationException("Registry key was not created."));
            public string[] GetValueNames() => _key.GetValueNames();
            public string[] GetSubKeyNames() => _key.GetSubKeyNames();
            public object? GetValue(string name, object? fallback = null, RegistryValueOptions options = RegistryValueOptions.None)
                => _key.GetValue(name, fallback, options);
            public RegistryValueKind GetValueKind(string name) => _key.GetValueKind(name);
            public void SetValue(string name, object value, RegistryValueKind kind) => _key.SetValue(name, value, kind);
            public void DeleteValue(string name, bool throwOnMissingValue = false) => _key.DeleteValue(name, throwOnMissingValue);
            public void DeleteSubKey(string path, bool throwOnMissingSubKey = false) => _key.DeleteSubKey(path, throwOnMissingSubKey);
            public void DeleteSubKeyTree(string path, bool throwOnMissingSubKey = false) => _key.DeleteSubKeyTree(path, throwOnMissingSubKey);
            public void Dispose() => _key.Dispose();
        }

        // Read every value and kind before the first mutation. On failure, restore each tree,
        // including previously absent keys. A second failure is logged and returned to the caller.
        internal static Exception? Change(string[] paths, Action write)
        {
            var before = new Dictionary<string, Tree?>();
            try
            {
                foreach (string path in paths)
                {
                    using IRuntimeRegistryKey? key = User.OpenSubKey(path);
                    before[path] = key == null ? null : Tree.Read(key);
                }
            }
            catch (Exception ex) { return ex; }
            try { write(); return null; }
            catch (Exception ex)
            {
                foreach (var pair in before)
                {
                    try
                    {
                        if (pair.Value == null) User.DeleteSubKeyTree(pair.Key, false);
                        else
                        {
                            using IRuntimeRegistryKey key = User.CreateSubKey(pair.Key);
                            pair.Value.Write(key);
                        }
                    }
                    catch (Exception rollback)
                    {
                        DiagnosticLog.Append(DiagnosticLog.Path("registry.log"), "rollback failed: " + rollback.GetType().Name);
                        ex = new AggregateException(ex, rollback);
                    }
                }
                return ex;
            }
        }

        private sealed class Tree
        {
            private readonly Dictionary<string, (object value, RegistryValueKind kind)> _values = new Dictionary<string, (object, RegistryValueKind)>();
            private readonly Dictionary<string, Tree> _children = new Dictionary<string, Tree>();
            internal static Tree Read(IRuntimeRegistryKey key)
            {
                var tree = new Tree();
                foreach (string name in key.GetValueNames())
                    tree._values[name] = (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                        ?? throw new InvalidOperationException("Registry value disappeared."), key.GetValueKind(name));
                foreach (string name in key.GetSubKeyNames())
                {
                    using IRuntimeRegistryKey child = key.OpenSubKey(name) ?? throw new InvalidOperationException("Registry key disappeared.");
                    tree._children[name] = Read(child);
                }
                return tree;
            }
            internal void Write(IRuntimeRegistryKey key)
            {
                foreach (string name in key.GetValueNames())
                    if (!_values.ContainsKey(name)) key.DeleteValue(name, false);
                foreach (string name in key.GetSubKeyNames())
                    if (!_children.ContainsKey(name)) key.DeleteSubKeyTree(name, false);
                foreach (var pair in _values) key.SetValue(pair.Key, pair.Value.value, pair.Value.kind);
                foreach (var pair in _children)
                {
                    using IRuntimeRegistryKey child = key.CreateSubKey(pair.Key);
                    pair.Value.Write(child);
                }
            }
        }
    }
}
