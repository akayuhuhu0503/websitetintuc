# Phase3I — runbook duyệt, refresh, Apply và checkpoint

> Cập nhật21/10: **12/12b02–b13đãAPPLIED/VERIFIEDLOCAL**,Dev250content/584media. [Báo cáoApply](phase-3i-bulk-apply-report.md),`.local/phase3i-bulk-current.json`. Các nhãnREADY/unsigned/baseline60bêndướilàlịchsử. **Không ký/Applylại12packet**;nếuresumephảiđọcreceipts/checkpointtrước.Khôngcóapprovalcho52blocked/NukeViet/Productiontừlượtbulk này.


**Hiện chỉ PREPARE;0signature/0Apply.** [12packet/hash](phase-3i-packets.md), [báo cáo](phase-3i-report.md). Currentpointer`.local/phase3i-current.json`; exactindex`.local/evidence/phase3f/phase3i-20261021T080800Z-c4f2ee3b/approval-packet.json`,SHA`19751c31c9911b7e92977c7c6d039a0bd1919211d2349a9910e7649dcbcc3b13`. Plan/reviewfile gốc giữnguyên,khôngtạoBatchIdmớiđểchetrạngtháidở.

1. ChủdựánxácnhậncáctupleBatchId+manifestSHA+delta+LandPortal_Devđượclựachọn. Chưa cóxácnhận12packet;chuẩn bị tài liệu khôngphảiphêduyệt. Existingb01approvalkhôngcoverbấtkỳpacketnàotrong12. Khôngtựký hoặc tạochứngchỉ.
2. Đọccurrentpointer vàactualmanifest/review. Mọibatchdùngcôngcụhiệnhành;chọnmộtlabelđượclựachọn,chạymỗireportvàorunIDmới. Cácbiến`$approvedBy`/`$approvalReference` bên dưới phải làxácnhận**thật**,khôngdùngliteralplaceholderđểký.
3. Freshpreflight trênDevcurrent,strictcurrentDefenderproof vàstamp/alias/schema/root/SQLtarget. NếuAlreadyCommittedđãverify,checkpointvàbỏquaApply. NếuTimeout/uncertainstate,reconcile,khôngretrymù/tăngtimeout. KhôngthayđổiSQLinstance/ExpectedDB/.envđểvượtguard.
4. FreshcompletebackupcurrentDev:SQLCOPY_ONLY/CHECKSUM/VERIFYONLY/header/SHA +media/snapshot/keys/envprivate+SQLTestdurablereceipts+scopeddependencies+signedapprovalreceiptthực. Backupha88e46496baseline40khôngthaybackupDev60. Tái dùngchỉkhicóđủbằngchứngbackupcònphùhợptoàntrạngthái;Devthayđổingoàibatchđượcduyệtphảibackupmới. Khôngnớilỏngbackupguardđểtáidùng;khônggọiVERIFYONLYlàrestoreSQL. Cóthểchuẩnbịdeps12mộtlượtđểtránhenumeratelặplại nhưngmỗichặngphảicóbackup/restorecheckpointphùhợp.
5. Ký **mỗi**reviewriêng sauownerconfirmation;verifynative/certpublicchain/pin/EKU/date/hash/batch/target/SID/actions. CNGverificationđãsửa,khôngmởprivatekeyđểverify. Nativesigningvẫncầnprivatekey;thiếuquyềnthìdừng,khôngACLgrant/newcert. Ký trước snapshotdependenciescuốiđểbackupcósignedapproval/receipt;validateapproval+backup+proofngaytrướcApply.
6. SafeApply mộtpacket,receipt/readback/verify vàcheckpoint ngay;kiểmnguồn/date/category/assetsbytes/scan/ACL/olddata. Mỗipacketphảiverify0mismatch;idempotencydựavàoreceipts+DataSHA,khônglặpApplyđãcommitvôích. Smoke theo nhóm sauApply hoặc khi lỗi,khôngUItoànsite. Nếuunsafe/olddataunexpectedchange,dừngtoànchặng.
7. Cólỗithìreconciletrướcretry/rollback. RollbackcầnactionRollback riêng;withdrawđúngbatchcònunedited,giữsource/revisions/files/aliases/audit;khôngfullDBrestorehoặcxóadữliệungoàibatch. Chỉchuyểnsangpacketlỗikhácnếuđãduyệtriêng+pairwisesource/alias/asset vàcommonprerequisitesvẫnan toàn.

