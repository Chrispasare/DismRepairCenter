using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DismRepairCenter;

/// <summary>Rulează un utilitar de consolă și livrează ieșirea în timp real.</summary>
public static class Runner
{
    [DllImport("kernel32.dll")]
    static extern uint GetOEMCP();

    static Runner() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Codarea folosită de dism.exe când ieșirea e redirecționată.</summary>
    public static Encoding Oem
    {
        get
        {
            try { return Encoding.GetEncoding((int)GetOEMCP()); }
            catch { return Encoding.UTF8; }
        }
    }

    /// <param name="onLine">Linie completă (terminată cu LF).</param>
    /// <param name="onTransient">Text suprascris cu CR — bara de progres a DISM/SFC.</param>
    public static async Task<int> RunAsync(string exe, string args, Encoding encoding,
        Action<string> onLine, Action<string> onTransient, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };
        using var p = new Process { StartInfo = psi };
        p.Start();
        using var reg = ct.Register(() =>
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
        });

        var err = Pump(p.StandardError, onLine, _ => { });
        await Pump(p.StandardOutput, onLine, onTransient);
        await err;
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return p.ExitCode;
    }

    static async Task Pump(StreamReader reader, Action<string> onLine, Action<string> onTransient)
    {
        var buf = new char[2048];
        var sb = new StringBuilder();
        bool cr = false;
        int n;
        while ((n = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                char c = buf[i];
                if (cr)
                {
                    cr = false;
                    if (c == '\n') { onLine(sb.ToString()); sb.Clear(); continue; }
                    if (c == '\r') { cr = true; continue; }   // sfc.exe termină liniile cu CR CR LF
                    if (sb.Length > 0) onTransient(sb.ToString());
                    sb.Clear();
                }
                if (c == '\r') cr = true;
                else if (c == '\n') { onLine(sb.ToString()); sb.Clear(); }
                else if (c != '\0') sb.Append(c);
            }
            // progres rămas în buffer fără terminator: îl arătăm imediat
            if (!cr && sb.Length > 0 && sb.ToString().Contains('%')) onTransient(sb.ToString());
        }
        if (sb.Length > 0) onLine(sb.ToString());
    }
}
