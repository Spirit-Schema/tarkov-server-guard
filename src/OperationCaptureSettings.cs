// Copyright © 2026 Spirit-Schema. All rights reserved.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal static class OperationCaptureSettings
    {
        private static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TarkovServerGuard", "OperationNotes", "capture-folders.json"); }
        }
        internal static List<string> LoadCustomFolders()
        {
            string path = SettingsPath;
            if (!File.Exists(path)) return new List<string>();
            // Corrupt configuration is an explicit error: don't silently replace the user's paths.
            return new JavaScriptSerializer().Deserialize<List<string>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<string>();
        }
        internal static IList<string> LoadFolders()
        {
            return OperationScreenshotLinker.DiscoverDefaultFolders().Concat(LoadCustomFolders())
                .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal static void SaveCustomFolders(IEnumerable<string> folders)
        {
            var paths = folders.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (MemoStoreWriteLease.Acquire(path))
            {
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(paths), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        internal static void ShowDialog(IWin32Window owner)
        {
            using (var form = new BrandedForm())
            {
                form.Text = OperationNoteWindow.T("작전노트 · 촬영 폴더", "Operation note · capture folders");
                form.ClientSize = new Size(650, 380); form.StartPosition = FormStartPosition.CenterParent;
                form.BackColor = Color.FromArgb(22, 27, 32); form.ForeColor = Color.Gainsboro;
                form.Font = new Font("Malgun Gothic", 9); form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false; form.MinimizeBox = false;
                var auto = new TextBox { Multiline = true, ReadOnly = true, Left = 15, Top = 42, Width = 620, Height = 108, ScrollBars = ScrollBars.Vertical,
                    Text = string.Join(Environment.NewLine, OperationScreenshotLinker.DiscoverDefaultFolders()) };
                var custom = new ListBox { Left = 15, Top = 182, Width = 620, Height = 102, HorizontalScrollbar = true };
                foreach (string path in LoadCustomFolders()) custom.Items.Add(path);
                var add = new Button { Text = OperationNoteWindow.T("폴더 추가", "Add folder"), Left = 15, Top = 298, Width = 105, Height = 30 };
                var remove = new Button { Text = OperationNoteWindow.T("목록에서 제외", "Remove from list"), Left = 126, Top = 298, Width = 120, Height = 30 };
                var save = new Button { Text = OperationNoteWindow.T("저장", "Save"), Left = 535, Top = 298, Width = 100, Height = 30 };
                var status = new Label { Left = 15, Top = 337, Width = 620, Height = 36, Text = OperationNoteWindow.T("사진 원본은 이동·삭제하지 않습니다. 저장 위치가 다른 경우 위 목록에 추가하세요.", "Original photos stay in place. Add your folder if you use another capture location.") };
                add.Click += delegate
                {
                    using (var picker = new FolderBrowserDialog { ShowNewFolderButton = false, Description = OperationNoteWindow.T("스크린샷이 저장되는 폴더", "Folder containing screenshots") })
                        if (picker.ShowDialog(form) == DialogResult.OK && !custom.Items.Cast<string>().Contains(picker.SelectedPath, StringComparer.OrdinalIgnoreCase)) custom.Items.Add(picker.SelectedPath);
                };
                remove.Click += delegate { if (custom.SelectedIndex >= 0) custom.Items.RemoveAt(custom.SelectedIndex); };
                save.Click += delegate
                {
                    try { SaveCustomFolders(custom.Items.Cast<string>()); form.DialogResult = DialogResult.OK; form.Close(); }
                    catch (Exception ex) { status.Text = ex.Message; status.ForeColor = Color.Salmon; }
                };
                form.Controls.AddRange(new Control[] {
                    new Label { Text = OperationNoteWindow.T("자동으로 찾은 촬영 폴더", "Automatically detected capture folders"), Left = 15, Top = 15, Width = 620 }, auto,
                    new Label { Text = OperationNoteWindow.T("추가 촬영 폴더 · Steam/NVIDIA 사용자 지정 위치", "Additional folders · custom Steam/NVIDIA capture locations"), Left = 15, Top = 159, Width = 620 }, custom, add, remove, save, status });
                form.ShowDialog(owner);
            }
        }
    }
}
