# Phase3I — 12 packet bulk migration để trình duyệt

> Cập nhật21/10: **12/12b02–b13đãAPPLIED/VERIFIEDLOCAL**,Dev250content/584media. [Báo cáoApply](phase-3i-bulk-apply-report.md),`.local/phase3i-bulk-current.json`. Các nhãnREADY/unsigned/baseline60bêndướilàlịchsử. **Không ký/Applylại12packet**;nếuresumephảiđọcreceipts/checkpointtrước.Khôngcóapprovalcho52blocked/NukeViet/Productiontừlượtbulk này.


**12/12 READY_FOR_APPROVAL; chưa ký, chưa Apply trong lượt này. CHƯA PRODUCTION READY.** Run `phase3i-20261021T080800Z-c4f2ee3b`, pointer `.local/phase3i-current.json`; bằng chứng `.local/evidence/phase3f/phase3i-20261021T080800Z-c4f2ee3b`. Đích **MinhAnh\SQLEXPRESS / LandPortal_Dev**, registered THANGPD là tên cũ đã được xác minh, không thay metadata. Tái dùng nguyên12 plan/review cũ, **0 manifest/BatchId mới**, revalidate trênDev60; không lập lại importer/scanner/signing/CNG/CMS.

## Bảng duyệt exact packet

|Packet|Batch ID|Content / revision / asset / alias|Manifest SHA-256|
|---|---|---:|---|
|b02|`0a78916b-6a3c-4c8f-9268-7751893c394d`|7 / 7 / 60 / 14|`196d2d70de06685d290d82c55422df61aa483bec2135e44ffab078e9b567d0e4`|
|b03|`3e98a353-1f9d-4609-8b7f-bb21c64a0d1d`|8 / 8 / 60 / 16|`005ad72393d6e70d4845f4449b2fea0d7b3d85514fc26d31fc2eea1abbb6d9d2`|
|b04|`e9c67ec4-771b-420b-966d-3c974a595cdd`|9 / 9 / 60 / 18|`11d1e21af805b419ee4e029c839f7e71d559eecbf95ab50bdd22346948eacbde`|
|b05|`4b87c5b3-d92d-40ca-a30d-5dce081b0046`|13 / 13 / 60 / 26|`4441cff3a9c43921c8abcf68b8a48894ab71163e4d7a18decb246625753b7959`|
|b06|`25b78327-0413-48ad-be66-ed21ef464846`|18 / 18 / 60 / 36|`c9c035a7100d046385a2b65a8e7163e44521b292b9f627e2a76f3c73022b995f`|
|b07|`1e9de4ac-a22b-416e-b047-4e6e188da1bd`|20 / 20 / 47 / 40|`daadd6139c41f71c8a750954c0641015e80ae15eda35853009c9388dd739aba2`|
|b08|`efac0201-1e94-4997-8417-74db6845739b`|20 / 20 / 23 / 40|`560fe711986513596b7a63b0ad5d68030bef5f3437824e3c28bd287c84959ed4`|
|b09|`76406435-4b8c-43a2-85a5-219ccc6ba9ee`|20 / 20 / 20 / 40|`ab959a08562355a9cc0dd9ff15e926e6e49926c619979e438ca7f002bd3e9a1a`|
|b10|`ddf068c9-1766-4d2e-ad73-516262466b9c`|20 / 20 / 19 / 40|`75f0c4f713249f65e283968be508faf29a49fa7be179ca8de56bb2e429839bf3`|
|b11|`b7df9c39-515a-4684-b236-12610c23226a`|20 / 20 / 20 / 40|`34bb1558a0f29d0e011142d769aba0917655cf0e84295241acd875f80307692c`|
|b12|`72b9ed79-f3d9-4113-86e2-cbf0c1349ff6`|20 / 20 / 20 / 40|`f7b00e930e3a917494d8aed84e1bc9c998bc0ea69830f752eac8c2e9c1a972f0`|
|b13|`becfc9bf-4f9e-420b-906b-3adcb613d908`|15 / 15 / 15 / 30|`84461b83958f22f9da3e07093d6cd2f56f856b85fbfdda5a1348d4517bec49ae`|


