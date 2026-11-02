using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using LandPortal.Infrastructure;
using LandPortal.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace LandPortal.Tests;

// No SQL connection or mutations. These checks are not SQL workflow acceptance.
public static class BaEditorOfflineTests
{
    public static async Task RunAsync()
    {
        var count=0;
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);count++;Console.WriteLine("PASS "+label);}
        bool Valid(EditorModel model)=>Validator.TryValidateObject(model,new ValidationContext(model),new List<ValidationResult>(),true);
        Check(Valid(new EditorModel{Title="Bản đang soạn"}),"Draft model accepts title without publication-only fields");
        Check(!Valid(new EditorModel{Title=" "})&&!Valid(new EditorModel{Title=new string('x',501)}),"Empty and oversized draft titles rejected");
        var weekly=new ContentMetadata{WeeklyYear=2026,WeeklyWeek=43,WeeklyRowsJson="[]"};
        V05ContentPolicy.Validate(weekly,ContentKind.StaticPage,allowIncomplete:true);
        bool Refused(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
        Check(Refused(()=>V05ContentPolicy.Validate(weekly,ContentKind.StaticPage)),"Empty weekly draft accepted but submit denied");
        weekly.WeeklyRowsJson=JsonSerializer.Serialize(new[]{new WeeklyScheduleRow{Time="08:00"}});
        V05ContentPolicy.Validate(weekly,ContentKind.StaticPage,allowIncomplete:true);
        Check(Refused(()=>V05ContentPolicy.Validate(weekly,ContentKind.StaticPage)),"Partial weekly row retained as draft but cannot publish");
        weekly.WeeklyRowsJson=JsonSerializer.Serialize(new[]{new WeeklyScheduleRow{Date=new DateOnly(2026,10,19),Period="Sáng",Content="Nội dung đã xác nhận"}});
        V05ContentPolicy.Validate(weekly,ContentKind.StaticPage);
        weekly.WeeklyRowsJson=JsonSerializer.Serialize(new[]{new WeeklyScheduleRow{Date=new DateOnly(2026,11,19),Period="Sáng",Content="Sai tuần"}});
        Check(Refused(()=>V05ContentPolicy.Validate(weekly,ContentKind.StaticPage,true)),"Wrong-week dates denied even in draft; complete valid row accepted");
        var metadata=new ContentMetadata();V05ContentPolicy.Validate(metadata,ContentKind.Article);WorkflowService.ValidateDisclosureRequired(metadata);
        metadata.DisclosureGroupId=V05ContentPolicy.Groups.Keys.First();V05ContentPolicy.Validate(metadata,ContentKind.Article,true);
        IReadOnlyDictionary<string,string>? errors=null;
        try{WorkflowService.ValidateDisclosureRequired(metadata);}catch(EditorialValidationException e){errors=e.Errors;}
        Check(errors?.Count==6&&errors.Keys.All(k=>k.StartsWith("Metadata.")),"Ordinary article unaffected; disclosure submit returns six field errors");
        metadata.Province="Hà Nội";metadata.ProvinceCode="01";metadata.DisclosureYear=2026;metadata.PublicationSource="Nguồn đã xác nhận";metadata.LegalBasisReference="Căn cứ";metadata.PublicationReviewEvidence="Đã rà soát";
        WorkflowService.ValidateDisclosureRequired(metadata);
        Check(Refused(()=>V05ContentPolicy.Validate(metadata,ContentKind.LegalDocument,true)),"Complete disclosure accepted only for its supported content type");
        await using var db=new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseSqlServer("Server=offline.invalid;Database=OfflineTranslation;Integrated Security=True").Options);
        var permissions=new CurrentPermissionService(db);
        var reviewer=new ContentActor("reviewer",new HashSet<string>{Permissions.Review}){CategoryIds=new HashSet<int>{7}};
        var sql=PendingReviewQuery.Filter(permissions.Scope(db.Contents.AsNoTracking(),reviewer),db).ToQueryString();
        Check(sql.Contains("SubmittedRevisionId")&&sql.Contains("CurrentRevisionId")&&sql.Contains("EXISTS")&&sql.Contains("PrimaryCategoryId")&&sql.Contains("IsTrashed"),"Pending query translates to SQL with exact revision and category/trash predicates");
        var writer=new ContentActor("writer",new HashSet<string>{Permissions.Create,Permissions.EditOwn,Permissions.Submit,Permissions.ViewOwn,Permissions.ManageMedia});
        Check(await PendingReviewQuery.CountAsync(db,permissions,writer)==0,"Writer cannot query reviewer count (no SQL connection)");
        var interactionReviewer=new ContentActor("reviewer",new HashSet<string>{Permissions.CommentRead,Permissions.CommentModerate,Permissions.InquiryList,Permissions.InquiryRead,Permissions.InquiryRespond});
        Check(InteractionPermissions.CanList(interactionReviewer,InteractionKind.Comment)&&InteractionPermissions.CanList(interactionReviewer,InteractionKind.Inquiry)&&!InteractionPermissions.CanList(interactionReviewer,InteractionKind.Contact)&&!InteractionPermissions.CanList(writer,InteractionKind.Comment)&&!InteractionPermissions.CanList(writer,InteractionKind.Inquiry),"Proposed reviewer catalog rights allow two inboxes; Writer/contact remain denied");
        Console.WriteLine($"BA editor offline: {count}/{count} PASS; SQL save/submit and role mutation NOT RUN.");
    }
}
