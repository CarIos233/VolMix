using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VolMix
{
    /// <summary>
    /// Adds or removes the Start menu shortcut, which is what makes the app show
    /// up in the Start menu "All apps" list.
    /// </summary>
    internal static class StartMenuShortcut
    {
        public static string ShortcutPath
        {
            get
            {
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                return Path.Combine(programs, "VolMix.lnk");
            }
        }

        public static bool Exists()
        {
            try
            {
                return File.Exists(ShortcutPath);
            }
            catch
            {
                return false;
            }
        }

        public static bool Create()
        {
            object shell = null;
            object shortcut = null;
            try
            {
                string exe = Assembly.GetEntryAssembly().Location;
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return false;
                }

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null,
                    shell, new object[] { ShortcutPath });
                if (shortcut == null)
                {
                    return false;
                }

                Type linkType = shortcut.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                    new object[] { Path.GetDirectoryName(exe) });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut,
                    new object[] { "VolMix" });
                linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut,
                    new object[] { exe + ",0" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                return File.Exists(ShortcutPath);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (shortcut != null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(shortcut);
                    }
                    catch
                    {
                    }
                }
                if (shell != null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(shell);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool Remove()
        {
            try
            {
                string path = ShortcutPath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return !File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public static bool Set(bool enabled)
        {
            return enabled ? Create() : Remove();
        }
    }
}