**Tổng190content/190SourceRecords/190revision/464asset/380alias**. Thứ tựb02→b13. Cả12freshpreflightABSENT_CONFIRMED,190eligible/0commit/0errors;12planSHA nguyên vẹn. Asset464 gồm **290ảnh (169JPEG/120PNG/1GIF),174PDF**,597,773,718byte. Danh tính190 gồm149tin,5thôngbáo,3trangtĩnh,31vănbản,2dựthảo;nguồnqlddthật,khôngfixture.

`approval-packet.json` ởrun chứa nguyênmapping190SourceIdentity→currentTestcontent/revision→rawhash→464assetSHA→380alias,plan/review paths+hash vàexecution gates từngpacket. SHA256 gói tổng hợp **`19751c31c9911b7e92977c7c6d039a0bd1919211d2349a9910e7649dcbcc3b13`**; gói này **chưa ký/chưa phê duyệt**, không thay manifest từngbatch hoặc tự cấp authority. `packet-summary.csv` và `source-identities.csv` đọc bằngExcel; bộreview cũ vẫnPending/nullapprover/unsigned.

## Bằng chứng và điều kiện còn thiếu

**Lưu ý số receipt:** truy vấn live trả **344 hàng audit lịch sử**, phân loại theo exact hai planSHA3F thành **190 CreatePublishedTest** (190 ContentId duy nhất) và **154 Keep** của lần chạy tiếp. Không có Detail trùng exact batch/hash/identity. Chỉ190receiptCreate được dùng xác nhận provenance;154Keep không phải154nguồn mới và không phải duplicate content. Quan hệ465ContentAssets tham chiếu464SHA vì một tài nguyên có nhiều liên kết;không ép các số này bằng nhau. Lầnassertcủaharnessbanđầuđếm344như190đãdừng trướcwritefinalartifacts;đãsửatheoactualplanAction,khôngsửaSQLhoặccộngreceiptthànhnguồn.


- LiveDev trước/sau **60content/60SourceRecords/71revision/120media(67ảnh53PDF)/120alias**;17groups0diff,Identity/envdigestkhôngđổi.53originalNotScanned+67Clean,70capture/68SQLsnapshot/8ngoạilệ/2videometadata/0videofile.52blockedledgerhashkhôngđổi:22staging+30Devprotected;nguyênnhângiaonhaukhôngcộngthànhsốbàikhác. `remaining-52.json` làbảnchépnguyênphânloại,khôngcrawlhayxửlýlạicácmụcđó.
- ĐốichiếuliveSQLTest **190nguồn/190receiptCreate3F/464asset/380alias/465relation** bằngnamedprojectdatabase,parameterizedGUID allowlist190,0mismatch. SourceIDs/publishedrevision/fingerprint/canonical/title/receipt/alias vàper-content assetlinks khớpsealedcohort3F (169+41creates) vàrawNetworkSHA;khôngdùngtổngContentsTestđểđếmnguồn. Testcache/quarantine/importedmedia bytehash khớp;khôngSQLTestwrite.
- **Không trùng** SourceIdentity,assetSHAhoặcalias giữacácpacket hoặcvớiDev60. Không suy luận độc lập từnhãnpacket: đãtínhsetpairwise vàDevintersection. Database/schema/categories/storage/scannerpolicy làđiềukiệnchung,khôngthểbỏqua sau mỗiApply. NếuDevthayđổingoàibatchđãduyệt hoặcmismatch/an toàn thìdừngtoànbộđểkiểmtra.
- **0/464 proof cần refresh hiện tại**:MicrosoftDefendernativeClean,currentengine1.1.26090.9/defs1.461.118.0;strictSQLreceipt/exactbytes24h/defsage72h đượcvalidatequa12preflight.0scanmới,khôngfallbackoldClean hoặc chuyển53originals. Earliestexpiry **22/10/2026 11:06:37VN** (04:06:37UTC);definitionsdriftinvalidngay. `refresh-worklist.json` liệt kê464SHA/job/quarantine/proof/path/hash/expiry đểrefreshđúngtậpcầnthiếtsátApply. RefreshproofsetSHA sẽ đổi plan.RecoveryProofSha256: phảifreshpreview+manifestvàownerapprovalSHA mới,khôngsửadependencydướichữkýcũ.
- Existingcert94797BD79E275B09DEF613036BC00DF4EA91345F/publicchain/CodeSigningEKU/hạn/nativeAuthenticodeValid kiểmđọcmộtlần;**0sign**,khôngrerun15/8tests. Giữnguyênprocess-onlyexactsignerpinđãcó;khôngtạocert/trust/persistentpolicy. Côngcụvẫnmỗiapprovaldocument/action/hash/targetriêng;khôngcómộtapprovalchungtựcover12. Ownercóthểxácnhậncáctupleđượclựachọntrongcùngmộtphảnhồi,nhưngkhikývẫntạotừngdocument/auditriêngđúngreview.
- **Chưa có approval cho12packet. Chưa có backup Dev60 trước bulkApply**. Backupha88e46496làbaselineDev40trướcb01,khôngtựgọilàbackupDev60. `backup-dependencies.json`2107targetedfiles/1,817,974,868byte **PREPARED_NOT_BACKED_UP** cho190raw+464cache/quarantine/proof/importedTestmedia+checkpoint/plan/review;khôngmanifesttoànrepository. Futurebackupcầnthêmactualsignedapprovals/receipts,currentDevSQL/media/snapshot/keys+SQLTestreceipts,metadataRESTORE. KhôngchạybackupsSQLhàngloạtkhichưacóphêduyệtApply.

