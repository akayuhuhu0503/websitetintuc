using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public static class V05ContentModel
{
    public static void Configure(ModelBuilder b)
    {
        var g=b.Entity<DisclosureGroup>();g.Property(x=>x.Id).HasMaxLength(50);g.Property(x=>x.Name).HasMaxLength(200);g.Property(x=>x.Status).HasMaxLength(40);
        g.HasData(new DisclosureGroup{Id="giay-chung-nhan-sai-hong-huy",Name="Giấy chứng nhận sai hỏng, hủy",Status="NameApproved"},new DisclosureGroup{Id="nguoi-nuoc-ngoai-dinh-cu-o-viet-nam",Name="Người nước ngoài định cư ở Việt Nam",Status="NeedsNameApproval"},new DisclosureGroup{Id="to-chuc-vi-pham-phap-luat-dat-dai",Name="Tổ chức vi phạm pháp luật đất đai",Status="NameApproved"});
        var w=b.Entity<WeeklyScheduleRelease>();w.HasIndex(x=>new{x.IsoYear,x.IsoWeek,x.Version}).IsUnique();w.HasIndex(x=>x.RevisionId).IsUnique();w.Property(x=>x.PublisherId).HasMaxLength(450);w.Property(x=>x.ChangeNote).HasMaxLength(1000);w.HasOne<ContentRevision>().WithMany().HasForeignKey(x=>x.RevisionId).OnDelete(DeleteBehavior.NoAction);w.HasOne<Content>().WithMany().HasForeignKey(x=>x.ContentId).OnDelete(DeleteBehavior.NoAction);
    }
}
public partial class PortalDbContext
{
    public DbSet<DisclosureGroup> DisclosureGroups=>Set<DisclosureGroup>();
    public DbSet<WeeklyScheduleRelease> WeeklyScheduleReleases=>Set<WeeklyScheduleRelease>();
}
