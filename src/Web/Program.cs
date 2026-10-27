using System.Threading.RateLimiting;
using LandPortal.Application;
using LandPortal.Infrastructure;
using LandPortal.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = PortalConfiguration.Load();
builder.Configuration.AddConfiguration(config);
var connection = DatabaseSettings.Connection(builder.Configuration);
var storage = StorageOptions.FromConfiguration(builder.Configuration);
var baseUrl = builder.Configuration["Public:BaseUrl"] ?? "http://127.0.0.1:5188";
var uat=UatDeployment.Validate(builder.Configuration,builder.Environment.EnvironmentName);
var lan=LanDemoDeployment.Validate(builder.Configuration,builder.Environment.EnvironmentName);
if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var publicUri) || !uat && !lan && !publicUri.IsLoopback || publicUri.Scheme is not ("http" or "https") || publicUri.AbsolutePath != "/")
    throw new InvalidOperationException("Bản chạy local chỉ cho phép Public__BaseUrl là địa chỉ HTTP(S) loopback.");
if(!uat)builder.WebHost.UseUrls(baseUrl); // ANCM owns its backend port under IIS.
builder.Configuration["AllowedHosts"] = uat||lan ? publicUri.IdnHost : "localhost;127.0.0.1;[::1]";
var publicUrls=new PublicUrlPolicy(builder.Configuration);
builder.Services.AddSingleton(publicUrls);
builder.Services.AddSingleton(storage);
builder.Services.AddScoped<DbContextOptions<PortalDbContext>>(_ => DatabaseSettings.Options(builder.Configuration));
builder.Services.AddScoped<PortalDbContext>();
builder.Services.AddIdentity<PortalUser, IdentityRole>(PortalIdentityPolicy.Configure).AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders().AddSignInManager<PasswordOnlySignInManager>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/admin/login";
    options.AccessDeniedPath = "/admin/access-denied";
    options.Cookie.Name = "LandPortal.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = publicUri.Scheme == "https" ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    var idle=TimeSpan.FromMinutes(int.TryParse(builder.Configuration["AdminSession:IdleMinutes"],out var minutes)&&minutes is >=1 and <=120?minutes:30);
    options.ExpireTimeSpan = idle;
    options.SlidingExpiration = false;
    options.Events.OnSigningIn=context=>{AdminSessionPolicy.Touch(context.Properties,DateTimeOffset.UtcNow,idle);return Task.CompletedTask;};
    options.Events.OnRedirectToLogin=context=>
    {
        if(context.Request.Headers["X-Editor-Request"]=="true"){context.Response.StatusCode=401;return context.Response.WriteAsJsonAsync(new{message="Phiên đã hết hạn. Xác thực lại để tiếp tục; nội dung chưa lưu vẫn ở trang hiện tại."});}
        context.Response.Redirect(context.RedirectUri);return Task.CompletedTask;
    };
    options.Events.OnValidatePrincipal = async context =>
    {
        var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<PortalUser>>();
        var user = await manager.GetUserAsync(context.Principal!);
        var stamp = context.Principal?.FindFirst(manager.Options.ClaimsIdentity.SecurityStampClaimType)?.Value;
        var demoAllowed=user==null||DevDemoPolicy.Allowed(await manager.GetClaimsAsync(user),builder.Environment.EnvironmentName,builder.Configuration["Sql:ExpectedDatabase"]);
        if (user is null || !demoAllowed || user.IsDisabled || user.LockoutEnd > DateTimeOffset.UtcNow ||
            string.IsNullOrEmpty(stamp) || !string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal)||AdminSessionPolicy.Expired(context.Properties,DateTimeOffset.UtcNow,idle))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        }
        else if(AdminSessionPolicy.IsUserActivity(context.Request)){AdminSessionPolicy.Touch(context.Properties,DateTimeOffset.UtcNow,idle);context.ShouldRenew=true;}
    };
    options.Events.OnRedirectToAccessDenied=context=>
    {
        if(context.Request.Headers["X-Editor-Request"]=="true"){context.Response.StatusCode=403;return context.Response.WriteAsJsonAsync(new{message="Quyền hoặc phạm vi chuyên mục hiện tại không cho phép thao tác."});}
        context.Response.Redirect(context.RedirectUri);return Task.CompletedTask;
    };
});
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All) options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new FreshPermissionRequirement(permission)));
    options.AddPolicy("EditorAccess", policy => policy.RequireAuthenticatedUser().AddRequirements(new FreshPermissionRequirement(null)));
});
Directory.CreateDirectory(storage.KeysRoot);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(storage.KeysRoot)).SetApplicationName("LandPortal");
builder.Services.AddAntiforgery(options => { options.Cookie.Name = "LandPortal.Antiforgery"; options.Cookie.SameSite = SameSiteMode.Strict; options.HeaderName = "X-CSRF-TOKEN"; });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected=async (context,token)=>{context.HttpContext.Response.Headers.RetryAfter=context.HttpContext.Request.Path=="/tuong-tac/gui"?"600":"60";await context.HttpContext.Response.WriteAsync("Quá nhiều yêu cầu. Vui lòng chờ trước khi thử lại.",token);};
    options.AddPolicy("interaction", context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    options.AddPolicy("public-submit",context=>RateLimitPartition.GetSlidingWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new SlidingWindowRateLimiterOptions{PermitLimit=appCaptchaLimit(),Window=TimeSpan.FromMinutes(10),SegmentsPerWindow=10,QueueLimit=0}));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
