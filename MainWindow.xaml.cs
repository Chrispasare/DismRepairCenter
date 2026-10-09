using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DismRepairCenter;

public partial class MainWindow : Window
{
    record Act(string Id, string Group, string Hint);

    static readonly Act[] Acts =
    {
        new("check",     "health", "DISM /Online /Cleanup-Image /CheckHealth"),
        new("scan",      "health", "DISM /Online /Cleanup-Image /ScanHealth"),
        new("restore",   "health", "DISM /Online /Cleanup-Image /RestoreHealth"),
        new("analyze",   "store",  "DISM /Online /Cleanup-Image /AnalyzeComponentStore"),
        new("cleanup",   "store",  "DISM /Online /Cleanup-Image /StartComponentCleanup"),
        new("sfc",       "sfc",    "sfc /scannow"),
        new("sfcverify", "sfc",    "sfc /verifyonly"),
        new("sfcfile",   "sfc",    "sfc /scanfile=<file>"),
        new("sfclog",    "sfc",    @"findstr /c:""[SR]"" CBS.log"),
        new("addpkg",    "tools",  "DISM /Online /Add-Package /PackagePath:<file>"),
        new("dismlog",   "tools",  @"%windir%\Logs\DISM\dism.log"),
        new("cbslog",    "tools",  @"%windir%\Logs\CBS\CBS.log"),
    };