## Lệnh mẫu theo công cụ hiện có — chưa thực thi

```powershell
. ./scripts/dev-signing-common.ps1
$current=Get-Content .local/phase3i-current.json -Raw -Encoding UTF8|ConvertFrom-Json
$index=Get-Content $current.approvalPacket -Raw -Encoding UTF8|ConvertFrom-Json
$label='b02' # only a packet explicitly approved by the owner
$p=$index.batches|Where-Object label -EQ $label
# Set explicit Development / LandPortal_Dev and project Dev storage using existing profile.
# Each report path belongs to a NEW execution run, never overwrite Phase3I evidence.
$deps=@('--checkpoint',$p.checkpoint,'--checkpoint-sha',$p.checkpointSha,
 '--inventory',$p.inventory,'--inventory-sha',$p.inventorySha,
 '--proof-set',$p.proofSet,'--proof-set-sha',$p.proofSetSha,
 '--plan',$p.plan,'--plan-sha',$p.manifestSha256)
rtk proxy $script:DotnetPath $current.cli batch-preflight --report ($out+'/preflight') @deps
# STOP on nonzero/errors/unexpected delta; fresh backup must pass before safe-apply.
& ./scripts/sign-dev-import-approval.ps1 -Review $p.review -ReviewSha $p.reviewSha `
 -Thumbprint 94797BD79E275B09DEF613036BC00DF4EA91345F `
 -ApprovedBy $approvedBy -ApprovalReference $approvalReference `
 -ConfirmPlanSha $p.manifestSha256 -ExpiresUtc ([DateTimeOffset]::UtcNow.AddHours(6)) `
 -Actions Apply -ConfirmUserApproval
# Back up actual approval/receipt, full currentDev SQL/media and scoped deps using existing helper.
# Existing backup helper AdditionalFiles entries MUST be project-relative to avoid dependency/C: keys.
$env:SafeImport__ApprovedSignerThumbprints='94797BD79E275B09DEF613036BC00DF4EA91345F'
rtk proxy $script:DotnetPath $current.cli safe-apply --report ($out+'/apply') @deps `
 --approval $approval --backup-receipt $backupReceipt
rtk proxy $script:DotnetPath $current.cli batch-reconcile --report ($out+'/reconcile') @deps
rtk proxy $script:DotnetPath $current.cli batch-verify --report ($out+'/readback') @deps
# Read receipts/status/errors and update checkpoint; do not infer success from exit0 alone.
```

Đây làcácbướcminhhọa,**khôngscriptbulkautoapprove**,khôngchạyquaexitlỗitựđộng. CầnexistingDevprofile vàbiếnSQL/storage/thưmụcbackupđãxácminhtrướcrun,khôngcấpcấu hìnhbí mậttrongdocs. Lệnhverifyapprovalđúngbindingvẫn`scripts/test-dev-import-signature.ps1 -Mode ApprovedDocument ...`;neverexecuteapproval.ps1.

## Refresh đúng tài nguyên

`refresh-worklist.json` có464SHA/job/quarantine/proofPath/proofSHA/proofset/inventorySHA/expiry. Hiện0refreshcầnthiết,khôngquétlại. KhiTTL/currentdefs/engine/defsage72hfail,dừngtrướcký/Apply,grouptheojobvàrefreshđúngtậpcầnthiết với **cùngjobID/budget3receipt/resource/180job** bằng`recovery-scan`/`scripts/phase3h.ps1 -Action Scan -Database LandPortal_Test`,newreporttrongphase3c,exactinventory/checkpointSHA. Khôngtạojobmớiđểvượtbudget. ChỉexactpublicTestcopieskhôngprotected53. Hashbytephảigiữnguyên,realnativeCleanreceiptSQL+outputdigestPASS. NewproofsetSHAđổiplanbinding: **freshpreview,reviewvàownerapprovalSHA mới**;khôngsửapacketcũtạichỗ. Giữmọiproof/rawcũ,khônggánNotScanned→Cleanbằngmetadata.

SQLtimeoutUNKNOWN/MONITORING;giữinstrumentationhiệnhành,khôngtăngtimeout/query/index/config. NukeViettheoSRSv1.0làluồngriêng,chưatrộnQLDD;52blockedkhôngngănpacketđộclập. CHƯA PRODUCTION READY.
