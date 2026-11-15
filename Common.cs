using System.Net;
using System.Net.Sockets;

namespace NexKVM;

internal enum EngineStatus { Stopped, Listening, Connecting, Connected, Controlling }

/// <summary>Which side of the host PC the target machine sits on (where the cursor crosses).</summary>
internal enum TargetSide { Left, Right, Top, Bottom }

internal static class NetUtil
{
    /// <summary>Best-effort list of this machine's LAN IPv4 addresses, for display in the UI.</summary>
    public static List<string> LocalIPv4()
    {
        var result = new List<string>();
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ip)) continue;
                result.Add(ip.ToString());
            }
        }
        catch { /* ignore */ }
        return result;
    }
}
