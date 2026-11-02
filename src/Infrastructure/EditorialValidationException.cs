namespace LandPortal.Infrastructure;

// Field names are editor contract names; messages contain no source HTML or credentials.
public sealed class EditorialValidationException(IReadOnlyDictionary<string, string> errors)
    : InvalidOperationException(string.Join(" ", errors.Values))
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}