    static readonly string Sys = Environment.SystemDirectory;
    static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    static readonly Regex Pct = new(@"(\d{1,3}(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);

    CancellationTokenSource? _cts;
    bool _lastBlank = true;
    string _statusKey = "Ready";
    object[] _statusArgs = Array.Empty<object>();

    public MainWindow()
    {
        InitializeComponent();
        OsInfo.Text = OsDescription();
        ApplyLanguage();
    }

    // ---------- interfață ----------

    void ApplyLanguage()
    {
        Subtitle.Text = Loc.T("Subtitle");
        LangBtn.Content = Loc.Lang == "ro" ? "EN" : "RO";
        FullTitle.Text = Loc.T("B_full");
        FullDesc.Text = Loc.T("D_full");
        OptTitle.Text = Loc.T("Options");
        ResetBaseTxt.Text = Loc.T("ResetBase");
        SourceLbl.Text = Loc.T("Source");
        LimitAccessTxt.Text = Loc.T("LimitAccess");
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Credit.Text = Loc.T("Credit", v is null ? "1.0" : $"{v.Major}.{v.Minor}.{v.Build}");
        CancelBtn.Content = Loc.T("Cancel");
        SaveBtn.Content = Loc.T("Save");
        ClearBtn.Content = Loc.T("Clear");
        ExitBtn.Content = Loc.T("Exit");
        Status.Text = Loc.T(_statusKey, _statusArgs);

        Actions.Children.Clear();
        var muted = (Brush)FindResource("Muted");
        foreach (var g in Acts.GroupBy(a => a.Group))
        {
            Actions.Children.Add(new TextBlock
            {
                Text = Loc.T("G_" + g.Key), FontWeight = FontWeights.SemiBold, FontSize = 11,
                Foreground = muted, Margin = new Thickness(2, 6, 0, 6)
            });
            foreach (var a in g)
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = Loc.T("B_" + a.Id), FontWeight = FontWeights.SemiBold });
                sp.Children.Add(new TextBlock
                {
                    Text = Loc.T("D_" + a.Id), Foreground = muted, FontSize = 12,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
                });
                var b = new Button { Style = (Style)FindResource("ActionButton"), Tag = a.Id, Content = sp, ToolTip = a.Hint };
                b.Click += Action_Click;
                Actions.Children.Add(b);
            }
        }
    }

    void SetStatus(string key, params object[] args)
    {
        _statusKey = key; _statusArgs = args;
        Status.Text = Loc.T(key, args);
    }

    void SetBusy(bool busy)
    {
        LeftPanel.IsEnabled = !busy;
        CancelBtn.IsEnabled = busy;
        if (!busy) Progress.IsIndeterminate = false;
    }

    void Line(string s)
    {
        s = s.TrimEnd();
        bool blank = s.Length == 0;
        if (blank && _lastBlank) return;          // fără rânduri goale repetate
        _lastBlank = blank;
        TrackProgress(s);
        Output.AppendText(s + Environment.NewLine);
        Output.ScrollToEnd();
    }

    // text suprascris cu CR: procentele mișcă doar bara, orice alt text se afișează
    void Transient(string s)
    {
        if (Pct.IsMatch(s)) TrackProgress(s);
        else Line(s);
    }

    void TrackProgress(string s)
    {
        var m = Pct.Match(s);
        if (!m.Success) return;
        if (double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 100)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = v;
        }
    }

    static string OsDescription()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = Environment.OSVersion.Version.Build;
            var name = build >= 22000 ? "Windows 11" : "Windows 10";
            return $"{name} {k?.GetValue("DisplayVersion")} · build {build}.{k?.GetValue("UBR")}";
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    // ---------- comenzi ----------

    async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null || (sender as FrameworkElement)?.Tag is not string id) return;

        try
        {
            switch (id)
            {
                case "dismlog": OpenFile(Path.Combine(Win, "Logs", "DISM", "dism.log")); return;
                case "cbslog": OpenFile(Path.Combine(Win, "Logs", "CBS", "CBS.log")); return;
                case "sfclog": ExtractSfcReport(); return;
            }

            // tot ce cere alegerea utilizatorului se rezolvă înainte de pornire
            var steps = id == "full" ? new[] { "scan", "restore", "sfc" } : new[] { id };
            var cmds = new List<(string name, string exe, string args, Encoding enc)>();
            foreach (var s in steps)
            {
                var c = Build(s);
                if (c is null) return;
                cmds.Add(c.Value);
            }

            _cts = new CancellationTokenSource();
            SetBusy(true);
            for (int i = 0; i < cmds.Count; i++)
            {
                var (name, exe, args, enc) = cmds[i];
                Progress.Value = 0;
                Progress.IsIndeterminate = true;
                SetStatus("Running", cmds.Count > 1 ? $"{name} ({Loc.T("Step", i + 1, cmds.Count)})" : name);
                _lastBlank = false;
                Line("");
                Line($"[{DateTime.Now:HH:mm:ss}] > {Path.GetFileName(exe)} {args}");

                int code = await Runner.RunAsync(exe, args, enc, Line, Transient, _cts.Token);

                Progress.IsIndeterminate = false;
                Line($"[{DateTime.Now:HH:mm:ss}] exit code {code}");
                if (code == 3010) { Progress.Value = 100; SetStatus("Reboot", name); }
                else if (code == 0) { Progress.Value = 100; SetStatus("Done", name); }
                else
                {
                    SetStatus("Failed", name, code);
                    // la reparare completă, un pas eșuat oprește pașii următori
                    if (i < cmds.Count - 1) break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Line(Loc.T("Cancelled"));
            SetStatus("Cancelled");
        }
        catch (Exception ex)
        {
            Line(Loc.T("Error", ex.Message));
            SetStatus("Error", ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    (string name, string exe, string args, Encoding enc)? Build(string id)
    {
        string dism = Path.Combine(Sys, "dism.exe"), sfc = Path.Combine(Sys, "sfc.exe");
        // /English: ieșire previzibilă pentru progres, indiferent de limba sistemului
        const string ci = "/Online /English /Cleanup-Image ";
        Encoding oem = Runner.Oem, utf16 = Encoding.Unicode;   // sfc.exe scrie UTF-16 când e redirecționat

        switch (id)
        {
            case "check": return ("CheckHealth", dism, ci + "/CheckHealth", oem);
            case "scan": return ("ScanHealth", dism, ci + "/ScanHealth", oem);
            case "restore":
            {
                var args = ci + "/RestoreHealth";
                var src = SourceBox.Text.Trim().Trim('"');
                if (src.Length > 0)
                {
                    var file = SourceFile(src);
                    if (file is not null && !File.Exists(file) && !Directory.Exists(file))
                    {
                        MessageBox.Show(this, Loc.T("SourceMissing", file), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return null;
                    }
                    args += $" /Source:\"{src}\"";
                    if (LimitAccess.IsChecked == true) args += " /LimitAccess";
                }
                return ("RestoreHealth", dism, args, oem);
            }
            case "analyze": return ("AnalyzeComponentStore", dism, ci + "/AnalyzeComponentStore", oem);
            case "cleanup":
            {
                var args = ci + "/StartComponentCleanup";
                if (ResetBase.IsChecked == true)
                {
                    if (MessageBox.Show(this, Loc.T("ConfirmResetBase"), Title, MessageBoxButton.YesNo,
                            MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return null;
                    args += " /ResetBase";
                }
                return ("StartComponentCleanup", dism, args, oem);
            }
            case "addpkg":
            {
                var f = Pick(Loc.T("PickPackage"), "Windows packages (*.cab;*.msu)|*.cab;*.msu", null);
                return f is null ? null : ("Add-Package", dism, $"/Online /English /Add-Package /PackagePath:\"{f}\" /NoRestart", oem);
            }
            case "sfc": return ("SFC /scannow", sfc, "/scannow", utf16);
            case "sfcverify": return ("SFC /verifyonly", sfc, "/verifyonly", utf16);
            case "sfcfile":
            {
                var f = Pick(Loc.T("PickSystemFile"), "System files (*.dll;*.exe;*.sys;*.mui)|*.dll;*.exe;*.sys;*.mui|*.*|*.*", Sys);
                return f is null ? null : ("SFC /scanfile", sfc, $"/scanfile=\"{f}\"", utf16);
            }
        }
        return null;
    }

    /// <summary>Calea de pe disc dintr-o sursă de forma wim:C:\x\install.wim:1 sau cale simplă.</summary>
    static string? SourceFile(string src)
    {
        var m = Regex.Match(src, @"^(?:wim|esd):(.+?)(?::\d+)?$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : src;
    }

    string? Pick(string title, string filter, string? dir)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (dir is not null) d.InitialDirectory = dir;
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    void OpenFile(string path)
    {
        if (!File.Exists(path)) { Line(Loc.T("LogMissing", path)); return; }
        Process.Start(new ProcessStartInfo(Path.Combine(Sys, "notepad.exe"), $"\"{path}\"") { UseShellExecute = false });
    }

    void ExtractSfcReport()
    {
        var cbs = Path.Combine(Win, "Logs", "CBS", "CBS.log");
        if (!File.Exists(cbs)) { Line(Loc.T("LogMissing", cbs)); return; }
        var lines = new List<string>();
        using (var fs = new FileStream(cbs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var r = new StreamReader(fs))
            while (r.ReadLine() is { } l)
                if (l.Contains("[SR]")) lines.Add(l);

        if (lines.Count == 0) { Line(Loc.T("SrNone")); SetStatus("SrNone"); return; }
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"SFC_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllLines(target, lines, Encoding.UTF8);
        Line(Loc.T("SrSaved", lines.Count, target));
        SetStatus("SrSaved", lines.Count, target);
        OpenFile(target);
    }

    // ---------- butoane auxiliare ----------

    void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var f = Pick(Loc.T("PickSource"), "Windows image (*.wim;*.esd)|*.wim;*.esd", null);
        if (f is null) return;
        var kind = Path.GetExtension(f).Equals(".esd", StringComparison.OrdinalIgnoreCase) ? "esd" : "wim";
        SourceBox.Text = $"{kind}:{f}:1";
    }

    void Lang_Click(object sender, RoutedEventArgs e)
    {
        Loc.Lang = Loc.Lang == "ro" ? "en" : "ro";
        ApplyLanguage();
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null) return;
        if (MessageBox.Show(this, Loc.T("ConfirmCancel"), Title, MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            _cts?.Cancel();
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog
        {
            Filter = "Text (*.txt)|*.txt",
            FileName = $"DismRepairCenter_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (d.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(d.FileName, Output.Text, Encoding.UTF8);
            SetStatus("Saved", d.FileName);
        }
        catch (Exception ex) { SetStatus("Error", ex.Message); }
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        Output.Clear();
        _lastBlank = true;
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_cts is not null)
        {
            if (MessageBox.Show(this, Loc.T("ConfirmExit"), Title, MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();
        }
        base.OnClosing(e);
    }
}
