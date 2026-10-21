using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class NativeSmokeTest
{
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine("PASS: " + description);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Dictionary<string, int> values = PolicyValues.Parse("[System Access]\r\nLockoutBadCount = 5\r\nLockoutDuration = 60\r\nResetLockoutCount = 60\r\nAllowAdministratorLockout = 1\r\nIgnored = text\r\nNegative = -1\r\n");
            Check(values.Count == 5 && values["lockoutbadcount"] == 5 && values["Negative"] == -1, "INF parsing and case-insensitive fields");
            using (VerificationForm form = new VerificationForm())
            {
                form.DisplayPolicy(values, "");
                Check(form.Grid.Rows.Count == 4 && form.Grid.Columns.Count == 4, "Four verification rows and columns");
                for (int i = 0; i < 4; i++)
                    Check(Convert.ToString(form.Grid.Rows[i].Cells[3].Value) == "通过", "Expected policy passes row " + i);
                Check(form.Summary.Text.Contains("原版策略 5/60/60") && form.Summary.Text.Contains("4 / 4"), "Original preset summary");
                Check(PolicyChanges.Matches(values, PolicyPreset.Original), "Original 5/60/60 matches");
                Check(!PolicyChanges.Matches(values, PolicyPreset.Recommended), "Original is not recommended preset");
                values["LockoutBadCount"] = 6;
                values["LockoutDuration"] = 0;
                values["ResetLockoutCount"] = 30;
                values["AllowAdministratorLockout"] = 0;
                form.DisplayPolicy(values, "");
                for (int i = 0; i < 4; i++)
                    Check(Convert.ToString(form.Grid.Rows[i].Cells[3].Value) == "不达标", "Nonmatching policy fails row " + i);
                values["LockoutBadCount"] = 5;
                values["LockoutDuration"] = 60;
                values["ResetLockoutCount"] = 60;
                values.Remove("AllowAdministratorLockout");
                form.DisplayPolicy(values, "");
                Check(Convert.ToString(form.Grid.Rows[3].Cells[3].Value) == "无法确认" && form.Summary.Text.Contains("符合 3 项") && form.Summary.Text.Contains("未知 1 项"), "Older Windows missing administrator policy stays unknown");

                values["LockoutBadCount"] = 10;
                values["LockoutDuration"] = 10;
                values["ResetLockoutCount"] = 10;
                values["AllowAdministratorLockout"] = 1;
                Check(PolicyChanges.Matches(values, PolicyPreset.Recommended), "Recommended 10/10/10 matches");
                Check(!PolicyChanges.Matches(values, PolicyPreset.Original), "Recommended does not match original");

                values["LockoutBadCount"] = 0;
                values["AllowAdministratorLockout"] = 0;
                Check(PolicyChanges.Matches(values, PolicyPreset.Disabled), "Disabled policy matches irrespective of stored duration");
                values.Remove("AllowAdministratorLockout");
                values.Remove("LockoutDuration");
                values.Remove("ResetLockoutCount");
                Check(PolicyChanges.Matches(values, PolicyPreset.Disabled), "Disabled export with omitted dormant settings matches");
                typeof(VerificationForm).GetField("selectedPreset", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, PolicyPreset.Disabled);
                form.DisplayPolicy(values, "");
                Check(form.Summary.Text.Contains("1 / 1"), "Disabled screen checks only effective lockout threshold");
                for (int i = 1; i < 4; i++)
                    Check(Convert.ToString(form.Grid.Rows[i].Cells[3].Value) == "不适用", "Disabled dormant setting is marked not applicable " + i);
                typeof(VerificationForm).GetField("selectedPreset", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, PolicyPreset.Original);

                form.DisplayPolicy(null, "read failure test");
                Check(form.Summary.Text.Contains("未知 4 项") && form.Source.Text.Contains("read failure test"), "Read failure stays unknown and displays reason");
                string[] beforeFiles = Directory.GetFiles(Path.GetTempPath(), "account-lockout-inspect-*");
                typeof(VerificationForm).GetMethod("RunVerification", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                bool admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                Check(form.Grid.Rows.Count == 4, "Real secedit attempt populates four rows");
                if (!admin)
                    Check(form.Source.Text.Contains("读取失败") || form.Grid.Rows.Count == 4, "Real unelevated export result is displayed");
                foreach (string temporaryFile in Directory.GetFiles(Path.GetTempPath(), "account-lockout-inspect-*"))
                    Check(Array.IndexOf(beforeFiles, temporaryFile) >= 0, "No new temporary policy files remain");
                Console.WriteLine("PASS: Temporary policy export files cleaned");
            }
            Assembly exe = Assembly.ReflectionOnlyLoadFrom(args[0]);
            Check(exe.ImageRuntimeVersion.StartsWith("v4.0"), ".NET Framework 4 runtime target");
            foreach (AssemblyName reference in exe.GetReferencedAssemblies())
            {
                Check(reference.Name == "mscorlib" || reference.Name == "System" || reference.Name == "System.Core" || reference.Name == "System.Drawing" || reference.Name == "System.Windows.Forms", "Only Windows framework dependency: " + reference.Name);
            }
            Check(exe.GetManifestResourceNames().Length == 0, "No embedded PowerShell payload");
            PortableExecutableKinds kinds;
            ImageFileMachine machine;
            exe.ManifestModule.GetPEKind(out kinds, out machine);
            Check((kinds & PortableExecutableKinds.ILOnly) != 0 && (kinds & PortableExecutableKinds.Required32Bit) == 0 && machine == ImageFileMachine.I386, "AnyCPU supports Windows x86 and x64");
            Check(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(args[0])).Contains("level=\"requireAdministrator\""), "Administrator launch manifest");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
