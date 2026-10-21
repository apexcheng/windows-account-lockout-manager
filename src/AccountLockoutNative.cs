using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new VerificationForm());
    }
}

internal static class PolicyValues
{
    internal static Dictionary<string, int> Parse(string content)
    {
        Dictionary<string, int> values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in Regex.Split(content, "\\r?\\n"))
        {
            Match match = Regex.Match(line, @"^\s*([A-Za-z][A-Za-z0-9]*)\s*=\s*(-?\d+)\s*$");
            if (match.Success)
                values[match.Groups[1].Value] = Int32.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        return values;
    }
}

internal enum PolicyPreset
{
    Disabled,
    Original,
    Recommended
}

internal static class PolicyChanges
{
    internal static bool Matches(Dictionary<string, int> values, PolicyPreset preset)
    {
        if (values == null) return false;
        int threshold, duration, window, administrator;
        if (!values.TryGetValue("LockoutBadCount", out threshold)) return false;
        // With threshold 0, secedit omits the duration, reset window, and admin flag.
        // The threshold alone proves that account lockout is effectively disabled.
        if (preset == PolicyPreset.Disabled) return threshold == 0;
        if (!values.TryGetValue("AllowAdministratorLockout", out administrator)) return false;
        if (!values.TryGetValue("LockoutDuration", out duration) ||
            !values.TryGetValue("ResetLockoutCount", out window)) return false;
        int target = preset == PolicyPreset.Original ? 60 : 10;
        int count = preset == PolicyPreset.Original ? 5 : 10;
        return threshold == count && duration == target && window == target && administrator == 1;
    }

    internal static string Name(PolicyPreset preset)
    {
        if (preset == PolicyPreset.Disabled) return "取消账户锁定限制";
        if (preset == PolicyPreset.Original) return "原版策略 5/60/60";
        return "新策略 10/10/10";
    }

