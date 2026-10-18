using LOL_GameAssistant.Infrastructure.GameData;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class GameOverlayRuntimeFilesTests
{
    [Fact]
    public void ActualWindowsDllMappingUsesCacheAndLeavesPublishImageWritable()
    {
        string root = Path.Combine(Path.GetTempPath(),"SC.Overlay.Load.Test",Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root,"publish"), cache = Path.Combine(root,"runtime");
        Directory.CreateDirectory(source);
        nint module = 0;
        try
        {
            foreach (string name in GameOverlayRuntimeFiles.Names)
                File.Copy(Path.Combine(AppContext.BaseDirectory,"Tools","GameOverlay",name),Path.Combine(source,name));
            string directory = GameOverlayRuntimeFiles.Prepare(source,cache);
            string dll = Path.Combine(directory,"sc-game-overlay.dll");
            module = LoadLibraryExW(dll,0,0x100|0x800);
            Assert.NotEqual(0,module);
            var actualPath = new StringBuilder(32768);
            Assert.NotEqual(0u,GetModuleFileNameW(module,actualPath,actualPath.Capacity));
            Assert.Equal(dll,actualPath.ToString(),ignoreCase:true);
            Assert.Throws<IOException>(() => { using var blocked = File.Open(dll,FileMode.Open,FileAccess.Write,FileShare.None); });
            foreach (string name in GameOverlayRuntimeFiles.Names)
                File.Copy(Path.Combine(AppContext.BaseDirectory,"Tools","GameOverlay",name),Path.Combine(source,name),overwrite:true);
        }
        finally
        {
            if(module!=0) FreeLibrary(module);
            foreach(string directory in Directory.GetDirectories(cache))
            {
                foreach(string name in GameOverlayRuntimeFiles.Names) File.Delete(Path.Combine(directory,name));
                Directory.Delete(directory);
            }
            foreach(string name in GameOverlayRuntimeFiles.Names) File.Delete(Path.Combine(source,name));
            Directory.Delete(source); Directory.Delete(cache); Directory.Delete(root);
        }
    }

    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern nint LoadLibraryExW(string path,nint file,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern uint GetModuleFileNameW(nint module,StringBuilder path,int size);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint module);

    [Fact]
    public void LockedRuntimeImagesDoNotBlockPublishingAndNewVersionsUseSeparateCache()
    {
        string root = Path.Combine(Path.GetTempPath(), "SC.Overlay.Test", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root,"publish"), cache = Path.Combine(root,"runtime");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllBytes(Path.Combine(source,GameOverlayRuntimeFiles.Names[0]), [1,2,3]);
            File.WriteAllBytes(Path.Combine(source,GameOverlayRuntimeFiles.Names[1]), [4,5,6]);
            string first = GameOverlayRuntimeFiles.Prepare(source, cache);
            Assert.NotEqual(source,first);
            using (var loadedHost = File.Open(Path.Combine(first,GameOverlayRuntimeFiles.Names[0]),FileMode.Open,FileAccess.Read,FileShare.Read))
            using (var loadedDll = File.Open(Path.Combine(first,GameOverlayRuntimeFiles.Names[1]),FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                Assert.Equal(first,GameOverlayRuntimeFiles.Prepare(source,cache));
                File.WriteAllBytes(Path.Combine(source,GameOverlayRuntimeFiles.Names[0]), [7,8,9]);
                File.WriteAllBytes(Path.Combine(source,GameOverlayRuntimeFiles.Names[1]), [10,11,12]);
                string second = GameOverlayRuntimeFiles.Prepare(source,cache);
                Assert.NotEqual(first,second);
                Assert.Equal(new byte[]{4,5,6},File.ReadAllBytes(Path.Combine(first,GameOverlayRuntimeFiles.Names[1])));
                Assert.Equal(new byte[]{10,11,12},File.ReadAllBytes(Path.Combine(second,GameOverlayRuntimeFiles.Names[1])));
                // Missing/corrupt cached images must fail instead of executing stale bytes.
                File.WriteAllBytes(Path.Combine(second,GameOverlayRuntimeFiles.Names[1]), [0]);
                Assert.Throws<InvalidDataException>(() => GameOverlayRuntimeFiles.Prepare(source,cache));
            }
        }
        finally
        {
            // Delete only the known files and directories owned by this test; never recurse.
            foreach (string directory in Directory.GetDirectories(cache))
            {
                foreach (string name in GameOverlayRuntimeFiles.Names) File.Delete(Path.Combine(directory,name));
                Directory.Delete(directory);
            }
            foreach (string name in GameOverlayRuntimeFiles.Names) File.Delete(Path.Combine(source,name));
            Directory.Delete(source); Directory.Delete(cache); Directory.Delete(root);
        }
    }
}
