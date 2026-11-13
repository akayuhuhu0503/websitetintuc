using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace LandPortal.Infrastructure;

// Encrypted one-use CMS handoff. This is not a password lookup API and never
// reads historical operator/CLI credential files. SQL authorization stays in
// AccountAdministrationService; files only hold a short-lived delivery envelope.
public sealed class CmsTemporaryPasswordDelivery(StorageOptions storage, IDataProtectionProvider protection, TimeProvider? clock = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();
    private IDataProtector Protector => protection.CreateProtector("LandPortal.CmsTemporaryCredential.v1");
    private sealed record Envelope(Guid Id, string ActorId, string TargetId, string UserName, string SecurityStamp,
        string? Password, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, bool Consumed);
    public CmsCredentialReceipt Write(string actorId, string targetId, string userName, string password, string securityStamp)
    {
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(securityStamp) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Không đủ thông tin để tạo phiên bàn giao mật khẩu.");
        var id = Guid.NewGuid(); var now = Now;
        var envelope = new Envelope(id, actorId, targetId, userName, securityStamp, password, now, now.Add(Lifetime), false);
        var path = PathFor(id); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = TemporaryPasswordDelivery.CreatePrivate(path);
        Save(file, envelope);
        return new(id, envelope.ExpiresUtc);
    }
    public CmsCredentialDisplay Consume(Guid id, string actorId, string targetId, string securityStamp)
    {
        try
        {
            var path = PathFor(id); RequirePrivateAcl(path);
            // Exclusive handle fences simultaneous requests/processes. A durable
            // encrypted tombstone is flushed before any credential can be returned.
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (file.Length is < 1 or > 16384) throw Unavailable();
            using var reader = new StreamReader(file, Encoding.UTF8, true, 1024, leaveOpen: true);
            var envelope = JsonSerializer.Deserialize<Envelope>(Protector.Unprotect(reader.ReadToEnd())) ?? throw Unavailable();
            if (envelope.Id != id || envelope.ActorId != actorId || envelope.TargetId != targetId ||
                envelope.Consumed || envelope.Password == null || envelope.CreatedUtc > Now || envelope.ExpiresUtc <= Now ||
                envelope.ExpiresUtc - envelope.CreatedUtc != Lifetime || !Same(envelope.SecurityStamp, securityStamp)) throw Unavailable();
            var result = new CmsCredentialDisplay(envelope.UserName, envelope.Password);
            Save(file, envelope with { Password = null, Consumed = true });
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or System.Security.SecurityException)
        { throw Unavailable(); }
    }
    private string PathFor(Guid id)
    {
        if (id == Guid.Empty) throw Unavailable();
        var directory = StorageOptions.ResolveWithin(storage.TempRoot, "cms-credential-delivery");
        var path = StorageOptions.ResolveWithin(directory, id.ToString("N") + ".protected");
        // Reuse the same private-storage boundary as uploads; no webroot/private
        // exception is created here. Temp must be an already validated store.
        MediaService.ValidatePrivateStorageRoot(storage.TempRoot, storage);
        for (string? node = path; node != null; node = Path.GetDirectoryName(node))
            if ((Directory.Exists(node) || File.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0) throw Unavailable();
        return path;
    }
    private void Save(FileStream file, Envelope envelope)
    {
        var bytes = Encoding.UTF8.GetBytes(Protector.Protect(JsonSerializer.Serialize(envelope)));
        file.Position = 0; file.Write(bytes); file.SetLength(bytes.Length); file.Flush(true);
    }
    private static bool Same(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static void RequirePrivateAcl(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var allowed = new[] { WindowsIdentity.GetCurrent().User!.Value, "S-1-5-18" };
            var rules = new FileInfo(path).GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
                if (rule.AccessControlType == AccessControlType.Allow && !allowed.Contains(rule.IdentityReference.Value, StringComparer.Ordinal)) throw Unavailable();
        }
        else if ((File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0) throw Unavailable();
    }
    private static InvalidOperationException Unavailable() => new("Phiên bàn giao không còn hợp lệ, đã dùng, đã hết 10 phút hoặc tài khoản đã thay đổi. Không thể đọc lại mật khẩu; tạo mật khẩu tạm mới nếu vẫn cần bàn giao.");
}
public sealed record CmsCredentialReceipt(Guid DeliveryId, DateTimeOffset ExpiresUtc);
public sealed record CmsCredentialDisplay(string UserName, string Password);
