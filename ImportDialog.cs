using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SteamLoginLite.Services;

namespace SteamLoginLite
{
    internal sealed class ImportDialog : Form
    {
        private readonly TextBox _text = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, MaxLength = 1024 * 1024 };
        private readonly DataGridView _preview = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = SystemColors.Window };
        private readonly Button _ok = new Button { Text = "导入账号", AutoSize = true, Enabled = false };
        private readonly Label _summary = new Label { AutoSize = true };
        public List<ImportedAccount> Accounts { get; private set; } = new List<ImportedAccount>();
        public ImportDialog()
        {
            Text = "批量导入"; ClientSize = new Size(720, 560); MinimumSize = new Size(600, 450); StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { AutoSize = true, Text = "每行一个账号，用一个或多个连续的 - 分隔。\r\n账号----密码 或 账号----密码----游戏昵称\r\n兼容旧格式：账号----密码----邮箱----邮箱密码[----游戏昵称]\r\n字段含 - 时可用中文标签格式（见使用说明）。\r\n同名本机账号只补充缺失信息，不覆盖已有内容。" }, 0, 0);
            root.Controls.Add(_text, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var parse = new Button { Text = "解析预览", AutoSize = true }; actions.Controls.Add(parse); actions.Controls.Add(_summary); root.Controls.Add(actions, 0, 2);
            foreach (var column in new[] { new[] { "LineNumber", "行号" }, new[] { "Username", "Steam 账号" }, new[] { "Email", "邮箱" }, new[] { "GameId", "游戏昵称" }, new[] { "Error", "校验结果" } })
                _preview.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = column[0], HeaderText = column[1], AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            root.Controls.Add(_preview, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true }; buttons.Controls.Add(_ok); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 4);
            parse.Click += (_, __) => Parse();
            _text.TextChanged += (_, __) => { _ok.Enabled = false; Accounts.Clear(); _summary.Text = "内容已变更，请重新解析。"; };
            _ok.Click += (_, __) => { if (Accounts.Count > 0 && Accounts.All(a => a.IsValid)) DialogResult = DialogResult.OK; };
            CancelButton = cancel; Controls.Add(root);
        }
        private void Parse()
        {
            Accounts = ImportParser.Parse(_text.Text);
            _preview.DataSource = null; _preview.DataSource = Accounts;
            var invalid = Accounts.Count(a => !a.IsValid);
            _summary.Text = "共 " + Accounts.Count + " 行，错误 " + invalid + " 行。" + (invalid > 0 ? "请修正后再导入。" : "可以导入。");
            _ok.Enabled = Accounts.Count > 0 && invalid == 0;
        }
    }
}
