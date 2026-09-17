using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace VolMix
{
    /// <summary>Appearance choice exposed on the settings page.</summary>
    internal enum AppearanceMode
    {
        FollowSystem = 0,
        Light = 1,
        Dark = 2
    }

    /// <summary>
    /// User preferences, stored as a tiny key/value file in the app data folder
    /// so nothing else on the machine is touched.
    /// </summary>
    internal sealed class Settings
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "VolMix";

        public bool HideInactive = false;
        public bool ShowSystemSounds = true;
        public bool ShowMasterVolume = true;

        /// <summary>0 = follow Windows, 1 = always light, 2 = always dark.</summary>
        public AppearanceMode Appearance = AppearanceMode.FollowSystem;

        /// <summary>0 = follow Windows display language, 1 = Chinese, 2 = English.</summary>
        public AppLanguage Language = AppLanguage.Auto;

        public event EventHandler Changed;

        public static string Folder
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "VolMix");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Folder, "settings.ini"); }
        }

        public bool ResolveDark(bool systemDark)
        {
            if (Appearance == AppearanceMode.Light)
            {
                return false;
            }
            if (Appearance == AppearanceMode.Dark)
            {
                return true;
            }
            return systemDark;
        }

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    bool? legacyFollow = null;
                    bool? legacyDark = null;
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int split = line.IndexOf('=');
                        if (split <= 0)
                        {
                            continue;
                        }
                        string key = line.Substring(0, split).Trim();
                        string value = line.Substring(split + 1).Trim();
                        bool flag = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                        switch (key)
                        {
                            case "HideInactive": settings.HideInactive = flag; break;
                            case "ShowSystemSounds": settings.ShowSystemSounds = flag; break;
                            case "ShowMasterVolume": settings.ShowMasterVolume = flag; break;
                            case "Language":
                                {
                                    int language;
                                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out language)
                                        && language >= 0 && language <= 2)
                                    {
                                        settings.Language = (AppLanguage)language;
                                    }
                                    break;
                                }
                            case "Appearance":
                                {
                                    int mode;
                                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out mode)
                                        && mode >= 0 && mode <= 2)
                                    {
                                        settings.Appearance = (AppearanceMode)mode;
                                    }
                                    break;
                                }
                            case "FollowSystemTheme": legacyFollow = flag; break;
                            case "DarkMode": legacyDark = flag; break;
                        }
                    }

                    if (legacyFollow.HasValue || legacyDark.HasValue)
                    {
                        bool follow = !legacyFollow.HasValue || legacyFollow.Value;
                        if (follow)
                        {
                            settings.Appearance = AppearanceMode.FollowSystem;
                        }
                        else
                        {
                            settings.Appearance = (legacyDark.HasValue && legacyDark.Value)
                                ? AppearanceMode.Dark
                                : AppearanceMode.Light;
                        }
                    }
                }
            }
            catch
            {
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(Folder))
                {
                    Directory.CreateDirectory(Folder);
                }

                var lines = new List<string>();
                lines.Add("HideInactive=" + (HideInactive ? "1" : "0"));
                lines.Add("ShowSystemSounds=" + (ShowSystemSounds ? "1" : "0"));
                lines.Add("ShowMasterVolume=" + (ShowMasterVolume ? "1" : "0"));
                lines.Add("Appearance=" + ((int)Appearance).ToString(CultureInfo.InvariantCulture));
                lines.Add("Language=" + ((int)Language).ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch
            {
            }
        }

        public void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        // ------------------------------------------------- start with windows

        public static bool GetStartWithWindows()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    object value = key.GetValue(RunValueName);
                    return value != null && !string.IsNullOrEmpty(value.ToString());
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetStartWithWindows(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    if (enabled)
                    {
                        string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                        // the marker tells a second launch that it came from autostart,
                        // so it must not ask the running instance to open its panel
                        key.SetValue(RunValueName, "\"" + exe + "\" --startup", RegistryValueKind.String);
                    }
                    else if (key.GetValue(RunValueName) != null)
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string VersionText
        {
            get
            {
                Version version = System.Reflection.Assembly.GetEntryAssembly().GetName().Version;
                return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", version.Major, version.Minor);
            }
        }
    }
}


