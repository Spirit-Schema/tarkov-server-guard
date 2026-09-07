// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    // Only the main window's normal client size, in 96-DPI units. This document
    // deliberately has no window position, monitor identity, or column settings.
    internal sealed class WindowSizeStore
    {
        private const string Format = "main-window-size-v1";
        private const int MaximumBytes = 4096;
        internal string SettingsPath { get; private set; }

        internal WindowSizeStore(string storageRoot)
        {
            if (string.IsNullOrWhiteSpace(storageRoot)) throw new ArgumentException("A settings folder is required.", "storageRoot");
            SettingsPath = Path.Combine(Path.GetFullPath(storageRoot), "main-window-size.json");
        }

        internal Size? Load()
        {
            try
            {
                byte[] bytes = new byte[MaximumBytes + 1];
                int count = 0;
                using (var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    int read;
                    while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) > 0)
                        count += read;
                }
                if (count == 0 || count > MaximumBytes) return null;
                string json = new UTF8Encoding(false, true).GetString(bytes, 0, count);
                var document = new JavaScriptSerializer { MaxJsonLength = MaximumBytes, RecursionLimit = 8 }
                    .DeserializeObject(json) as Dictionary<string, object>;
                object format, width, height;
                if (document == null || document.Count != 3
                    || !document.TryGetValue("Format", out format) || !object.Equals(format, Format)
                    || !document.TryGetValue("Width", out width) || !(width is int)
                    || !document.TryGetValue("Height", out height) || !(height is int)) return null;
                var size = new Size((int)width, (int)height);
                return IsValid(size) ? (Size?)size : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal bool Save(Size logicalClientSize)
        {
            if (!IsValid(logicalClientSize)) return false;
            string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var document = new Dictionary<string, object>
                {
                    { "Format", Format }, { "Width", logicalClientSize.Width }, { "Height", logicalClientSize.Height }
                };
                string json = new JavaScriptSerializer().Serialize(document);
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
                else File.Move(temporary, SettingsPath);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static Size ToLogicalClientSize(Size client, int dpi)
        {
            double scale = 96D / (dpi > 0 ? dpi : 96);
            return new Size((int)Math.Round(client.Width * scale), (int)Math.Round(client.Height * scale));
        }

        internal static Size RestoreClientSize(Size logical, int dpi, Size minimumClient, Size availableClient)
        {
            double scale = (dpi > 0 ? dpi : 96) / 96D;
            return new Size(
                Clamp((int)Math.Round(logical.Width * scale), minimumClient.Width, availableClient.Width),
                Clamp((int)Math.Round(logical.Height * scale), minimumClient.Height, availableClient.Height));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            int available = Math.Max(1, maximum);
            int effectiveMinimum = Math.Min(Math.Max(1, minimum), available);
            return Math.Max(effectiveMinimum, Math.Min(available, value));
        }

        private static bool IsValid(Size size)
        {
            return size.Width >= 320 && size.Width <= 16384 && size.Height >= 200 && size.Height <= 16384;
        }
    }
}
