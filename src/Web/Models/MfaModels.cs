using System.ComponentModel.DataAnnotations;
namespace LandPortal.Web.Models;
public sealed class MfaCodeModel
{
    [Required,StringLength(32)]public string Code { get; set; }="";
    public bool Recovery { get; set; }
}
public sealed class MfaManageModel
{
    public bool Enabled { get; set; }
    public string? SharedKey { get; set; }
    public string[] RecoveryCodes { get; set; }=[];
    public bool Required { get; set; }
}
