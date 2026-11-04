using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace LandPortal.Web.Services;

// No EF context/application-role interceptor: this is a separate, SELECT-only source.
public sealed record GiayHuyPublicRow(Guid Id, string Year, string Province, string DecisionNumber,
    string CertificateSerial, string DecisionContent);
public sealed record GiayHuyPublication(Guid Id, string Sha256, bool IncludeDecisionContent = false);
public sealed class GiayHuyLookupModel
{
    public int? Year { get; init; }
    public string? Province { get; init; }
    public string? Query { get; init; }
    public int Page { get; init; } = 1;
    public int Total { get; init; }
    public bool Unavailable { get; init; }
    public IReadOnlyList<int> Years { get; init; } = [];
    public IReadOnlyList<string> Provinces { get; init; } = [];
    public IReadOnlyList<GiayHuyPublicRow> Rows { get; init; } = [];
    public const int PageSize = 10;
}

public interface IGiayHuyLookup
{
    Task<GiayHuyLookupModel> SearchAsync(int? year, string? province, string? query, int page, CancellationToken cancellationToken);
    Task<(GiayHuyPublicRow? Row, bool Unavailable)> DetailAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class GiayHuyLookup(IConfiguration configuration, ILogger<GiayHuyLookup> logger) : IGiayHuyLookup
{
    public const string Route = "/cong-khai-thong-tin/giay-chung-nhan-sai-hong-huy";
    // Bound both memory/SQL reads and simultaneous source connections. No retries.
    private static readonly SemaphoreSlim Connections = new(2, 2);
    public const int MaximumApprovedRecords = 1000;

    public static string PublicationHash(GiayHuyPublicRow row) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row))));

    public static bool Approved(GiayHuyPublicRow row, GiayHuyPublication approval) =>
        row.Id == approval.Id && approval.Sha256.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(PublicationHash(row)),
            Encoding.ASCII.GetBytes(approval.Sha256.ToLowerInvariant()));

    public static GiayHuyLookupModel Filter(IReadOnlyList<GiayHuyPublicRow> rows, int? year,
        string? province, string? query, int page)
    {
        var years = rows.Select(x => int.TryParse(x.Year, out var n) ? (int?)n : null)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().OrderDescending().ToArray();
        var byYear = rows.Where(x => year == null || x.Year == year.Value.ToString(CultureInfo.InvariantCulture)).ToArray();
        var provinces = byYear.Select(x => x.Province).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), true)).ToArray();
        // A year change must clear an obsolete province instead of hiding valid rows.
        if (!provinces.Contains(province, StringComparer.Ordinal)) province = null;
        var term = (query ?? "").Trim();
        var matches = byYear.Where(x => province == null || x.Province == province)
            .Where(x => term.Length == 0 || Contains(x.DecisionNumber, term) ||
                Contains(x.CertificateSerial, term) || Contains(x.DecisionContent, term))
            .OrderByDescending(x => int.TryParse(x.Year, out var n) ? n : 0)
            .ThenBy(x => x.DecisionNumber, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
        page = Math.Clamp(page, 1, Math.Max(1, (matches.Length + 9) / 10));
        return new() { Year = year, Province = province, Query = term, Page = page, Total = matches.Length,
            Years = years, Provinces = provinces, Rows = matches.Skip((page - 1) * 10).Take(10).ToArray() };
    }

    private static bool Contains(string value, string term) => SearchKey(value).Contains(SearchKey(term), StringComparison.Ordinal);
    private static string SearchKey(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
        return result.ToString();
    }

    public async Task<GiayHuyLookupModel> SearchAsync(int? year, string? province, string? query, int page,
        CancellationToken cancellationToken)
    {
        try { return Filter(await ReadApprovedAsync(cancellationToken), year, province, query, page); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is SqlException or InvalidOperationException or ArgumentException or TimeoutException or OperationCanceledException)
        {
            // Never log exception messages: provider exceptions may contain infrastructure/private values.
            logger.LogWarning("GiayHuy lookup unavailable; type={Type}; sqlNumber={Number}", e.GetType().Name,
                e is SqlException sql ? sql.Number : 0);
            return new() { Year = year, Province = province, Query = query, Unavailable = true };
        }
    }

    public async Task<(GiayHuyPublicRow? Row, bool Unavailable)> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        try { return ((await ReadApprovedAsync(cancellationToken)).SingleOrDefault(x => x.Id == id), false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is SqlException or InvalidOperationException or ArgumentException or TimeoutException or OperationCanceledException)
        {
            logger.LogWarning("GiayHuy detail unavailable; type={Type}; sqlNumber={Number}", e.GetType().Name, e is SqlException sql ? sql.Number : 0);
            return (null, true);
        }
    }

    private async Task<IReadOnlyList<GiayHuyPublicRow>> ReadApprovedAsync(CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("GiayHuy:Publication");
        // Neither source confirmation nor a database credential authorizes public disclosure.
        if (string.IsNullOrWhiteSpace(section["ApprovalReference"]) ||
            !DateTimeOffset.TryParse(section["ExpiresUtc"], out var expires) || expires <= DateTimeOffset.UtcNow)
            return [];
        var approvals = section.GetSection("Records").Get<GiayHuyPublication[]>() ?? [];
        if (approvals.Length == 0) return [];
        if (approvals.Length > MaximumApprovedRecords || approvals.Any(x => x.Id == Guid.Empty ||
            x.Sha256 is null || !System.Text.RegularExpressions.Regex.IsMatch(x.Sha256, "\\A[0-9a-fA-F]{64}\\z")) ||
            approvals.Select(x => x.Id).Distinct().Count() != approvals.Length)
            throw new InvalidOperationException("Invalid publication policy.");
        var raw = configuration.GetConnectionString("GiayHuyReadOnly");
        var sql = ValidateConnection(raw, configuration["GiayHuy:ExpectedLogin"]);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        if (!await Connections.WaitAsync(TimeSpan.FromSeconds(1), budget.Token)) throw new TimeoutException();
        try
        {
            await using var connection = new SqlConnection(sql.ConnectionString);
            await connection.OpenAsync(budget.Token);
            await using (var guard = new SqlCommand(IdentitySql, connection) { CommandTimeout = 4 })
            {
                guard.Parameters.Add("@login", SqlDbType.NVarChar, 128).Value = sql.UserID;
                if (Convert.ToInt32(await guard.ExecuteScalarAsync(budget.Token), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("Source identity/SELECT-only guard failed.");
            }
            await using var command = new SqlCommand(PublicRowsSql, connection) { CommandTimeout = 6 };
            command.Parameters.Add("@approved", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(approvals);
            var result = new List<GiayHuyPublicRow>();
            var seen = new HashSet<Guid>();
            var lookup = approvals.ToDictionary(x => x.Id);
            await using var reader = await command.ExecuteReaderAsync(budget.Token);
            while (await reader.ReadAsync(budget.Token))
            {
                if (result.Count >= MaximumApprovedRecords || !seen.Add(reader.GetGuid(0)))
                    throw new InvalidOperationException("Ambiguous effective source rows.");
                var row = new GiayHuyPublicRow(reader.GetGuid(0), reader.GetString(1).Trim(), reader.GetString(2).Trim(),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5));
                if (Approved(row, lookup[row.Id])) result.Add(row with { Province = GiayHuyProvince.Normalize(row.Province) });
            }
            return result;
        }
        finally { Connections.Release(); }
    }

    public static SqlConnectionStringBuilder ValidateConnection(string? raw, string? expectedLogin)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(expectedLogin))
            throw new InvalidOperationException("Separate SELECT-only source credential required.");
        var sql = new SqlConnectionStringBuilder(raw);
        if (sql.DataSource != "45.120.229.43,1433" || sql.InitialCatalog != "QuanLyDuAnTrungTamDuLieuDatDai" ||
            sql.IntegratedSecurity || sql.UserID != expectedLogin || sql.UserID.Equals("sa", StringComparison.OrdinalIgnoreCase) ||
            sql.UserID.Equals("website_tin_tuc_app", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(sql.Password) ||
            !sql.Encrypt.Equals(SqlConnectionEncryptOption.Mandatory) || sql.TrustServerCertificate ||
            sql.Authentication is not (SqlAuthenticationMethod.NotSpecified or SqlAuthenticationMethod.SqlPassword) ||
            !string.IsNullOrEmpty(sql.AttachDBFilename))
            throw new InvalidOperationException("Invalid source SQL connection policy.");
        sql.ConnectTimeout = 4;
        sql.ApplicationIntent = ApplicationIntent.ReadOnly;
        sql.ApplicationName = "LandPortal.GiayHuy.ReadOnly";
        sql.MaxPoolSize = 2;
        sql.ConnectRetryCount = 0;
        sql.PersistSecurityInfo = false;
        return sql;
    }

    public const string IdentitySql = """
        SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) = N'WIN-S1IBJB84OOO'
          AND DB_NAME() = N'QuanLyDuAnTrungTamDuLieuDatDai' AND ORIGINAL_LOGIN() = @login
          AND IS_SRVROLEMEMBER(N'sysadmin') = 0 AND IS_MEMBER(N'db_owner') = 0
          AND IS_SRVROLEMEMBER(N'serveradmin') = 0 AND IS_SRVROLEMEMBER(N'securityadmin') = 0
          AND IS_SRVROLEMEMBER(N'setupadmin') = 0 AND IS_SRVROLEMEMBER(N'processadmin') = 0
          AND IS_SRVROLEMEMBER(N'diskadmin') = 0 AND IS_SRVROLEMEMBER(N'dbcreator') = 0
          AND IS_SRVROLEMEMBER(N'bulkadmin') = 0 AND IS_MEMBER(N'db_securityadmin') = 0
          AND IS_MEMBER(N'db_accessadmin') = 0 AND IS_MEMBER(N'db_backupoperator') = 0
          AND IS_MEMBER(N'db_ddladmin') = 0 AND IS_MEMBER(N'db_datawriter') = 0
          AND HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CONTROL') = 0
          AND HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER') = 0
          AND HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'SELECT') = 0
          AND NOT EXISTS (SELECT 1 FROM sys.tables t CROSS APPLY
            (VALUES (N'INSERT'),(N'UPDATE'),(N'DELETE'),(N'ALTER'),(N'CONTROL')) p(permission)
            WHERE HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(t.schema_id))+N'.'+QUOTENAME(t.name),N'OBJECT',p.permission)=1)
          AND NOT EXISTS (SELECT 1 FROM (VALUES
            (N'GiayChungNhanHuy',N'giayChungNhanHuyID'),(N'GiayChungNhanHuy',N'namPhatHanh'),
            (N'GiayChungNhanHuy',N'donViHanhChinh'),(N'GiayChungNhanHuy',N'soQuyetDinh'),
            (N'GiayChungNhanHuy',N'soHieuGiayChungNhan'),(N'GiayChungNhanHuy',N'noiDungQuyetDinh'),
            (N'GiayChungNhanHuy',N'nguonDuLieuDoiSoatID'),(N'GiayChungNhanHuy',N'hieuLuc'),(N'GiayChungNhanHuy',N'isDel'),
            (N'GiayChungNhanHuyGiaTriChot',N'giaTriChotID'),(N'GiayChungNhanHuyGiaTriChot',N'giaTri'),
            (N'GiayChungNhanHuyGiaTriChot',N'giayChungNhanHuyID'),(N'GiayChungNhanHuyGiaTriChot',N'fieldKey'),
            (N'GiayChungNhanHuyGiaTriChot',N'hieuLuc'),(N'GiayChungNhanHuyGiaTriChot',N'isDel'),
            (N'GiayChungNhanHuyDuLieuDoiSoat',N'duLieuDoiSoatID'),(N'GiayChungNhanHuyDuLieuDoiSoat',N'giayChungNhanHuyID'),
            (N'GiayChungNhanHuyDuLieuDoiSoat',N'taiLieuID'),(N'GiayChungNhanHuyDuLieuDoiSoat',N'trangThaiXacNhan'),
            (N'GiayChungNhanHuyDuLieuDoiSoat',N'hieuLuc'),(N'GiayChungNhanHuyDuLieuDoiSoat',N'isDel'),
            (N'GiayChungNhanHuyDuLieuDoiSoat',N'thoiGianXacNhan'),
            (N'GiayChungNhanHuyTaiLieu',N'taiLieuID'),(N'GiayChungNhanHuyTaiLieu',N'hieuLuc'),
            (N'GiayChungNhanHuyTaiLieu',N'isDel'),(N'GiayChungNhanHuyTaiLieu',N'trangThaiDoiSoat')) required(tableName,columnName)
            WHERE ISNULL(HAS_PERMS_BY_NAME(N'dbo.'+required.tableName,N'OBJECT',N'SELECT',required.columnName,N'COLUMN'),0)<>1)
          THEN 1 ELSE 0 END;
        """;

    // Whitelisted projection only. A present-but-empty final value MUST NOT fall back to the typed column.
    // No owner names, addresses, identity documents, OCR, file paths or audit data are queried.
    public static readonly string PublicRowsSql = BuildPublicRowsSql();
    private static string BuildPublicRowsSql()
    {
        var fields = new[] { ("y", "NamPhatHanh", "CONVERT(nvarchar(10),g.namPhatHanh)"),
            ("p", "DonViHanhChinh", "g.donViHanhChinh"), ("d", "SoQuyetDinh", "g.soQuyetDinh"),
            ("s", "SoHieuGiayChungNhan", "g.soHieuGiayChungNhan"), ("c", "NoiDungQuyetDinh", "g.noiDungQuyetDinh") };
        string Effective((string Alias, string Key, string Column) f) =>
            $"COALESCE(CASE WHEN {f.Alias}.giaTriChotID IS NOT NULL THEN {f.Alias}.giaTri ELSE {f.Column} END,N'')";
        return "SELECT TOP (1001) g.giayChungNhanHuyID," + string.Join(",", fields.Select(f => f.Item1 == "c" ?
            $"CASE WHEN a.IncludeDecisionContent=1 THEN {Effective(f)} ELSE N'' END" : Effective(f))) + """
             FROM OPENJSON(@approved) WITH (Id uniqueidentifier, IncludeDecisionContent bit) a
             INNER JOIN dbo.GiayChungNhanHuy g ON g.giayChungNhanHuyID=a.Id
             CROSS APPLY (SELECT TOP (1) d.taiLieuID
                FROM dbo.GiayChungNhanHuyDuLieuDoiSoat d
                WHERE d.giayChungNhanHuyID=g.giayChungNhanHuyID AND d.trangThaiXacNhan=N'DA_XAC_NHAN'
                  AND d.hieuLuc=1 AND d.isDel=0
                  AND (g.nguonDuLieuDoiSoatID IS NULL OR d.duLieuDoiSoatID=g.nguonDuLieuDoiSoatID)
                ORDER BY CASE WHEN d.duLieuDoiSoatID=g.nguonDuLieuDoiSoatID THEN 0 ELSE 1 END,
                    d.thoiGianXacNhan DESC,d.duLieuDoiSoatID) confirmed
             INNER JOIN dbo.GiayChungNhanHuyTaiLieu t ON t.taiLieuID=confirmed.taiLieuID
                AND t.hieuLuc=1 AND t.isDel=0 AND ISNULL(t.trangThaiDoiSoat,N'')<>N'LOAI_BO'
            """ + string.Join("\n", fields.Select(f => $"""
             LEFT JOIN dbo.GiayChungNhanHuyGiaTriChot {f.Item1} ON {f.Item1}.giayChungNhanHuyID=g.giayChungNhanHuyID
                AND {f.Item1}.fieldKey=N'{f.Item2}' AND {f.Item1}.hieuLuc=1 AND {f.Item1}.isDel=0
            """)) + " WHERE g.hieuLuc=1 AND g.isDel=0 AND " +
            string.Join(" AND ", fields.Select(f => f.Item1 == "c" ?
                $"(a.IncludeDecisionContent=0 OR DATALENGTH({Effective(f)})<=32000)" :
                $"DATALENGTH({Effective(f)})<={(f.Item1 == "y" ? 20 : f.Item1 == "p" ? 400 : 1000)}")) +
            " ORDER BY g.giayChungNhanHuyID;";
    }
}
