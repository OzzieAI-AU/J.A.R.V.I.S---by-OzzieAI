using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OzzieAI.Jarvis.Services.Sensors;

public record WindowContext(string Title, string ProcessName, int ProcessId, DateTime Timestamp);

public interface IActiveWindowTrackerService
{
    WindowContext GetCurrentForegroundContext();
}

public class ActiveWindowTrackerService : IActiveWindowTrackerService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public WindowContext GetCurrentForegroundContext()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return new WindowContext("Desktop / Idle", "explorer", 0, DateTime.UtcNow);
            }

            var sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = "Unknown";
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch {}

            return new WindowContext(
                string.IsNullOrWhiteSpace(title) ? processName : title,
                processName,
                (int)pid,
                DateTime.UtcNow);
        }
        catch
        {
            return new WindowContext("Stark Command Center", "OzzieAI.Jarvis", Process.GetCurrentProcess().Id, DateTime.UtcNow);
        }
    }
}
