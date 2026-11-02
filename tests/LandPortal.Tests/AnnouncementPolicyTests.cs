using System.ComponentModel.DataAnnotations;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;

namespace LandPortal.Tests;

// Pure business/policy checks. No SQL writes, scanner invocation, or fabricated scan proof.
public static class AnnouncementPolicyTests
{
    public static void Run()
    {
        var count=0;
        void Check(bool result,string name){if(!result)throw new InvalidOperationException(name);count++;Console.WriteLine("PASS "+name);}
        Check(AnnouncementTemplatePolicy.RequiresArticleFields(ContentKind.Article)&&!AnnouncementTemplatePolicy.RequiresArticleFields(ContentKind.Announcement),"Article fields remain required; Announcement uses distinct template");
        Check(AnnouncementTemplatePolicy.ContentError("",true)==null,"Verified PDF can replace announcement body");
        Check(AnnouncementTemplatePolicy.ContentError("Thông báo có nội dung",false)==null,"Written announcement does not require a fabricated PDF or cover");
        Check(AnnouncementTemplatePolicy.ContentError(" ",false)!=null,"Empty announcement without verified PDF cannot be submitted/published");
        var draft=new EditorModel{Kind=ContentKind.Announcement,Title="Nháp thông báo"};
        Check(Validator.TryValidateObject(draft,new ValidationContext(draft),new List<ValidationResult>(),true),"Incomplete announcement draft remains valid at form layer");
        var development=new MediaUploadOptions{IsDevelopmentLoopback=true,AllowUnscannedDevelopmentUploads=true};
        var asset=new MediaAsset{MimeType="application/pdf",ByteLength=12,Origin="Source",ScanStatus="NotScanned"};
        Check(!AnnouncementTemplatePolicy.IsPdfCandidate(asset,development),"NotScanned source cannot replace body even with development exception");
        asset.ScanStatus="Infected";Check(!AnnouncementTemplatePolicy.IsPdfCandidate(asset,development),"Infected denied");
        asset.ScanStatus="Clean";asset.IsQuarantined=true;Check(!AnnouncementTemplatePolicy.IsPdfCandidate(asset,development),"Quarantined denied");
        asset.IsQuarantined=false;asset.MimeType="image/jpeg";Check(!AnnouncementTemplatePolicy.IsPdfCandidate(asset,development),"Image cannot substitute for announcement content");
        asset.MimeType="application/pdf";asset.Origin="Uploaded";asset.ValidationStatus="Validated";Check(!AnnouncementTemplatePolicy.IsPdfCandidate(asset,development),"Old upload path cannot bypass document scanner queue");
        asset.Origin="Source";asset.ValidationStatus=BatchMediaProof.Validation;
        Check(!AnnouncementTemplatePolicy.IsPdfCandidate(asset,new MediaUploadOptions()),"Development batch proof cannot authorize production PDF");
        Console.WriteLine($"Announcement policy: {count}/{count} PASS offline; actual SQL workflow/scanning NOT RUN.");
    }
}
