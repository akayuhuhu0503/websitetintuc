# Development Code Signing — 20/10/2026

**Code Signing Development + fixture: PASS LOCAL sau xác nhận Windows. Actual Dev Apply vẫn chưa thực hiện/chưa đủ điều kiện. CHƯA PRODUCTION READY.**

## Cập nhật sau khi người dùng chọn Yes

Run tiếp nối `dev-signing-confirmed-20261020T030517Z-a0e9ad77`, evidence `.local/evidence/phase3f/dev-signing-confirmed-20261020T030517Z-a0e9ad77`, pointer `.local/dev-signing-confirmed-current.json`. Người dùng xác nhận chọn Yes trong Windows; đã kiểm tra Root entry thật, tiến trình Trust cũ hoàn thành exit0 và ghi report vào run trước. Không tự bấm hoặc né prompt.

- Certificate **94797BD79E275B09DEF613036BC00DF4EA91345F** đã có ở **CurrentUser/Root**. Inspect mới xác nhận chain hợp lệ, EKU Code Signing, RSA3072 và khóa NonExportable; fixture có Authenticode **Valid**, đúng signer thumbprint. Không tạo thêm certificate.
- **16/16 focused signing checks PASS,0FAIL**, trên chữ ký thật Valid và code cuối: exact plan/manifest binding; sửa signed bytes/plan/manifest, sai thumbprint, hết hạn, sai operator/database/action đều bị từ chối; fixture không thành real approval; Production/Staging/path traversal và thiếu xác nhận user bị chặn. Đã đọc lại lý do reject từng case, không tính các case bị chặn do untrusted root thành PASS phạm vi khác. Không full regression hoặc SQL/CMS tests không liên quan.
- Dev **0khác/17nhóm**:37content/37source/47revision/74alias/53NotScanned41ảnh12PDF,70capture/68snapshot/8ngoạilệ/2videometadata0file. Identity/env nguyên. So trước setup: chỉ thêm đúng certificate này vào CurrentUser/My và CurrentUser/Root; LocalMachine/My+Root, CurrentUser/TrustedPublisher, ExecutionPolicy mọi scope không đổi.
- **0 real approval file, application trusted thumbprint chưa cấu hình, draft vẫn PENDING_USER_APPROVAL. Không Dev Apply/backup/migration/grants/.env/SQLmetadata change.** Chữ ký fixture không cấp quyền cho batch thật. Còn cần người có thẩm quyền chọn application signer policy, phê duyệt exact fresh batch, proof/stamps và backup hợp lệ trước Apply.

Các đoạn dưới giữ diễn biến checkpoint trước Yes; trạng thái chờ trust đã được giải quyết bằng bằng chứng mới bên trên.

## Checkpoint trước xác nhận Windows (lịch sử)

Run `dev-signing-20261020T021606Z-c89a0a6f`, evidence `.local/evidence/phase3f/dev-signing-20261020T021606Z-c89a0a6f`, pointer `.local/dev-signing-current.json`. Chủ máy đã xác nhận lịch sử đổi tên THANGPD → MinhAnh và cùng SQL instance; không còn yêu cầu xác nhận lại chỉ vì thiếu hồ sơ rename riêng. Không sửa SQL metadata hoặc guard.

## Kết quả tại checkpoint ban đầu

- Cơ chế cần **Authenticode cho approval `.ps1`**; planJSON được bind bằng SHA256 nằm trong metadata ký, không có detached JSON signature riêng. Approval chỉ được đọc. Certificate hợp lệ, application trusted thumbprint và phê duyệt batch là ba điều kiện khác nhau.
- Tạo đúng1 certificate CurrentUser/My: **`94797BD79E275B09DEF613036BC00DF4EA91345F`**, Code Signing EKU `1.3.6.1.5.5.7.3.3`, RSA3072/SHA256, DigitalSignature; CNG ExportPolicy **None/NonExportable**. Hạn30ngày, hết `2026-11-19T02:16:50Z` (09:16:50 giờ Việt Nam). Không export private key/PFX; `.cer` public/receipt ở ignored `.local/signing/certificates/`.
- Fixture tách biệt ở `.local/signing/fixtures/<run>/`: database `LandPortal_Test`, không rows nghiệp vụ, action **FixtureVerify**, TechnicalFixture=true/BusinessApproval=false, không đặt trong thư mục approval thật. Đã ký bằng khóa thật; Windows nhận đúng signer nhưng trả **UnknownError** với lý do chain kết thúc ở root chưa được tin cậy. Không biến trạng thái này thành Valid.
- Lệnh trust chỉ hướng tới **CurrentUser/Root**. Windows mở **Security Warning**, process của lượt PID38248; tại checkpoint chưa có root entry và chưa có report trust hoàn tất. Đã yêu cầu người dùng kiểm tra/xác nhận trong hộp thoại Windows; không tự bấm, gọi công cụ khác để né prompt, đổi LocalMachine/TrustedPublisher hoặc execution policy toàn máy.
- Đã tạo draft `PENDING_USER_APPROVAL` cho batch ưu tiên, có exact plan/manifest hash, batch ID, Dev target/operatorSID. ApprovedBy/reference còn rỗng, executableAuthorization=false. **Không ký phê duyệt batch thật, không cấu hình application trusted thumbprint**, không coi việc tạo certificate là cấp quyền nhập dữ liệu.

