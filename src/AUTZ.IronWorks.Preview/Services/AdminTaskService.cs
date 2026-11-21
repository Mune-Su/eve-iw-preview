using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace AUTZ.IronWorks.Preview.Services;

/// <summary>
/// "Always start as administrator" without a Windows prompt every time. The prompt (UAC) only appears when a program asks
/// for administrator rights while it starts. A scheduled task set to "run with highest privileges" is allowed to start the
/// app elevated without asking again, and your own account can run that task whenever it likes. So: the task is created once
/// (while the app is running as administrator, which is why that step asks you), and from then on a normal start of the app
/// runs the task and closes itself, and the task's copy carries on as administrator.
/// The task has no trigger of its own: it only runs when the app starts it.
/// </summary>
internal static class AdminTaskService
{
    public const string TaskName = "EVE-IronWorks-Preview (administrator)";

    /// <summary>The task starts the app with this, so a copy that is started by the task never starts the task again.</summary>
    public const string TaskArgument = "--elevated-task";

    private static bool Run(string arguments, out string output)
    {
        output = "";
        try
        {
            var start = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(start);
            if (process is null) return false;
            string text = process.StandardOutput.ReadToEnd() + " " + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(); } catch { }
                output = "Windows took too long to answer.";
                return false;
            }
            output = text.Trim();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    public static bool Exists() => Run($"/Query /TN \"{TaskName}\"", out _);

    /// <summary>Starts the task (the app, as administrator). Works from a normal, non-elevated copy of the app.</summary>
    public static bool RunNow(out string error)
    {
        bool ok = Run($"/Run /TN \"{TaskName}\"", out string output);
        error = ok ? "" : output;
        return ok;
    }

    public static bool Remove(out string error)
    {
        bool ok = Run($"/Delete /TN \"{TaskName}\" /F", out string output);
        error = ok ? "" : output;
        return ok;
    }

    /// <summary>Creates (or updates) the task so it starts the given exe. Needs administrator rights.</summary>
    public static bool Create(string exePath, out string error)
    {
        error = "";
        string xmlFile = Path.Combine(Path.GetTempPath(), "eve-iw-preview-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            string? sid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrEmpty(sid))
            {
                error = "Couldn't work out which Windows account is running the app.";
                return false;
            }
            string folder = Path.GetDirectoryName(exePath) ?? "";
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                "  <RegistrationInfo><Description>Starts EVE-IronWorks-Preview as administrator without a Windows prompt. Created by the app; turn it off in Configuration &gt; General.</Description></RegistrationInfo>\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(sid) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
                "  <Settings>\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\n" +
                "    <StartWhenAvailable>false</StartWhenAvailable>\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\n" +
                "    <Enabled>true</Enabled>\n" +
                "    <Hidden>false</Hidden>\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n" +
                "    <Priority>7</Priority>\n" +
                "  </Settings>\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exePath) + "</Command><Arguments>" + TaskArgument + "</Arguments><WorkingDirectory>" + SecurityElement.Escape(folder) + "</WorkingDirectory></Exec></Actions>\n" +
                "</Task>\n";
            File.WriteAllText(xmlFile, xml, Encoding.Unicode);
            bool ok = Run($"/Create /TN \"{TaskName}\" /XML \"{xmlFile}\" /F", out string output);
            if (!ok) error = output.Length > 0 ? output : "Windows refused to create the task.";
            return ok;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { File.Delete(xmlFile); } catch { }
        }
    }
}


