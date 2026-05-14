using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LieblingstypenRemote;

public partial class MainWindow : Window
{
    private GlobalHotkeyManager? _hotkeyMgr;
    private readonly YouTubeService _youtube = new();
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private DispatcherTimer? _statsTimer;
    private Dictionary<int, TimeSpan> _lastCpuByPid = new();
    private DateTime _lastSampleTime = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Userdata next to the exe so it travels with the project (visible & portable).
        // Fallback to LocalAppData if the exe folder is read-only (e.g. Program Files install).
        var userDataFolder = Path.Combine(AppContext.BaseDirectory, "userdata", "WebView2");
        try
        {
            Directory.CreateDirectory(userDataFolder);
        }
        catch
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            userDataFolder = Path.Combine(localAppData, "LieblingstypenRemote", "WebView2");
            Directory.CreateDirectory(userDataFolder);
        }
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(env);

        var webappFolder = Path.Combine(AppContext.BaseDirectory, "webapp");
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "lieblingstypen.local",
            webappFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        webView.CoreWebView2.WebMessageReceived += OnWebMessage;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

        // Open target="_blank" links in the system default browser, not inside the app
        webView.CoreWebView2.NewWindowRequested += (s, ev) =>
        {
            ev.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ev.Uri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open external URL: {ex}");
            }
        };

        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeyMgr = new GlobalHotkeyManager(hwnd, OnHotkeyTriggered);

        webView.Source = new Uri("https://lieblingstypen.local/index.html");

        // Start stats sampling for memory/CPU display
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statsTimer.Tick += (s, ev) => SampleAndSendStats();
        _statsTimer.Start();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr h, int c, ref PROCESS_BASIC_INFORMATION pbi, int len, out int retLen);

    private static int GetParentPid(Process p)
    {
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            NtQueryInformationProcess(p.Handle, 0, ref pbi, Marshal.SizeOf(pbi), out _);
            return pbi.InheritedFromUniqueProcessId.ToInt32();
        }
        catch { return 0; }
    }

    private static HashSet<int> DescendantPids(int rootPid, IEnumerable<Process> allProcs)
    {
        var parent = new Dictionary<int, int>();
        foreach (var p in allProcs)
        {
            try { parent[p.Id] = GetParentPid(p); } catch { }
        }
        var result = new HashSet<int> { rootPid };
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (child, par) in parent)
            {
                if (result.Contains(par) && !result.Contains(child))
                {
                    result.Add(child);
                    changed = true;
                }
            }
        }
        return result;
    }

    private void SampleAndSendStats()
    {
        try
        {
            // Find OUR webview2 child processes (not other apps' webview2 instances)
            int myPid = Environment.ProcessId;
            var allWebview = Process.GetProcessesByName("msedgewebview2");
            var ours = DescendantPids(myPid, allWebview);
            var procs = new List<Process> { Process.GetCurrentProcess() };
            foreach (var p in allWebview)
            {
                if (ours.Contains(p.Id)) procs.Add(p);
                else p.Dispose();
            }

            long totalMem = 0;
            TimeSpan totalCurrentCpu = TimeSpan.Zero;
            var nextCpuByPid = new Dictionary<int, TimeSpan>();

            foreach (var p in procs)
            {
                try
                {
                    totalMem += p.WorkingSet64;
                    var cpu = p.TotalProcessorTime;
                    totalCurrentCpu += cpu;
                    nextCpuByPid[p.Id] = cpu;
                }
                catch { }
                finally
                {
                    if (p.Id != Environment.ProcessId) p.Dispose();
                }
            }

            var now = DateTime.UtcNow;
            var elapsedMs = (now - _lastSampleTime).TotalMilliseconds;

            TimeSpan deltaCpu = TimeSpan.Zero;
            foreach (var (pid, curr) in nextCpuByPid)
            {
                if (_lastCpuByPid.TryGetValue(pid, out var prev))
                    deltaCpu += curr - prev;
            }

            _lastCpuByPid = nextCpuByPid;
            _lastSampleTime = now;

            double cpuPercent = elapsedMs > 0
                ? deltaCpu.TotalMilliseconds / elapsedMs * 100.0 / Environment.ProcessorCount
                : 0;

            var msg = JsonSerializer.Serialize(new
            {
                type = "stats",
                memMB = totalMem / 1024.0 / 1024.0,
                cpuPercent = cpuPercent
            }, _jsonOpts);
            webView.CoreWebView2?.PostWebMessageAsString(msg);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SampleStats failed: {ex}");
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "registerHotkeys")
            {
                var list = new List<HotkeyDef>();
                foreach (var hk in root.GetProperty("hotkeys").EnumerateArray())
                {
                    list.Add(new HotkeyDef(
                        hk.GetProperty("id").GetString() ?? "",
                        hk.GetProperty("modifiers").GetUInt32(),
                        hk.GetProperty("vkey").GetUInt32()));
                }
                _hotkeyMgr?.SetHotkeys(list);
            }
            else if (type == "clearHotkeys")
            {
                _hotkeyMgr?.SetHotkeys(Array.Empty<HotkeyDef>());
            }
            else if (type == "fetchVideos")
            {
                var handle = root.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "";
                _ = FetchAndSendVideosAsync(handle);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebMessage error: {ex}");
        }
    }

    private async Task FetchAndSendVideosAsync(string handle)
    {
        try
        {
            var videos = await _youtube.FetchLatestAsync(handle);
            var json = JsonSerializer.Serialize(new { type = "videos", videos }, _jsonOpts);
            await Dispatcher.InvokeAsync(() =>
            {
                webView.CoreWebView2.PostWebMessageAsString(json);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FetchVideos failed: {ex}");
        }
    }

    private void OnHotkeyTriggered(string id)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var encoded = JsonSerializer.Serialize(id);
            var js = $"window.__onHotkey && window.__onHotkey({encoded});";
            webView.CoreWebView2.ExecuteScriptAsync(js);
        });
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeyMgr?.Dispose();
    }
}

public record HotkeyDef(string Id, uint Modifiers, uint VirtualKey);
