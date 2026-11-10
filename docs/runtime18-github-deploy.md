# Runtime18: cap nhat IIS tu GitHub

Pham vi: WIN-S1IBJB84OOO/website_tin_tuc da co 18 migrations, cuoi HomeEventBanner. Khong chay migration. Ban cu chi chap nhan 17 migrations nen SQL runtime bi tu choi. Web va 5 Worker dung chung Infrastructure.dll da sua.

Tren may IIS, dung source checkout NGOAI `D:\WWW_GDLA\publish\TTDL-IIS`. Can .NET SDK theo global.json, Git, va PowerShell Administrator. Giu nguyen private config, appsettings, IIS binding, media va SQL. Neu checkout co thay doi rieng thi dung de doi soat, khong reset/stash tu dong.

```powershell
git status --short
git fetch origin
git switch main
git pull --ff-only origin main
git rev-parse HEAD
# Doi chieu SHA voi commit Developer ban giao truoc khi tiep tuc.
powershell -NoProfile -File scripts/build-runtime18-hotfix.ps1
```

Lenh build in ra DUNG mot lenh `powershell -NoProfile -File "...\CAP-NHAT-RUNTIME18.ps1"`; chay lenh do de deploy. Khong chay template `scripts/update-uat-runtime18.ps1` truc tiep. Khong chep de ca publish directory.

Build Web/Worker/MigrationCli Release trong thu muc moi va tao hotfix co hash, khong can .env moi, khong ZIP. Chi thay Infrastructure.dll cua Web/Worker (day la binary duy nhat thay doi runtime); probe moi di kem de kiem tra read-only. Tu choi neu dependency ban dang chay khong tuong thich.

Truoc ghi: kiem tra site WWW_GDLA, webroot, HTTPS gdla.gov.vn:443, manifest/config/hash, dependency, SQL identity/role va exact18 history. Luu hai binary cu va manifest vao `runtime-rollback/runtime18-<id>`, xac minh hash; dung dung pool va cac dich vu Worker dang chay. Khong bat cac Worker truoc do da dung. Worker chay ngoai service se bi chan de tranh kill nham.

Sau thay binary: giu nguyen config, tu kiem tra `/`, `/tin-tuc`, `/admin/login`, `/health` deu HTTP200 truoc khi bao `UPDATED_HTTP_4_PASS`. Ket qua o `runtime-rollback/runtime18-<id>/result.json`. Neu cap nhat/HTTP that bai, tu rollback hai binary va manifest vua luu; khong rollback SQL. Binary truoc cap nhat co the chinh la ban loi schema17, nen rollback khong dong nghia website da on dinh. Khong dung backup cu chua doi chieu. Neu rollback khong hoan tat, giu dich vu dung de xu ly.

Da kiem tra Developer: build Web/Worker/probe PASS; 33 checks TLS/identity PASS; live SQL probe18 PASS; copy/hash va rollback khi ngat giua hai binary PASS. Chua thuc thi IIS that; chi ket luan khoi phuc khi bon URL PASS sau deploy. Khong can chay lai 33 tests tren IIS.
