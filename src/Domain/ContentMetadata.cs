namespace LandPortal.Domain;

// Typed fields are owned by an immutable revision or the mutable working draft.
// Legacy LegalDocuments/DraftDocuments stay untouched as source provenance.
public sealed class ContentMetadata
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public Guid? WeeklyOriginalAssetId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? WeeklyOriginalReviewEvidence { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? DisclosureGroupId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? Province { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? ProvinceCode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public int? DisclosureYear { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? PublicationSource { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? LegalBasisReference { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? PublicationReviewEvidence { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public int? WeeklyYear { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public int? WeeklyWeek { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? WeeklyRowsJson { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public string? WeeklyChangeNote { get; set; }
    public DateOnly? PublicationDate { get; set; }
    public string Unit { get; set; } = "";
    public string Number { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string IssuingBody { get; set; } = "";
    public string Signer { get; set; } = "";
    public string Field { get; set; } = "";
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? EffectiveUntilDate { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? EffectivenessBoundary { get; set; }
    public string? EffectivenessStatus { get; set; }
    public string EffectivenessEvidence { get; set; } = "";
    public DateOnly? ConsultationStartDate { get; set; }
    public DateOnly? ConsultationEndDate { get; set; }
    public DateTimeOffset? ConsultationStartUtc { get; set; }
    public DateTimeOffset? ConsultationEndUtc { get; set; }
    public string ConsultationDateEvidence { get; set; } = "";
    public string MappingStatus { get; set; } = "Unmapped";
    public string MetadataOrigin { get; set; } = "Editorial";
    public ContentMetadata Copy() => (ContentMetadata)MemberwiseClone();
    public string EffectiveStatusAt(DateTimeOffset now)
    {
        var date=DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var ended=EffectiveUntilDate is DateOnly last && !string.IsNullOrWhiteSpace(EffectivenessEvidence) &&
            (EffectivenessBoundary=="StartOfVietnamDay"&&date>=last||EffectivenessBoundary=="EndOfVietnamDay"&&date>last);
        return ended && EffectivenessStatus is "ConfirmedEffective" or "ConfirmedPartial"?"ConfirmedExpired":EffectivenessStatus??"Unknown";
    }
    public string SearchFields() => $"{Number} {DocumentType} {IssuingBody} {Signer} {Field} {Unit}"+(DisclosureGroupId==null?"":$" {Province} {ProvinceCode} {DisclosureYear} {PublicationSource}");
    public string ConsultationState(DateTimeOffset now)
    {
        var offset = TimeSpan.FromHours(7);
        var start = ConsultationStartDate is DateOnly first ? new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime() : ConsultationStartUtc;
        var end = ConsultationEndDate is DateOnly last ? new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime() : ConsultationEndUtc;
        if (start is null || end is null) return "Chưa xác định";
        return now < start ? "Chưa mở" : now >= end ? "Đã đóng" : "Đang mở";
    }
}
