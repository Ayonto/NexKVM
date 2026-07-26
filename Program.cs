using System.Windows.Forms;

namespace NexKVM;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Sets Per-Monitor-V2 DPI awareness (via ApplicationHighDpiMode) so screen metrics and
        // absolute cursor coordinates are true pixels for both WinForms and the input engine.
        ApplicationConfiguration.Initialize();

        // Optional power-user auto-start: NexKVM.exe host <ip>  |  NexKVM.exe receiver
        string? role = null, ip = null;
        if (args.Length > 0)
        {
            string a = args[0].ToLowerInvariant();
            if (a is "host" or "pc") { role = "host"; if (args.Length > 1) ip = args[1]; }
            else if (a is "receiver" or "target" or "laptop") { role = "receiver"; }
        }

        Application.Run(new MainForm(role, ip));
    }
}