## Script và kiểm tra

Đã tạo6script `dev-signing-common.ps1`, `dev-code-signing.ps1`, `prepare-dev-import-review.ps1`, `sign-dev-import-approval.ps1`, `test-dev-import-signature.ps1`, `test-dev-code-signing.ps1`. Không đổi code C#/SQL schema/migration. [Hướng dẫn cài, kiểm tra và lệnh vận hành](dev-code-signing-setup.md) có các lệnh dry-run/backup/sign/Apply/verify chính xác; chúng là lệnh chuẩn bị, chưa Apply.

| Kiểm tra thực | Kết quả |
|---|---|
|Parse6script PowerShell|PASS,0parse errors|
|Create + Inspect certificate thật|PASS EKU/key3072/NonExportable/hạn; chain chưa trusted|
|Ký fixture trước trust|Đã ký thật; Authenticode UnknownError do UntrustedRoot, không PASS trust|
|5negative guard độc lập|PASS Production/Staging bị chặn, thiếu xác nhận user không tạo approval, path traversal và planSHA sai bị từ chối|
|CLI hiện hành với fixture Test|PASS negative: `safe-verify` từ chối fixture ngoài import evidence scope, trước truy vấn SQL; không dùng safe-apply|
|Bộ crypto/scope đầy đủ|BLOCKED trước các case, exit2/testsRun0 khi chữ ký chưa Valid; không đếm negative bị chặn bởi root thành PASS phạm vi khác|
|Windows CurrentUser trust|PENDING Security Warning; chưa báo thành công|
|SQLTest nghiệp vụ/Dev Apply/backup/verify-after-Apply/full regression|NOT RUN; chọn fixture riêng để không cần ghi SQL|

Script kiểm tra certificate chain local với NoCheck revocation vì self-signed Dev không có CRL; đây **không phải** thay thế `Get-AuthenticodeSignature == Valid`. Verifier cũng kiểm signed bytes, plan/manifest SHA, operator, target, expiry và action. Chữ ký có thêm ApprovedBy/reference chỉ là attestation do caller cung cấp; authority nghiệp vụ bên ngoài vẫn phải được người vận hành kiểm tra. `ApprovedBatchExecution.cs` và mọi guard Apply gốc giữ nguyên.

## Hoàn tất phần đang chờ

1. Kiểm tra hộp thoại Windows Security Warning đúng certificate/subject và thumbprint ở trên. Nếu người dùng chọn Yes, tiến trình Trust hiện tại sẽ ghi `certificate-trusted-currentuser.json`; nếu không đồng ý, giữ certificate chưa trusted và checkpoint PARTIAL. Không chạy thêm nhiều lệnh trust khi hộp thoại cũ còn mở.
2. Sau trust, chạy `Inspect` với report mới và `test-dev-code-signing.ps1 -Stage Verify -Thumbprint 94797BD79E275B09DEF613036BC00DF4EA91345F -RunRecord .local/dev-signing-current.json`. Test tạo attempt mới, không ghi đè raw. Kiểm status Valid, exact thumbprint và các negative scope trên chữ ký hợp lệ trước khi báo PASS crypto.
3. Người dùng/authority chọn thumbprint cho application policy Dev qua cấu hình tiến trình được phép; không sửa `.env` hoặc tự pin vào Production. Fresh preview/proofs/stamps, backup Dev và proof dependencies còn hiệu lực; phê duyệt exact batch mới có ApprovedBy/reference/action/expiry trước khi ký real approval. Công cụ sign thật yêu cầu switch `ConfirmUserApproval`, chưa được gọi trong lượt.
4. Batch mở rộng còn521asset ngoài scope refresh và alias3589/3607/3684 cần xử lý riêng; không đưa toàn Test hoặc fixture vào Dev. Không gọi setup chứng chỉ là READY_APPLY.

Đối chiếu cuối thực chạy: **0sai khác/17nhóm**, Dev37content/37SourceRecord/47revision/74alias/53NotScanned (41ảnh/12PDF),70capture/68snapshot/8ngoạilệ/2videometadata0file; hash media/snapshot0mismatch. Identity/password/stamp/roles/claims digest và `.env` giữ nguyên. CurrentUser/My chỉ thêm đúng certificate trên; Root chưa thêm, LocalMachine/My+Root, CurrentUser/TrustedPublisher và ExecutionPolicy mọi scope không đổi. Không signed real approval file hoặc application signer pin. Xem `scope-after.json`, `dev-after.json`, `identity-after.json`.

SQL timeout vẫn ROOT CAUSE UNKNOWN; incident artifact cũ PARTIAL. Không dùng số test lịch sử làm kết quả mới. Website **CHƯA PRODUCTION READY**.
