using LOL_GameAssistant.Infrastructure.Pengu;
using System.Reflection;
using System.Security.Cryptography;

using var http = new HttpClient();
var installer = (PenguReleaseInstaller)Activator.CreateInstance(typeof(PenguReleaseInstaller), BindingFlags.Instance | BindingFlags.NonPublic,
    null, new object[] { Path.GetFullPath("tmp/gitee-visual-release/installed"), http,
        "565b955e13c02de53e71975a485eef9de405a378640db46cfb7d2bdb799b1179", "c59d37ac8a3fbad2fa75b5541bd8f1d1f7036a67f0c0cdb716d2bd4e17efd66a" }, null)!;
string root = await installer.InstallAsync();
Console.WriteLine($"Installed from Gitee: {root}");
Console.WriteLine($"Core SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "bin/core.dll"))))}");
http.Dispose();
if (await installer.InstallAsync() != root) throw new Exception("Reuse failed");
Console.WriteLine("Verified cached installation without network access.");
