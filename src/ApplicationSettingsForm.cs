// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal static class ApplicationSettingsTheme
    {
        internal static readonly Color Background = Color.FromArgb(15, 18, 22);
        internal static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        internal static readonly Color Border = Color.FromArgb(54, 63, 74);
        internal static readonly Color Accent = Color.FromArgb(232, 157, 54);
        internal static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        internal static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        internal static readonly Color Danger = Color.FromArgb(192, 68, 75);

        internal static Button CreateButton(string text, bool primary)
        {
            var button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(88, 32),
                Padding = new Padding(12, 0, 12, 0),
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : SurfaceAlt,
                ForeColor = primary ? Color.FromArgb(29, 24, 17) : TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                Margin = new Padding(6, 0, 0, 0)
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            return button;
        }

    }

    /// <summary>
    /// Language confirmation follows a user-committed selection. Loading preferences never
    /// prompts or restarts, and changing language preserves unrelated stored preferences.
    /// </summary>
    public sealed class ApplicationSettingsForm : BrandedForm
    {
        private sealed class LanguageChoice
        {
            internal LanguageChoice(string value, string label)
            {
                Value = value;
                Label = label;
            }

            internal string Value { get; private set; }
            internal string Label { get; private set; }
            public override string ToString() { return Label; }
        }

        private readonly AppPreferences _original;
        private readonly Func<AppPreferences, string> _applyPreferences;
        private ComboBox _languageComboBox;
        private Label _statusLabel;
        private bool _loading;

        public ApplicationSettingsForm(AppPreferences preferences)
            : this(preferences, null)
        {
        }

        internal ApplicationSettingsForm(
            AppPreferences preferences,
            Func<AppPreferences, string> applyPreferences)
        {
            _original = (preferences ?? AppPreferences.CreateDefault()).Clone();
            _applyPreferences = applyPreferences;
            SelectedPreferences = _original.Clone();
            InitializeWindow();
            BuildInterface();
            LoadValues();
        }

        public AppPreferences SelectedPreferences { get; private set; }
        public bool RestartRequested { get; private set; }

        private void InitializeWindow()
        {
            Text = AppText.Get("Settings.WindowTitle");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 220);
            MinimumSize = new Size(520, 220);
            BackColor = ApplicationSettingsTheme.Background;
            ForeColor = ApplicationSettingsTheme.TextPrimary;
            Font = new Font("Malgun Gothic", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = ApplicationSettingsTheme.Background,
                Padding = new Padding(18),
                ColumnCount = 2,
                RowCount = 4
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            Controls.Add(root);

            _languageComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ApplicationSettingsTheme.SurfaceAlt,
                ForeColor = ApplicationSettingsTheme.TextPrimary,
                Margin = new Padding(0, 5, 0, 5),
                AccessibleName = AppText.Get("Settings.Language.Label"),
                AccessibleDescription = AppText.Get("Settings.Language.RestartRequired")
            };
            _languageComboBox.Items.Add(new LanguageChoice(
                AppText.KoreanLanguage,
                AppText.Get("Settings.Language.Korean")));
            _languageComboBox.Items.Add(new LanguageChoice(
                AppText.EnglishLanguage,
                AppText.Get("Settings.Language.English")));
            _languageComboBox.SelectionChangeCommitted += LanguageSelectionCommitted;
            root.Controls.Add(CreateLabel(AppText.Get("Settings.Language.Label")), 0, 0);
            root.Controls.Add(_languageComboBox, 1, 0);

            Label hint = new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("Settings.Language.RestartRequired"),
                ForeColor = ApplicationSettingsTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            root.Controls.Add(hint, 0, 1);
            root.SetColumnSpan(hint, 2);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = string.Empty,
                ForeColor = ApplicationSettingsTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AccessibleName = AppText.Get("Settings.WindowTitle"),
                AccessibleRole = AccessibleRole.Alert
            };
            root.Controls.Add(_statusLabel, 0, 2);
            root.SetColumnSpan(_statusLabel, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = ApplicationSettingsTheme.Background,
                Margin = new Padding(0)
            };
            Button save = ApplicationSettingsTheme.CreateButton(AppText.Get("Common.Save"), true);
            Button cancel = ApplicationSettingsTheme.CreateButton(AppText.Get("Common.Button.Cancel"), false);
            save.Click += delegate { SaveAndClose(false); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 3);
            root.SetColumnSpan(buttons, 2);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void LoadValues()
        {
            _loading = true;
            try
            {
                SelectLanguage(_original.Language);
            }
            finally
            {
                _loading = false;
            }
        }

        private void LanguageSelectionCommitted(object sender, EventArgs e)
        {
            if (_loading) return;
            LanguageChoice selected = _languageComboBox.SelectedItem as LanguageChoice;
            if (selected == null
                || string.Equals(selected.Value, _original.Language, StringComparison.Ordinal))
                return;

            DialogResult answer = MessageBox.Show(
                this,
                AppText.Format("Main.Language.ChangePrompt", selected.Label),
                AppText.Get("Settings.Language.Label"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                SelectLanguage(_original.Language);
                return;
            }
            SaveAndClose(true);
        }

        private void SaveAndClose(bool restartRequested)
        {
            LanguageChoice language = _languageComboBox.SelectedItem as LanguageChoice;
            if (language == null) return;
            // Retain preferences from earlier test builds, including dormant shortcut values.
            // This dialog changes only the language while Operation Notes are rolled back.
            AppPreferences selected = _original.Clone();
            selected.SchemaVersion = AppPreferencesStore.CurrentSchemaVersion;
            selected.Language = language.Value;
            if (_applyPreferences != null)
            {
                string error = _applyPreferences(selected.Clone());
                if (!string.IsNullOrWhiteSpace(error))
                {
                    ShowStatus(error, ApplicationSettingsTheme.Danger);
                    return;
                }
            }
            SelectedPreferences = selected;
            RestartRequested = restartRequested;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SelectLanguage(string value)
        {
            for (int index = 0; index < _languageComboBox.Items.Count; index++)
            {
                LanguageChoice choice = _languageComboBox.Items[index] as LanguageChoice;
                if (choice == null
                    || !string.Equals(choice.Value, value, StringComparison.Ordinal))
                    continue;
                _languageComboBox.SelectedIndex = index;
                return;
            }
            _languageComboBox.SelectedIndex = 0;
        }

        private void ShowStatus(string text, Color color)
        {
            _statusLabel.Text = text ?? string.Empty;
            _statusLabel.ForeColor = color;
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                ForeColor = ApplicationSettingsTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

    }
}
