// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class EnglishUiReviewTests
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static Assembly _app;
        private static string _root, _artifacts;
        private static bool _captureOnly;
        private static bool _pointerOnly;
        private static readonly List<string> Findings = new List<string>();
        private static readonly List<string> Inventory = new List<string>();
        private static int _captures;
        private static readonly List<string> Failures = new List<string>();

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr window, IntPtr destination, uint flags);

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                _app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                _artifacts = Path.Combine(Path.GetFullPath(args[1]), "english-ui");
                _captureOnly = args.Length > 2 && args[2] == "--capture";
                _pointerOnly = args.Length > 2 && args[2] == "--pointer-only";
                Directory.CreateDirectory(_artifacts);
                _root = Path.Combine(Path.GetTempPath(), "TSG-EnglishUi-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                StaUiTestHarness.Run(Run);
                if (!_captureOnly && Failures.Count != 0)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, Failures));
                Console.WriteLine("EnglishUiReviewTests: PASS (" + _captures + " rendered views)");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                if (_artifacts != null)
                {
                    File.WriteAllLines(Path.Combine(_artifacts, "layout-findings.tsv"), Findings, new UTF8Encoding(false));
                    File.WriteAllLines(Path.Combine(_artifacts, "controls.tsv"), Inventory, new UTF8Encoding(false));
                }
                if (_root != null && Path.GetFullPath(_root).StartsWith(
                    Path.Combine(Path.GetTempPath(), "TSG-EnglishUi-"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(_root, true);
            }
        }

        private static void Run()
        {
            TypeOf("AppText").GetMethod("SetLanguage").Invoke(null, new object[] { "en" });
            if (_pointerOnly) { CheckResizePointers(); return; }
            var raidStore = New("RaidNoteStore", Path.Combine(_root, "raids"));
            var reportStore = New("UserReportMemoStore", Path.Combine(_root, "reports"));
            object raid = New("RaidNoteRecord");
            Set(raid, "Key", new string('a', 64)); Set(raid, "Game", "EFT");
            Set(raid, "MapName", "Streets of Tarkov"); Set(raid, "GameType", "PvP/S1 · PMC · 2인");
            Set(raid, "RaidStartedUtc", new DateTime(2026, 9, 7, 9, 42, 0, DateTimeKind.Utc));
            Set(raid, "NoteText", "사용자 원문 — Check the courtyard before crossing.");
            ((IList)Get(raid, "Tags")).Add("route");
            Call(raidStore, "Save", Get(raid, "Key"), raid);
            object reportRecord = New("UserReportMemoRecord");
            Set(reportRecord, "Key", new string('b', 64)); Set(reportRecord, "Game", "EFT");
            Set(reportRecord, "MapName", "Customs"); Set(reportRecord, "ReportCount", 1);
            Set(reportRecord, "RaidStartedUtc", new DateTime(2026, 9, 7, 9, 15, 0, DateTimeKind.Utc));
            var entries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(TypeOf("UserReportMemoEntry")));
            object reportEntry = New("UserReportMemoEntry");
            Set(reportEntry, "Nickname", "사용자닉네임"); Set(reportEntry, "Reason", "Original report reason");
            entries.Add(reportEntry); Set(reportRecord, "Entries", entries);
            Call(reportStore, "Save", Get(reportRecord, "Key"), reportRecord);
            var session = New("ServerSession");
            Set(session, "Game", Enum.Parse(TypeOf("TarkovGame"), "Eft"));
            Set(session, "SessionKey", "english-review-fixture");
            Set(session, "SessionFolderName", "log_2026.09.07_fixture");
            Set(session, "SessionStarted", new DateTime(2026, 9, 7, 18, 42, 0));
            Set(session, "MapName", "Streets of Tarkov");
            Set(session, "UserReportCount", 2);
            Review("main", delegate { return NewForm("MainForm", true); });
            Review("main-medium", delegate { var f = NewForm("MainForm", true); f.Size = new Size(1180, 860); return f; });
            Review("usage", delegate { return NewForm("UsageNoticeForm"); });
            Review("settings", delegate { return NewForm("ApplicationSettingsForm", New("AppPreferences")); });
            Review("arena-warning", delegate { return NewForm("ArenaBlockWarningForm"); });
            Review("license", delegate { return NewForm("LicenseForm"); });
            Review("third-party-notices", delegate { var f = NewForm("LicenseForm"); Call(f, "ShowThirdPartyDocument"); return f; });
            Review("update", delegate { return NewForm("UpdatePromptForm", "0.8.5"); });
            Review("update-progress", delegate {
                var f = NewForm("UpdatePromptForm", "0.8.5");
                Call(f, "BeginDownload"); Call(f, "ReportProgress", 57); return f;
            });
            Review("update-error", delegate {
                var f = NewForm("UpdatePromptForm", "0.8.5"); Call(f, "ShowDownloadError"); return f;
            });
            Review("update-install-error", delegate {
                var f = NewForm("UpdatePromptForm", "0.8.5"); Call(f, "ShowApplyDidNotRestartError"); return f;
            });
            Review("patch-notes", delegate {
                var entry = TypeOf("ReleaseNotesCatalog").GetMethod("FindBundled", Static)
                    .Invoke(null, new object[] { "0.8.5" });
                return NewForm("PatchNotesForm", entry);
            });
            Review("raid-note", delegate { return NewForm("RaidNoteForm", session, raidStore); });
            Review("saved-raid-note", delegate { return NewForm("RaidNoteForm", raid, raidStore); });
            Review("readonly-raid-note", delegate { return NewForm("RaidNoteForm", raid, raidStore, true); });
            Review("report-note", delegate { return NewForm("UserReportMemoForm", session, reportStore); });
            Review("saved-report-note", delegate { return NewForm("UserReportMemoForm", reportRecord, reportStore); });
            Review("readonly-report-note", delegate { return NewForm("UserReportMemoForm", reportRecord, reportStore, true); });
            Review("saved-notes", delegate { return NewForm("RaidNoteArchiveForm", raidStore, reportStore); });
            Review("blocked-servers", delegate {
                var f = NewForm("BlockedServersForm"); RemoveShown(f); return f;
            });
            Review("block-note", delegate { return NewForm("BlockedServersForm+BlockNoteEditorForm", "203.0.113.42", "사용자 원문 — keep this note"); });
            Review("party-add", delegate { return NewForm("PartyBlockInputForm"); });
            Review("party-release", delegate {
                var f = NewForm("PartyBlockBundleReleaseForm", New("PartyBlockBundleStore", Path.Combine(_root, "party.json")));
                RemoveShown(f); return f;
            });
            Review("block-restore", delegate {
                Array items = Array.CreateInstance(TypeOf("BlockedServerRestoreItem"), 3);
                string[] states = { "NewBlock", "AlreadyBlocked", "Excluded" };
                string[] details = { "새 앱 관리 규칙으로 차단합니다.", "이미 앱 관리 규칙으로 차단되어 있습니다.", "정확한 공인 IPv4 주소가 아닙니다." };
                for (int i = 0; i < 3; i++)
                {
                    object item = New("BlockedServerRestoreItem");
                    Set(item, "Status", Enum.Parse(TypeOf("BlockedServerRestoreStatus"), states[i]));
                    Set(item, "Detail", details[i]);
                    Set(item, "SourceIpAddress", "203.0.113." + (40 + i));
                    object entry = New("BlockedServerBackupEntry");
                    Set(entry, "IpAddress", "203.0.113." + (40 + i));
                    Set(entry, "Note", "사용자 메모"); Set(item, "Entry", entry); items.SetValue(item, i);
                }
                return NewForm("BlockedServerRestorePreviewForm", items);
            });
            Review("block-restore-failures", delegate {
                return NewForm("BlockedServerRestoreFailuresForm", new[] {
                    new KeyValuePair<string, string>("203.0.113.42", "The operation was canceled.") });
            });
            Review("note-restore", delegate {
                Type itemList = typeof(IEnumerable<>).MakeGenericType(TypeOf("MemoArchiveRestoreItem"));
                Type result = TypeOf("MemoArchiveRestoreResult");
                Type callbackType = typeof(Func<,>).MakeGenericType(itemList, result);
                var parameter = System.Linq.Expressions.Expression.Parameter(itemList, "items");
                Delegate callback = System.Linq.Expressions.Expression.Lambda(callbackType,
                    System.Linq.Expressions.Expression.Default(result), parameter).Compile();
                Array items = Array.CreateInstance(TypeOf("MemoArchiveRestoreItem"), 1);
                object source = New("MemoArchiveBackupParsedItem");
                Set(source, "Kind", Enum.Parse(TypeOf("MemoArchiveBackupKind"), "RaidNote"));
                Set(source, "RaidNote", raid); Set(source, "Key", Get(raid, "Key"));
                object item = New("MemoArchiveRestoreItem");
                Set(item, "Source", source); Set(item, "Selected", true);
                items.SetValue(item, 0);
                return NewForm("MemoArchiveRestorePreviewForm", items, callback);
            });
            ReviewCustomDateDialog();
            if (!_captureOnly)
            {
                CheckMeaningAndData(raid, reportRecord);
                CheckResizePointers();
                foreach (string language in new[] { "ko-KR", "en" })
                {
                    TypeOf("AppText").GetMethod("SetLanguage").Invoke(null, new object[] { language });
                    using (Form blocked = NewForm("BlockedServersForm"))
                    {
                        RemoveShown(blocked);
                        blocked.StartPosition = FormStartPosition.Manual;
                        blocked.Location = new Point(-16000, -16000);
                        blocked.Show();
                        blocked.Size = blocked.MinimumSize;
                        Application.DoEvents();
                        var release = (Button)blocked.GetType().GetField("_partyReleaseButton", Instance).GetValue(blocked);
                        var add = (Button)blocked.GetType().GetField("_partyAddButton", Instance).GetValue(blocked);
                        Check(release.Width >= release.GetPreferredSize(Size.Empty).Width,
                            "Party unblock button fits its complete label: " + language);
                        Check(release.Parent.ClientRectangle.Contains(release.Bounds)
                                && add.Parent.ClientRectangle.Contains(add.Bounds)
                                && !release.Bounds.IntersectsWith(add.Bounds),
                            "Both party actions fit without overlap: " + language);
                        Capture(blocked, "party-actions-" + language, release.Parent);
                    }
                    using (Form localized = NewForm("PartyBlockInputForm"))
                    {
                        localized.ShowInTaskbar = false;
                        localized.StartPosition = FormStartPosition.Manual;
                        localized.Location = new Point(-16000, -16000);
                        localized.Show();
                        localized.Size = localized.MinimumSize;
                        Application.DoEvents();
                        var preview = (Button)localized.GetType().GetField("_previewButton", Instance).GetValue(localized);
                        Check(preview.Height >= preview.GetPreferredSize(Size.Empty).Height,
                            "The IP preview button fits its text and border: " + language);
                        Capture(localized, "party-preview-" + language);
                        var helpButton = (Button)localized.GetType().GetField("_helpButton", Instance).GetValue(localized);
                        Check(helpButton.Parent.ClientRectangle.Contains(helpButton.Bounds)
                            && helpButton.Width >= helpButton.GetPreferredSize(Size.Empty).Width,
                            "Party usage guide button fits the minimum window: " + language);
                        bool opened = false;
                        using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
                        {
                            timer.Tick += delegate
                            {
                                Form help = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "PartyBlockHelpForm");
                                if (help == null) return;
                                timer.Stop();
                                opened = true;
                                try
                                {
                                    var body = Descendants(help).OfType<RichTextBox>().Single();
                                    Check(body.ReadOnly && body.WordWrap && body.ScrollBars == RichTextBoxScrollBars.Vertical,
                                        "Guide text is read-only and scrollable: " + language);
                                    Check(body.Text.Contains(language == "en" ? "Remove Party Blocks" : "파티 차단 해제")
                                        && body.Text.Contains(language == "en" ? "personal blocks" : "개인적으로 차단"),
                                        "Guide includes removal steps and personal block preservation: " + language);
                                    help.Size = help.MinimumSize;
                                    Application.DoEvents();
                                    Capture(help, "party-help-" + language);
                                    body.SelectionStart = body.TextLength;
                                    body.ScrollToCaret();
                                    Capture(help, "party-help-bottom-" + language);
                                }
                                finally { help.Close(); }
                            };
                            timer.Start();
                            helpButton.PerformClick();
                        }
                        Check(opened && !localized.IsDisposed, "Usage button opens a dismissible guide without closing the input form: " + language);
                    }
                    using (Form license = NewForm("LicenseForm"))
                    {
                        license.StartPosition = FormStartPosition.Manual;
                        license.Location = new Point(-16000, -16000);
                        license.Show();
                        license.Size = license.MinimumSize;
                        Application.DoEvents();
                        Check(Descendants(license).OfType<Label>().Any(l => l.Text.Contains(language == "en" ? "Future features" : "향후 추가되는 기능")),
                            "License summary includes the current-release free-use scope: " + language);
                        Capture(license, "license-scope-" + language);
                    }
                }
            }
        }

        private static void Review(string name, Func<Form> create)
        {
            foreach (bool minimum in new[] { false, true })
            using (var form = create())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-16000, -16000);
                form.ShowInTaskbar = false;
                form.Show();
                if (minimum && !form.MinimumSize.IsEmpty) form.Size = form.MinimumSize;
                Application.DoEvents();
                form.PerformLayout();
                Application.DoEvents();
                string view = name + (minimum ? "-minimum" : "-default");
                Capture(form, view);
                if (!_captureOnly && name == "blocked-servers")
                {
                    var party = Descendants(form).Single(c => c.Name == "PartyActions");
                    Check(party.Parent.ClientRectangle.Contains(party.Bounds), "Party actions must stay inside the header at " + view);
                }
                if (!_captureOnly && name == "license")
                    Check(Descendants(form).OfType<RichTextBox>().Single().Text.Length > 1000, "The original license document is loaded.");
                if (!_captureOnly && name.StartsWith("main", StringComparison.Ordinal))
                {
                    var history = (DataGridView)form.GetType().GetField("_historyGrid", Instance).GetValue(form);
                    Check(history.DisplayedRowCount(false) >= 1, "At least one whole connection row must remain visible in " + view);
                    var advanced = (Control)form.GetType().GetField("_advancedDetailsLayout", Instance).GetValue(form);
                    var map = (Label)form.GetType().GetField("_mapValueLabel", Instance).GetValue(form);
                    Check(!advanced.Visible || map.Right < advanced.Left, "Raid details must not overlap the advanced fields in " + view);
                    Check(history.Columns["packetLoss"].Width == 108, "English packet-loss width increases by only 12 pixels.");
                    Check(history.Columns["result"].MinimumWidth == 186
                        && history.Columns["result"].HeaderText == "Connection\r\nResult",
                        "English connection-result header uses two lines and a slightly wider column.");
                }
            }
        }

        private static void ReviewCustomDateDialog()
        {
            using (var main = NewForm("MainForm", true))
            {
                main.StartPosition = FormStartPosition.Manual;
                main.Location = new Point(-16000, -16000); main.ShowInTaskbar = false; main.Show();
                Exception failure = null;
                bool captured = false;
                main.BeginInvoke(new Action(delegate
                {
                    Form dialog = null;
                    try
                    {
                        dialog = Application.OpenForms.Cast<Form>().Single(f => f.Text == Text("Main.Period.DialogTitle"));
                        dialog.Location = new Point(-16000, -16000);
                        dialog.PerformLayout();
                        Capture(dialog, "custom-date-range");
                        captured = true;
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { if (dialog != null) { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); } }
                }));
                object[] dates = { DateTime.MinValue, DateTime.MinValue };
                bool accepted = (bool)main.GetType().GetMethod("TryShowCustomPeriodDialog", Instance).Invoke(main, dates);
                if (failure != null) throw failure;
                Check(captured && !accepted, "The custom date dialog renders and cancels without scanning logs.");
            }
        }

        private static void Capture(Form form, string view, Control inspectRoot = null)
        {
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                // PrintWindow includes native RichEdit text; DrawToBitmap can
                // leave these controls blank even though their text is present.
                using (Graphics graphics = Graphics.FromImage(bmp))
                {
                    IntPtr dc = graphics.GetHdc();
                    try { if (!PrintWindow(form.Handle, dc, 0)) throw new InvalidOperationException("PrintWindow failed: " + view); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                bmp.Save(Path.Combine(_artifacts, view + ".png"), ImageFormat.Png);
            }
            _captures++;
            bool english = (string)TypeOf("AppText").GetProperty("CurrentLanguage", Static).GetValue(null, null) == "en";
            foreach (Control control in Descendants(inspectRoot ?? form)) Inspect(view, control, english);
        }

        private static void Inspect(string view, Control c, bool english)
        {
            var grid = c as DataGridView;
            if (grid != null && grid.Visible)
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    if (!column.Visible || string.IsNullOrEmpty(column.HeaderText)) continue;
                    DataGridViewCellStyle style = column.HeaderCell.InheritedStyle;
                    string[] lines = column.HeaderText.Replace("\r", "").Split('\n');
                    int available = column.Width - 8;
                    if (column.SortMode == DataGridViewColumnSortMode.Programmatic) available -= 14;
                    foreach (string line in lines)
                    {
                        Size headerMeasured = TextRenderer.MeasureText(line, style.Font,
                            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                        // Main metric headers explicitly wrap at fixed line breaks.
                        if (headerMeasured.Width > available && style.WrapMode != DataGridViewTriState.True)
                            Finding(view + "\tGRID HEADER\t" + column.Name + "\t" + available + " needs " + headerMeasured.Width + "\t" + line);
                    }
                    if (style.WrapMode == DataGridViewTriState.True)
                    {
                        Size block = TextRenderer.MeasureText(column.HeaderText, style.Font,
                            new Size(Math.Max(1, available), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                        if (block.Height > grid.ColumnHeadersHeight - 4)
                            Finding(view + "\tGRID HEADER HEIGHT\t" + column.Name + "\t" + block + "\t" + column.HeaderText);
                    }
                }
            }
            if (!c.Visible || string.IsNullOrWhiteSpace(c.Text) || c is TextBoxBase || c is ComboBox || c is DataGridView) return;
            string text = c.Text.Replace("\r", "").Replace("\n", " | ").Replace("\t", " ");
            Inventory.Add(view + "\t" + c.GetType().Name + "\t" + c.Name + "\t" + c.Bounds + "\t" + c.Font.SizeInPoints + "\t" + text);
            if (!(c is Label) && !(c is ButtonBase)) return;
            int width = Math.Max(1, c.ClientSize.Width - c.Padding.Horizontal);
            var label = c as Label;
            var flags = TextFormatFlags.NoPrefix | (c is ButtonBase ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak);
            Size measured = TextRenderer.MeasureText(c.Text, c.Font, new Size(width, int.MaxValue), flags);
            if (measured.Height > c.ClientSize.Height - c.Padding.Vertical + 2 && (label == null || !label.AutoEllipsis))
                Finding(view + "\tHEIGHT\t" + c.Name + "\t" + c.ClientSize + " needs " + measured + "\t" + text);
            if (c.Parent != null && !c.Parent.ClientRectangle.Contains(c.Bounds) && !(c.Parent is ScrollableControl && ((ScrollableControl)c.Parent).AutoScroll))
                Finding(view + "\tBOUNDS\t" + c.Name + "\t" + c.Bounds + " parent " + c.Parent.ClientSize + "\t" + text);
            if (c.Font.SizeInPoints < 7.5F)
                Finding(view + "\tSMALL FONT\t" + c.Name + "\t" + c.Font.SizeInPoints + "\t" + text);
            if (c is Button && measured.Width > width)
                Finding(view + "\tBUTTON WIDTH\t" + c.Name + "\t" + width + " needs " + measured.Width + "\t" + text);
            // These checks stress text metrics at 125% and 150%. They are not a
            // claim that moving the app between physical DPI monitors was tested.
            foreach (float scale in label != null && label.AutoSize ? new float[0] : new[] { 1.25F, 1.5F })
            using (var font = new Font(c.Font.FontFamily, c.Font.SizeInPoints * scale, c.Font.Style))
            {
                int scaledWidth = (int)Math.Round(width * scale);
                int scaledHeight = (int)Math.Round((c.ClientSize.Height - c.Padding.Vertical) * scale);
                Size scaled = TextRenderer.MeasureText(c.Text, font, new Size(Math.Max(1, scaledWidth), int.MaxValue), flags);
                if ((scaled.Height > scaledHeight + 3 && (label == null || !label.AutoEllipsis))
                    || (c is Button && scaled.Width > scaledWidth + 2))
                    Finding(view + "\tSCALED TEXT " + scale + "\t" + c.Name + "\t" + scaledWidth + "x" + scaledHeight + " needs " + scaled + "\t" + text);
            }
            if (english && c.Text.Any(ch => ch >= '\uAC00' && ch <= '\uD7A3'))
                Finding(view + "\tUNTRANSLATED CHROME\t" + text);
        }

        private static void CheckMeaningAndData(object raid, object report)
        {
            string notice = (string)TypeOf("UsageNoticeForm").GetMethod("BuildLocalizedNoticeText", Static).Invoke(null, null);
            Check(notice.Contains("Instead of Exit, select ESCAPE FROM TARKOV."), "Step 1 must choose EFT instead of Exit.");
            Check(notice.Contains("Instead of Reconnect, select Confirm Leave."), "Step 2 must choose leaving instead of reconnecting.");
            string prompt = Text("Main.Language.ChangePrompt");
            Check(!prompt.Contains("automatically") && !prompt.Contains("saved"), "Restart prompt must not promise note autosave.");
            Check(((string)Get(raid, "NoteText")).StartsWith("사용자 원문"), "Review does not translate the source note.");
            Check((string)Get(((IList)Get(report, "Entries"))[0], "Nickname") == "사용자닉네임", "Review preserves the player name.");
            Check(Text("NoteArchive.ReportCount") == "Reports: {0}", "Report count handles zero, one, and many without incorrect plurals.");
            var diagnostic = TypeOf("AppText").GetMethod("TranslateDiagnostic");
            string translated = (string)diagnostic.Invoke(null, new object[] { "Note 값의 길이 또는 문자가 올바르지 않습니다.", "Diagnostic.Unknown" });
            Check(translated == "Note contains invalid characters or exceeds the length limit.", "Known diagnostic templates preserve the field name.");
            string native = "Access denied (0x80070005).";
            Check((string)diagnostic.Invoke(null, new object[] { native, "Diagnostic.Unknown" }) == native, "Unknown English diagnostics remain intact.");
            object bundled = TypeOf("ReleaseNotesCatalog").GetMethod("FindBundled", Static).Invoke(null, new object[] { "0.8.3" });
            MethodInfo display = TypeOf("ReleaseNotesCatalog").GetMethod("GetDisplayNotes", Static);
            Check(!((string)display.Invoke(null, new[] { bundled })).Any(ch => ch >= '\uAC00' && ch <= '\uD7A3'), "Bundled release notes have an English version.");
            Set(bundled, "NotesText", "사용자가 제공한 변경 사항");
            Check((string)display.Invoke(null, new[] { bundled }) == "사용자가 제공한 변경 사항", "An external document is never replaced by a bundled translation.");
        }

        private static string Text(string key) { return (string)TypeOf("AppText").GetMethod("Get").Invoke(null, new object[] { key }); }

        private static void CheckResizePointers()
        {
            foreach (string language in new[] { "ko-KR", "en" })
            foreach (string name in new[] { "MainForm", "BlockedServersForm" })
            {
                TypeOf("AppText").GetMethod("SetLanguage").Invoke(null, new object[] { language });
                using (Form form = name == "MainForm" ? NewForm(name, true) : NewForm(name))
                {
                    if (name == "BlockedServersForm") RemoveShown(form);
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-16000, -16000); form.ShowInTaskbar = false;
                    form.Show(); Application.DoEvents();
                    var grid = (DataGridView)form.GetType().GetField(name == "MainForm" ? "_historyGrid" : "_grid", Instance).GetValue(form);
                    if (grid.Rows.Count == 0) grid.Rows.Add();
                    // Reproduce the exact native-cursor -> application-handler
                    // collision in the installed .NET Framework runtime. Private
                    // cursor access is confined to the regression fixture.
                    PropertyInfo nativeCursor = typeof(DataGridView).GetProperty("CursorInternal", Instance);
                    nativeCursor.SetValue(grid, Cursors.SizeWE, null);
                    int changes = 0;
                    grid.CursorChanged += delegate { changes++; };
                    for (int i = 0; i < 32; i++)
                        Call(grid, "OnCellMouseMove", new DataGridViewCellMouseEventArgs(0, -1, i, 10,
                            new MouseEventArgs(MouseButtons.None, 0, i, 10, 0)));
                    Check(grid.Cursor == Cursors.SizeWE && changes == 0,
                        name + " " + language + ": hovering a divider must not replace its resize cursor.");
                    int note = grid.Columns["note"].Index;
                    nativeCursor.SetValue(grid, Cursors.Default, null);
                    Call(grid, "OnCellMouseMove", new DataGridViewCellMouseEventArgs(note, 0,
                        grid.Columns["note"].Width / 2, grid.RowTemplate.Height / 2,
                        new MouseEventArgs(MouseButtons.None, 0, 20, 16, 0)));
                    Check(grid.Cursor == Cursors.Hand, name + " " + language + ": note links retain their hand cursor after a drag.");
                    Call(grid, "OnCellMouseLeave", new DataGridViewCellEventArgs(note, 0));
                    Check(grid.Cursor == Cursors.Default, name + " " + language + ": leaving a note releases its hand cursor.");
                    DataGridViewColumn column = grid.Columns[name == "MainForm" ? "time" : "ip"];
                    Rectangle header = grid.GetCellDisplayRectangle(column.Index, -1, false);
                    int x = header.Right - 1, y = header.Top + header.Height / 2;
                    int originalWidth = column.Width;
                    Call(grid, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
                    Check(grid.Cursor == Cursors.SizeWE, name + " " + language + ": native hit testing recognizes the column divider.");
                    FieldInfo sortField = form.GetType().GetField(name == "MainForm" ? "_historySortOrder" : "_blockedServerSortOrder", Instance);
                    object sortBefore = sortField.GetValue(form);
                    Call(grid, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
                    Check(grid.Capture, name + " " + language + ": native column resizing captures the mouse.");
                    changes = 0;
                    bool stable = true;
                    for (int dx = 1; dx <= 24; dx++)
                    {
                        // Cross into the body while resizing, using the real
                        // DataGridView resize operation rather than faking Capture.
                        int moveY = dx < 12 ? y : header.Bottom + grid.RowTemplate.Height / 2;
                        Call(grid, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x + dx, moveY, 0));
                        stable &= grid.Capture && grid.Cursor == Cursors.SizeWE;
                        if (dx == 12) CheckGuidePixels(grid, x + dx, true, null);
                    }
                    Check(stable && changes == 0, name + " " + language + ": the native drag keeps one cursor without repeated overrides.");
                    CheckGuidePixels(grid, x + 24, true, "guide-" + name + "-" + language + "-drag");
                    CheckGuidePixels(grid, x + 12, false, null);
                    Call(grid, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, x + 24, y, 0));
                    CheckGuidePixels(grid, x + 24, false, "guide-" + name + "-" + language + "-released");
                    Check(!grid.Capture && column.Width == originalWidth + 24,
                        name + " " + language + ": releasing the drag applies its exact width and releases capture.");
                    Check(object.Equals(sortBefore, sortField.GetValue(form)), name + " " + language + ": resizing must not trigger a header sort.");
                    Console.WriteLine("Resize " + name + " " + language + ": " + originalWidth + " -> " + column.Width
                        + "; cursor changes during drag: " + changes);
                    // Losing capture must also erase a guide without a MouseUp.
                    header = grid.GetCellDisplayRectangle(column.Index, -1, false);
                    x = header.Right - 1;
                    Call(grid, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
                    Call(grid, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
                    Call(grid, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x + 10, y, 0));
                    CheckGuidePixels(grid, x + 10, true, null);
                    grid.Capture = false;
                    CheckGuidePixels(grid, x + 10, false, null);
                    Call(grid, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, x + 10, y, 0));
                }
            }
            TypeOf("AppText").GetMethod("SetLanguage").Invoke(null, new object[] { "en" });
        }

        private static void CheckGuidePixels(DataGridView grid, int x, bool expected, string imageName)
        {
            // A refresh deliberately erases any transient screen-only resize bar.
            // The guide must still be in the actual rendered client content.
            grid.Refresh();
            using (var bitmap = new Bitmap(grid.ClientSize.Width, grid.ClientSize.Height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr dc = graphics.GetHdc();
                    try { if (!PrintWindow(grid.Handle, dc, 1)) throw new InvalidOperationException("Grid capture failed."); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                int top = grid.ColumnHeadersHeight + 4;
                int bottom = grid.DisplayRectangle.Bottom - 4;
                int color = (SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(232, 157, 54)).ToArgb();
                int matches = 0;
                for (int y = top; y < bottom; y++)
                    if (bitmap.GetPixel(x, y).ToArgb() == color) matches++;
                bool visible = matches >= (bottom - top) * 0.9;
                Check(visible == expected, "Resize guide must " + (expected ? "remain visible through a repaint" : "leave no stale line")
                    + " at x=" + x + "; colored pixels=" + matches + "/" + (bottom - top));
                if (imageName != null)
                {
                    bitmap.Save(Path.Combine(_artifacts, imageName + ".png"), ImageFormat.Png);
                    _captures++;
                }
            }
        }
        private static void Check(bool condition, string message) { if (!condition) Failures.Add(message); }
        private static void Finding(string message) { Findings.Add(message); if (!_captureOnly) Failures.Add(message); }

        // Showing this fixture must never enumerate or modify the user's firewall.
        // Remove only the form's Shown subscribers, before creating its handle.
        private static void RemoveShown(Form form)
        {
            object key = typeof(Form).GetField("EVENT_SHOWN", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var events = (EventHandlerList)typeof(Component).GetProperty("Events", Instance).GetValue(form, null);
            events.RemoveHandler(key, events[key]);
        }
        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (Control child in Descendants(c)) yield return child; }
        }
        private static Type TypeOf(string name) { return _app.GetType("TarkovServerReporter." + name, true); }
        private static object New(string name, params object[] args) { return Activator.CreateInstance(TypeOf(name), Instance, null, args, null); }
        private static Form NewForm(string name, params object[] args) { return (Form)New(name, args); }
        private static void Set(object o, string property, object value) { o.GetType().GetProperty(property, Instance).SetValue(o, value, null); }
        private static object Get(object o, string property) { return o.GetType().GetProperty(property, Instance).GetValue(o, null); }
        private static void Call(object o, string method, params object[] args) { o.GetType().InvokeMember(method, Instance | BindingFlags.InvokeMethod, null, o, args); }
    }
}
