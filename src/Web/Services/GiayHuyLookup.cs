using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
namespace LandPortal.Web.Services;
public sealed record GiayHuyPublicRow(Guid Id,string IssueDate,string DecisionNumber,string DecisionContent,string CertificateSerial,string RegisterNumber,string Owner1,string Owner2,string Year)
{
    public string OwnerName=>string.Join("; ",new[]{Owner1.Trim(),Owner2.Trim()}.Where(x=>x.Length>0));
}
public sealed class GiayHuyLookupModel
{
    public int? Year{get;init;} public string? Province{get;init;} public string? Query{get;init;}
    public int Page{get;init;}=1;public long Total{get;init;}public bool Unavailable{get;init;}
    public IReadOnlyList<int> Years{get;init;}=[];public IReadOnlyList<GiayHuyPublicRow> Rows{get;init;}=[];
    public double QueryMilliseconds{get;init;}public const int PageSize=20;
}
public interface IGiayHuyLookup
{
    Task<GiayHuyLookupModel> SearchAsync(int? year,string? province,string? query,int page,CancellationToken cancellationToken);
}
public sealed class GiayHuyLookup(IConfiguration configuration,ILogger<GiayHuyLookup> logger):IGiayHuyLookup
{
    public const string Route="/cong-khai-thong-tin/giay-chung-nhan-sai-hong-huy";
    public const string PublicationScope="OfficialConfirmedEightFields-OwnerApproved-20261104";
    public const string Login="landportal_giayhuy_reader";
    private static readonly SemaphoreSlim Connections=new(2,2);
    public static SqlConnectionStringBuilder ValidateConnection(IConfiguration config)
    {
        if(config["GiayHuy:PublicationScope"]!=PublicationScope)throw new InvalidOperationException("Publication scope missing.");
        var sql=new SqlConnectionStringBuilder(config.GetConnectionString("GiayHuyReadOnly")??"");
        var environment=config["DOTNET_ENVIRONMENT"]??Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??"Production";
        var lan=environment=="Development"&&config["Deployment:Profile"]=="LAN_DEMO"&&config["Sql:ExpectedDatabase"]=="LandPortal_LanDemo";
        var uat=environment is "UAT" or "Staging"&&config["Deployment:Profile"]=="UAT";
        if(sql.DataSource!="45.120.229.43,1433"||sql.InitialCatalog!="QuanLyDuAnTrungTamDuLieuDatDai"||sql.UserID!=Login||sql.IntegratedSecurity||string.IsNullOrEmpty(sql.Password)||
            !sql.Encrypt.Equals(SqlConnectionEncryptOption.Mandatory)||sql.TrustServerCertificate&&(!(lan||uat)||config["GiayHuy:LanUatTlsException"]!="OwnerApproved-20261104")||!string.IsNullOrEmpty(sql.AttachDBFilename))
            throw new InvalidOperationException("Invalid source SQL policy.");
        sql.ApplicationName="LandPortal.GiayHuy.ViewOnly";sql.ApplicationIntent=ApplicationIntent.ReadOnly;sql.ConnectTimeout=4;sql.ConnectRetryCount=0;sql.MaxPoolSize=2;sql.PersistSecurityInfo=false;sql.Enlist=false;
        return sql;
    }
    public async Task<GiayHuyLookupModel> SearchAsync(int? year,string? province,string? query,int page,CancellationToken cancellationToken)
    {
        try{
            var settings=ValidateConnection(configuration);
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);budget.CancelAfter(TimeSpan.FromSeconds(15));
            if(!await Connections.WaitAsync(TimeSpan.FromSeconds(1),budget.Token))throw new TimeoutException();
            try{
                await using var connection=new SqlConnection(settings.ConnectionString);await connection.OpenAsync(budget.Token);
                await using var guard=new SqlCommand(IdentitySql,connection){CommandTimeout=3};
                if(Convert.ToInt32(await guard.ExecuteScalarAsync(budget.Token))!=1)throw new InvalidOperationException("Source guard failed.");
                var watch=Stopwatch.StartNew();
                await using var command=new SqlCommand(YearsSql+PageSql,connection){CommandTimeout=10};
                AddFilters(command,year,province,query);command.Parameters.Add("@offset",SqlDbType.Int).Value=checked((page-1)*20);
                var years=new List<int>();var rows=new List<GiayHuyPublicRow>();long total=0;
                await using(var reader=await command.ExecuteReaderAsync(budget.Token)){
                    while(await reader.ReadAsync(budget.Token))years.Add(reader.GetInt32(0));
                    await reader.NextResultAsync(budget.Token);
                    while(await reader.ReadAsync(budget.Token)){
                        total=reader.GetInt64(0);rows.Add(new(reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetString(9)));
                    }
                }
                if(rows.Count==0){
                    await using var count=new SqlCommand("SELECT COUNT_BIG(1) FROM dbo.vw_GiayChungNhanHuyCongKhai v WHERE "+WhereSql,connection){CommandTimeout=5};
                    AddFilters(count,year,province,query);total=Convert.ToInt64(await count.ExecuteScalarAsync(budget.Token));
                }
                watch.Stop();return new(){Year=year,Province=province,Query=query,Page=page,Total=total,Years=years,Rows=rows,QueryMilliseconds=watch.Elapsed.TotalMilliseconds};
            }finally{Connections.Release();}
        }catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception e)when(e is SqlException or InvalidOperationException or ArgumentException or TimeoutException or OperationCanceledException){
            logger.LogWarning("GiayHuy unavailable; type={Type}; SQL={Number}",e.GetType().Name,e is SqlException s?s.Number:0);
            return new(){Year=year,Province=province,Query=query,Page=page,Unavailable=true};
        }
    }
    public static void AddFilters(SqlCommand command,int? year,string? province,string? query)
    {
        command.Parameters.Add("@year",SqlDbType.Int).Value=(object?)year??DBNull.Value;
        command.Parameters.Add("@province",SqlDbType.NVarChar,50).Value=string.IsNullOrEmpty(province)?DBNull.Value:province;
        var term=(query??"").Trim().Replace('đ','d').Replace('Đ','D').Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");
        command.Parameters.Add("@term",SqlDbType.NVarChar,1000).Value=term.Length==0?DBNull.Value:"%"+term+"%";
    }
    public const string YearsSql="SELECT DISTINCT YearNumber FROM dbo.vw_GiayChungNhanHuyCongKhai WHERE YearNumber BETWEEN 1 AND 9999 ORDER BY YearNumber DESC;";
    public const string WhereSql="""
        (@year IS NULL OR v.YearNumber=@year) AND (@province IS NULL OR v.ProvinceKey=@province)
        AND (@term IS NULL OR REPLACE(REPLACE(v.IssueDateText,N'đ',N'd'),N'Đ',N'D') COLLATE Latin1_General_100_CI_AI LIKE @term ESCAPE N'\'
          OR REPLACE(REPLACE(v.DecisionNumber,N'đ',N'd'),N'Đ',N'D') COLLATE Latin1_General_100_CI_AI LIKE @term ESCAPE N'\'
          OR REPLACE(REPLACE(v.DecisionContent,N'đ',N'd'),N'Đ',N'D') COLLATE Latin1_General_100_CI_AI LIKE @term ESCAPE N'\'
          OR REPLACE(REPLACE(v.CertificateSerial,N'đ',N'd'),N'Đ',N'D') COLLATE Latin1_General_100_CI_AI LIKE @term ESCAPE N'\'
          OR REPLACE(REPLACE(v.RegisterNumber,N'đ',N'd'),N'Đ',N'D') COLLATE Latin1_General_100_CI_AI LIKE @term ESCAPE N'\'
          OR REPLACE(REPLACE(v.Owner1,N'đ',N'd'),N'Đ',N'D') COLLATE Latin1_General_100_CI_AI LIKE @term ESCAPE N'\'
          OR REPLACE(REPLACE(v.Owner2,N'đ',N'd'),N'Đ',N'D') COLLATE Latin1_General_100_CI_AI LIKE @term ESCAPE N'\')
        """;
    public static readonly string PageSql="""
        SELECT COUNT_BIG(1) OVER() Total,v.RecordId,v.IssueDateText,v.DecisionNumber,v.DecisionContent,
          v.CertificateSerial,v.RegisterNumber,v.Owner1,v.Owner2,v.YearText
        FROM dbo.vw_GiayChungNhanHuyCongKhai v WHERE
        """+WhereSql+" ORDER BY v.IssueDate DESC,v.RecordId OFFSET @offset ROWS FETCH NEXT 20 ROWS ONLY OPTION(RECOMPILE);";
    public const string IdentitySql="""
        SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))=N'WIN-S1IBJB84OOO'
          AND DB_NAME()=N'QuanLyDuAnTrungTamDuLieuDatDai' AND ORIGINAL_LOGIN()=N'landportal_giayhuy_reader'
          AND IS_SRVROLEMEMBER('sysadmin')=0 AND IS_MEMBER('db_owner')=0 AND IS_MEMBER('db_datareader')=0
          AND IS_MEMBER('db_datawriter')=0 AND IS_MEMBER('db_ddladmin')=0 AND IS_MEMBER('db_securityadmin')=0
          AND HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL')=0 AND HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','SELECT')=0
          AND NOT EXISTS(SELECT 1 FROM sys.database_role_members WHERE member_principal_id=USER_ID())
          AND NOT EXISTS(SELECT 1 FROM sys.server_role_members WHERE member_principal_id=SUSER_ID())
          AND NOT EXISTS(SELECT 1 FROM sys.database_permissions WHERE grantee_principal_id=USER_ID() AND state IN('G','W')
            AND NOT(state='G' AND ((class=1 AND major_id=OBJECT_ID(N'dbo.vw_GiayChungNhanHuyCongKhai') AND minor_id=0 AND permission_name='SELECT') OR(class=0 AND permission_name='CONNECT'))))
          AND HAS_PERMS_BY_NAME('dbo.vw_GiayChungNhanHuyCongKhai','OBJECT','SELECT')=1
          AND ISNULL(HAS_PERMS_BY_NAME('dbo.GiayChungNhanHuy','OBJECT','SELECT'),0)=0
          AND ISNULL(HAS_PERMS_BY_NAME('dbo.GiayChungNhanHuyGiaTriChot','OBJECT','SELECT'),0)=0
          AND ISNULL(HAS_PERMS_BY_NAME('dbo.GiayChungNhanHuyDuLieuDoiSoat','OBJECT','SELECT'),0)=0
          AND ISNULL(HAS_PERMS_BY_NAME('dbo.GiayChungNhanHuyTaiLieu','OBJECT','SELECT'),0)=0
          AND HAS_PERMS_BY_NAME('dbo.vw_GiayChungNhanHuyCongKhai','OBJECT','INSERT')=0
          AND HAS_PERMS_BY_NAME('dbo.vw_GiayChungNhanHuyCongKhai','OBJECT','UPDATE')=0
          AND HAS_PERMS_BY_NAME('dbo.vw_GiayChungNhanHuyCongKhai','OBJECT','DELETE')=0 THEN 1 ELSE 0 END;
        """;
}
