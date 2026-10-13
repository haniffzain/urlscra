using System.Runtime.InteropServices;
using System.Text;

namespace URLScra.Services;

public sealed class AdblockRustService : IDisposable
{
    private const string NativeLibraryName = "urlscra_adblock.dll";

    private IntPtr _engine;
    private int _blockedCount;
    private bool _disposed;

    public bool IsAvailable => _engine != IntPtr.Zero;
    public int BlockedCount => Volatile.Read(ref _blockedCount);
    public string Status { get; private set; } = "Adblock Rust unavailable";

    public AdblockRustService()
    {
        try
        {
            var rules = LoadRules();
            if (string.IsNullOrWhiteSpace(rules))
            {
                Status = "Adblock Rust: no rules loaded";
                return;
            }

            _engine = NativeCreate(rules);
            Status = _engine != IntPtr.Zero
                ? "Adblock Rust active"
                : "Adblock Rust failed to initialize";
        }
        catch (DllNotFoundException)
        {
            Status = "Adblock Rust DLL not built";
        }
        catch (BadImageFormatException)
        {
            Status = "Adblock Rust DLL architecture mismatch";
        }
        catch (EntryPointNotFoundException)
        {
            Status = "Adblock Rust bridge mismatch";
        }
        catch (Exception ex)
        {
            Status = $"Adblock Rust unavailable: {ex.Message}";
        }
    }

    public bool ShouldBlock(string requestUrl, string sourceUrl, string resourceType, string method)
    {
        if (_disposed || _engine == IntPtr.Zero)
            return false;

        if (string.IsNullOrWhiteSpace(requestUrl))
            return false;

        sourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? requestUrl : sourceUrl;
        resourceType = NormalizeResourceType(resourceType);
        method = string.IsNullOrWhiteSpace(method) ? "get" : method.ToLowerInvariant();

        try
        {
            var blocked = NativeShouldBlock(
                _engine,
                requestUrl,
                sourceUrl,
                resourceType,
                method) != 0;

            if (blocked)
                Interlocked.Increment(ref _blockedCount);

            return blocked;
        }
        catch
        {
            // Adblocking is intentionally best-effort. Scraping must continue even if the
            // native bridge fails for a particular request.
            return false;
        }
    }

    public void ResetBlockedCount() => Interlocked.Exchange(ref _blockedCount, 0);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_engine != IntPtr.Zero)
        {
            try
            {
                NativeDestroy(_engine);
            }
            catch
            {
                // Process shutdown should not fail because of optional adblock cleanup.
            }
            finally
            {
                _engine = IntPtr.Zero;
            }
        }

        GC.SuppressFinalize(this);
    }

    ~AdblockRustService() => Dispose();

    private static string LoadRules()
    {
        var builder = new StringBuilder();

        var builtInPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "adblock",
            "urlscra-base.txt");

        AppendFile(builder, builtInPath);

        var dataRoot = Directory.Exists(@"D:\")
            ? @"D:\URLScra\Adblock"
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "URLScra",
                "Adblock");

        AppendFile(builder, Path.Combine(dataRoot, "custom-filters.txt"));

        var external = Environment.GetEnvironmentVariable("URLSCRA_ADBLOCK_FILTERS");
        if (!string.IsNullOrWhiteSpace(external))
        {
            foreach (var path in external.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AppendFile(builder, path);
            }
        }

        return builder.ToString();
    }

    private static void AppendFile(StringBuilder builder, string path)
    {
        if (!File.Exists(path))
            return;

        builder.AppendLine();
        builder.AppendLine(File.ReadAllText(path));
    }

    private static string NormalizeResourceType(string? resourceType)
    {
        return resourceType?.ToLowerInvariant() switch
        {
            "document" => "document",
            "stylesheet" => "stylesheet",
            "image" => "image",
            "media" => "media",
            "font" => "font",
            "script" => "script",
            "xhr" => "xmlhttprequest",
            "fetch" => "fetch",
            "websocket" => "websocket",
            "eventsource" => "other",
            "manifest" => "other",
            "texttrack" => "other",
            _ => "other"
        };
    }

    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr urlscra_adblock_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string rules);

    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void urlscra_adblock_destroy(IntPtr handle);

    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int urlscra_adblock_should_block(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string resourceType,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string method);

    private static IntPtr NativeCreate(string rules) => urlscra_adblock_create(rules);
    private static void NativeDestroy(IntPtr handle) => urlscra_adblock_destroy(handle);
    private static int NativeShouldBlock(
        IntPtr handle,
        string url,
        string sourceUrl,
        string resourceType,
        string method) =>
        urlscra_adblock_should_block(handle, url, sourceUrl, resourceType, method);
}
