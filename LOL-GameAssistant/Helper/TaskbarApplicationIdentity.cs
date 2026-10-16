using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LOL_GameAssistant.Helper;

/// <summary>为 Shell 提供固定身份及可直接读取的图标，兼容单文件发布与提权启动。</summary>
internal static class TaskbarApplicationIdentity
{
    internal const string ApplicationId = "SC.LOLGameAssistant.Desktop";
    private static readonly Guid PropertySet = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly Lazy<string> IconResource = new(CreateIconResource);

    internal static void InitializeProcess()
    {
        int result = SetCurrentProcessExplicitAppUserModelID(ApplicationId);
        if (result < 0) RuntimeDiagnostics.Report("任务栏", "设置程序身份失败", $"HRESULT=0x{result:X8}");
    }

    internal static void ConfigureWindow(nint handle)
    {
        try
        {
            WithStore(handle, store =>
            {
                Set(store, 2, $"\"{Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath}\"");
                Set(store, 3, IconResource.Value);
                Set(store, 4, "SC · LOL GameAssistant");
                // 最后设置 ID：Shell 此时会重新读取窗口的图标和启动信息。
                Set(store, 5, ApplicationId);
            });
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            RuntimeDiagnostics.Report("任务栏", "配置图标来源失败", ex.Message);
        }
    }

    internal static void ClearWindow(nint handle)
    {
        try { WithStore(handle, store => { foreach (uint id in new uint[] { 2, 3, 4, 5 }) Set(store, id, null); }); }
        catch (COMException) { /* 窗口销毁时 Shell 可能已经移除属性。 */ }
    }

    internal static string? ReadWindowProperty(nint handle, uint id)
    {
        string? result = null;
        WithStore(handle, store =>
        {
            var key = new PropertyKey(PropertySet, id);
            store.GetValue(ref key, out var value);
            try { if (value.Type == 31) result = Marshal.PtrToStringUni(value.Text); }
            finally { PropVariantClear(ref value); }
        });
        return result;
    }

    private static string CreateIconResource()
    {
        using Stream source = typeof(AppIcon).Assembly.GetManifestResourceStream("LOL_GameAssistant.AppIcon.ico")!;
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        string version = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LOL-GameAssistant", "Brand");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"sc-{version}.ico");
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
        return $"{path},0";
    }

    private static void WithStore(nint handle, Action<IPropertyStore> action)
    {
        Guid iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(handle, ref iid, out var store));
        try { action(store); }
        finally { Marshal.ReleaseComObject(store); }
    }

    private static void Set(IPropertyStore store, uint id, string? text)
    {
        var key = new PropertyKey(PropertySet, id);
        var value = new PropVariant();
        if (text != null) { value.Type = 31; value.Text = Marshal.StringToCoTaskMemUni(text); }
        try { store.SetValue(ref key, ref value); }
        finally { PropVariantClear(ref value); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }

    // PROPVARIANT is 24 bytes on x64 and 16 on x86.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type, Reserved1, Reserved2, Reserved3;
        public nint Text, ReservedValue;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint handle, ref Guid iid, out IPropertyStore store);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
