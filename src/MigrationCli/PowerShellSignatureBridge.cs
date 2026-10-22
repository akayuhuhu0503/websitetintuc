using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LandPortal.MigrationCli;

// The signed document is data only. Only the OS PowerShell host and OS modules run.
public static class PowerShellSignatureBridge
{
    const int MaxOutputBytes = 32768;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    static readonly string[] Fields = ["Protocol", "Nonce", "FileSha256", "Success", "Status", "Thumbprint", "CertificateTrusted", "CodeSigningEku", "NotBeforeUtc", "NotAfterUtc"];

    public static async Task VerifyAsync(string path, string expectedSha256, ISet<string> trusted)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows signature verification required.");
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Command(path, nonce))) }) start.ArgumentList.Add(argument);
        // Do not load user/repository modules through a caller-controlled search path.
        start.Environment["PSModulePath"] = Path.Combine(Path.GetDirectoryName(host)!, "Modules");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Signature host unavailable.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError.BaseStream, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            ValidateResult(process.ExitCode, await output, await error, nonce, expectedSha256, trusted, DateTimeOffset.UtcNow);
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }

    static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(block, token)) != 0)
        {
            if (buffer.Length + count > MaxOutputBytes) throw new InvalidDataException("Signature response too large.");
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    public static void ValidateResult(int exitCode, byte[] stdout, byte[] stderr, string nonce, string expectedSha256, ISet<string> trusted, DateTimeOffset now)
    {
        // Progress is suppressed at its source. Never discard/whitelist stderr here:
        // CLIXML (including Error records), warnings, unknown bytes all fail closed.
        if (exitCode != 0 || stderr.Length != 0 || stdout.Length is 0 or > MaxOutputBytes) throw Denied();
        try
        {
            using var json = JsonDocument.Parse(StrictUtf8.GetString(stdout), new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Denied();
            var names = root.EnumerateObject().Select(x => x.Name).ToList();
            if (names.Count != Fields.Length || names.Distinct(StringComparer.Ordinal).Count() != Fields.Length || names.Except(Fields, StringComparer.Ordinal).Any()) throw Denied();
            string Text(string name) => root.GetProperty(name).GetString() ?? throw Denied();
            bool True(string name) => root.GetProperty(name).ValueKind == JsonValueKind.True;
            if (root.GetProperty("Protocol").GetInt32() != 1 || Text("Nonce") != nonce || Text("FileSha256") != expectedSha256 ||
                !True("Success") || Text("Status") != "Valid" || !True("CertificateTrusted") || !True("CodeSigningEku") || !trusted.Contains(Text("Thumbprint").ToUpperInvariant())) throw Denied();
            var before = DateTimeOffset.Parse(Text("NotBeforeUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            var after = DateTimeOffset.Parse(Text("NotAfterUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            if (before > now || after <= now || after <= before) throw Denied();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or DecoderFallbackException or KeyNotFoundException or OverflowException) { throw Denied(); }
    }

    static UnauthorizedAccessException Denied() => new("Approval signature bridge rejected host output, certificate or signer.");

    static string Command(string path, string nonce) => """
        $ProgressPreference='SilentlyContinue'
        $ErrorActionPreference='Stop'
        $PSModuleAutoLoadingPreference='None'
        [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        $OutputEncoding=[Console]::OutputEncoding
        $result=@{Protocol=1;Nonce='NONCE';FileSha256='';Success=$false;Status='BridgeError';Thumbprint='';CertificateTrusted=$false;CodeSigningEku=$false;NotBeforeUtc='';NotAfterUtc=''}
        try {
          Import-Module ([IO.Path]::Combine($PSHOME,'Modules/Microsoft.PowerShell.Security/Microsoft.PowerShell.Security.psd1')) -ErrorAction Stop
          Import-Module ([IO.Path]::Combine($PSHOME,'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1')) -ErrorAction Stop
          $path='FILEPATH'
          $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
          try {$sha=[Security.Cryptography.SHA256]::Create();try{$result.FileSha256=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}}finally{$stream.Dispose()}
          $signature=Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $path -ErrorAction Stop
          $result.Status=$signature.Status.ToString()
          $cert=$signature.SignerCertificate
          if($cert){
            $result.Thumbprint=$cert.Thumbprint
            $result.NotBeforeUtc=$cert.NotBefore.ToUniversalTime().ToString('O')
            $result.NotAfterUtc=$cert.NotAfter.ToUniversalTime().ToString('O')
            $chain=[Security.Cryptography.X509Certificates.X509Chain]::new()
            try {$chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck;$result.CertificateTrusted=$chain.Build($cert)}finally{$chain.Dispose()}
            foreach($extension in $cert.Extensions){if($extension -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]){foreach($oid in $extension.EnhancedKeyUsages){if($oid.Value -eq '1.3.6.1.5.5.7.3.3'){$result.CodeSigningEku=$true}}}}
            $result.Success=$result.Status -ceq 'Valid' -and $result.CertificateTrusted -and $result.CodeSigningEku -and $cert.NotBefore.ToUniversalTime() -le [DateTime]::UtcNow -and $cert.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow
          }
          $result|Microsoft.PowerShell.Utility\ConvertTo-Json -Compress
          if($result.Success){exit 0}else{exit 1}
        } catch {
          # Do not include exception text, document content or credentials in output.
          [Console]::Out.Write('{"Protocol":1,"Nonce":"NONCE","FileSha256":"","Success":false,"Status":"BridgeError","Thumbprint":"","CertificateTrusted":false,"CodeSigningEku":false,"NotBeforeUtc":"","NotAfterUtc":""}')
          exit 2
        }
        """.Replace("NONCE", nonce).Replace("FILEPATH", Path.GetFullPath(path).Replace("'", "''"));
}
