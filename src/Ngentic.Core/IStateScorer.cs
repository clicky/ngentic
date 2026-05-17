namespace Ngentic;

public interface IStateScorer<TSnapshot>
{
    Task<TSnapshot> SnapshotAsync(CancellationToken ct = default);
}
