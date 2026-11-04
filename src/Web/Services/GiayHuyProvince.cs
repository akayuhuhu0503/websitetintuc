namespace LandPortal.Web.Services;
public sealed record GiayHuyProvinceEntry(string Key, string Name, string[] SourceNames);
public static class GiayHuyProvince
{
    // Verified NQ202/2025/QH15 Art.1-2; Thua Thien Hue -> Hue: NQ175/2024/QH15.
    public const string Source="https://baochinhphu.vn/nghi-quyet-cua-quoc-hoi-ve-sap-xep-don-vi-hanh-chinh-cap-tinh-102250612191145158.htm";
    public const string Unknown="chua-xac-dinh";
    public static readonly GiayHuyProvinceEntry[] All = [
        new("ha-noi","Thành phố Hà Nội",["Hà Nội"]),new("hue","Thành phố Huế",["Huế","Thừa Thiên Huế"]),
        new("hai-phong","Thành phố Hải Phòng",["Hải Phòng","Hải Dương"]),new("da-nang","Thành phố Đà Nẵng",["Đà Nẵng","Quảng Nam"]),
        new("ho-chi-minh","Thành phố Hồ Chí Minh",["Hồ Chí Minh","HCM","TPHCM","TP HCM","Bình Dương","Bà Rịa - Vũng Tàu"]),
        new("can-tho","Thành phố Cần Thơ",["Cần Thơ","Hậu Giang","Sóc Trăng"]),
        new("cao-bang","Tỉnh Cao Bằng",["Cao Bằng"]),new("dien-bien","Tỉnh Điện Biên",["Điện Biên"]),new("ha-tinh","Tỉnh Hà Tĩnh",["Hà Tĩnh"]),
        new("lai-chau","Tỉnh Lai Châu",["Lai Châu"]),new("lang-son","Tỉnh Lạng Sơn",["Lạng Sơn"]),new("nghe-an","Tỉnh Nghệ An",["Nghệ An"]),
        new("quang-ninh","Tỉnh Quảng Ninh",["Quảng Ninh"]),new("thanh-hoa","Tỉnh Thanh Hóa",["Thanh Hóa"]),new("son-la","Tỉnh Sơn La",["Sơn La"]),
        new("tuyen-quang","Tỉnh Tuyên Quang",["Tuyên Quang","Hà Giang"]),new("lao-cai","Tỉnh Lào Cai",["Lào Cai","Yên Bái"]),
        new("thai-nguyen","Tỉnh Thái Nguyên",["Thái Nguyên","Bắc Kạn"]),new("phu-tho","Tỉnh Phú Thọ",["Phú Thọ","Vĩnh Phúc","Hòa Bình"]),
        new("bac-ninh","Tỉnh Bắc Ninh",["Bắc Ninh","Bắc Giang"]),new("hung-yen","Tỉnh Hưng Yên",["Hưng Yên","Thái Bình"]),
        new("ninh-binh","Tỉnh Ninh Bình",["Ninh Bình","Nam Định","Hà Nam"]),new("quang-tri","Tỉnh Quảng Trị",["Quảng Trị","Quảng Bình"]),
        new("quang-ngai","Tỉnh Quảng Ngãi",["Quảng Ngãi","Kon Tum"]),new("gia-lai","Tỉnh Gia Lai",["Gia Lai","Bình Định"]),
        new("khanh-hoa","Tỉnh Khánh Hòa",["Khánh Hòa","Ninh Thuận"]),new("lam-dong","Tỉnh Lâm Đồng",["Lâm Đồng","Đắk Nông","Bình Thuận"]),
        new("dak-lak","Tỉnh Đắk Lắk",["Đắk Lắk","Phú Yên"]),new("dong-nai","Tỉnh Đồng Nai",["Đồng Nai","Bình Phước"]),
        new("tay-ninh","Tỉnh Tây Ninh",["Tây Ninh","Long An"]),new("vinh-long","Tỉnh Vĩnh Long",["Vĩnh Long","Bến Tre","Trà Vinh"]),
        new("dong-thap","Tỉnh Đồng Tháp",["Đồng Tháp","Tiền Giang"]),new("ca-mau","Tỉnh Cà Mau",["Cà Mau","Bạc Liêu"]),
        new("an-giang","Tỉnh An Giang",["An Giang","Kiên Giang"])
    ];
}
