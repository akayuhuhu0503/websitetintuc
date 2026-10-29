# Phase3H — sửa CNG và thực thi đúng batch được duyệt

**APPLIED / VERIFIED LOCAL** cho batch20 nội dung đầu tiên; **CHƯA PRODUCTION READY**. Run `phase3h-cng-20261021T073758Z-a48acba7`; pointer `.local/phase3h-cng-current.json` và `.local/phase3h-apply-current.json`; raw/evidence `.local/evidence/phase3f/phase3h-cng-20261021T073758Z-a48acba7`. Xác nhận trong tệp người dùng đính kèm được lưu nguyên văn ở `request.txt`/`authority.json`; chỉ áp dụng một batch, không cấp scope cho12packet khác.

## Kết quả thực tế

|Câu hỏi|Kết quả có bằng chứng|
|---|---|
|1. Nguyên nhân và sửa CNG?|Verifier dùng `Get-DevSigningCertificateFacts` để mở **private key** qua GetRSAPrivateKey chỉ nhằm đọc export policy. Thao tác xác minh cần public certificate; đã thêm `Get-DevVerificationCertificateFacts` dùng bản sao RawData chỉ có public key và GetRSAPublicKey. Giữ native Authenticode/pin/chain/EKU/date/RSA/hash/SID/scope/authority; kiểm explicit BatchId đối chiếu plan. Không thay provider, ACL hoặc trust|
|2. Kiểm tra bảo mật?|**15/15 CNG-focused PASS**, riêng **8/8 existing bridge groups PASS**; parse3scriptPASS. Không cộng thành fullsuite. Native Set-AuthenticodeSignature ký fixtureTest bằng cert hiện có thành công; public-only certificate không ký được; mọi biến thể tampered/wrongpin/hash/DB/batch/expiry/operator/action bị chặn|
|3. Approval/backup/proof?|Reuse approval đã ký33b5a7ff, đúngSHA/scope/SID/hạn; backupha88e46496khớp137files+215dependencies vàSQLbaks;Dev17groupskhôngđổi trướcApply. Strictproof/currentDefender118/engine1.1.26090.9/TTL vàSQLdurablereceiptsPASS.0resign/0newbackup/0nativeRescan|
|4. Apply thực tế?|**APPLIED**; safe-applyexit0,20CreateNeedsApproval; reconcileCOMMITTED_VERIFIED20/20,0missing/0errors; verify/readbackthật đạt|
|5. Delta commit?|**20Contents +20SourceRecords +20Revisions +60MediaAssets +40SourceUrlAliases**;60asset gồm **19ảnh +41PDF**,27.972.901byte;không fixture hoặc metadata video mới|
|6. Tổng Dev?|**60 nội dung nguồn thật/60SourceRecords/71revision/120alias/120media =67ảnh+53PDF**. Scanstatus **53originalNotScanned +67Clean**;70capture/68SQLsnapshot/8ngoại lệ/2videometadata/0videofile|
|7. Verify/idempotency?|Verify0mismatch; readback20source/20revision/60media/40alias,hashbyte/date/author/category/metadata/sourceidentity/URL đều đạt. Repeat20AlreadyCommitted,0delta/receiptvẫn20;DataSHA`fb97b03e1613ab8972140cb61be6a2edd29d2866cb44aef67184fea0fa79cb48`giữ nguyên. HTTP11/11PASS riêng|
|8. Dữ liệu cũ?|**40Contents/51Revisions/40SourceRecords cũ mọi cột không đổi**,17groups0unexpected differences sau loại đúng approved delta. Postrepeat17rawgroups0diff.53originalNotScanned/52blockedledger/Identity/.envgiữ nguyên. Không sửa bài đã biên tập|
|9.12packet tiếp theo?|Còn **190content/190revision/464asset/380alias** trong12packet, chưa ký/Apply ở lượt này. Cần owner exactapproval riêng; revalidate trênDev60vàstrictproof,previewmới nếu stamp/plan thay đổi và xác nhậnSHA mới.52blocked vẫn giữ, không tính vào190|

