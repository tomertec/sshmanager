using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing host entries.
/// </summary>
public sealed class HostRepository : IHostRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HostRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<HostEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Hosts
            .Include(h => h.Group)
            .Include(h => h.Tags)
            .OrderBy(h => h.Group != null ? h.Group.SortOrder : int.MaxValue)
            .ThenBy(h => h.SortOrder)
            .ThenBy(h => h.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<List<HostEntry>> GetByGroupAsync(Guid? groupId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Hosts
            .Where(h => h.GroupId == groupId)
            .OrderBy(h => h.SortOrder)
            .ThenBy(h => h.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<List<HostEntry>> SearchAsync(string? searchTerm, IEnumerable<Guid>? tagIds = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.Hosts
            .Include(h => h.Group)
            .Include(h => h.Tags)
            .AsQueryable();

        // Apply text search filter if provided
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            query = query.Where(h =>
                h.DisplayName.ToLower().Contains(term) ||
                h.Hostname.ToLower().Contains(term) ||
                h.Username.ToLower().Contains(term) ||
                (h.Notes != null && h.Notes.ToLower().Contains(term)));
        }

        // Apply tag filter if provided (OR logic - host must have at least one of the specified tags)
        if (tagIds != null)
        {
            var tagIdsList = tagIds.ToList();
            if (tagIdsList.Count > 0)
            {
                query = query.Where(h => h.Tags.Any(t => tagIdsList.Contains(t.Id)));
            }
        }

        // Order by display name when searching, otherwise maintain group/sort order
        if (!string.IsNullOrWhiteSpace(searchTerm) || (tagIds != null && tagIds.Any()))
        {
            return await query
                .OrderBy(h => h.DisplayName)
                .ToListAsync(ct);
        }

        // Return all hosts with proper ordering when no filters applied
        return await query
            .OrderBy(h => h.Group != null ? h.Group.SortOrder : int.MaxValue)
            .ThenBy(h => h.SortOrder)
            .ThenBy(h => h.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<HostEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Hosts
            .Include(h => h.Group)
            .Include(h => h.Tags)
            .FirstOrDefaultAsync(h => h.Id == id, ct);
    }

    public async Task AddAsync(HostEntry host, CancellationToken ct = default)
    {
        var validationContext = new ValidationContext(host);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(host, validationContext, validationResults, validateAllProperties: true))
        {
            throw new ValidationException(validationResults.First().ErrorMessage);
        }

        host.CreatedAt = DateTimeOffset.UtcNow;
        host.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Set sort order to be last in the group
        var maxOrder = await db.Hosts
            .Where(h => h.GroupId == host.GroupId)
            .MaxAsync(h => (int?)h.SortOrder, ct) ?? -1;
        host.SortOrder = maxOrder + 1;

        // Clear navigation property to prevent EF from trying to insert existing groups
        // The GroupId foreign key is sufficient for the relationship
        host.Group = null;

        db.Hosts.Add(host);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(HostEntry host, CancellationToken ct = default)
    {
        var validationContext = new ValidationContext(host);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(host, validationContext, validationResults, validateAllProperties: true))
        {
            throw new ValidationException(validationResults.First().ErrorMessage);
        }

        host.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Clear navigation property to prevent EF from trying to insert existing groups
        host.Group = null;

        db.Hosts.Update(host);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.FindAsync([id], ct);
        if (host != null)
        {
            db.Hosts.Remove(host);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateSortOrderAsync(Guid id, int sortOrder, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.FindAsync([id], ct);
        if (host != null)
        {
            host.SortOrder = sortOrder;
            host.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ReorderHostsAsync(List<(Guid Id, int SortOrder)> hostOrders, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ids = hostOrders.Select(h => h.Id).ToList();
        var hosts = await db.Hosts.Where(h => ids.Contains(h.Id)).ToDictionaryAsync(h => h.Id, ct);

        foreach (var (id, sortOrder) in hostOrders)
        {
            if (hosts.TryGetValue(id, out var host))
            {
                host.SortOrder = sortOrder;
                host.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SetHostTagsAsync(Guid hostId, IEnumerable<Guid> tagIds, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Find the host with its current tags
        var host = await db.Hosts
            .Include(h => h.Tags)
            .FirstOrDefaultAsync(h => h.Id == hostId, ct);

        if (host == null)
        {
            throw new InvalidOperationException($"Host with ID {hostId} not found.");
        }

        // Clear existing tags
        host.Tags.Clear();

        // Add new tags if any were provided
        var tagIdsList = tagIds.ToList();
        if (tagIdsList.Count > 0)
        {
            var tags = await db.Tags
                .Where(t => tagIdsList.Contains(t.Id))
                .ToListAsync(ct);

            foreach (var tag in tags)
            {
                host.Tags.Add(tag);
            }
        }

        host.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
