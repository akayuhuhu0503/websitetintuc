using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LandPortal.Web.Models;

// Text inputs guarantee a Vietnamese display independently of browser locale.
// ISO stays supported for existing bookmarked URLs and API clients.
public sealed class VietnameseFilterDateBinder:IModelBinder
{
    public static bool TryParse(string value,out DateOnly date)=>DateOnly.TryParseExact(value,["dd/MM/yyyy","yyyy-MM-dd"],CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value=context.ValueProvider.GetValue(context.ModelName);
        if(value==ValueProviderResult.None)return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName,value);
        if(value.Length!=1){context.ModelState.TryAddModelError(context.ModelName,"Mỗi ô chỉ nhận một ngày theo dd/MM/yyyy.");return Task.CompletedTask;}
        if(string.IsNullOrWhiteSpace(value.FirstValue)){context.Result=ModelBindingResult.Success(null);return Task.CompletedTask;}
        if(TryParse(value.FirstValue!,out var date))context.Result=ModelBindingResult.Success(date);
        else context.ModelState.TryAddModelError(context.ModelName,"Ngày không hợp lệ. Nhập dd/MM/yyyy, ví dụ 23/10/2026.");
        return Task.CompletedTask;
    }
}
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class VietnameseFilterDateAttribute:ModelBinderAttribute
{
    public VietnameseFilterDateAttribute():base(typeof(VietnameseFilterDateBinder)){}
}
