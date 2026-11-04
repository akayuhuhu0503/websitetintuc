-- READ ONLY. Run manually in SSMS with an authorized DBA connection to the source database.
SET NOCOUNT ON;
IF CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'WIN-S1IBJB84OOO'
    THROW 51000, 'Unexpected physical SQL server.', 1;
IF DB_NAME()<>N'QuanLyDuAnTrungTamDuLieuDatDai'
    THROW 51000, 'Select the source database before running this inspection.', 1;
SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) PhysicalServer, DB_NAME() DatabaseName;
SELECT COUNT_BIG(1) OfficialEligibleCount
FROM dbo.GiayChungNhanHuy g
WHERE g.hieuLuc=1 AND g.isDel=0 AND EXISTS (
    SELECT 1 FROM dbo.GiayChungNhanHuyDuLieuDoiSoat d
    INNER JOIN dbo.GiayChungNhanHuyTaiLieu t ON t.taiLieuID=d.taiLieuID
      AND t.hieuLuc=1 AND t.isDel=0 AND ISNULL(t.trangThaiDoiSoat,N'')<>N'LOAI_BO'
    WHERE d.giayChungNhanHuyID=g.giayChungNhanHuyID
      AND d.trangThaiXacNhan=N'DA_XAC_NHAN' AND d.hieuLuc=1 AND d.isDel=0
      AND (g.nguonDuLieuDoiSoatID IS NULL OR d.duLieuDoiSoatID=g.nguonDuLieuDoiSoatID));
-- This count measures eligible official records, NOT permission to publish.
SELECT s.name SchemaName,t.name TableName,c.name ColumnName,ty.name TypeName,c.max_length,c.is_nullable
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE s.name=N'dbo' AND t.name IN (N'GiayChungNhanHuy',N'GiayChungNhanHuyGiaTriChot',N'GiayChungNhanHuyDuLieuDoiSoat',N'GiayChungNhanHuyTaiLieu')
ORDER BY t.name,c.column_id;
