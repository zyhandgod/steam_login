using System;
using System.Drawing;
using System.Windows.Forms;
using SteamLoginLite.Models;

namespace SteamLoginLite
{
    internal sealed class AccountEditDialog : Form
    {
        private readonly TextBox _username = new TextBox();
        private readonly TextBox _password = new TextBox { UseSystemPasswordChar = true };
        private readonly TextBox _email = new TextBox();
        private readonly TextBox _emailPassword = new TextBox { UseSystemPasswordChar = true };
        private readonly TextBox _gameId = new TextBox();
        private readonly TextBox _note = new TextBox();
        public string Username => _username.Text.Trim();
        public string Password => _password.Text;
        public string Email => _email.Text.Trim().Replace("\\@", "@");
        public string EmailPassword => _emailPassword.Text;
        public string GameId => _gameId.Text.Trim();
        public string Note => _note.Text.Trim();
        public AccountEditDialog(AccountRecord account, bool isNew = false, Func<string, bool> accountExists = null)
        {
            Text = isNew ? "添加账号" : "编辑账号";
            ClientSize = new Size(465, 365); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Microsoft YaHei UI", 9F);
            _username.Text = account.Username; _email.Text = account.Email; _gameId.Text = account.GameId; _note.Text = account.Note;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(layout, 0, "Steam 账号", _username); Add(layout, 1, "Steam 密码", _password);
            Add(layout, 2, "邮箱", _email); Add(layout, 3, "邮箱密码", _emailPassword);
            Add(layout, 4, "游戏昵称 / 查询标识", _gameId); Add(layout, 5, "备注", _note);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var hint = new Label { Text = isNew ? "密码使用当前 Windows 用户加密保存。" : "密码留空保留原值；本机账号可使用 Steam 已记住的登录状态。", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, AutoSize = false };
            layout.Controls.Add(hint, 0, 6); layout.SetColumnSpan(hint, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            save.Click += (_, __) => {
                if (Username.Length == 0 || (isNew && Password.Length == 0 && !(accountExists?.Invoke(Username) ?? false)))
                { MessageBox.Show(this, Username.Length == 0 ? "请输入 Steam 账号。" : "请输入密码；本机已有的账号可先刷新自动同步。", "提示"); DialogResult = DialogResult.None; }
            };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        }
        private static void Add(TableLayoutPanel layout, int row, string label, TextBox input)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            input.Dock = DockStyle.Fill; input.Margin = new Padding(0, 6, 0, 4); layout.Controls.Add(input, 1, row);
        }
    }
}
