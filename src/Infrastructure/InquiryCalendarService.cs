using System.Data;
using System.Text.Json;
using LandPortal.Application;
using LandPortal.Domain;
using Microsoft.EntityFrameworkCore;
namespace LandPortal.Infrastructure;
public sealed record CalendarDay(DateOnly Date,bool WorkingDay,string Note);
public sealed record InquiryCalendarPolicy(Guid? CalendarId,string Label,string PolicyVersion,int WorkingDays,TimeOnly Cutoff,bool CountReceiptDay,string WeekendDays,bool IsApproved,IReadOnlyList<CalendarDay> Exceptions);
public sealed class InquiryCalendarService(PortalDbContext db,InteractionService interaction,CurrentPermissionService permissions,TimeProvider? clock=null)
{
    public const string TemporaryLabel="Tạm tính — chưa phải SLA chính thức";
    private DateTimeOffset Now=>(clock??TimeProvider.System).GetUtcNow();
    public static DateTimeOffset Compute(InquiryCalendarPolicy policy,DateTimeOffset received)
    {
        var local=received.ToOffset(TimeSpan.FromHours(7));var day=DateOnly.FromDateTime(local.DateTime);var before=TimeOnly.FromDateTime(local.DateTime)<policy.Cutoff;
        if(!policy.CountReceiptDay||!before)day=day.AddDays(1);
        var weekends=policy.WeekendDays.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();var exceptions=policy.Exceptions.ToDictionary(x=>x.Date,x=>x.WorkingDay);var remaining=policy.WorkingDays;
        for(var attempts=0;attempts<730;attempts++,day=day.AddDays(1))
        {
            var working=exceptions.TryGetValue(day,out var special)?special:!weekends.Contains((int)day.DayOfWeek);
            if(working&&--remaining==0)return new DateTimeOffset(day.ToDateTime(policy.Cutoff),TimeSpan.FromHours(7)).ToUniversalTime();
        }
        throw new InvalidOperationException("Lịch không có đủ ngày làm việc trong giới hạn.");
    }
    public static async Task ApplyNewAsync(PortalDbContext db,InteractionSubmission row,DateTimeOffset received)
    {
        var date=DateOnly.FromDateTime(received.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var calendar=await db.InquiryCalendars.AsNoTracking().Include(x=>x.Exceptions).Where(x=>x.EffectiveFrom<=date).OrderByDescending(x=>x.EffectiveFrom).ThenByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync();
        var policy=calendar==null?new InquiryCalendarPolicy(null,"local-assumption-v1","next-day-local-v1",10,new(17,0),false,"0,6",false,[]):new(calendar.Id,calendar.Label,calendar.PolicyVersion,calendar.WorkingDays,calendar.Cutoff,calendar.CountReceiptDay,calendar.WeekendDays,calendar.IsApproved,calendar.Exceptions.Select(x=>new CalendarDay(x.Date,x.WorkingDay,x.Note)).ToArray());
        row.InquiryCalendarId=policy.CalendarId;row.SlaPolicySnapshot=JsonSerializer.Serialize(policy);row.SlaIsOfficial=policy.IsApproved;row.InquiryDueUtc=Compute(policy,received);
    }
    public async Task<Guid> CreateAsync(ContentActor actor,InquiryCalendar calendar)
    {
        actor=await permissions.GetAsync(actor.UserId);if(!actor.Has(Permissions.InquiryCalendarManage)||!actor.Has(Permissions.InquirySupervise))throw new UnauthorizedAccessException();
        if(calendar.WorkingDays is <1 or >60||calendar.Label.Length is <3 or >200||calendar.PolicyVersion.Length is <3 or >80||calendar.WeekendDays.Split(',').Any(x=>!int.TryParse(x,out var n)||n is <0 or >6)||calendar.WeekendDays.Split(',').Distinct().Count()>=7||calendar.Exceptions.Count>366||calendar.Exceptions.Select(x=>x.Date).Distinct().Count()!=calendar.Exceptions.Count||calendar.Exceptions.Any(x=>x.Note.Length>200))throw new InvalidOperationException("Lịch/chính sách không hợp lệ.");
        // Business approval is external to this local milestone. Never mark a temporary calendar official via a form checkbox.
        if(calendar.IsApproved)throw new InvalidOperationException("Chưa có quy trình xác nhận nghiệp vụ cho SLA chính thức. Lịch local phải là tạm tính.");
        calendar.CreatedBy=actor.UserId;calendar.CreatedUtc=Now;db.InquiryCalendars.Add(calendar);db.AuditLogs.Add(new(){Actor=actor.UserId,Action="inquiry.calendar.created",Detail=JsonSerializer.Serialize(new{calendar.Id,calendar.EffectiveFrom,calendar.IsApproved})});await db.SaveChangesAsync();return calendar.Id;
    }
    public async Task ExtendAsync(ContentActor actor,Guid id,byte[] version,Guid command,DateTimeOffset due,string reason)
    {
        actor=await permissions.GetAsync(actor.UserId);if(!actor.Has(Permissions.InquiryRead)||!actor.Has(Permissions.InquiryExtend)||!actor.Has(Permissions.InquirySupervise))throw new UnauthorizedAccessException();
        if(command==Guid.Empty||string.IsNullOrWhiteSpace(reason)||reason.Length>500)throw new InvalidOperationException("Thiếu lý do/mã gia hạn.");
        var hash=TextNormalizer.Hash(JsonSerializer.Serialize(new{id,version,due,reason}));await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var row=await db.InteractionSubmissions.FromSqlInterpolated($"SELECT * FROM dbo.InteractionSubmissions WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync()??throw new KeyNotFoundException();if(row.Kind!=InteractionKind.Inquiry||!await interaction.Inbox(actor,InteractionKind.Inquiry).AnyAsync(x=>x.Id==id))throw new UnauthorizedAccessException();
        var previous=await db.InquiryExtensions.FindAsync(command);if(previous!=null){if(previous.SubmissionId!=id||previous.ActorId!=actor.UserId||previous.RequestHash!=hash)throw new InvalidOperationException("Mã gia hạn đã dùng.");await tx.CommitAsync();return;}
        if(!row.RowVersion.SequenceEqual(version))throw new DbUpdateConcurrencyException();if(row.InquiryDueUtc==null||due<=row.InquiryDueUtc||due>Now.AddDays(365))throw new InvalidOperationException("Hạn gia hạn không hợp lệ.");
        db.InquiryExtensions.Add(new(){Id=command,SubmissionId=id,PreviousDueUtc=row.InquiryDueUtc,DueUtc=due,ActorId=actor.UserId,ProtectedReason=interaction.Protect(reason),RequestHash=hash,CreatedUtc=Now});row.InquiryDueUtc=due;db.AuditLogs.Add(new(){Actor=actor.UserId,Action="inquiry.extended",Detail=JsonSerializer.Serialize(new{id,due})});await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
