using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace LandPortal.Application;
public static partial class TextNormalizer
{
    public static string Search(string? text)
    {
        var result = new StringBuilder();
        foreach (var ch in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            result.Append(ch is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(ch));
        }
        return Spaces().Replace(result.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
public static class Permissions
{
    public const string Create = "content.create";
    public const string ViewOwn = "content.view.own";
    public const string ViewAll = "content.view.all";
    public const string EditOwn = "content.edit.own";
    public const string EditAll = "content.edit.all";
    public const string Submit = "content.submit";
    public const string Publish = "content.publish";
    public const string PublishDirect = Publish;
    public const string PublishApproved = "content.publish.approved";
    public const string Review = "content.review";
    public const string Unpublish = "content.unpublish";
    public const string Trash = "content.trash";
    public const string Restore = "content.restore";
    public const string ManageMedia = "media.manage";
    public const string ManageVideos = "video.manage";
    public const string PublishDisclosure="content.disclosure.publish";
    public const string Moderate = "interaction.moderate";
    public const string ManageSettings = "settings.manage";
    public const string ManageUsers = "users.manage";
    public const string ManageRoles = "roles.manage";
    public const string GrantPermissions = "permissions.grant";
    public const string GrantPrivileged = "permissions.grant.privileged";
    public const string ManageSite = "site.manage";
    public const string PublishSite = "site.publish";
    public const string ViewSite = "site.view";
    public const string Schedule = "content.schedule";
    public const string Import = "migration.run";
    public const string CommentRead="interaction.comment.read", CommentModerate="interaction.comment.moderate", CommentReply="interaction.comment.reply";
    public const string InquiryRead="interaction.inquiry.read", InquiryList="interaction.inquiry.list", InquiryAssign="interaction.inquiry.assign", InquiryRespond="interaction.inquiry.respond", InquiryPublish="interaction.inquiry.publish";
    public const string ContactRead="interaction.contact.read", ContactProcess="interaction.contact.process", ConsultationRead="interaction.consultation.read", ConsultationProcess="interaction.consultation.process", InteractionExport="interaction.export";
    public const string ContactList="interaction.contact.list",ContactFilesRead="interaction.contact.file.read",ContactExportPii="interaction.contact.export.pii";
    public const string InquirySupervise="interaction.inquiry.supervise", InquiryFilesRead="interaction.inquiry.file.read", InquiryExport="interaction.inquiry.export", InquiryExportPii="interaction.inquiry.export.pii", InquiryCalendarManage="interaction.inquiry.calendar.manage", InquiryExtend="interaction.inquiry.extend", OutboxReconcile="outbox.reconcile";
    public static readonly string[] All = [Create, ViewOwn, ViewAll, EditOwn, EditAll, Submit, PublishDirect, PublishApproved, Review, Unpublish, Trash, Restore, ManageMedia, Moderate, ManageSettings, ManageUsers, ManageRoles, GrantPermissions, GrantPrivileged, ManageSite, PublishSite, ViewSite, Schedule, Import,CommentRead,CommentModerate,CommentReply,InquiryRead,InquiryList,InquiryAssign,InquiryRespond,InquiryPublish,ContactRead,ContactProcess,ConsultationRead,ConsultationProcess,InteractionExport,InquirySupervise,InquiryFilesRead,InquiryExport,InquiryExportPii,InquiryCalendarManage,InquiryExtend,OutboxReconcile,ManageVideos,ContactList,ContactFilesRead,ContactExportPii,PublishDisclosure];
}
