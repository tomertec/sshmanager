using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

public interface ISessionRecordingRepository
{
    Task<List<SessionRecording>> GetAllAsync(CancellationToken ct = default);
    Task<List<SessionRecording>> GetByHostAsync(Guid hostId, CancellationToken ct = default);
    Task<SessionRecording?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(SessionRecording recording, CancellationToken ct = default);
    Task UpdateAsync(SessionRecording recording, CancellationToken ct = default);
    Task UpdateDurationAndSizeAsync(Guid id, TimeSpan duration, long fileSizeBytes, long eventCount, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<List<SessionRecording>> GetOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
    Task<long> GetTotalStorageSizeAsync(CancellationToken ct = default);
}
