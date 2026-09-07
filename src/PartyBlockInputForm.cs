// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal enum PartyBlockInputPreviewStatus
    {
        NewBlock,
        AlreadyBlocked,
        Duplicate,
        Excluded
    }

    internal sealed class PartyBlockInputPreviewItem
    {
        internal string OriginalText { get; set; }
        internal string IpAddress { get; set; }
        internal PartyBlockInputPreviewStatus Status { get; set; }
        internal string Detail { get; set; }
        internal FirewallQueryResult InitialState { get; set; }
    }

    internal sealed class PartyBlockInputRequest
    {
        internal PartyBlockInputRequest()
        {
            Addresses = new List<string>();
            InitialStates = new Dictionary<string, FirewallQueryResult>(
                StringComparer.OrdinalIgnoreCase);
        }

        internal string SourceName { get; set; }
        internal string ReasonCode { get; set; }
        internal IList<string> Addresses { get; set; }
        internal IDictionary<string, FirewallQueryResult> InitialStates { get; set; }
    }

    internal sealed class PartyBlockInputForm : BrandedForm
    {
        private sealed class ReasonOption
        {
            internal string Code { get; set; }
            internal string Text { get; set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private static readonly Color Background = Color.FromArgb(22, 27, 33);
        private static readonly Color Surface = Color.FromArgb(30, 37, 45);
        private static readonly Color SurfaceAlt = Color.FromArgb(37, 45, 55);
        private static readonly Color Border = Color.FromArgb(57, 68, 80);
        private static readonly Color TextPrimary = Color.FromArgb(232, 235, 238);
        private static readonly Color TextMuted = Color.FromArgb(158, 169, 180);
        private static readonly Color Success = Color.FromArgb(68, 184, 121);
        private static readonly Color Danger = Color.FromArgb(224, 91, 91);
        private static readonly Color Warning = Color.FromArgb(231, 184, 73);

        private readonly TextBox _sourceTextBox;
        private readonly ComboBox _reasonComboBox;
        private readonly TextBox _inputTextBox;
        private readonly DataGridView _grid;
        private readonly Label _summaryLabel;
        private readonly Button _previewButton;
        private readonly Button _applyButton;
        private readonly Button _helpButton;
        private IList<PartyBlockInputPreviewItem> _previewItems =
            new List<PartyBlockInputPreviewItem>();
        private string _previewedInput;
        private bool _busy;

        internal PartyBlockInputForm()
        {
            Text = AppText.Get("파티 IP 추가");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(860, 650);
            MinimumSize = new Size(700, 540);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowIcon = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 6
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            _helpButton = MainForm.CreateUsageGuideButton();
            _helpButton.Name = "PartyUsageGuideButton";
            _helpButton.AccessibleName = AppText.Get("PartyHelp.Title");
            _helpButton.Click += delegate
            {
                using (var help = new PartyBlockHelpForm()) help.ShowDialog(this);
            };
            header.Controls.Add(_helpButton);
            EventHandler positionHelp = delegate
            {
                _helpButton.Width = Math.Max(_helpButton.Width, _helpButton.GetPreferredSize(Size.Empty).Width);
                _helpButton.Location = new Point(Math.Max(0, header.ClientSize.Width - _helpButton.Width), 0);
            };
            header.Resize += positionHelp;
            header.HandleCreated += positionHelp;
            positionHelp(null, EventArgs.Empty);
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(0, 0),
                Text = AppText.Get("파티 IP 직접 입력"),
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(2, 37),
                Text = AppText.Get("P2P·자동 동기화 없이 받은 공인 IPv4를 이 PC에만 적용합니다."),
                ForeColor = TextMuted
            });
            root.Controls.Add(header, 0, 0);

            var metadata = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 2, 0, 8)
            };
            metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                AppText.CurrentLanguage == AppText.EnglishLanguage ? 82F : 72F));
            metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                AppText.CurrentLanguage == AppText.EnglishLanguage ? 78F : 58F));
            metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            metadata.Controls.Add(CreateFieldLabel(AppText.Get("출처명")), 0, 0);
            _sourceTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 7, 12, 7),
                BackColor = Surface,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = PartyBlockBundleStore.MaximumSourceNameLength,
                AccessibleName = AppText.Get("파티 IP 출처명")
            };
            metadata.Controls.Add(_sourceTextBox, 1, 0);
            metadata.Controls.Add(CreateFieldLabel(AppText.Get("PartyInput.SharedBy")), 2, 0);
            _reasonComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 7, 0, 7),
                BackColor = Surface,
                ForeColor = TextPrimary,
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = AppText.Get("파티 IP 차단 사유")
            };
            _reasonComboBox.Items.Add(new ReasonOption
            {
                Code = "party-leader",
                Text = AppText.Get("파티장 공유")
            });
            _reasonComboBox.Items.Add(new ReasonOption
            {
                Code = "party-member",
                Text = AppText.Get("파티원 공유")
            });
            _reasonComboBox.Items.Add(new ReasonOption
            {
                Code = "manual-confirmed",
                Text = AppText.Get("직접 확인")
            });
            _reasonComboBox.Items.Add(new ReasonOption
            {
                Code = "other",
                Text = AppText.Get("기타")
            });
            _reasonComboBox.SelectedIndex = 0;
            metadata.Controls.Add(_reasonComboBox, 3, 0);
            root.Controls.Add(metadata, 0, 1);

            var inputHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0)
            };
            inputHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inputHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118F));
            // The preview button needs its full 32px height plus bottom spacing
            // in both languages; a 24px label row clipped the Korean button.
            inputHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            inputHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            inputHost.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("서버 IP 붙여넣기 · 공백, 쉼표, 세미콜론, 줄바꿈으로 구분"),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            _inputTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = false,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Surface,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5F),
                MaxLength = PartyBlockInputParser.MaximumInputLength + 1,
                AccessibleName = AppText.Get("파티 서버 IP 입력")
            };
            inputHost.Controls.Add(_inputTextBox, 0, 1);
            inputHost.SetColumnSpan(_inputTextBox, 2);
            _previewButton = CreateButton(AppText.Get("IP 미리보기"), false, 110);
            _previewButton.Dock = DockStyle.Fill;
            _previewButton.Margin = new Padding(8, 0, 0, 4);
            inputHost.Controls.Add(_previewButton, 1, 0);
            root.Controls.Add(inputHost, 0, 2);

            _grid = BuildGrid();
            root.Controls.Add(_grid, 0, 3);

            _summaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("IP를 입력한 뒤 미리보기로 새 차단·기존 차단·제외 항목을 확인하세요."),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AccessibleName = AppText.Get("파티 IP 미리보기 요약")
            };
            root.Controls.Add(_summaryLabel, 0, 4);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = Background
            };
            var cancelButton = CreateButton(AppText.Get("취소"), false, 82);
            cancelButton.DialogResult = DialogResult.Cancel;
            _applyButton = CreateButton(AppText.Get("한 번에 적용"), true, 118);
            _applyButton.Enabled = false;
            actions.Controls.Add(cancelButton);
            actions.Controls.Add(_applyButton);
            actions.Controls.Add(new Label
            {
                AutoSize = false,
                Size = new Size(360, 32),
                Text = AppText.Get("새 IP는 한 번의 관리자 권한 요청으로 함께 차단합니다."),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted
            });
            root.Controls.Add(actions, 0, 5);
            CancelButton = cancelButton;

            _previewButton.Click += async delegate { await RefreshPreviewAsync(); };
            _applyButton.Click += ApplyClicked;
            _inputTextBox.TextChanged += delegate { InvalidatePreview(); };
            _sourceTextBox.TextChanged += delegate { UpdateApplyButton(); };
            _reasonComboBox.SelectedIndexChanged += delegate { UpdateApplyButton(); };
        }

        internal PartyBlockInputRequest Request { get; private set; }

        private DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ColumnHeadersHeight = 36,
                RowHeadersVisible = false,
                RowTemplate = { Height = 32 },
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                ReadOnly = true,
                AccessibleName = AppText.Get("파티 IP 적용 미리보기")
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                SelectionBackColor = SurfaceAlt,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = TextPrimary,
                SelectionBackColor = Color.FromArgb(48, 59, 70),
                SelectionForeColor = TextPrimary,
                Padding = new Padding(5, 0, 5, 0)
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(33, 40, 49);
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "status",
                HeaderText = AppText.Get("구분"),
                Width = 112
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ip",
                HeaderText = AppText.Get("서버 IP"),
                Width = 145
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "input",
                HeaderText = AppText.Get("입력값"),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 38F
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "detail",
                HeaderText = AppText.Get("확인 결과"),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 62F
            });
            return grid;
        }

        private async Task RefreshPreviewAsync()
        {
            if (_busy) return;
            _busy = true;
            SetInteractive(false);
            _summaryLabel.ForeColor = TextMuted;
            _summaryLabel.Text = AppText.Get("공인 IPv4 형식과 현재 방화벽 상태를 확인하는 중…");

            string raw = _inputTextBox.Text;
            PartyBlockInputParseResult parsed = await Task.Run(
                () => PartyBlockInputParser.Parse(raw));
            Dictionary<string, FirewallQueryResult> states = parsed.UniqueAddresses.Count == 0
                ? new Dictionary<string, FirewallQueryResult>(StringComparer.OrdinalIgnoreCase)
                : await Task.Run(() => FirewallRuleManager.QueryMany(parsed.UniqueAddresses));
            if (IsDisposed) return;

            var preview = new List<PartyBlockInputPreviewItem>();
            foreach (PartyBlockInputToken token in parsed.Items)
            {
                var item = new PartyBlockInputPreviewItem
                {
                    OriginalText = token.OriginalText,
                    IpAddress = token.IpAddress
                };
                if (token.Status == PartyBlockInputTokenStatus.Duplicate)
                {
                    item.Status = PartyBlockInputPreviewStatus.Duplicate;
                    item.Detail = AppText.Get("같은 입력에서 먼저 확인된 IP와 중복되어 적용하지 않습니다.");
                }
                else if (token.Status == PartyBlockInputTokenStatus.InvalidPublicIpv4)
                {
                    item.Status = PartyBlockInputPreviewStatus.Excluded;
                    item.Detail = AppText.Get("정확한 공인 IPv4가 아닙니다. 사설·예약·특수 주소도 제외합니다.");
                }
                else if (token.Status == PartyBlockInputTokenStatus.AddressLimitExceeded)
                {
                    item.Status = PartyBlockInputPreviewStatus.Excluded;
                    item.Detail = AppText.Format(
                        "한 번에 {0}개까지 적용할 수 있어 제외했습니다.",
                        PartyBlockInputParser.MaximumAddressCount);
                }
                else if (token.Status == PartyBlockInputTokenStatus.InputTooLong)
                {
                    item.Status = PartyBlockInputPreviewStatus.Excluded;
                    item.Detail = AppText.Get("붙여넣기 입력 길이 한도를 초과했습니다.");
                }
                else
                {
                    FirewallQueryResult state;
                    states.TryGetValue(token.IpAddress, out state);
                    item.InitialState = state;
                    if (state == null || !state.Success)
                    {
                        item.Status = PartyBlockInputPreviewStatus.Excluded;
                        item.Detail = state == null || string.IsNullOrWhiteSpace(state.ErrorMessage)
                            ? AppText.Get("현재 방화벽 상태를 확인하지 못해 적용에서 제외합니다.")
                            : AppText.TranslateDiagnostic(state.ErrorMessage,
                                "현재 방화벽 상태를 확인하지 못해 적용에서 제외합니다.");
                    }
                    else if (state.IsBlocked)
                    {
                        item.Status = PartyBlockInputPreviewStatus.AlreadyBlocked;
                        item.Detail = AppText.Get("이미 앱 관리 차단 규칙이 있어 묶음 참조만 추가합니다.");
                    }
                    else
                    {
                        item.Status = PartyBlockInputPreviewStatus.NewBlock;
                        item.Detail = AppText.Get("적용하면 새 앱 관리 차단 규칙을 만듭니다.");
                    }
                }
                preview.Add(item);
            }

            _previewItems = preview;
            _previewedInput = raw;
            PopulateRows();
            _busy = false;
            SetInteractive(true);
            UpdateApplyButton();
        }

        private void PopulateRows()
        {
            _grid.Rows.Clear();
            foreach (PartyBlockInputPreviewItem item in _previewItems)
            {
                int index = _grid.Rows.Add(
                    GetStatusText(item.Status),
                    item.IpAddress ?? AppText.Get("-"),
                    item.OriginalText ?? string.Empty,
                    item.Detail ?? string.Empty);
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = item;
                row.Cells["status"].Style.ForeColor =
                    item.Status == PartyBlockInputPreviewStatus.NewBlock
                        ? Success
                        : item.Status == PartyBlockInputPreviewStatus.AlreadyBlocked
                            ? Warning
                            : item.Status == PartyBlockInputPreviewStatus.Duplicate
                                ? TextMuted
                                : Danger;
            }

            int newCount = _previewItems.Count(
                item => item.Status == PartyBlockInputPreviewStatus.NewBlock);
            int existingCount = _previewItems.Count(
                item => item.Status == PartyBlockInputPreviewStatus.AlreadyBlocked);
            int duplicateCount = _previewItems.Count(
                item => item.Status == PartyBlockInputPreviewStatus.Duplicate);
            int excludedCount = _previewItems.Count(
                item => item.Status == PartyBlockInputPreviewStatus.Excluded);
            _summaryLabel.ForeColor = excludedCount == 0 ? TextMuted : Warning;
            _summaryLabel.Text = AppText.Format(
                "새로 차단 {0}개 · 이미 차단됨 {1}개 · 중복 {2}개 · 적용 제외 {3}개",
                newCount,
                existingCount,
                duplicateCount,
                excludedCount);
        }

        private void ApplyClicked(object sender, EventArgs e)
        {
            if (_busy || !string.Equals(_previewedInput, _inputTextBox.Text, StringComparison.Ordinal))
            {
                _summaryLabel.ForeColor = Warning;
                _summaryLabel.Text = AppText.Get("변경된 IP 입력을 다시 미리보기로 확인해 주세요.");
                return;
            }
            string source = _sourceTextBox.Text == null ? null : _sourceTextBox.Text.Trim();
            ReasonOption reason = _reasonComboBox.SelectedItem as ReasonOption;
            IList<PartyBlockInputPreviewItem> eligible = _previewItems.Where(
                item => item.Status == PartyBlockInputPreviewStatus.NewBlock
                    || item.Status == PartyBlockInputPreviewStatus.AlreadyBlocked).ToList();
            if (string.IsNullOrWhiteSpace(source))
            {
                _summaryLabel.ForeColor = Warning;
                _summaryLabel.Text = AppText.Get("파티 IP의 출처명을 입력해 주세요.");
                _sourceTextBox.Focus();
                return;
            }
            if (reason == null || eligible.Count == 0) return;

            Request = new PartyBlockInputRequest
            {
                SourceName = source,
                ReasonCode = reason.Code,
                Addresses = eligible.Select(item => item.IpAddress)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                InitialStates = eligible.ToDictionary(
                    item => item.IpAddress,
                    item => item.InitialState,
                    StringComparer.OrdinalIgnoreCase)
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void InvalidatePreview()
        {
            if (_previewedInput == null) return;
            _previewedInput = null;
            _previewItems = new List<PartyBlockInputPreviewItem>();
            _grid.Rows.Clear();
            _summaryLabel.ForeColor = TextMuted;
            _summaryLabel.Text = AppText.Get("입력이 변경되었습니다. 다시 미리보기로 확인해 주세요.");
            UpdateApplyButton();
        }

        private void UpdateApplyButton()
        {
            bool hasEligible = !_busy
                && string.Equals(_previewedInput, _inputTextBox.Text, StringComparison.Ordinal)
                && _previewItems.Any(item => item.Status == PartyBlockInputPreviewStatus.NewBlock
                    || item.Status == PartyBlockInputPreviewStatus.AlreadyBlocked)
                && !string.IsNullOrWhiteSpace(_sourceTextBox.Text)
                && _reasonComboBox.SelectedItem is ReasonOption;
            _applyButton.Enabled = hasEligible;
            _applyButton.BackColor = hasEligible ? Success : SurfaceAlt;
            _applyButton.ForeColor = hasEligible ? Color.FromArgb(18, 36, 27) : TextMuted;
        }

        private void SetInteractive(bool enabled)
        {
            _sourceTextBox.Enabled = enabled;
            _reasonComboBox.Enabled = enabled;
            _inputTextBox.Enabled = enabled;
            _previewButton.Enabled = enabled;
            _grid.Enabled = enabled;
            UseWaitCursor = !enabled;
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static string GetStatusText(PartyBlockInputPreviewStatus status)
        {
            if (status == PartyBlockInputPreviewStatus.NewBlock)
                return AppText.Get("새로 차단");
            if (status == PartyBlockInputPreviewStatus.AlreadyBlocked)
                return AppText.Get("이미 차단됨");
            if (status == PartyBlockInputPreviewStatus.Duplicate)
                return AppText.Get("중복 제외");
            return AppText.Get("적용 제외");
        }

        private static Button CreateButton(string text, bool emphasized, int width)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 32),
                Margin = new Padding(8, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = emphasized ? Success : SurfaceAlt,
                ForeColor = emphasized ? Color.FromArgb(18, 36, 27) : TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = emphasized
                ? Color.FromArgb(42, 137, 87)
                : Border;
            button.FlatAppearance.MouseOverBackColor = emphasized
                ? Color.FromArgb(82, 201, 139)
                : Color.FromArgb(45, 54, 64);
            return button;
        }
    }
}
