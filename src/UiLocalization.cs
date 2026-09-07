// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    /// <summary>
    /// Compatibility localization pass for WinForms that still construct static
    /// Korean UI text in code. New UI should prefer AppText.Get/Format directly.
    /// </summary>
    public static class UiLocalization
    {
        private const string EnglishUiFont = "Segoe UI";

        public static void Apply(Control root)
        {
            if (root == null) throw new ArgumentNullException("root");
            var observedToolStrips = new HashSet<ToolStrip>();
            ApplyControl(root, observedToolStrips);
        }

        private static void ApplyControl(Control control, ISet<ToolStrip> observedToolStrips)
        {
            if (control == null || control.IsDisposed) return;

            ApplyAccessibleText(control);
            ApplyControlText(control);
            ApplyEnglishControlFont(control);

            var comboBox = control as ComboBox;
            if (comboBox != null) ApplyComboBoxItems(comboBox);

            var grid = control as DataGridView;
            if (grid != null) ApplyDataGridView(grid);

            var toolStrip = control as ToolStrip;
            if (toolStrip != null) ApplyToolStrip(toolStrip, observedToolStrips);

            if (control.ContextMenuStrip != null)
                ApplyToolStrip(control.ContextMenuStrip, observedToolStrips);

            foreach (Control child in control.Controls)
                ApplyControl(child, observedToolStrips);
        }

        private static void ApplyControlText(Control control)
        {
            // TextBoxBase includes TextBox, RichTextBox, and custom memo/path editors.
            // Their text is user or document data, never UI chrome.
            if (control is TextBoxBase) return;

            // ComboBox text can be free-form input. Only its explicit string items
            // are safe to localize, and those are handled separately.
            if (control is ComboBox) return;

            // Grid data is handled at the column/static-cell level below. Never run
            // a blanket translation over row values.
            if (control is DataGridView) return;

            control.Text = AppText.TranslateLiteral(control.Text);
        }

        private static void ApplyAccessibleText(Control control)
        {
            control.AccessibleName = AppText.TranslateLiteral(control.AccessibleName);
            control.AccessibleDescription =
                AppText.TranslateLiteral(control.AccessibleDescription);
        }

        private static void ApplyComboBoxItems(ComboBox comboBox)
        {
            // Data-bound entries are domain data and must be presented by their
            // binding/presentation layer, not mutated in place here.
            if (comboBox.DataSource != null) return;

            int selectedIndex = comboBox.SelectedIndex;
            string freeFormText = selectedIndex < 0 ? comboBox.Text : null;
            if (selectedIndex >= 0) comboBox.SelectedIndex = -1;
            comboBox.BeginUpdate();
            try
            {
                for (int index = 0; index < comboBox.Items.Count; index++)
                {
                    string item = comboBox.Items[index] as string;
                    if (item == null) continue;
                    comboBox.Items[index] = AppText.TranslateLiteral(item);
                }
            }
            finally
            {
                comboBox.EndUpdate();
            }

            if (selectedIndex >= 0 && selectedIndex < comboBox.Items.Count)
                comboBox.SelectedIndex = selectedIndex;
            else if (comboBox.DropDownStyle != ComboBoxStyle.DropDownList)
                comboBox.Text = freeFormText ?? string.Empty;
        }

        private static void ApplyDataGridView(DataGridView grid)
        {
            ApplyCellStyleFont(grid.DefaultCellStyle);
            ApplyCellStyleFont(grid.AlternatingRowsDefaultCellStyle);
            ApplyCellStyleFont(grid.RowsDefaultCellStyle);
            ApplyCellStyleFont(grid.ColumnHeadersDefaultCellStyle);
            ApplyCellStyleFont(grid.RowHeadersDefaultCellStyle);

            TranslateCellStyleNullValue(grid.DefaultCellStyle);
            TranslateCellStyleNullValue(grid.AlternatingRowsDefaultCellStyle);
            TranslateCellStyleNullValue(grid.RowsDefaultCellStyle);

            if (grid.TopLeftHeaderCell != null)
            {
                string topLeft = grid.TopLeftHeaderCell.Value as string;
                if (topLeft != null)
                    grid.TopLeftHeaderCell.Value = AppText.TranslateLiteral(topLeft);
                grid.TopLeftHeaderCell.ToolTipText =
                    AppText.TranslateLiteral(grid.TopLeftHeaderCell.ToolTipText);
            }

            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderText = AppText.TranslateLiteral(column.HeaderText);
                column.ToolTipText = AppText.TranslateLiteral(column.ToolTipText);
                if (column.HeaderCell != null)
                {
                    column.HeaderCell.ToolTipText =
                        AppText.TranslateLiteral(column.HeaderCell.ToolTipText);
                }
                ApplyCellStyleFont(column.DefaultCellStyle);
                TranslateCellStyleNullValue(column.DefaultCellStyle);

                var buttonColumn = column as DataGridViewButtonColumn;
                if (buttonColumn != null)
                    buttonColumn.Text = AppText.TranslateLiteral(buttonColumn.Text);
            }

            // Text/link/check cells can contain IPs, paths, map names, memo text, or
            // other domain data. A button cell is the only row cell whose value is
            // unambiguously static UI action text.
            foreach (DataGridViewRow row in grid.Rows)
            {
                foreach (DataGridViewCell cell in row.Cells)
                {
                    var buttonCell = cell as DataGridViewButtonCell;
                    if (buttonCell == null) continue;
                    string value = buttonCell.Value as string;
                    if (value != null)
                        buttonCell.Value = AppText.TranslateLiteral(value);
                    buttonCell.ToolTipText =
                        AppText.TranslateLiteral(buttonCell.ToolTipText);
                    ApplyCellStyleFont(buttonCell.Style);
                }
            }
        }

        private static void TranslateCellStyleNullValue(DataGridViewCellStyle style)
        {
            if (style == null) return;
            string nullValue = style.NullValue as string;
            if (nullValue != null)
                style.NullValue = AppText.TranslateLiteral(nullValue);
        }

        private static void ApplyToolStrip(
            ToolStrip toolStrip,
            ISet<ToolStrip> observedToolStrips)
        {
            if (toolStrip == null || !observedToolStrips.Add(toolStrip)) return;

            toolStrip.AccessibleName = AppText.TranslateLiteral(toolStrip.AccessibleName);
            toolStrip.AccessibleDescription =
                AppText.TranslateLiteral(toolStrip.AccessibleDescription);
            ApplyEnglishToolStripFont(toolStrip);

            foreach (ToolStripItem item in toolStrip.Items)
                ApplyToolStripItem(item, observedToolStrips);
        }

        private static void ApplyToolStripItem(
            ToolStripItem item,
            ISet<ToolStrip> observedToolStrips)
        {
            if (item == null) return;

            // ToolStrip text boxes can contain paths and user input. ToolStrip combo
            // boxes preserve free-form text and translate only explicit string items.
            if (!(item is ToolStripTextBox) && !(item is ToolStripComboBox))
                item.Text = AppText.TranslateLiteral(item.Text);
            item.ToolTipText = AppText.TranslateLiteral(item.ToolTipText);
            item.AccessibleName = AppText.TranslateLiteral(item.AccessibleName);
            item.AccessibleDescription =
                AppText.TranslateLiteral(item.AccessibleDescription);
            ApplyEnglishToolStripItemFont(item);

            var comboBox = item as ToolStripComboBox;
            if (comboBox != null) ApplyToolStripComboBoxItems(comboBox);

            var host = item as ToolStripControlHost;
            if (host != null && host.Control != null && !(item is ToolStripComboBox))
                ApplyControl(host.Control, observedToolStrips);

            var dropDownItem = item as ToolStripDropDownItem;
            if (dropDownItem != null)
            {
                foreach (ToolStripItem child in dropDownItem.DropDownItems)
                    ApplyToolStripItem(child, observedToolStrips);
            }
        }

        private static void ApplyToolStripComboBoxItems(ToolStripComboBox toolStripComboBox)
        {
            ComboBox comboBox = toolStripComboBox.ComboBox;
            if (comboBox != null) ApplyComboBoxItems(comboBox);
        }

        private static void ApplyEnglishControlFont(Control control)
        {
            if (!IsEnglishSelected() || control.Font == null) return;

            bool mustChange = control is Form || IsKoreanUiFont(control.Font);
            if (!mustChange || IsEnglishUiFont(control.Font)) return;
            TrySetControlFont(control, CreateEnglishFont(control.Font));
        }

        private static void ApplyEnglishToolStripFont(ToolStrip toolStrip)
        {
            if (!IsEnglishSelected() || toolStrip.Font == null
                || !IsKoreanUiFont(toolStrip.Font)) return;
            Font font = CreateEnglishFont(toolStrip.Font);
            if (font != null) toolStrip.Font = font;
        }

        private static void ApplyEnglishToolStripItemFont(ToolStripItem item)
        {
            if (!IsEnglishSelected() || item.Font == null
                || !IsKoreanUiFont(item.Font)) return;
            Font font = CreateEnglishFont(item.Font);
            if (font != null) item.Font = font;
        }

        private static void ApplyCellStyleFont(DataGridViewCellStyle style)
        {
            if (style == null || !IsEnglishSelected() || style.Font == null
                || !IsKoreanUiFont(style.Font)) return;
            Font font = CreateEnglishFont(style.Font);
            if (font != null) style.Font = font;
        }

        private static void TrySetControlFont(Control control, Font font)
        {
            if (font == null) return;
            try
            {
                control.Font = font;
            }
            catch
            {
                font.Dispose();
            }
        }

        private static Font CreateEnglishFont(Font source)
        {
            try
            {
                return new Font(
                    EnglishUiFont,
                    source.Size,
                    source.Style,
                    source.Unit,
                    source.GdiCharSet,
                    source.GdiVerticalFont);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsEnglishSelected()
        {
            return string.Equals(
                AppText.CurrentLanguage,
                AppText.EnglishLanguage,
                StringComparison.Ordinal);
        }

        private static bool IsEnglishUiFont(Font font)
        {
            return font != null
                && string.Equals(font.Name, EnglishUiFont, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKoreanUiFont(Font font)
        {
            if (font == null) return false;
            return string.Equals(font.Name, "Malgun Gothic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(font.Name, "맑은 고딕", StringComparison.OrdinalIgnoreCase);
        }
    }
}
