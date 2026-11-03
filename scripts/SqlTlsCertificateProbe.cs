using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Diagnostic PRELOGIN + TLS only. Never sends LOGIN7, a password, or a SQL batch.
// Invalid peer certificates are rejected; callback never changes trust policy.
public static class SqlTlsCertificateProbe
{
    public sealed class PeerCertificate
    {
        public string Subject, Issuer, Sha256, NotBeforeUtc, NotAfterUtc, SubjectAlternativeName;
        public string[] EnhancedKeyUsage;
    }
    public sealed class Result
    {
        public string Endpoint, TargetName, Status, PolicyErrors, FailureType;
        public string[] ChainStatus;
        public PeerCertificate Leaf;
        public PeerCertificate[] Chain;
        public byte[] PublicCertificateDer;
        public bool LoginSent, SqlExecuted, ValidationBypassed;
        public int ServerEncryption;
        public string Protocol;
    }
    static PeerCertificate Describe(X509Certificate2 certificate)
    {
        var result = new PeerCertificate();
        result.Subject = certificate.Subject; result.Issuer = certificate.Issuer;
        result.NotBeforeUtc = certificate.NotBefore.ToUniversalTime().ToString("O");
        result.NotAfterUtc = certificate.NotAfter.ToUniversalTime().ToString("O");
        using (var sha = SHA256.Create()) result.Sha256 = BitConverter.ToString(sha.ComputeHash(certificate.RawData)).Replace("-", "");
        var usages = new List<string>();
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid.Value == "2.5.29.17") result.SubjectAlternativeName = extension.Format(true);
            if (extension.Oid.Value == "2.5.29.37")
                foreach (Oid usage in new X509EnhancedKeyUsageExtension(extension, extension.Critical).EnhancedKeyUsages) usages.Add(usage.Value);
        }
        result.EnhancedKeyUsage = usages.ToArray();
        return result;
    }
    static byte[] Exact(Stream stream, int size)
    {
        byte[] result = new byte[size]; int position = 0;
        while (position < size) { int read = stream.Read(result, position, size - position); if (read == 0) throw new EndOfStreamException(); position += read; }
        return result;
    }
    static byte[] Packet(Stream stream, byte expectedType)
    {
        byte[] header = Exact(stream, 8); int length = (header[2] << 8) | header[3];
        if (header[0] != expectedType || length < 8 || length > 65535) throw new InvalidDataException("Unexpected TDS packet");
        return Exact(stream, length - 8);
    }
    sealed class HandshakeStream : Stream
    {
        readonly Stream inner; byte[] pending = new byte[0]; int offset;
        public HandshakeStream(Stream stream) { inner = stream; }
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override int Read(byte[] buffer, int start, int count)
        {
            if (count == 0) return 0;
            while (offset == pending.Length) { pending = Packet(inner, 0x12); offset = 0; }
            int size = Math.Min(count, pending.Length - offset); Buffer.BlockCopy(pending, offset, buffer, start, size); offset += size; return size;
        }
        public override void Write(byte[] buffer, int start, int count)
        {
            byte id = 1;
            while (count > 0)
            {
                int size = Math.Min(count, 4088), length = size + 8;
                byte[] header = { 0x12, (byte)(size == count ? 1 : 0), (byte)(length >> 8), (byte)length, 0, 0, id++, 0 };
                inner.Write(header, 0, 8); inner.Write(buffer, start, size); start += size; count -= size;
            }
            inner.Flush();
        }
        public override void Flush() { inner.Flush(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
    }
    public static Result Inspect()
    {
        const string host = "45.120.229.43";
        var result = new Result { Endpoint = host + ",1433", TargetName = host, Status = "BLOCKED", Chain = new PeerCertificate[0], ChainStatus = new string[0] };
        using (var client = new TcpClient())
        {
            try
            {
                IAsyncResult connect = client.BeginConnect(IPAddress.Parse(host), 1433, null, null);
                using (connect.AsyncWaitHandle) { if (!connect.AsyncWaitHandle.WaitOne(10000)) throw new TimeoutException(); client.EndConnect(connect); }
                var network = client.GetStream(); network.ReadTimeout = 15000; network.WriteTimeout = 15000;
                // VERSION (first option) + ENCRYPTION=ON, offsets relative to body.
                byte[] prelogin = { 0x12,1,0,26,0,0,1,0, 0,0,11,0,6, 1,0,17,0,1, 0xFF, 15,0,0,0,0,0, 1 };
                network.Write(prelogin, 0, prelogin.Length); network.Flush();
                byte[] response = Packet(network, 0x04); int encryption = -1;
                for (int p = 0; p < response.Length && response[p] != 0xFF; p += 5)
                {
                    if (p + 5 > response.Length) throw new InvalidDataException();
                    int at = (response[p + 1] << 8) | response[p + 2], size = (response[p + 3] << 8) | response[p + 4];
                    if (at < 0 || at + size > response.Length) throw new InvalidDataException();
                    if (response[p] == 1 && size == 1) encryption = response[at];
                }
                result.ServerEncryption = encryption;
                if (encryption != 1 && encryption != 3) throw new InvalidDataException("Server did not negotiate required encryption");
                using (var framed = new HandshakeStream(network))
                using (var tls = new SslStream(framed, true, delegate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
                {
                    result.PolicyErrors = errors.ToString();
                    if (certificate != null) using (var peer = new X509Certificate2(certificate)) { result.Leaf = Describe(peer); result.PublicCertificateDer = peer.Export(X509ContentType.Cert); }
                    var elements = new List<PeerCertificate>(); var statuses = new List<string>();
                    if (chain != null)
                    {
                        foreach (var element in chain.ChainElements) elements.Add(Describe(element.Certificate));
                        foreach (var status in chain.ChainStatus) statuses.Add(status.Status.ToString());
                    }
                    result.Chain = elements.ToArray(); result.ChainStatus = statuses.ToArray();
                    return errors == SslPolicyErrors.None;
                }))
                {
                    tls.AuthenticateAsClient(host, null, SslProtocols.Tls12, false);
                    result.Protocol = tls.SslProtocol.ToString(); result.Status = "TLS_CERTIFICATE_VALID_ON_THIS_CLIENT";
                    // Stop here even on success. No authenticated SQL traffic exists.
                }
            }
            catch (Exception failure) { result.FailureType = failure.GetType().Name; }
        }
        return result;
    }
}