    private static void Run(string executable, string arguments, string step)
    {
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = Path.Combine(Environment.SystemDirectory, executable);
        info.Arguments = arguments;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using (Process process = Process.Start(info))
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(step + "失败（退出码 " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "）");
        }
    }

    private static void ConfigureAdministrator(int enabled)
    {
        string prefix = Path.Combine(Path.GetTempPath(), "account-lockout-change-" + Guid.NewGuid().ToString("N"));
        string inf = prefix + ".inf";
        string db = prefix + ".sdb";
        string log = prefix + ".log";
        try
        {
            File.WriteAllText(inf,
                "[Unicode]\r\nUnicode=yes\r\n[System Access]\r\nAllowAdministratorLockout = " +
                enabled.ToString(CultureInfo.InvariantCulture) +
                "\r\n[Version]\r\nsignature=\"$CHICAGO$\"\r\nRevision=1\r\n", Encoding.Unicode);
            Run("secedit.exe", "/configure /db \"" + db + "\" /cfg \"" + inf +
                "\" /areas SECURITYPOLICY /log \"" + log + "\" /quiet", "内置管理员账户锁定配置");
        }
        finally
        {
            foreach (string path in new string[] { inf, db, log, prefix + ".jfm" })
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static void Apply(PolicyPreset preset, Dictionary<string, int> current)
    {
        if (preset == PolicyPreset.Disabled)
        {
            Run("net.exe", "accounts /lockoutthreshold:0", "关闭账户锁定阈值");
            ConfigureAdministrator(0);
            return;
        }

        int currentWindow;
        int duration = preset == PolicyPreset.Original ? 60 : 10;
        int threshold = preset == PolicyPreset.Original ? 5 : 10;

        // Windows requires the reset window not to exceed the lockout duration.
        // Choose a safe order when changing from 60/60 to 10/10 or back.
        if (current == null || !current.TryGetValue("ResetLockoutCount", out currentWindow))
        {
            // With lockout disabled, secedit does not export its dormant settings.
            // One minute is a valid safe window before setting either duration.
            Run("net.exe", "accounts /lockoutwindow:1", "初始化错误计数窗口");
            Run("net.exe", "accounts /lockoutduration:" + duration, "修改锁定时长");
            Run("net.exe", "accounts /lockoutwindow:" + duration, "修改错误计数窗口");
        }
        else if (currentWindow > duration)
        {
            Run("net.exe", "accounts /lockoutwindow:" + duration, "修改错误计数窗口");
            Run("net.exe", "accounts /lockoutduration:" + duration, "修改锁定时长");
        }
        else
        {
            Run("net.exe", "accounts /lockoutduration:" + duration, "修改锁定时长");
            Run("net.exe", "accounts /lockoutwindow:" + duration, "修改错误计数窗口");
        }
        Run("net.exe", "accounts /lockoutthreshold:" + threshold, "修改锁定阈值");
        ConfigureAdministrator(1);
    }
}

internal sealed class VerificationForm : Form
{
    private readonly DataGridView grid;
    private readonly Label summary;
    private readonly Label source;
    private PolicyPreset selectedPreset = PolicyPreset.Original;
    private bool firstRead = true;

    internal DataGridView Grid { get { return grid; } }
    internal Label Summary { get { return summary; } }
    internal Label Source { get { return source; } }

    internal VerificationForm()
    {
        Text = "Windows 账户锁定策略管理";
        ClientSize = new Size(930, 568);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10);

        Label heading = new Label();
        heading.Text = "Windows 账户锁定策略管理";
        heading.Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold);
        heading.Location = new Point(24, 24);
        heading.AutoSize = true;
        Controls.Add(heading);

        Label intro = new Label();
        intro.Text = "查看当前策略，选择一种方案后确认执行；程序不会尝试登录密码。";
        intro.ForeColor = Color.DimGray;
        intro.Location = new Point(25, 67);
        intro.Size = new Size(740, 26);
        Controls.Add(intro);

        Button refresh = new Button();
        refresh.Text = "刷新状态";
        refresh.Location = new Point(794, 27);
        refresh.Size = new Size(106, 35);
        refresh.FlatStyle = FlatStyle.System;
        refresh.Click += delegate { RunVerification(); };
        Controls.Add(refresh);

        grid = new DataGridView();
        grid.Location = new Point(24, 106);
        grid.Size = new Size(880, 236);
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.ColumnHeadersHeight = 43;
        grid.RowTemplate.Height = 45;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        grid.Columns.Add("Item", "验收项目");
        grid.Columns.Add("Expected", "预期值");
        grid.Columns.Add("Actual", "实际读取值");
        grid.Columns.Add("Result", "验收结果");
        grid.Columns[0].FillWeight = 145;
        grid.Columns[1].FillWeight = 90;
        grid.Columns[2].FillWeight = 100;
        grid.Columns[3].FillWeight = 95;
        grid.Columns[3].DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        Controls.Add(grid);

        Button cancel = MakeButton("① 取消锁定限制", new Point(24, 358), new Size(280, 49));
        cancel.Click += delegate { ApplySelected(PolicyPreset.Disabled); };
        Controls.Add(cancel);

        Button original = MakeButton("② 恢复原版 5/60/60", new Point(324, 358), new Size(280, 49));
        original.Click += delegate { ApplySelected(PolicyPreset.Original); };
        Controls.Add(original);

        Button recommended = MakeButton("③ 设置 10/10/10", new Point(624, 358), new Size(280, 49));
        recommended.Click += delegate { ApplySelected(PolicyPreset.Recommended); };
        Controls.Add(recommended);

        summary = new Label();
        summary.Location = new Point(24, 423);
        summary.Size = new Size(882, 32);
        summary.Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
        Controls.Add(summary);

        source = new Label();
        source.Location = new Point(25, 467);
        source.Size = new Size(881, 24);
        source.ForeColor = Color.DimGray;
        Controls.Add(source);

        Label note = new Label();
        note.Text = "取消限制：阈值设为 0、关闭内置管理员锁定；其余数值不生效且 Windows 可能不导出。已锁定账户不保证立即解锁。";
        note.Location = new Point(25, 506);
        note.Size = new Size(885, 25);
        note.ForeColor = Color.DimGray;
        Controls.Add(note);

        Label domainNote = new Label();
        domainNote.Text = "原版和新方案均启用内置 Administrator 锁定；如果设备受域策略管理，可能覆盖本地设置。";
        domainNote.Location = new Point(25, 532);
        domainNote.Size = new Size(885, 25);
        domainNote.ForeColor = Color.DimGray;
        Controls.Add(domainNote);

        Shown += delegate { RunVerification(); };
    }

    private static Button MakeButton(string label, Point location, Size size)
    {
        Button button = new Button();
        button.Text = label;
        button.Location = location;
        button.Size = size;
        button.FlatStyle = FlatStyle.System;
        return button;
    }

    private void ApplySelected(PolicyPreset preset)
    {
        string detail = preset == PolicyPreset.Disabled
            ? "将锁定阈值设置为 0，关闭内置 Administrator 锁定。不会清除账户密码，也不保证立即解锁已锁定账户。"
            : (preset == PolicyPreset.Original
                ? "连续输错 5 次，锁定 60 分钟；60 分钟重置计数；内置 Administrator 也应用锁定。"
                : "连续输错 10 次，锁定 10 分钟；10 分钟重置计数；内置 Administrator 也应用锁定。");
        DialogResult decision = MessageBox.Show(this, "准备执行：" + PolicyChanges.Name(preset) +
            "\r\n\r\n" + detail + "\r\n\r\n确定要更改此电脑的账户锁定策略吗？",
            "确认策略修改", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (decision != DialogResult.Yes) return;

        selectedPreset = preset;
        try
        {
            Dictionary<string, int> before = ReadSecurityPolicy();
            PolicyChanges.Apply(preset, before);
            Dictionary<string, int> after = ReadSecurityPolicy();
            DisplayPolicy(after, "");
            if (PolicyChanges.Matches(after, preset))
                MessageBox.Show(this, PolicyChanges.Name(preset) + " 已应用，并通过本机策略回读验证。",
                    "设置成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, "设置命令已执行，但回读结果未完全符合目标。请查看红色或黄色验收项。\r\n部分 Windows 版本不支持内置管理员锁定设置。",
                    "设置未完全生效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            RunVerification();
            MessageBox.Show(this, "更改中断，可能已有部分设置生效。请查看最新状态。\r\n\r\n" + exception.Message,
                "设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Dictionary<string, int> ReadSecurityPolicy()
    {
        string prefix = Path.Combine(Path.GetTempPath(), "account-lockout-inspect-" +
            Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        string file = prefix + ".inf";
        string log = prefix + ".log";
        try
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = Path.Combine(Environment.SystemDirectory, "secedit.exe");
            start.Arguments = "/export /cfg \"" + file + "\" /areas SECURITYPOLICY /log \"" + log + "\" /quiet";
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(file))
                    throw new InvalidOperationException("secedit 安全策略导出失败");
            }
            return PolicyValues.Parse(File.ReadAllText(file, Encoding.Unicode));
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { File.Delete(log); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void RunVerification()
    {
        Dictionary<string, int> values = null;
        string errorText = "";
        try { values = ReadSecurityPolicy(); }
        catch (Exception exception) { errorText = exception.Message; }
        if (firstRead && values != null)
        {
            firstRead = false;
            if (PolicyChanges.Matches(values, PolicyPreset.Disabled)) selectedPreset = PolicyPreset.Disabled;
            else if (PolicyChanges.Matches(values, PolicyPreset.Recommended)) selectedPreset = PolicyPreset.Recommended;
        }
        DisplayPolicy(values, errorText);
    }

    internal void DisplayPolicy(Dictionary<string, int> values, string errorText)
    {
        grid.Rows.Clear();
        int passed = 0;
        int failed = 0;
        int unknown = 0;
        string[] names = { "密码错误锁定阈值", "账户锁定时长", "错误计数重置时间", "内置管理员账户锁定" };
        string[] keys = { "LockoutBadCount", "LockoutDuration", "ResetLockoutCount", "AllowAdministratorLockout" };
        bool disabled = selectedPreset == PolicyPreset.Disabled;
        int n = selectedPreset == PolicyPreset.Original ? 60 : 10;
        int[] expected = { disabled ? 0 : (selectedPreset == PolicyPreset.Original ? 5 : 10), n, n, disabled ? 0 : 1 };
        string[] expectedText = { disabled ? "0 次（禁用）" : expected[0] + " 次",
            disabled ? "不适用" : n + " 分钟", disabled ? "不适用" : n + " 分钟",
            disabled ? "随阈值停用" : "已启用" };
        for (int i = 0; i < keys.Length; i++)
        {
            string actual = "未读取到";
            string result = "无法确认";
            Color color = Color.DarkGoldenrod;
            int value;
            if (disabled && i > 0)
            {
                actual = "阈值为 0 时不生效";
                result = "不适用";
                color = Color.DimGray;
            }
            else if (values != null && values.TryGetValue(keys[i], out value))
            {
                if (keys[i] == "AllowAdministratorLockout")
                    actual = value == 1 ? "已启用" : "未启用（" + value.ToString(CultureInfo.InvariantCulture) + "）";
                else
                    actual = value.ToString(CultureInfo.InvariantCulture) + (keys[i] == "LockoutBadCount" ? " 次" : " 分钟");
                if (value == expected[i])
                {
                    result = "通过";
                    color = Color.ForestGreen;
                    passed++;
                }
                else
                {
                    result = "不达标";
                    color = Color.Firebrick;
                    failed++;
                }
            }
            else
                unknown++;
            int index = grid.Rows.Add(names[i], expectedText[i], actual, result);
            grid.Rows[index].Cells[3].Style.ForeColor = color;
        }
        grid.ClearSelection();
        int required = disabled ? 1 : 4;
        if (passed == required)
        {
            summary.Text = "已符合「" + PolicyChanges.Name(selectedPreset) + "」：" + required + " / " + required + " 项";
            summary.ForeColor = Color.ForestGreen;
        }
        else
        {
            summary.Text = String.Format(CultureInfo.InvariantCulture,
                "对照「{0}」：符合 {1} 项  |  不符 {2} 项  |  未知 {3} 项", PolicyChanges.Name(selectedPreset), passed, failed, unknown);
            summary.ForeColor = failed > 0 ? Color.Firebrick : Color.DarkGoldenrod;
        }
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (values != null)
            source.Text = "验收时间：" + timestamp + "    数据来源：Windows 本机安全策略（secedit 回读）";
        else
            source.Text = "验收时间：" + timestamp + "    读取失败：" + errorText;
    }
}