Batch `aa52120d-a6b9-41b9-b4b5-22ced4330113`; plan/manifestSHA256 `41c7bf061b472ec0d8aa5129eaa9384bb27e1966baa977fc74c82e78f63d6dfa`; đích **MinhAnh\SQLEXPRESS/LandPortal_Dev**, DBGUIDB499750C-A0C8-4003-B7EE-DCE4938F78DC. `@@SERVERNAME=THANGPD\SQLEXPRESS` vẫn là tên đăng ký cũ đã xác minh, không sửa metadata. Migration giữ `20261015091551_V05EditorialDocuments`, datafile72MiB trướcApply, ổC còn đủ reserve4GiB; exacttarget/media-root/schema guards giữ nguyên.

## Bằng chứng CNG và ranh giới kết luận

`cng-diagnosis.json` ghi cùng tài khoản MINHANH\ADMIN/SID hiện hành, CurrentUser/My/thumb94797BD79E275B09DEF613036BC00DF4EA91345F. CảCertProvider trước/sauAuthenticode vàfreshX509Store đều tái hiện `System.ArgumentException`, HRESULT **0x80070057** (-2147024809), stackCngKey.Open→GetRSAPrivateKey: ephemeralhandle nhưng không cóEphemeralKeyopenoption. `.NET10.0.12`probe cũng lỗi tương tự (CngKey.OpenNoDuplicate); **không kết luận chỉ doPowerShell5.1, không có bằng chứng AccessDenied/thiếuquyền**.

`provider-inspection.json`: actual provider **Microsoft Software Key Storage Provider**, RSAOID1.2.840.113549.1.1.1;privatekeycó,certutil báo **NOT exportable**,native encryption testpassed. CácfixtureTestđược kýnative bằngcert này thànhcông; khôngexportkey/PFX,khôngcert/trust/ACL/executionpolicychange. Đây là bằng chứng khóa dùng được qua native signing; nguyên nhân sâu bên trong CNG/handle interoperability chưa chứng minh, không sửaWindowsđểépGetRSAPrivateKeypass. Fix ở **cách verifier gọi API sai mục đích**; provisioning/private-key inspection cũ vẫnfailclosed nếuAPIcũ lỗi, không gán giảexportpolicyNone. Thiếuprivatekey khi cầnký được kiểm bằngpublic-only certificate, **không** thayACL thật để tạoAccessDenied.

Verification mới clonepubliccert,kiểm RSA3072/chainlocal/EKUCodeSigning/hạn/Devsubject;NativeAuthenticode/pin/hash/plan/manifest/BatchID/database/operator/expiry/actions/authority còn nguyên. Kết quả `keyExportPolicy=NOT_ACCESSED_VERIFICATION` nói rõ không attestationprivatekey; provisioning/signing checks riêng giữ nguyên. Cơ sởAPI: [Microsoft GetRSAPublicKey](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.rsacertificateextensions.getrsapublickey), [GetRSAPrivateKey](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.rsacertificateextensions.getrsaprivatekey). Không bypass bridge; actualsafeApply vẫn dùngCLIsha`35624589caacf0f26737e581175355d7fd5108de24076652b157f96c1e7f5f09`/PowerShellSignatureBridge strict envelope/stderr/exit/nativechainpin như trước.

8/8bridge tests tách raw native tampered/unsigned/unpinned/wrongSHA/PowerShellfailures với structured trust/expiry/nonce/output negatives; **không** nói đã cài certificate khôngtrusted/hết hạn để test thực. Không chạy fullregression.

## Backup, approval và trình tự thực

Approval đã ký ID33b5a7ff-1ae6-4c1e-b8d3-f143469d5de1, SHA`24f8352fe2582172546b99be85120b7df0f08f85eee8cf8a4f825b94b5cc2eb1`, hạn21/10/2026 **13:16:09UTC (20:16:09VN)**;actionApplyonly. NativeValid và public-onlycertificate/exactbinding đều xác minh trướcApply. Không ký lại vìsignedbytes/manifestkhôngđổi.

