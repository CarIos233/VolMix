using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VolMix
{
    internal sealed class AppIdentity
    {
        public string Name { get; set; }
        public string ImagePath { get; set; }
    }

    /// <summary>
    /// Works out a friendly name and the executable behind an audio session.
    /// Audio sessions expose their image path in the session instance
    /// identifier, which also covers processes we cannot open directly
    /// (elevated or protected processes).
    /// </summary>
    internal static class AppResolver
    {
        private static readonly Dictionary<string, AppIdentity> Cache =
            new Dictionary<string, AppIdentity>(StringComparer.Ordinal);

        public static AppIdentity Resolve(uint processId, string sessionInstanceId)
        {
            string key = sessionInstanceId == null ? processId.ToString() : sessionInstanceId;
            AppIdentity cached;
            if (Cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var identity = new AppIdentity();

            string imagePath = null;
            string description = null;
            string processName = null;

            if (processId != 0)
            {
                try
                {
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        try
                        {
                            imagePath = process.MainModule.FileName;
                        }
                        catch
                        {
                        }
                        try
                        {
                            FileVersionInfo version = process.MainModule.FileVersionInfo;
                            if (version != null)
                            {
                                description = Clean(version.FileDescription);
                                if (string.IsNullOrEmpty(description))
                                {
                                    description = Clean(version.ProductName);
                                }
                            }
                        }
                        catch
                        {
                        }
                        try
                        {
                            processName = process.ProcessName;
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                    // process already gone, fall through to the session data
                }
            }

            if (string.IsNullOrEmpty(imagePath))
            {
                imagePath = ImagePathFromSession(sessionInstanceId);
            }

            if (string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(imagePath))
            {
                description = DescriptionFromFile(imagePath);
            }

            if (string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(imagePath))
            {
                description = Prettify(Path.GetFileNameWithoutExtension(imagePath));
            }

            if (string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(processName))
            {
                description = Prettify(processName);
            }

            identity.Name = description;
            identity.ImagePath = imagePath;

            if (Cache.Count > 512)
            {
                Cache.Clear();
            }
            Cache[key] = identity;
            return identity;
        }

        private static string ImagePathFromSession(string sessionInstanceId)
        {
            if (string.IsNullOrEmpty(sessionInstanceId))
            {
                return null;
            }

            string[] parts = sessionInstanceId.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string candidate = parts[i];
                int percent = candidate.IndexOf("%b", StringComparison.Ordinal);
                if (percent >= 0)
                {
                    candidate = candidate.Substring(0, percent);
                }
                candidate = candidate.Trim();
                if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    string dos = NativeMethods.DevicePathToDosPath(candidate);
                    if (!string.IsNullOrEmpty(dos))
                    {
                        return dos;
                    }
                }
            }
            return null;
        }

        private static string DescriptionFromFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                string description = Clean(info.FileDescription);
                if (string.IsNullOrEmpty(description))
                {
                    description = Clean(info.ProductName);
                }
                return description;
            }
            catch
            {
                return null;
            }
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            value = value.Trim();
            if (value.StartsWith("@", StringComparison.Ordinal) || value.Length == 0)
            {
                return null;
            }
            return value;
        }

        private static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return L.T("session.application");
            }
            if (name.Length > 1)
            {
                return char.ToUpperInvariant(name[0]) + name.Substring(1);
            }
            return name.ToUpperInvariant();
        }
    }
}

