namespace LandPortal.Infrastructure;

public sealed partial class WorkflowService
{
    private bool bulkGuard;
    // Scoped workflow instance, one item/request. Existing workflow commands,
    // receipts and audit remain authoritative; the restriction is additional.
    public IDisposable RestrictBulkToUnpublishedUnscheduled()
    {
        if (bulkGuard) throw new InvalidOperationException("Thao tác hàng loạt đang chạy.");
        bulkGuard = true;
        return new BulkScope(this);
    }
    private sealed class BulkScope(WorkflowService owner) : IDisposable
    {
        public void Dispose() => owner.bulkGuard = false;
    }
}
