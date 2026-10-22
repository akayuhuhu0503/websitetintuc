namespace LandPortal.Web.Models;
public sealed record VideoListItem(Guid Id,string Title,string State,bool IsPublic,DateTimeOffset CreatedUtc)
{
    public string FileState {get;init;}="Chưa có tệp";
    public Guid? AssetId {get;init;}
    public string Uploader {get;init;}="";
}
public sealed class VideoIndexModel
{
    public IReadOnlyList<VideoListItem> Items {get;init;}=[];
    public string? Query {get;init;}
    public string? State {get;init;}
    public int Page {get;init;}
    public int Total {get;init;}
    public bool Enabled {get;init;}
    public bool CanCreate {get;init;}
    public string Processor {get;init;}="";
}
public sealed record VideoUploadModel(Guid ContentId,string Title,string RowVersion,long MaxFileBytes,int ChunkBytes,string Processor);
public sealed record VideoEmbedModel(Guid Id,string Caption,IReadOnlyList<Guid> Tracks);
