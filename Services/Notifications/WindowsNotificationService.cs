using System.Diagnostics;

namespace OzzieAI.Jarvis.Services.Notifications;

public interface IWindowsNotificationService
{
    Task<bool> ShowToastAsync(string title, string message, string? attribution = "J.A.R.V.I.S. Command");
}

public class WindowsNotificationService : IWindowsNotificationService
{
    private readonly ILogger<WindowsNotificationService> _logger;

    public WindowsNotificationService(ILogger<WindowsNotificationService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ShowToastAsync(string title, string message, string? attribution = "J.A.R.V.I.S. Command")
    {
        return await Task.Run(() =>
        {
            try
            {
                // Escape strings for PowerShell command
                var cleanTitle = title.Replace("\"", "`\"").Replace("'", "''");
                var cleanMsg = message.Replace("\"", "`\"").Replace("'", "''");
                var cleanAttr = (attribution ?? "J.A.R.V.I.S.").Replace("\"", "`\"").Replace("'", "''");

                var psScript = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$xml = @""
<toast>
    <visual>
        <binding template=""ToastGeneric"">
            <text>{cleanTitle}</text>
            <text>{cleanMsg}</text>
            <text placement=""attribution"">{cleanAttr}</text>
        </binding>
    </visual>
    <audio src=""ms-winsoundevent:Notification.Default"" />
</toast>
""@
$doc = New-Object Windows.Data.Xml.Dom.XmlDocument
$doc.LoadXml($xml)
$toast = New-Object Windows.UI.Notifications.ToastNotification $doc
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier(""J.A.R.V.I.S."").Show($toast)
";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{psScript.Replace("\r\n", " ")}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Native Windows Toast failed: {Title}", title);
                return false;
            }
        });
    }
}
