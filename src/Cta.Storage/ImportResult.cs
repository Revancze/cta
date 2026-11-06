namespace Cta.Storage;

public enum ImportStatus
{
    Imported,
    AlreadyImported,
}

public sealed record ImportResult(ImportStatus Status, long RunId, long ResultCount);
