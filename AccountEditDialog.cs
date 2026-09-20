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

        public AccountEditDialog(AccountRecord account)
        {
            Text = "编辑账号";
            Width = 500;
            Height = 430;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(244, 247, 251);

            _username.Text = account.Username;
            _email.Text = account.Email;
            _gameId.Text = account.GameId;
            _note.Text = account.Note;

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 24, 28, 22), ColumnCount = 2, RowCount = 7, BackColor = Color.White };
            table.Paint += (_, e) => { using (var pen = new Pen(Color.FromArgb(224, 230, 239))) e.Graphics.DrawRectangle(pen, 0, 0, table.Width - 1, table.Height - 1); };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(table, 0, "Steam 账号", _username);
            AddRow(table, 1, "Steam 密码", _password);
            AddRow(table, 2, "邮箱", _email);
            AddRow(table, 3, "邮箱密码", _emailPassword);
            AddRow(table, 4, "PUBG 查询ID", _gameId);
            AddRow(table, 5, "备注", _note);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "保存", DialogResult = DialogResult.OK, Width = 94, Height = 36, BackColor = Color.FromArgb(67, 97, 238), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 94, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(19, 28, 46), Cursor = Cursors.Hand };
            save.FlatAppearance.BorderColor = Color.FromArgb(67, 97, 238);
            save.FlatAppearance.MouseOverBackColor = Color.FromArgb(56, 82, 210);
            cancel.FlatAppearance.BorderColor = Color.FromArgb(224, 230, 239);
            cancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 248, 252);
            save.Click += (_, e) =>
            {
                if (Username.Length == 0) { MessageBox.Show("Steam 账号不能为空。"); DialogResult = DialogResult.None; }
            };
            actions.Controls.Add(save); actions.Controls.Add(cancel);
            table.Controls.Add(actions, 1, 6);
            Controls.Add(table);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control input)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0, 7, 0, 7);
            if (input is TextBox textBox)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = Color.White;
            }
            table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(table.Font, FontStyle.Bold), ForeColor = Color.FromArgb(19, 28, 46) }, 0, row);
            table.Controls.Add(input, 1, row);
        }
    }
}
