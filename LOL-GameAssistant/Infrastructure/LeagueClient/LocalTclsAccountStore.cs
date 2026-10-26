using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>Passwords are encrypted before serialization, scoped to this Windows user and account.</summary>
public sealed class LocalTclsAccountStore(string? storagePath = null) : ITclsAccountStore
{
    private static readonly object Sync = new();
    private readonly string _path = storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LOL-GameAssistant", "tcls-accounts.json");

    public IReadOnlyList<TclsAccount> Load()
    {
        lock (Sync)
        {
            if (!File.Exists(_path)) return [];
            try { return JsonSerializer.Deserialize<List<TclsAccount>>(File.ReadAllText(_path)) ?? []; }
            catch (JsonException) { throw new InvalidOperationException("TCLS 账号文件格式错误，已停止读取，原文件已保留。"); }
        }
    }

    public TclsAccount Save(string? id, string name, string username, string region, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(region))
            throw new InvalidOperationException("请填写账号和默认大区。");
        lock (Sync)
        {
            var accounts = Load().ToList();
            var previous = accounts.SingleOrDefault(account => account.Id == id);
            if (previous != null && previous.Username != username.Trim() && password.Length == 0)
                throw new InvalidOperationException("更换账号时请重新填写密码。");
            string accountId = previous?.Id ?? Guid.NewGuid().ToString("N");
            string encrypted = previous?.ProtectedPassword ?? "";
            if (password.Length > 0)
            {
                byte[] plain = Encoding.UTF8.GetBytes(password); // Password whitespace is significant.
                try { encrypted = Convert.ToBase64String(ProtectedData.Protect(plain, Entropy(accountId), DataProtectionScope.CurrentUser)); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            if (encrypted.Length == 0) throw new InvalidOperationException("首次保存账号需要填写密码。");
            var saved = new TclsAccount { Id = accountId, Name = string.IsNullOrWhiteSpace(name) ? username.Trim() : name.Trim(),
                Username = username.Trim(), Region = region.Trim(), ProtectedPassword = encrypted };
            accounts.RemoveAll(account => account.Id == accountId);
            accounts.Add(saved);
            Write(accounts);
            return saved;
        }
    }

    public void Delete(string id)
    {
        lock (Sync) { var accounts = Load().Where(account => account.Id != id).ToList(); Write(accounts); }
    }

    public string ReadPassword(string id)
    {
        var account = Load().SingleOrDefault(account => account.Id == id)
            ?? throw new InvalidOperationException("所选 TCLS 账号不存在，请重新选择。");
        try
        {
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(account.ProtectedPassword), Entropy(id), DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        { throw new InvalidOperationException("无法解密此账号密码，请在当前 Windows 用户下重新保存密码。"); }
    }

    private void Write(IReadOnlyList<TclsAccount> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(accounts)); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static byte[] Entropy(string id) => Encoding.UTF8.GetBytes("LOL-GameAssistant.TCLS.v1:" + id);
}
