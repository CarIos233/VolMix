using System;
using System.Collections.Generic;
using System.Globalization;

namespace VolMix
{
    /// <summary>Language chosen on the settings page.</summary>
    internal enum AppLanguage
    {
        Auto = 0,
        Chinese = 1,
        English = 2
    }

    /// <summary>
    /// Tiny two language string table. "Auto" follows the Windows display
    /// language: Chinese systems get Chinese, everything else gets English.
    /// </summary>
    internal static class L
    {
        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _english;

        public static AppLanguage Setting = AppLanguage.Auto;

        private static readonly string[,] Table = new string[,]
        {
            { "device.caption", "输出设备", "Output device" },
            { "device.detecting", "正在检测…", "Detecting…" },
            { "device.none", "未找到播放设备", "No playback device" },
            { "page.devices.title", "选择输出设备", "Choose output device" },
            { "page.devices.subtitle", "点击即可切换默认播放设备", "Click a device to make it the default" },
            { "page.settings.title", "设置", "Settings" },
            { "page.settings.subtitle", "所有偏好都会自动保存", "Preferences are saved automatically" },
            { "mixer.empty", "没有检测到音频会话", "No audio sessions found" },
            { "tooltip.back", "返回", "Back" },
            { "tooltip.refresh", "立即刷新", "Refresh now" },
            { "tooltip.devices", "选择输出设备", "Choose output device" },
            { "tooltip.close", "关闭", "Close" },
            { "set.hideInactive", "隐藏不活动的应用", "Hide inactive apps" },
            { "set.hideInactive.desc", "只列出正在播放声音的应用", "Only list apps that are playing audio" },
            { "set.systemSounds", "显示系统声音", "Show system sounds" },
            { "set.systemSounds.desc", "包含 Windows 提示音的会话", "Include the Windows notification sounds session" },
            { "set.masterVolume", "显示输出设备音量", "Show device volume" },
            { "set.masterVolume.desc", "在列表顶部显示主音量滑块", "Show a master volume slider above the list" },
            { "set.followSystem", "跟随系统主题", "Follow the system theme" },
            { "set.followSystem.desc", "界面外观自动跟随 Windows 的浅色 / 深色设置", "Match the Windows light / dark setting automatically" },
            { "set.appearance", "界面外观", "Appearance" },
            { "set.appearance.following", "正在跟随 Windows：{0}", "Following Windows: {0}" },
            { "set.appearance.fixed", "已固定为{0}界面", "Fixed to {0}" },
            { "theme.light", "浅色", "light" },
            { "theme.dark", "深色", "dark" },
            { "theme.light.label", "浅色", "Light" },
            { "theme.dark.label", "深色", "Dark" },
            { "tooltip.settings", "设置", "Settings" },
            { "set.language", "语言", "Language" },
            { "set.language.desc", "首次运行会根据 Windows 显示语言自动选择", "Chosen from the Windows display language on first run" },
            { "language.auto", "跟随系统", "Follow system" },
            { "language.zh", "简体中文", "简体中文" },
            { "language.en", "English", "English" },
            { "set.startup", "开机时自动启动", "Start with Windows" },
            { "set.startup.desc", "在注册表 Run 项中添加启动条目", "Add a Run entry in the registry" },
            { "set.startMenu", "在开始菜单中显示", "Show in the Start menu" },
            { "set.startMenu.desc", "在“所有应用”列表中保留 VolMix 快捷方式", "Keep a VolMix shortcut in the All apps list" },
            { "set.exit", "退出 VolMix", "Exit VolMix" },
            { "set.exit.desc", "关闭应用并从通知区域移除图标", "Close the app and remove the tray icon" },
            { "about.tagline", "完全原创的 Windows 11 音量混音器 · MIT License", "An original Windows 11 volume mixer · MIT License" },
            { "about.hint", "左键托盘图标打开面板 · 右键打开菜单 · Esc 关闭", "Left click the tray icon to open · right click for the menu · Esc closes" },
            { "about.link", "在浏览器中打开 https://github.com/CarIos233", "Open https://github.com/CarIos233 in the browser" },
            { "mixer.master", "主音量", "Master volume" },
            { "mixer.mute", "静音（中键）", "Mute (middle click)" },
            { "mixer.unmute", "取消静音（中键）", "Unmute (middle click)" },
            { "menu.open", "打开音量混音器", "Open volume mixer" },
            { "menu.settings", "设置", "Settings" },
            { "menu.exit", "退出 VolMix", "Exit VolMix" },
            { "menu.language.toEn", "切换到英文 / Switch to English", "Switch to English" },
            { "menu.language.toZh", "切换到简体中文", "Switch to Chinese" },
            { "tray.tooltip", "VolMix 音量混音器", "VolMix Volume Mixer" },
            { "balloon.title", "VolMix 已在通知区域运行", "VolMix is running in the notification area" },
            { "balloon.text", "左键点击图标打开音量面板，右键打开菜单。", "Left click the icon for the volume panel, right click for the menu." },
            { "session.systemSounds", "系统声音", "System sounds" },
            { "session.application", "应用程序", "Application" }
        };

        static L()
        {
            for (int i = 0; i < Table.GetLength(0); i++)
            {
                string key = Table[i, 0];
                Zh[key] = Table[i, 1];
                En[key] = Table[i, 2];
            }
        }

        public static bool IsEnglish
        {
            get { return _english; }
        }

        public static void Apply(AppLanguage language)
        {
            Setting = language;
            _english = Resolve(language);
        }

        public static string T(string key)
        {
            Dictionary<string, string> table = _english ? En : Zh;
            string value;
            if (table.TryGetValue(key, out value))
            {
                return value;
            }
            return key;
        }

        public static string T(string key, object argument)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), argument);
        }

        private static bool Resolve(AppLanguage language)
        {
            if (language == AppLanguage.English)
            {
                return true;
            }
            if (language == AppLanguage.Chinese)
            {
                return false;
            }

            try
            {
                string name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return !string.Equals(name, "zh", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }
}


