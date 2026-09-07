// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class AppHotKeyPreference
    {
        public int Modifiers { get; set; }
        public int Key { get; set; }

        public AppHotKeyPreference Clone()
        {
            return new AppHotKeyPreference
            {
                Modifiers = Modifiers,
                Key = Key
            };
        }
    }

    public sealed class AppPreferences
    {
        public int SchemaVersion { get; set; }
        public string Language { get; set; }
        public AppHotKeyPreference OverlayToggleHotKey { get; set; }
        public AppHotKeyPreference OverlayEditHotKey { get; set; }

        public static AppPreferences CreateDefault()
        {
            return new AppPreferences
            {
                SchemaVersion = AppPreferencesStore.CurrentSchemaVersion,
                Language = AppText.DefaultLanguage,
                OverlayToggleHotKey = AppPreferencesStore.CreateDefaultOverlayToggleHotKey(),
                OverlayEditHotKey = AppPreferencesStore.CreateDefaultOverlayEditHotKey()
            };
        }

        public AppPreferences Clone()
        {
            return new AppPreferences
            {
                SchemaVersion = SchemaVersion,
                Language = Language,
                OverlayToggleHotKey = OverlayToggleHotKey == null
                    ? null : OverlayToggleHotKey.Clone(),
                OverlayEditHotKey = OverlayEditHotKey == null
                    ? null : OverlayEditHotKey.Clone()
            };
        }
    }

    /// <summary>
    /// Stores user-facing application preferences separately from log paths,
    /// firewall metadata, and memo data. The primary and backup files are each
    /// committed by an atomic replace on supported Windows file systems.
    /// </summary>
    public sealed class AppPreferencesStore
    {
        public const int HotKeyModifierAlt = 0x0001;
        public const int HotKeyModifierControl = 0x0002;
        public const int HotKeyModifierShift = 0x0004;
        public const int HotKeyModifierWindows = 0x0008;
        public const int CurrentSchemaVersion = 2;
        public const string FileName = "app-preferences.json";
        private const int MaximumFileBytes = 16 * 1024;

        private readonly string _settingsPath;
        private readonly string _backupPath;
        private readonly object _sync = new object();

        public AppPreferencesStore()
            : this(GetDefaultStorageRoot())
        {
        }

        public AppPreferencesStore(string storageRoot)
        {
            if (string.IsNullOrWhiteSpace(storageRoot))
                throw new ArgumentException("Application preferences storage root is required.", "storageRoot");

            string normalizedRoot = Path.GetFullPath(storageRoot);
            _settingsPath = Path.Combine(normalizedRoot, FileName);
            _backupPath = _settingsPath + ".bak";
        }

        public string SettingsPath
        {
            get { return _settingsPath; }
        }

        public string BackupPath
        {
            get { return _backupPath; }
        }

        public AppPreferences Load()
        {
            lock (_sync)
            {
                AppPreferences preferences;
                if (TryLoadFile(_settingsPath, out preferences)) return preferences;
                if (TryLoadFile(_backupPath, out preferences)) return preferences;
                return AppPreferences.CreateDefault();
            }
        }

        public void Save(AppPreferences preferences)
        {
            if (preferences == null) throw new ArgumentNullException("preferences");

            string normalizedLanguage;
            if (!AppText.TryNormalizeLanguage(preferences.Language, out normalizedLanguage))
                throw new ArgumentException("Unsupported application language.", "preferences");

            var document = new AppPreferences
            {
                SchemaVersion = CurrentSchemaVersion,
                Language = normalizedLanguage,
                OverlayToggleHotKey = NormalizeHotKey(
                    preferences.OverlayToggleHotKey
                        ?? CreateDefaultOverlayToggleHotKey(),
                    "overlay visibility hotkey"),
                OverlayEditHotKey = NormalizeHotKey(
                    preferences.OverlayEditHotKey
                        ?? CreateDefaultOverlayEditHotKey(),
                    "overlay edit hotkey")
            };
            if (AreSameHotKey(
                document.OverlayToggleHotKey,
                document.OverlayEditHotKey))
                throw new ArgumentException(
                    "Overlay hotkeys must use different key combinations.",
                    "preferences");

            var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumFileBytes };
            byte[] bytes = new UTF8Encoding(false).GetBytes(serializer.Serialize(document));
            if (bytes.Length <= 0 || bytes.Length > MaximumFileBytes)
                throw new InvalidOperationException("Application preferences are too large to save.");

            lock (_sync)
            {
                string directory = Path.GetDirectoryName(_settingsPath);
                if (string.IsNullOrWhiteSpace(directory))
                    throw new InvalidOperationException("Application preferences directory is unavailable.");
                Directory.CreateDirectory(directory);

                // Stage the recovery copy first. If that write fails, the authoritative
                // primary remains unchanged, so a reported save failure cannot silently
                // become the active setting on the next launch.
                CommitFile(_backupPath, bytes);
                CommitFile(_settingsPath, bytes);
            }
        }

        public static string GetDefaultStorageRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard");
        }

        public static AppHotKeyPreference CreateDefaultOverlayToggleHotKey()
        {
            return new AppHotKeyPreference
            {
                Modifiers = HotKeyModifierControl | HotKeyModifierShift,
                Key = (int)Keys.O
            };
        }

        public static AppHotKeyPreference CreateDefaultOverlayEditHotKey()
        {
            return new AppHotKeyPreference
            {
                Modifiers = HotKeyModifierControl | HotKeyModifierShift,
                Key = (int)Keys.E
            };
        }

        public static bool IsValidHotKey(AppHotKeyPreference preference)
        {
            if (preference == null) return false;
            const int allowed = HotKeyModifierAlt
                | HotKeyModifierControl
                | HotKeyModifierShift
                | HotKeyModifierWindows;
            int modifiers = preference.Modifiers;
            if (modifiers == 0 || (modifiers & ~allowed) != 0)
                return false;

            Keys key = ((Keys)preference.Key) & Keys.KeyCode;
            if (key == Keys.None || preference.Key != (int)key) return false;
            return key != Keys.ControlKey
                && key != Keys.LControlKey
                && key != Keys.RControlKey
                && key != Keys.ShiftKey
                && key != Keys.LShiftKey
                && key != Keys.RShiftKey
                && key != Keys.Menu
                && key != Keys.LMenu
                && key != Keys.RMenu
                && key != Keys.LWin
                && key != Keys.RWin;
        }

        public static bool AreSameHotKey(
            AppHotKeyPreference left,
            AppHotKeyPreference right)
        {
            return left != null && right != null
                && left.Modifiers == right.Modifiers
                && left.Key == right.Key;
        }

        private static bool TryLoadFile(string path, out AppPreferences preferences)
        {
            preferences = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumFileBytes)
                    return false;

                byte[] bytes;
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.SequentialScan))
                {
                    if (stream.Length <= 0 || stream.Length > MaximumFileBytes)
                        return false;
                    bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) return false;
                        offset += read;
                    }
                    if (stream.ReadByte() >= 0) return false;
                }

                string json = new UTF8Encoding(false, true).GetString(bytes);
                if (json.Length > 0 && json[0] == '\ufeff') json = json.Substring(1);

                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumFileBytes };
                AppPreferences parsed = serializer.Deserialize<AppPreferences>(json);
                string normalizedLanguage;
                if (parsed == null
                    || (parsed.SchemaVersion != 1
                        && parsed.SchemaVersion != CurrentSchemaVersion)
                    || !AppText.TryNormalizeLanguage(parsed.Language, out normalizedLanguage))
                    return false;

                AppHotKeyPreference toggle = parsed.SchemaVersion == 1
                    ? CreateDefaultOverlayToggleHotKey()
                    : parsed.OverlayToggleHotKey;
                AppHotKeyPreference edit = parsed.SchemaVersion == 1
                    ? CreateDefaultOverlayEditHotKey()
                    : parsed.OverlayEditHotKey;
                if (!IsValidHotKey(toggle)
                    || !IsValidHotKey(edit)
                    || AreSameHotKey(toggle, edit))
                    return false;

                preferences = new AppPreferences
                {
                    SchemaVersion = CurrentSchemaVersion,
                    Language = normalizedLanguage,
                    OverlayToggleHotKey = toggle.Clone(),
                    OverlayEditHotKey = edit.Clone()
                };
                return true;
            }
            catch
            {
                preferences = null;
                return false;
            }
        }

        private static AppHotKeyPreference NormalizeHotKey(
            AppHotKeyPreference preference,
            string field)
        {
            if (!IsValidHotKey(preference))
                throw new ArgumentException("Invalid " + field + ".", "preferences");
            return preference.Clone();
        }

        private static void CommitFile(string destinationPath, byte[] bytes)
        {
            string temporaryPath = destinationPath + ".tmp." + Guid.NewGuid().ToString("N");
            string replacedPath = destinationPath + ".previous." + Guid.NewGuid().ToString("N");
            try
            {
                WriteNewFile(temporaryPath, bytes);
                if (File.Exists(destinationPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, destinationPath, replacedPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ReplaceFallback(temporaryPath, destinationPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
                DeleteTemporaryFile(replacedPath);
            }
        }

        private static void ReplaceFallback(string temporaryPath, string destinationPath)
        {
            // This branch is only for file systems without File.Replace support.
            // The normal Windows path above remains an atomic same-volume replace.
            File.Copy(temporaryPath, destinationPath, true);
            File.Delete(temporaryPath);
        }

        private static void WriteNewFile(string path, byte[] bytes)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Recovery artifacts must never make preferences unreadable.
            }
        }
    }
}
