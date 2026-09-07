// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class LocalizationTests
    {
        private static int _assertions;

        [STAThread]
        public static int Main()
        {
            try
            {
                TestDefaultAndLanguageSelection();
                TestRequiredProductTerms();
                TestCatalogKeysAndFormats();
                TestLiteralTranslationRules();
                TestWinFormsTreeTranslation();
                TestBrandedFormAppliesOnlyOnce();
                TestPreferencesRoundTrip();
                TestPreferencesSaveFailureLeavesPrimaryUnchanged();
                TestPreferencesRecovery();
                TestPreferencesValidation();
                Console.WriteLine("Localization tests passed: " + _assertions + " assertions");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Localization tests failed: " + exception);
                return 1;
            }
            finally
            {
                AppText.SetLanguage(AppText.DefaultLanguage);
            }
        }

        private static void TestDefaultAndLanguageSelection()
        {
            Assert(AppText.CurrentLanguage == AppText.DefaultLanguage,
                "Korean is the default application language");
            Assert(AppText.Get("Common.Button.Close") == "닫기",
                "the default catalog is Korean");

            string[] supported = AppText.GetSupportedLanguages();
            Assert(supported.SequenceEqual(new[] { "ko-KR", "en" }),
                "the supported language whitelist is stable");
            supported[0] = "changed";
            Assert(AppText.GetSupportedLanguages()[0] == "ko-KR",
                "callers cannot mutate the supported language whitelist");

            Assert(AppText.SetLanguage("EN")
                    && AppText.CurrentLanguage == "en"
                    && AppText.Get("Common.Button.Close") == "Close",
                "supported language codes are normalized case-insensitively");
            Assert(!AppText.SetLanguage("fr")
                    && AppText.CurrentLanguage == "en",
                "unsupported languages are rejected without changing the selection");
            Assert(!AppText.SetLanguage(null)
                    && AppText.CurrentLanguage == "en",
                "a missing language is rejected without changing the selection");
            Assert(AppText.SetLanguage("ko-kr")
                    && AppText.CurrentLanguage == "ko-KR",
                "the canonical Korean language code is restored");
            Assert(AppText.Get("Unknown.Test.Key") == "[[Unknown.Test.Key]]",
                "unknown catalog keys remain visible to developers");
            Assert(AppText.Get(null) == "[[missing-key]]",
                "a missing key has a deterministic marker");
        }

        private static void TestRequiredProductTerms()
        {
            Assert(AppText.SetLanguage(AppText.EnglishLanguage),
                "English can be selected for terminology tests");

            var required = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Memo.Legacy.RaidNoteTitle", "Raid Note" },
                { "Memo.Legacy.UserReportTitle", "Report Note" },
                { "Settings.WindowTitle", "Settings" },
                { "Notes.Navigation.Label", "Saved Notes" },
                { "BlockedServers.Window.Title", "Blocked Servers" },
                { "Common.Button.Scan", "Scan" },
                { "Domain.Participation.Solo", "Solo" }
            };
            foreach (KeyValuePair<string, string> item in required)
                Assert(AppText.Get(item.Key) == item.Value,
                    "required English product term is stable: " + item.Key);

            Assert(AppText.Get("Main.Connection.LogUnavailable") == "No Log"
                    && AppText.Get("Common.Status.NotVerified") == "Not Verified"
                    && AppText.Get("Common.Status.Unknown") == "Unknown",
                "different unavailable and unknown states remain distinct");
            Assert(AppText.Get("Common.Button.UnblockSelected") == "Unblock Selected",
                "unblocking is not translated as selection clearing");

            AppText.SetLanguage(AppText.KoreanLanguage);
            Assert(AppText.Get("Domain.Participation.Solo") == "단독",
                "the approved Korean solo term is used");
            Assert(AppText.Get("Domain.Progression.PvpSeason") == "PvP/S{0}",
                "the approved compact PvP season notation is used");

            Assert(AppText.NormalizePvpSeasonDisplay("PvPs1 · PMC · 사용자 데이터")
                    == "PvP/S1 · PMC · 사용자 데이터"
                    && AppText.NormalizePvpSeasonDisplay("PvP시즌2") == "PvP/S2"
                    && AppText.NormalizePvpSeasonDisplay("PvPs") == "PvP/S?"
                    && AppText.NormalizePvpSeasonDisplay("PvPs1000") == "PvP/S?"
                    && AppText.NormalizePvpSeasonDisplay("PvP시즌0") == "PvP/S?"
                    && AppText.NormalizePvpSeasonDisplay("PvP/S?") == "PvP/S?",
                "historical PvP season labels are normalized for display without changing surrounding text");
        }

        private static void TestCatalogKeysAndFormats()
        {
            string[] keys = AppText.GetKeys();
            Assert(keys.Length >= 150, "the catalog retains core UI and legacy memo localization");
            Assert(keys.SequenceEqual(keys.OrderBy(item => item, StringComparer.Ordinal)),
                "catalog keys are returned in stable ordinal order");
            Assert(keys.Distinct(StringComparer.Ordinal).Count() == keys.Length,
                "catalog keys are unique");

            var korean = new Dictionary<string, string>(StringComparer.Ordinal);
            var english = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                AppText.SetLanguage(AppText.KoreanLanguage);
                korean[key] = AppText.Get(key);
                AppText.SetLanguage(AppText.EnglishLanguage);
                english[key] = AppText.Get(key);
            }

            object[] safeArguments =
            {
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10
            };
            foreach (string key in keys)
            {
                Assert(!string.IsNullOrWhiteSpace(korean[key])
                        && !string.IsNullOrWhiteSpace(english[key]),
                    "both catalogs contain non-empty text: " + key);
                Assert(GetPlaceholderSignature(korean[key]) == GetPlaceholderSignature(english[key]),
                    "format placeholders match between catalogs: " + key);

                AppText.SetLanguage(AppText.KoreanLanguage);
                string koreanFormatted = AppText.Format(key, safeArguments);
                AppText.SetLanguage(AppText.EnglishLanguage);
                string englishFormatted = AppText.Format(key, safeArguments);
                Assert(!string.IsNullOrWhiteSpace(koreanFormatted)
                        && !string.IsNullOrWhiteSpace(englishFormatted),
                    "both catalog values are valid composite formats: " + key);
            }

            AppText.SetLanguage(AppText.KoreanLanguage);
            Assert(AppText.Format("Common.Count.Selected", 2)
                    == "2개 선택",
                "Korean selection counts format predictably");
            AppText.SetLanguage(AppText.EnglishLanguage);
            Assert(AppText.Format("Common.Count.Selected", 2)
                    == "2 selected",
                "English selection counts format predictably");
            Assert(AppText.Format("Unknown.Format.Key", 1) == "[[Unknown.Format.Key]]",
                "an unknown format key has a deterministic marker");
        }

        private static void TestLiteralTranslationRules()
        {
            AppText.SetLanguage(AppText.KoreanLanguage);
            Assert(AppText.TranslateLiteral("닫기") == "닫기",
                "literal translation is a no-op in Korean");

            AppText.SetLanguage(AppText.EnglishLanguage);
            Assert(AppText.TranslateLiteral("닫기") == "Close",
                "an exact unambiguous Korean literal translates to English");
            Assert(AppText.TranslateLiteral("레이드 메모") == "Raid Note",
                "the restored raid memo title translates to English");
            Assert(AppText.TranslateLiteral("차단") == "Block",
                "another same-meaning duplicate literal translates safely");
            Assert(AppText.TranslateLiteral("확인") == "확인",
                "a Korean literal with conflicting English meanings remains unchanged");
            Assert(AppText.TranslateLiteral(" 닫기") == " 닫기",
                "literal translation does not trim or guess near matches");
            Assert(AppText.TranslateLiteral("사용자 데이터") == "사용자 데이터",
                "unknown literals remain unchanged");
            Assert(AppText.TranslateLiteral(null) == null,
                "null literal input remains null");
        }

        private static void TestWinFormsTreeTranslation()
        {
            AppText.SetLanguage(AppText.EnglishLanguage);
            using (var form = new Form())
            using (var contextMenu = new ContextMenuStrip())
            using (var malgunFormFont = new Font("Malgun Gothic", 10f, FontStyle.Regular))
            using (var malgunHeadingFont = new Font("Malgun Gothic", 14f, FontStyle.Bold))
            using (var codeFont = new Font("Consolas", 9f, FontStyle.Italic))
            {
                form.Text = "서버차단현황";
                form.AccessibleName = "서버차단현황";
                form.Font = malgunFormFont;

                var panel = new Panel();
                var heading = new Label
                {
                    Text = "사용 방법",
                    AccessibleName = "업데이트 확인",
                    AccessibleDescription = "게임 로그를 다시 읽고 현재 서버 정보를 조회합니다.",
                    Font = malgunHeadingFont
                };
                var scanButton = new Button { Text = "조회" };
                var codeLabel = new Label { Text = "PMC", Font = codeFont };
                var memoTextBox = new TextBox { Text = "닫기" };
                var pathTextBox = new TextBox { Text = @"C:\Games\EFT\Logs" };
                var documentTextBox = new RichTextBox { Text = "저장" };
                var comboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                var nonStringItem = new DisplayItem("오늘");
                comboBox.Items.Add("오늘");
                comboBox.Items.Add("최근 7일");
                comboBox.Items.Add(nonStringItem);
                comboBox.SelectedIndex = 0;

                var toolStrip = new ToolStrip();
                var saveItem = new ToolStripButton("저장");
                var inputItem = new ToolStripTextBox { Text = "닫기" };
                var menuItem = new ToolStripDropDownButton("차단");
                menuItem.DropDownItems.Add(new ToolStripMenuItem("차단 해제"));
                toolStrip.Items.Add(saveItem);
                toolStrip.Items.Add(inputItem);
                toolStrip.Items.Add(menuItem);

                contextMenu.Items.Add(new ToolStripMenuItem("차단 해제"));
                heading.ContextMenuStrip = contextMenu;

                var grid = new DataGridView
                {
                    AllowUserToAddRows = false,
                    AutoGenerateColumns = false
                };
                grid.DefaultCellStyle.NullValue = "확인 안 됨";
                var valueColumn = new DataGridViewTextBoxColumn
                {
                    Name = "value",
                    HeaderText = "서버 IP",
                    ToolTipText = "현재 핑"
                };
                var actionColumn = new DataGridViewButtonColumn
                {
                    Name = "action",
                    HeaderText = "차단",
                    Text = "차단 해제",
                    UseColumnTextForButtonValue = true
                };
                grid.Columns.Add(valueColumn);
                grid.Columns.Add(actionColumn);
                grid.Rows.Add("닫기", null);

                panel.Controls.Add(heading);
                panel.Controls.Add(scanButton);
                panel.Controls.Add(codeLabel);
                panel.Controls.Add(memoTextBox);
                panel.Controls.Add(pathTextBox);
                panel.Controls.Add(documentTextBox);
                panel.Controls.Add(comboBox);
                panel.Controls.Add(toolStrip);
                panel.Controls.Add(grid);
                form.Controls.Add(panel);

                UiLocalization.Apply(form);

                Assert(form.Text == "Blocked Servers"
                        && form.AccessibleName == "Blocked Servers",
                    "form text and accessible text translate");
                Assert(heading.Text == "Usage Guide"
                        && heading.AccessibleName == "Check for Updates"
                        && heading.AccessibleDescription
                            == "Reads the game logs again and scans the current server.",
                    "nested control and accessibility properties translate");
                Assert(scanButton.Text == "Scan", "nested button text translates");
                Assert(memoTextBox.Text == "닫기"
                        && pathTextBox.Text == @"C:\Games\EFT\Logs"
                        && documentTextBox.Text == "저장",
                    "TextBox and RichTextBox document or path contents are never translated");
                Assert(comboBox.Items[0] as string == "Today"
                        && comboBox.Items[1] as string == "Last 7 Days",
                    "ComboBox string items translate");
                Assert(object.ReferenceEquals(comboBox.Items[2], nonStringItem),
                    "ComboBox non-string items are not mutated");
                Assert(comboBox.SelectedIndex == 0 && comboBox.Text == "Today",
                    "ComboBox selection follows its translated item");
                Assert(saveItem.Text == "Save"
                        && inputItem.Text == "닫기"
                        && menuItem.Text == "Block"
                        && menuItem.DropDownItems[0].Text == "Unblock",
                    "ToolStrip action text translates while ToolStrip input remains untouched");
                Assert(contextMenu.Items[0].Text == "Unblock",
                    "attached context-menu items translate");
                Assert(grid.Columns[0].HeaderText == "Server IP"
                        && grid.Columns[0].ToolTipText == "Current Ping"
                        && grid.Columns[1].HeaderText == "Block"
                        && ((DataGridViewButtonColumn)grid.Columns[1]).Text == "Unblock"
                        && (string)grid.DefaultCellStyle.NullValue == "Not Verified",
                    "grid headers and static cell presentation text translate");
                Assert((string)grid.Rows[0].Cells[0].Value == "닫기",
                    "DataGridView data-row values are never translated");
                Assert(string.Equals(form.Font.Name, "Segoe UI", StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(form.Font.Size - 10f) < 0.01f
                        && form.Font.Style == FontStyle.Regular,
                    "the English form default font switches to Segoe UI");
                Assert(string.Equals(heading.Font.Name, "Segoe UI", StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(heading.Font.Size - 14f) < 0.01f
                        && heading.Font.Style == FontStyle.Bold,
                    "an emphasized Korean UI font keeps its size and style");
                Assert(string.Equals(codeLabel.Font.Name, "Consolas", StringComparison.OrdinalIgnoreCase)
                        && codeLabel.Font.Style == FontStyle.Italic,
                    "an intentional non-UI font remains unchanged");
            }
        }

        private static void TestBrandedFormAppliesOnlyOnce()
        {
            AppText.SetLanguage(AppText.EnglishLanguage);
            using (var form = new TestBrandedForm())
            {
                form.Text = "서버차단현황";
                var button = new Button { Text = "조회" };
                form.Controls.Add(button);
                form.RaiseShown();
                Assert(form.Text == "Blocked Servers" && button.Text == "Scan",
                    "BrandedForm applies localization before its first Shown event");

                form.Text = "서버차단현황";
                button.Text = "조회";
                form.RaiseShown();
                Assert(form.Text == "서버차단현황" && button.Text == "조회",
                    "BrandedForm does not reapply localization after the first show");
            }
        }

        private static void TestPreferencesRoundTrip()
        {
            string root = CreateTestDirectory();
            try
            {
                var store = new AppPreferencesStore(root);
                AppPreferences initial = store.Load();
                Assert(initial.SchemaVersion == AppPreferencesStore.CurrentSchemaVersion
                        && initial.Language == AppText.DefaultLanguage
                        && AppPreferencesStore.IsValidHotKey(initial.OverlayToggleHotKey)
                        && AppPreferencesStore.IsValidHotKey(initial.OverlayEditHotKey),
                    "missing preferences load safe Korean defaults");

                store.Save(new AppPreferences { Language = "EN" });
                Assert(File.Exists(store.SettingsPath) && File.Exists(store.BackupPath),
                    "a save commits both primary and backup files");
                Assert(File.ReadAllBytes(store.SettingsPath)
                        .SequenceEqual(File.ReadAllBytes(store.BackupPath)),
                    "the primary and backup contain the same committed settings");

                AppPreferences loaded = new AppPreferencesStore(root).Load();
                Assert(loaded.SchemaVersion == AppPreferencesStore.CurrentSchemaVersion
                        && loaded.Language == AppText.EnglishLanguage
                        && loaded.OverlayToggleHotKey.Key
                            == AppPreferencesStore.CreateDefaultOverlayToggleHotKey().Key,
                    "English preferences and default shortcuts round-trip canonically");
                string json = File.ReadAllText(store.SettingsPath, Encoding.UTF8);
                Assert(json.Contains("\"SchemaVersion\":2")
                        && json.Contains("\"Language\":\"en\"")
                        && json.Contains("\"OverlayToggleHotKey\"")
                        && json.Contains("\"OverlayEditHotKey\""),
                    "the stored document carries an explicit schema, language, and key codes");

                store.Save(new AppPreferences
                {
                    SchemaVersion = 999,
                    Language = AppText.KoreanLanguage
                });
                loaded = store.Load();
                Assert(loaded.SchemaVersion == AppPreferencesStore.CurrentSchemaVersion
                        && loaded.Language == AppText.KoreanLanguage,
                    "Save writes the authoritative current schema version");
                AssertNoTemporaryArtifacts(root);
            }
            finally
            {
                DeleteTestDirectory(root);
            }
        }

        private static void TestPreferencesRecovery()
        {
            string root = CreateTestDirectory();
            try
            {
                var store = new AppPreferencesStore(root);
                store.Save(new AppPreferences { Language = AppText.EnglishLanguage });

                File.WriteAllText(store.SettingsPath, "{broken-json", new UTF8Encoding(false));
                AppPreferences recovered = store.Load();
                Assert(recovered.Language == AppText.EnglishLanguage,
                    "a damaged primary recovers from the valid backup");

                File.WriteAllText(
                    store.SettingsPath,
                    "{\"SchemaVersion\":1,\"Language\":\"fr\"}",
                    new UTF8Encoding(false));
                recovered = store.Load();
                Assert(recovered.Language == AppText.EnglishLanguage,
                    "an unsupported primary language recovers from the whitelist-valid backup");

                File.WriteAllText(
                    store.SettingsPath,
                    "{\"SchemaVersion\":1,\"Language\":\"ko\"}",
                    new UTF8Encoding(false));
                File.WriteAllText(store.BackupPath, "[]", new UTF8Encoding(false));
                recovered = store.Load();
                Assert(recovered.SchemaVersion == AppPreferencesStore.CurrentSchemaVersion
                        && recovered.Language == AppText.KoreanLanguage
                        && AppPreferencesStore.IsValidHotKey(recovered.OverlayToggleHotKey)
                        && AppPreferencesStore.IsValidHotKey(recovered.OverlayEditHotKey),
                    "schema-one language preferences migrate with safe default shortcuts");

                store.Save(new AppPreferences { Language = AppText.EnglishLanguage });

                File.WriteAllBytes(store.SettingsPath, new byte[] { 0xc3, 0x28 });
                recovered = store.Load();
                Assert(recovered.Language == AppText.EnglishLanguage,
                    "invalid UTF-8 in the primary recovers from the backup");

                File.WriteAllText(store.SettingsPath, "{}", new UTF8Encoding(false));
                File.WriteAllText(store.BackupPath, "[]", new UTF8Encoding(false));
                recovered = store.Load();
                Assert(recovered.SchemaVersion == AppPreferencesStore.CurrentSchemaVersion
                        && recovered.Language == AppText.DefaultLanguage,
                    "two damaged preference files fall back to safe defaults");

                string oversized = new string('x', 17 * 1024);
                File.WriteAllText(store.SettingsPath, oversized, new UTF8Encoding(false));
                File.WriteAllText(store.BackupPath, oversized, new UTF8Encoding(false));
                recovered = store.Load();
                Assert(recovered.Language == AppText.DefaultLanguage,
                    "oversized preference files are rejected");
            }
            finally
            {
                DeleteTestDirectory(root);
            }
        }

        private static void TestPreferencesSaveFailureLeavesPrimaryUnchanged()
        {
            string root = CreateTestDirectory();
            try
            {
                var store = new AppPreferencesStore(root);
                AppPreferences previous = AppPreferences.CreateDefault();
                previous.Language = AppText.EnglishLanguage;
                previous.OverlayToggleHotKey = new AppHotKeyPreference
                {
                    Modifiers = AppPreferencesStore.HotKeyModifierControl
                        | AppPreferencesStore.HotKeyModifierAlt,
                    Key = (int)Keys.F7
                };
                previous.OverlayEditHotKey = new AppHotKeyPreference
                {
                    Modifiers = AppPreferencesStore.HotKeyModifierControl
                        | AppPreferencesStore.HotKeyModifierAlt,
                    Key = (int)Keys.F8
                };
                store.Save(previous);
                byte[] previousPrimary = File.ReadAllBytes(store.SettingsPath);
                byte[] previousBackup = File.ReadAllBytes(store.BackupPath);

                AppPreferences next = AppPreferences.CreateDefault();
                next.Language = AppText.KoreanLanguage;
                next.OverlayToggleHotKey = new AppHotKeyPreference
                {
                    Modifiers = AppPreferencesStore.HotKeyModifierControl
                        | AppPreferencesStore.HotKeyModifierShift,
                    Key = (int)Keys.F9
                };
                next.OverlayEditHotKey = new AppHotKeyPreference
                {
                    Modifiers = AppPreferencesStore.HotKeyModifierControl
                        | AppPreferencesStore.HotKeyModifierShift,
                    Key = (int)Keys.F10
                };

                bool saveFailed = false;
                using (var lockedBackup = new FileStream(
                    store.BackupPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None))
                {
                    try
                    {
                        store.Save(next);
                    }
                    catch (IOException)
                    {
                        saveFailed = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        saveFailed = true;
                    }
                }

                Assert(saveFailed,
                    "a locked backup makes the settings save report failure");
                Assert(File.ReadAllBytes(store.SettingsPath).SequenceEqual(previousPrimary),
                    "a failed backup commit leaves the authoritative primary unchanged");
                Assert(File.ReadAllBytes(store.BackupPath).SequenceEqual(previousBackup),
                    "a failed backup commit preserves the previous recovery copy");

                AppPreferences loaded = new AppPreferencesStore(root).Load();
                Assert(loaded.Language == AppText.EnglishLanguage
                        && loaded.OverlayToggleHotKey.Key == (int)Keys.F7
                        && loaded.OverlayEditHotKey.Key == (int)Keys.F8,
                    "loading after a reported save failure returns the previous settings");
                AssertNoTemporaryArtifacts(root);
            }
            finally
            {
                DeleteTestDirectory(root);
            }
        }

        private static void TestPreferencesValidation()
        {
            string root = CreateTestDirectory();
            try
            {
                var store = new AppPreferencesStore(root);
                AssertThrows<ArgumentNullException>(delegate { store.Save(null); },
                    "saving null preferences is rejected");
                AssertThrows<ArgumentException>(delegate
                {
                    store.Save(new AppPreferences { Language = "ja" });
                }, "saving a language outside the whitelist is rejected");
                AppPreferences duplicate = AppPreferences.CreateDefault();
                duplicate.OverlayEditHotKey = duplicate.OverlayToggleHotKey.Clone();
                AssertThrows<ArgumentException>(delegate { store.Save(duplicate); },
                    "duplicate overlay shortcuts are rejected");
                AppPreferences modifierless = AppPreferences.CreateDefault();
                modifierless.OverlayToggleHotKey.Modifiers = 0;
                AssertThrows<ArgumentException>(delegate { store.Save(modifierless); },
                    "a modifierless overlay shortcut is rejected");
                Assert(!File.Exists(store.SettingsPath) && !File.Exists(store.BackupPath),
                    "invalid settings are rejected before files are created");
                AssertThrows<ArgumentException>(delegate { new AppPreferencesStore(" "); },
                    "an empty storage root is rejected");
            }
            finally
            {
                DeleteTestDirectory(root);
            }
        }

        private static string GetPlaceholderSignature(string value)
        {
            MatchCollection matches = Regex.Matches(value, @"\{(?<index>\d+)(?:[^{}]*)\}");
            return string.Join(",", matches.Cast<Match>()
                .Select(match => match.Groups["index"].Value)
                .OrderBy(item => item, StringComparer.Ordinal));
        }

        private static string CreateTestDirectory()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "TSG-LocalizationTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return Path.GetFullPath(root);
        }

        private static void DeleteTestDirectory(string root)
        {
            string fullRoot = Path.GetFullPath(root);
            string expectedPrefix = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar
                + "TSG-LocalizationTests-";
            if (!fullRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected localization test cleanup path.");
            if (Directory.Exists(fullRoot)) Directory.Delete(fullRoot, true);
        }

        private static void AssertNoTemporaryArtifacts(string root)
        {
            Assert(Directory.GetFiles(root, "*.tmp.*").Length == 0
                    && Directory.GetFiles(root, "*.previous.*").Length == 0,
                "atomic saves clean up transient files");
        }

        private static void AssertThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                Assert(true, message);
                return;
            }
            throw new InvalidOperationException("Assertion failed: " + message);
        }

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        private sealed class DisplayItem
        {
            private readonly string _text;

            internal DisplayItem(string text)
            {
                _text = text;
            }

            public override string ToString()
            {
                return _text;
            }
        }

        private sealed class TestBrandedForm : BrandedForm
        {
            internal void RaiseShown()
            {
                OnShown(EventArgs.Empty);
            }
        }
    }
}
