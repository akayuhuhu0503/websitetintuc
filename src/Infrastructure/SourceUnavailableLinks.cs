using System.Security.Cryptography;
using System.Text;

namespace LandPortal.Infrastructure;

// Stable source identity + URL digest, not a filesystem path or a fetch proxy.
public static class SourceUnavailableLinks
{
    public static string Key(string sourceUrl) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl)));
    public static string Path(int kind,string sourceId,string sourceUrl) => $"/tep-nguon/{kind}/{Uri.EscapeDataString(sourceId)}/{Key(sourceUrl)}";
}
