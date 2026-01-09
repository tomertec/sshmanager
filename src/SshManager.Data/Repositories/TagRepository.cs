using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing tags.
/// </summary>
public sealed class TagRepository : ITagRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public TagRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<Tag>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tags
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    public async Task<Tag?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tags
            .Include(t => t.Hosts)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<Tag?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tags
            .FirstOrDefaultAsync(t => t.Name.ToLower() == name.ToLower(), ct);
    }

    public async Task<Tag> GetOrCreateAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name cannot be null or empty", nameof(name));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Try to find existing tag (case-insensitive)
        var existingTag = await db.Tags
            .FirstOrDefaultAsync(t => t.Name.ToLower() == name.ToLower(), ct);

        if (existingTag != null)
            return existingTag;

        // Create new tag if not found
        var newTag = new Tag
        {
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var validationContext = new ValidationContext(newTag);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(newTag, validationContext, validationResults, validateAllProperties: true))
        {
            throw new ValidationException(validationResults.First().ErrorMessage);
        }

        db.Tags.Add(newTag);
        await db.SaveChangesAsync(ct);

        return newTag;
    }

    public async Task AddAsync(Tag tag, CancellationToken ct = default)
    {
        var validationContext = new ValidationContext(tag);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(tag, validationContext, validationResults, validateAllProperties: true))
        {
            throw new ValidationException(validationResults.First().ErrorMessage);
        }

        tag.CreatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Tag tag, CancellationToken ct = default)
    {
        var validationContext = new ValidationContext(tag);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(tag, validationContext, validationResults, validateAllProperties: true))
        {
            throw new ValidationException(validationResults.First().ErrorMessage);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Tags.Update(tag);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.FindAsync([id], ct);
        if (tag != null)
        {
            db.Tags.Remove(tag);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<Tag>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tags
            .Where(t => t.Hosts.Any(h => h.Id == hostId))
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }
}