int appCaptchaLimit()=>int.TryParse(builder.Configuration["Captcha:AttemptsPer10Minutes"],out var limit)&&limit is >=1 and <=20?limit:5;
builder.Services.AddScoped<InquiryCaptchaLookup>();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.ModelMetadataDetailsProviders.Add(new LandPortal.Web.Models.VietnameseValidationMetadata());
    options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(_=>"Thông tin này không được để trống.");
    options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((_,name)=>$"{name} chưa hợp lệ. Vui lòng kiểm tra lại.");
    options.ModelBindingMessageProvider.SetValueIsInvalidAccessor(_=>"Giá trị chưa hợp lệ. Vui lòng kiểm tra lại.");
    options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(name=>$"{name} phải là số.");
});
builder.Services.AddScoped<HtmlPolicy>();
builder.Services.AddScoped<DocumentUsage>();
builder.Services.AddScoped<ArticleDiscovery>();
builder.Services.AddScoped<LandStatisticsService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<PublicTraffic>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddSingleton(SchedulerOptions.FromConfiguration(builder.Configuration));
builder.Services.AddScoped<SchedulerService>();
builder.Services.AddScoped<CurrentPermissionService>();
builder.Services.AddSingleton(InteractionMailOptions.FromConfiguration(builder.Configuration,builder.Environment.EnvironmentName));
builder.Services.AddScoped<InteractionService>();
builder.Services.AddSingleton(PublicCaptchaOptions.Load(builder.Configuration,storage,builder.Environment.EnvironmentName));
builder.Services.AddScoped<PublicCaptchaService>();
builder.Services.AddSingleton(InquiryFileOptions.FromConfiguration(builder.Configuration,builder.Environment.EnvironmentName));
builder.Services.AddScoped<InquiryAttachmentService>();
builder.Services.AddSingleton<IInquiryScanner>(sp=>InquiryScannerFactory.Create(builder.Configuration,builder.Environment.EnvironmentName));
builder.Services.AddScoped<InquiryScanWorker>();
builder.Services.AddSingleton(VideoOptions.FromConfiguration(builder.Configuration,builder.Environment.EnvironmentName));
builder.Services.AddScoped<VideoUploadService>();
builder.Services.AddScoped<VideoSubtitleService>();
builder.Services.AddScoped<VideoProcessor>();
builder.Services.AddScoped<InquiryAssignmentService>();
builder.Services.AddScoped<InquiryLookupService>();
builder.Services.AddScoped<InquiryCalendarService>();
builder.Services.AddScoped<InquiryExcelService>();
builder.Services.AddScoped<InteractionBatchService>();
builder.Services.AddScoped<OutboxReconciliationService>();
builder.Services.AddScoped<AccountAdministrationService>();
builder.Services.AddScoped<IAuthorizationHandler, FreshPermissionHandler>();
builder.Services.AddScoped<MediaService>();
builder.Services.AddScoped<EditorialDocumentService>();
builder.Services.AddSingleton(MediaUploadOptions.FromConfiguration(builder.Configuration, builder.Environment.EnvironmentName));
builder.Services.AddScoped<PublicContentQuery>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SiteConfigurationService>();
builder.Services.AddScoped<PublicSiteService>();
var app = builder.Build();
// Statistics uploads stay in bounded memory before CSRF/model binding can read the form.
// LAN sample policy must reject unknown bytes before Windows real-time AV can see a temp file.
app.Use(async(context,next)=>
{
    if(context.Request.Path.Value?.TrimEnd('/').Equals("/admin/thong-ke-dat-dai/preview",StringComparison.OrdinalIgnoreCase)==true)
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IFormFeature>(new Microsoft.AspNetCore.Http.Features.FormFeature(context.Request,new Microsoft.AspNetCore.Http.Features.FormOptions{MemoryBufferThreshold=11*1024*1024,MultipartBodyLengthLimit=11*1024*1024}));
    await next();
});
var bufferedForms=StorageOptions.ResolveWithin(storage.TempRoot,"http-form-buffer");Directory.CreateDirectory(bufferedForms);Environment.SetEnvironmentVariable("ASPNETCORE_TEMP",bufferedForms);
if (bool.TryParse(builder.Configuration["Media:AllowUnscannedDevelopmentUploads"], out var requestedBypass) && requestedBypass && !app.Services.GetRequiredService<MediaUploadOptions>().DevelopmentExceptionEnabled)
    app.Logger.LogWarning("Unscanned media development exception is disabled outside Development loopback; files remain NotScanned and uploads/publication are blocked by policy.");
