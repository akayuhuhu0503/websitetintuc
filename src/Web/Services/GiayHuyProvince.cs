using System.Globalization;
using System.Text;

namespace LandPortal.Web.Services;

// Same exact-name aliases as the source grouping service. No speculative province mergers.
public static class GiayHuyProvince
{
    private static readonly string[] Names = "An Giang|Bà Rịa - Vũng Tàu|Bắc Giang|Bắc Kạn|Bạc Liêu|Bắc Ninh|Bến Tre|Bình Định|Bình Dương|Bình Phước|Bình Thuận|Cà Mau|Cao Bằng|Cần Thơ|Đà Nẵng|Đắk Lắk|Đắk Nông|Điện Biên|Đồng Nai|Đồng Tháp|Gia Lai|Hà Giang|Hà Nam|Hà Nội|Hà Tĩnh|Hải Dương|Hải Phòng|Hậu Giang|Hòa Bình|Hưng Yên|Khánh Hòa|Kiên Giang|Kon Tum|Lai Châu|Lâm Đồng|Lạng Sơn|Lào Cai|Long An|Nam Định|Nghệ An|Ninh Bình|Ninh Thuận|Phú Thọ|Phú Yên|Quảng Bình|Quảng Nam|Quảng Ngãi|Quảng Ninh|Quảng Trị|Sóc Trăng|Sơn La|Tây Ninh|Thái Bình|Thái Nguyên|Thanh Hóa|Thừa Thiên Huế|Tiền Giang|Thành phố Hồ Chí Minh|Trà Vinh|Tuyên Quang|Vĩnh Long|Vĩnh Phúc|Yên Bái|Huế".Split('|');
    private static readonly HashSet<string> Cities = ["Hà Nội", "Hải Phòng", "Đà Nẵng", "Cần Thơ", "Huế"];
    private static readonly Dictionary<string, string> Known = Names.ToDictionary(Key, x => x, StringComparer.Ordinal);
    public static string Normalize(string value)
    {
        var key = Key(value);
        if (key is "hcm" or "tphcm") return "Thành phố Hồ Chí Minh";
        if (!Known.TryGetValue(key, out var name)) return "Chưa xác định Tỉnh/Thành phố";
        return name.StartsWith("Thành phố ", StringComparison.Ordinal) ? name : (Cities.Contains(name) ? "Thành phố " : "Tỉnh ") + name;
    }
    private static string Key(string value)
    {
        var b = new StringBuilder();
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = ch is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(ch);
            if (char.IsLetterOrDigit(c)) b.Append(c);
            else if (b.Length > 0 && b[^1] != ' ') b.Append(' ');
        }
        var key = b.ToString().Trim();
        foreach (var prefix in new[] { "thanh pho ", "tinh ", "tp " })
            if (key.StartsWith(prefix, StringComparison.Ordinal)) return key[prefix.Length..];
        return key;
    }
}
