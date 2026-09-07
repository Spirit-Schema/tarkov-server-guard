// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    // Stable column names, in 96-DPI units. Each view/language owns a small file
    // so opening another list cannot overwrite this list's preferences.
    internal sealed class ColumnWidthStore
    {
        private const int MaximumBytes = 16384;
        private const string Format = "column-widths-v1";
        internal string SettingsPath { get; private set; }

        internal ColumnWidthStore(string root, string view, string language)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A settings folder is required.", "root");
            if (view != "main" && view != "blocked" && view != "notes") throw new ArgumentException("Unknown view.", "view");
            if (language != "ko-KR" && language != "en") throw new ArgumentException("Unknown language.", "language");
            SettingsPath = Path.Combine(Path.GetFullPath(root), "column-widths-" + view + "-" + language + ".json");
        }

        internal Dictionary<string, int> Load()
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                byte[] bytes = new byte[MaximumBytes + 1];
                int count = 0;
                using (var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int read;
                    while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) > 0) count += read;
                }
                if (count == 0 || count > MaximumBytes) return result;
                var document = new JavaScriptSerializer { MaxJsonLength = MaximumBytes, RecursionLimit = 8 }
                    .DeserializeObject(new UTF8Encoding(false, true).GetString(bytes, 0, count)) as Dictionary<string, object>;
                object format, columns;
                if (document == null || document.Count != 2 || !document.TryGetValue("Format", out format)
                    || !object.Equals(format, Format) || !document.TryGetValue("Columns", out columns)) return result;
                var values = columns as Dictionary<string, object>;
                if (values == null || values.Count > 128) return result;
                foreach (var pair in values)
                    if (ValidName(pair.Key) && pair.Value is int && ValidWidth((int)pair.Value)) result[pair.Key] = (int)pair.Value;
                return result;
            }
            catch (IOException) { return result; }
            catch (UnauthorizedAccessException) { return result; }
            catch (ArgumentException) { return result; }
            catch (InvalidOperationException) { return result; }
        }

        internal bool Save(Dictionary<string, int> widths)
        {
            if (widths == null || widths.Count > 128) return false;
            foreach (var pair in widths) if (!ValidName(pair.Key) || !ValidWidth(pair.Value)) return false;
            string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                    { { "Format", Format }, { "Columns", widths } });
                if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) return false;
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

        internal static int LogicalWidth(int pixels, int dpi)
        {
            return Math.Max(2, Math.Min(8192, (int)Math.Round(pixels * 96D / Math.Max(1, dpi))));
        }

        internal static int PixelWidth(int logical, int dpi, int minimum)
        {
            return Math.Max(minimum, Math.Min(65536, (int)Math.Round(logical * Math.Max(1, dpi) / 96D)));
        }

        private static bool ValidWidth(int width) { return width >= 2 && width <= 8192; }
        private static bool ValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            foreach (char c in name) if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }
    }

    internal static class ColumnWidthPersistence
    {
        internal static void Attach(Form form, string view, string storageRoot)
        {
            if (storageRoot == null) return;
            AttachControls(form, form, new ColumnWidthStore(storageRoot, view, AppText.CurrentLanguage));
        }

        private static void AttachControls(Form form, Control parent, ColumnWidthStore store)
        {
            foreach (Control child in parent.Controls)
            {
                var grid = child as ResizeGuideDataGridView;
                if (grid != null && grid.AllowUserToResizeColumns) AttachGrid(form, grid, store);
                else AttachControls(form, child, store);
            }
        }

        private static void AttachGrid(Form form, ResizeGuideDataGridView grid, ColumnWidthStore store)
        {
            var widths = store.Load();
            EventHandler restore = delegate
            {
                foreach (var pair in widths)
                {
                    DataGridViewColumn column = grid.Columns[pair.Key];
                    if (column == null || !column.Visible || column.Resizable == DataGridViewTriState.False) continue;
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    column.Width = ColumnWidthStore.PixelWidth(pair.Value, grid.DeviceDpi, column.MinimumWidth);
                }
            };
            if (form.Visible) restore(form, EventArgs.Empty);
            else form.Load += restore;
            // Run after each view's DPI metrics, which may reset action widths.
            grid.DpiChangedAfterParent += restore;
            grid.UserColumnResized += delegate(object sender, DataGridViewColumnEventArgs args)
            {
                DataGridViewColumn column = args.Column;
                if (!column.Visible || column.Resizable == DataGridViewTriState.False) return;
                int pixels = column.Width;
                // An explicitly resized fill column becomes user-sized. Untouched
                // fill columns continue to expand with the window as before.
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = pixels;
                widths[column.Name] = ColumnWidthStore.LogicalWidth(pixels, grid.DeviceDpi);
                store.Save(widths);
            };
        }
    }
}
