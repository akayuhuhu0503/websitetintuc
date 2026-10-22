using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public static class VideoModel
{
    public static void Configure(ModelBuilder b)
    {
        var track=b.Entity<VideoSubtitle>();track.HasKey(x=>x.MediaAssetId);track.HasOne(x=>x.Original).WithOne().HasForeignKey<VideoSubtitle>(x=>x.MediaAssetId).OnDelete(DeleteBehavior.NoAction);track.HasOne<VideoFile>().WithMany().HasForeignKey(x=>x.VideoMediaAssetId).OnDelete(DeleteBehavior.NoAction);track.HasOne<VideoUploadSession>().WithMany().HasForeignKey(x=>x.UploadSessionId).OnDelete(DeleteBehavior.NoAction);track.Property(x=>x.RowVersion).IsRowVersion();track.Property(x=>x.ScannedSha256).HasMaxLength(64).IsUnicode(false);track.Property(x=>x.ScannerVersion).HasMaxLength(200);track.Property(x=>x.ScannerProvider).HasMaxLength(50);track.Property(x=>x.SignatureVersion).HasMaxLength(100);track.Property(x=>x.Error).HasMaxLength(200);track.HasIndex(x=>new{x.State,x.AvailableUtc});track.HasIndex(x=>x.VideoMediaAssetId);
        b.Entity<VideoUploadSession>().Property(x=>x.ChunkManifestSha256).HasMaxLength(64).IsUnicode(false);
        var s=b.Entity<VideoUploadSession>();s.HasKey(x=>x.Id);s.Property(x=>x.OwnerUserId).HasMaxLength(450);s.Property(x=>x.Extension).HasMaxLength(10);s.Property(x=>x.Mime).HasMaxLength(64);s.Property(x=>x.Sha256).HasMaxLength(64).IsUnicode(false);s.Property(x=>x.RowVersion).IsRowVersion();s.HasIndex(x=>new{x.OwnerUserId,x.State,x.ExpiresUtc});s.HasOne<Content>().WithMany().HasForeignKey(x=>x.ContentId).OnDelete(DeleteBehavior.NoAction);s.HasOne<MediaAsset>().WithMany().HasForeignKey(x=>x.MediaAssetId).OnDelete(DeleteBehavior.NoAction);
        var c=b.Entity<VideoChunk>();c.HasKey(x=>new{x.SessionId,x.Index});c.Property(x=>x.Sha256).HasMaxLength(64).IsUnicode(false);c.HasOne<VideoUploadSession>().WithMany().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.NoAction);
        var v=b.Entity<VideoFile>();v.HasKey(x=>x.MediaAssetId);v.HasOne(x=>x.Original).WithOne().HasForeignKey<VideoFile>(x=>x.MediaAssetId).OnDelete(DeleteBehavior.NoAction);v.HasOne<VideoUploadSession>().WithMany().HasForeignKey(x=>x.UploadSessionId).OnDelete(DeleteBehavior.NoAction);v.HasIndex(x=>new{x.State,x.AvailableUtc});v.Property(x=>x.RowVersion).IsRowVersion();v.Property(x=>x.Error).HasMaxLength(200);
        foreach(var name in new[]{nameof(VideoFile.ScannedSha256),nameof(VideoFile.Mp4Sha256),nameof(VideoFile.PosterSha256)})v.Property<string>(name).HasMaxLength(64).IsUnicode(false);
        foreach(var name in new[]{nameof(VideoFile.Mp4Key),nameof(VideoFile.PosterKey)})v.Property<string>(name).HasMaxLength(250);
        v.Property(x=>x.ScannerProvider).HasMaxLength(50);v.Property(x=>x.ScannerVersion).HasMaxLength(200);v.Property(x=>x.SignatureVersion).HasMaxLength(100);v.Property(x=>x.VideoCodec).HasMaxLength(40);v.Property(x=>x.AudioCodec).HasMaxLength(40);
    }
}