## Trạng thái Apply thật và dữ liệu Dev

|Lần|Thực nhập|Dev sau lần đó|Verify|
|---|---|---|---|
|Phase3G ưu tiên, lịch sử|3content/4revision/7JPEG/6alias|40content/60media|Đãcommit/readbacktheobáocáo3G|
|Phase3H b01, trước lượt này|20content/20revision/60asset/40alias|60content/120media|Applied/verified;receipt20,DataSHAfb97b03e...,repeat20Already;livebaseline17groupsbảotoàn|
|**Phase3I lượt này**|**0content/0revision/0asset/0alias**|**60content/120media**|12readonlypreflights vàliveTestmappingPASS,khôngpostApplyverify/idempotency/HTTPmới|

Nếu190đượcduyệt+Apply+verifyđủsauđó,mớiđạt250sourcecontents/261revisions/584media/500aliases;đâylà**dự kiến**,khôngphảisốDevhiệntạivàkhônglànghiệmthutoànbộnguồn.

## Sau khi chủ dự án duyệt

Theo [runbook3I](phase-3i-runbook.md):xácnhậnbatch/hash/target/deltađượclựa→freshpreflight→backupcurrentDev/Test+exactdependencies→sign/validateauthority/proof→safeApply→reconcile/readback/verify→checkpoint;thứtựký/backupgiữsignedapproval/receiptnằmtrongbộbackup trướcApply. ChỉApplypacketđãcóexactapprovalriêng. KhôngApplylạib01;AlreadyCommittedreceiptđãđượcverifykhôngcầnchạylạichỉđểtăngtestcount. Từngpacketcommitkhôngrõphảireconciletrướcretry;packetlỗichỉbỏquasangpacketkhácnếuđãđượcduyệtriêng vàvẫnchứngminhđộclập/an toàn. RollbackvẫnactionRollbackriêng,withdrawbatch-owneduneditedpublications,giữsource/revision/files/audit/aliases,khôngreset/fullDBrestore.

## Kiểm tra thực chạy

Khôngappcode/migration/signing/scannerpolicychange,**khôngbuild/fullregression/signingtest/UI/HTTP/crawl/scan mới**.12preflightPASS là12packetSQLreadonlychecks,khôngsuite137nhóm. SQLTestboundedSELECTtrace lưufingerprint/clientConnectionId/SPID/time/success;12DevCLIlog+exit/elapsedlưuđúngrun,mỗicáimộtlần,khôngblindretry/tăngtimeout/observergiữDBmở. KhôngSQLtimeoutmới;**SQL ROOT CAUSE UNKNOWN / MONITORING**. HistoricalartifactPARTIAL/NukeVietmapping/ProductionScanner/MFA/SQLrestore/UAT/cutovergiữriêng.

Harnessđãchạy: `.local/phase3i-init.ps1`, `phase3i-prepare.py`, `phase3i-preflight.ps1`, `phase3i-relations.ps1`, `phase3i-finalize.py`. Khôngrerunvàorawrunhiệncó. Không.env/Identity/quyền/Production/DNS/email/Gitpushchanges.**CHƯA PRODUCTION READY.**
