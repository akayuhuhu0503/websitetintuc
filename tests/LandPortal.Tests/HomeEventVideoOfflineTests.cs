using System.Reflection;
using System.ComponentModel.DataAnnotations;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LandPortal.Tests;
public static class HomeEventVideoOfflineTests
{
    public static void Run()
    {
        using var db=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer("Server=127.0.0.1;Database=OfflineModelOnly;Integrated Security=true;Encrypt=true").Options);
        Check(!db.Database.HasPendingModelChanges(),"migration snapshot matches model");
        var sql=db.GetService<IMigrator>().GenerateScript("20261102042625_DraftConsultationEmail","20261110050000_HomeEventBanner");
        Check(sql.Contains("ADD [Description]")&&sql.Contains("ADD [Enabled]")&&!sql.Contains("DROP TABLE"),"additive banner schema only");
        var original=new SiteConfigurationRevision();original.Banners.Add(new(){Label="Fixture",Alt="Fixture",MediaAssetId=Guid.NewGuid()});
        var legacy=(string)typeof(SiteConfigurationService).GetMethod("LegacyHash",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[original])!;
        Check(SiteConfigurationService.Hash(original)==legacy,"existing revision hashes preserved");
        var draft=SiteConfigurationService.Clone(original,"fixture",DateTimeOffset.UtcNow);draft.Banners[0].Enabled=false;draft.Banners[0].Description="Nội dung kiểm thử";
        Check(SiteConfigurationService.Hash(draft)!=legacy&&original.Banners[0].Enabled&&original.Banners[0].Description=="","draft isolated and new fields hash-bound");
        var restored=SiteConfigurationService.Clone(draft,"fixture",DateTimeOffset.UtcNow);
        Check(!restored.Banners[0].Enabled&&restored.Banners[0].Description==draft.Banners[0].Description,"restore preserves banner state and description");
        var invalid=new SiteBannerInput{Description=new string('x',501)};
        Check(!Validator.TryValidateObject(invalid,new ValidationContext(invalid),[],true),"description length validation");
        var card=new ContentCard(Guid.NewGuid(),"fixture","Fixture",null,null,null,"Video");
        Check(new HomeVideoCard(card,Guid.NewGuid(),3661).Duration=="1:01:01"&&new HomeVideoCard(card,Guid.NewGuid(),null).Duration==null,"optional video duration");
        var video=new VideoFile{State=VideoState.Ready,Original=new(){Origin="VideoUpload",ScanStatus="Clean",ValidationStatus="VideoValidated",Sha256=new string('a',64),IsQuarantined=true},ScannedSha256=new string('a',64),ScannedUtc=DateTimeOffset.UtcNow,ReadyUtc=DateTimeOffset.UtcNow,ScannerVersion="fixture-only",ScannerProvider=LanMediaSamples.Provider,VideoCodec="h264",Mp4Bytes=100,PosterBytes=100,Mp4Sha256=new string('b',64),PosterSha256=new string('c',64)};
        Check(VideoReadiness.HasProof(video,true),"Ready derived video allowed while original remains quarantined");
        Check(!VideoReadiness.HasProof(video,false),"LAN anonymous scan proof rejected outside Development");
        video.Original.ScanStatus="NotScanned";
        Check(!VideoReadiness.HasProof(video,true),"unscanned video denied even on Local");
        Console.WriteLine("Home event/video: 10 focused checks PASS; SQL connections=0; live CMS/video playback not exercised by this suite.");
    }
    private static void Check(bool pass,string name){if(!pass)throw new InvalidOperationException(name);}
}
