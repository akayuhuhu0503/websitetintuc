namespace LandPortal.Web.Services;
public static class GiayHuyViewDefinition
{
    public static string Build()
    {
        var fields=new[]{("dt","NgayPhatHanh","CONVERT(nvarchar(10),g.ngayPhatHanh,103)"),("no","SoQuyetDinh","g.soQuyetDinh"),("body","NoiDungQuyetDinh","g.noiDungQuyetDinh"),("serial","SoHieuGiayChungNhan","g.soHieuGiayChungNhan"),("reg","SoVaoSo","g.soVaoSo"),("o1","TenChuSuDung1","g.tenChuSuDung1"),("o2","TenChuSuDung2","g.tenChuSuDung2"),("yr","NamPhatHanh","CONVERT(nvarchar(10),g.namPhatHanh)"),("prov","DonViHanhChinh","g.donViHanhChinh")};
        var projection=string.Join(",\n",fields.Select(f=>$"COALESCE(CASE WHEN [{f.Item1}].giaTriChotID IS NOT NULL THEN [{f.Item1}].giaTri ELSE {f.Item3} END,N'') AS [{f.Item1}]"));
        var joins=string.Join("\n",fields.Select(f=>$"LEFT JOIN dbo.GiayChungNhanHuyGiaTriChot [{f.Item1}] ON [{f.Item1}].giayChungNhanHuyID=g.giayChungNhanHuyID AND [{f.Item1}].fieldKey=N'{f.Item2}' AND [{f.Item1}].hieuLuc=1 AND [{f.Item1}].isDel=0"));
        var map=string.Join(",\n",GiayHuyProvince.All.SelectMany(x=>x.SourceNames.Select(a=>$"(N'{a.Replace("'","''")}',N'{x.Key}')")));
        return """
            CREATE VIEW dbo.vw_GiayChungNhanHuyCongKhai AS
            WITH Effective AS (SELECT g.giayChungNhanHuyID RecordId,
            """+projection+"\n"+"""
              FROM dbo.GiayChungNhanHuy g
              CROSS APPLY(SELECT TOP(1) d.taiLieuID FROM dbo.GiayChungNhanHuyDuLieuDoiSoat d
                WHERE d.giayChungNhanHuyID=g.giayChungNhanHuyID AND d.trangThaiXacNhan=N'DA_XAC_NHAN'
                  AND d.hieuLuc=1 AND d.isDel=0 AND(g.nguonDuLieuDoiSoatID IS NULL OR d.duLieuDoiSoatID=g.nguonDuLieuDoiSoatID)
                ORDER BY CASE WHEN d.duLieuDoiSoatID=g.nguonDuLieuDoiSoatID THEN 0 ELSE 1 END,d.thoiGianXacNhan DESC,d.duLieuDoiSoatID) confirmed
              INNER JOIN dbo.GiayChungNhanHuyTaiLieu t ON t.taiLieuID=confirmed.taiLieuID
                AND t.hieuLuc=1 AND t.isDel=0 AND ISNULL(t.trangThaiDoiSoat,N'')<>N'LOAI_BO'
            """+"\n"+joins+"\n"+"""
              WHERE g.hieuLuc=1 AND g.isDel=0
            ), Parsed AS (
              SELECT RecordId,dt,[no],body,serial,reg,o1,o2,yr,
                COALESCE(TRY_CONVERT(date,NULLIF(LTRIM(RTRIM(dt)),N''),103),TRY_CONVERT(date,NULLIF(LTRIM(RTRIM(dt)),N''),105),TRY_CONVERT(date,NULLIF(LTRIM(RTRIM(dt)),N''),23)) IssueDate,
                LOWER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(prov,N'.',N' '),N'  ',N' '),N'  ',N' '),N'đ',N'd'),N'Đ',N'D')))) COLLATE Latin1_General_100_CI_AI ProvinceRaw
              FROM Effective
            ), Normalized AS (
              SELECT RecordId,dt,[no],body,serial,reg,o1,o2,yr,IssueDate,
                CASE WHEN ProvinceRaw LIKE N'thành phố %' THEN SUBSTRING(ProvinceRaw,11,4000)
                  WHEN ProvinceRaw LIKE N'tỉnh %' THEN SUBSTRING(ProvinceRaw,6,4000)
                  WHEN ProvinceRaw LIKE N'tp %' THEN SUBSTRING(ProvinceRaw,4,4000) ELSE ProvinceRaw END ProvinceName
              FROM Parsed
            )
            SELECT n.RecordId,n.IssueDate,COALESCE(CONVERT(nvarchar(10),n.IssueDate,103),n.dt) IssueDateText,
              n.[no] DecisionNumber,n.body DecisionContent,n.serial CertificateSerial,n.reg RegisterNumber,
              n.o1 Owner1,n.o2 Owner2,n.yr YearText,TRY_CONVERT(int,NULLIF(LTRIM(RTRIM(n.yr)),N'')) YearNumber,
              COALESCE(mapping.ProvinceKey,N'chua-xac-dinh') ProvinceKey
            FROM Normalized n LEFT JOIN(VALUES
            """+map+"""
            ) mapping(SourceName,ProvinceKey) ON n.ProvinceName=REPLACE(REPLACE(mapping.SourceName,N'đ',N'd'),N'Đ',N'D') COLLATE Latin1_General_100_CI_AI;
            """;
    }
}