Backup `C:\TrungTamDuLieuVaThongTinDatDai\website_tin_tuc\.local\backups\LandPortal_Test-20261021T015310Z\ha88e46496` tạo trước lượt này, reuse<12h:SQLDev/TestSHA/header/CHECKSUM/VERIFYONLY trước đó được giữ; lượt này rehash2SQLbak vàguardApplyđọcHEADERONLYexacttarget. File nguồn vàbảncopy137Devfiles+215dependency đượcverifylại0diff,coverageSQLTestgiữscanreceipts.137files48.806.964byte,215deps62.189.622byte;file restore-checkPASSởlượt trước,**SQLrestore drillNOTRUN**. Không gọi reuse là backupmới. Bộhaf1354be1incomplete lịch sử vẫn giữ nguyên.

|Thao tác CLI thật|Kết quả|Elapsed ms|
|---|---|---:|
|Fresh preflight/reconcile trướcApply|ABSENT_CONFIRMED20/0commit/0errors|2098|
|SafeApply exactapprovedbatch|20CreateNeedsApproval,exit0|11191|
|Reconcile|COMMITTED_VERIFIED20/20/0missing/errors|11305|
|Verify/readback|PASS0mismatch,20/20/60/40|11750|
|Idempotent repeat|20AlreadyCommitted,0newdata|17223|
|Verify/readback repeat|PASS0mismatch/DataSHAsame|13024|

Reconciliation trướcApply nằm trongfreshpreflight, khớp20ABSENT. Không có SQLtimeout/COMMITuncertainty mới; **SQL ROOT CAUSE UNKNOWN / MONITORING** vẫn giữ. Khôngrollbackcầnthiết; approvalApplyonly không cho actionRollback. Nếu sau này cầnwithdraw, reconcile và dùngapprovalRollbackđúngrunbook,khôngreset/fullrestoređểxửlýbatchnhỏ.

## File, kiểm thử và lệnh

Thay đổi **3script**: `scripts/dev-signing-common.ps1` (publicverificationfacts mới), `scripts/test-dev-import-signature.ps1` (public-only+explicitBatchID), `scripts/test-phase3h-cng.ps1` (focusedtests). Không C#/migration/EFschema/importer/scanner/CMS/videochange. Không rebuildWeb/CLI vì thayPowerShell; chỉbuild diagnostic.NETprobe:0error/3warning, ghiđúnglog, khônggọi làproductbuild0warning. Giữ binaryWebvàCLIđãcó.

```powershell
# ĐÃ CHẠY; dùng report path mới nếu kiểm tra lại, không ghi đè raw
rtk proxy powershell -NoProfile -File .local/phase3h-cng-tests.ps1
rtk proxy powershell -NoProfile -File .local/phase3h-cng-apply.ps1
rtk proxy python -X utf8 .local/phase3h-cng-preservation.py
rtk proxy powershell -NoProfile -File .local/phase3h-cng-repeat.ps1
rtk proxy powershell -NoProfile -File .local/phase3h-cng-http.ps1
```

Các harness dùngcurrentpointer vàunique labels; **không rerun nguyên lệnh vào cùngrun/report**. LệnhCLI/plan/deps/approval/receipt thực được ghi trongharness+guard-attestation; reportpaths nằm trongrun. HTTP11actualGETở`http://127.0.0.1:5391`:2tin+1dựthảo,ảnh/PDFđúngSHA,category/search/alias301,adminmedia/revisionpreview302vàphysicalstorage404. TemporaryWebđãstopđúngPIDcủalượt,previewkhácgiữnguyên;khôngUItoànsite/fullsuite/crawl/email.

Hai lỗi harness được phân biệt với nghiệp vụ: wrapperđọcLASTEXITCODEchưaset saucmdlet15/15PASSđãsửa cách đọcchecksJSONvàchỉchạyphầnbridge/probecònlại,khôngchạylại15checks; independentassertCanonUrl==capturedSourceUrlkhôngđúngdesign. Raw20canonicallegacy URLs khớpverifiedTestcohort,vàcảcanonical+friendlysourceURLthuộc40sealedaliases; sửaasserttheoevidence,**khôngsửaSQL hoặcplan đểépPASS**. Tất cả41PDF+19ảnh mới thựcbyteClean; khôngbiến53oldNotScannedthànhClean.

Saubatchnày còn190nguồn/12packet cầnexactapprovalriêng và52blocked; NukeViet/Production scanner/MFA/SQLrestore/UAT/cutovervàincidentartifactlịchsửgiữtrạngtháiriêng. **CHƯA PRODUCTION READY.**
