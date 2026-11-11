using LandPortal.Infrastructure;
using Microsoft.Extensions.Configuration;

internal static class MinimalConfigurationChecks
{
    public static void Run(string path)
    {
        // Input is private; never print settings or underlying exception messages.
        var c=new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(path),false,false).Build();
        int passed=0;
        void Check(bool value,string name){if(!value)throw new InvalidOperationException("Offline config check failed: "+name);passed++;}
        void Denied(Action action,string name){var denied=false;try{action();}catch(InvalidOperationException){denied=true;}Check(denied,name);}
        Check(UatDeployment.Validate(c,"Staging"),"UAT technical guards");
        var video=VideoOptions.FromConfiguration(c,"Staging");
        var media=MediaUploadOptions.FromConfiguration(c,"Staging");
        Check(video.Enabled&&video.ApprovedManagedVideoWithoutAv,"video enabled and scoped no-AV");
        Check(media.ApprovedManagedImageWithoutAv&&media.ApprovedManagedDocumentsWithoutAv,"managed image/document policies");
        Check(!media.AllowUnscannedDevelopmentUploads,"development bypass remains off");
        Check(video.FfmpegPath==""&&video.FfprobePath==""&&video.FfmpegSha256==""&&video.FfprobeSha256=="","automatic discovery inputs");
        Check(c["Video:ProcessorApproved"]==null&&c["Video:LicenseReference"]==null,"no fabricated approval");
        Check(c["Mail:Transport"]=="Disabled"&&c["Mail:DeliveryEnabled"]=="false","SMTP disabled");
        Check(UatDeployment.RuntimeMigrationCount==18,"Runtime18 unchanged");
        Denied(()=>VideoOptions.FromConfiguration(c,"Production"),"Production rejected");
        Denied(()=>MediaUploadOptions.FromConfiguration(c,"Production"),"Production image/doc rejected");
        var url=c["Public:BaseUrl"];c["Public:BaseUrl"]="http://gdla.gov.vn/";
        Denied(()=>VideoOptions.FromConfiguration(c,"Staging"),"HTTP rejected");c["Public:BaseUrl"]=url;
        c["Video:Enabled"]="false";Check(!VideoOptions.FromConfiguration(c,"Staging").Enabled,"video disabled respected");c["Video:Enabled"]="true";
        c["Video:AllowManagedVideoWithoutAv"]="false";Check(!VideoOptions.FromConfiguration(c,"Staging").ApprovedManagedVideoWithoutAv,"video no-AV off respected");
        c["Media:AllowManagedImageWithoutAv"]="false";c["Media:AllowManagedDocumentsWithoutAv"]="false";
        media=MediaUploadOptions.FromConfiguration(c,"Staging");Check(!media.ApprovedManagedImageWithoutAv&&!media.ApprovedManagedDocumentsWithoutAv,"image/doc no-AV off respected");
        Console.WriteLine($"PASS: {passed} offline runtime configuration checks; no SQL connections or native execution.");
    }
}