app.UseExceptionHandler("/loi");
if(uat){app.UseHsts();app.UseHttpsRedirection();}
app.Use(async (context, next) =>
{
    if(lan&&(DateTimeOffset.Parse(builder.Configuration["LanDemo:ExpiresUtc"]!)<=DateTimeOffset.UtcNow||context.Request.Host.Host!=publicUri.Host||context.Connection.RemoteIpAddress is not{} remote||!LanDemoDeployment.PrivateV4(remote.MapToIPv4()))){context.Response.StatusCode=403;return;}
    if(!publicUrls.AllowsHost(context.Request.Host.Host)||context.Request.Path.Value?.StartsWith("//",StringComparison.Ordinal)==true||context.Request.Path.Value?.Contains('\\')==true){context.Response.StatusCode=400;return;}
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self'; font-src 'self'; media-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    if (context.Request.Path.StartsWithSegments("/admin")) context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/admin") &&
        !context.Request.Path.StartsWithSegments("/admin/account/change-password") &&
        !context.Request.Path.StartsWithSegments("/admin/account/logout"))
    {
        var manager = context.RequestServices.GetRequiredService<UserManager<PortalUser>>();
        var user = await manager.GetUserAsync(context.User);
        if (user?.MustChangePassword == true) { context.Response.Redirect("/admin/account/change-password"); return; }
    }
    await next();
});
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "running", phase = "2B" }));
app.MapGet("/ready", async (PortalDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/robots.txt", () => Results.Text("User-agent: *\nDisallow: /\n", "text/plain"));
app.MapControllers();
app.Run();
public partial class Program { }
