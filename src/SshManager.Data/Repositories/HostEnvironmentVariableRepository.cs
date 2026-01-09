using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing host environment variables.
/// </summary>
public sealed class HostEnvironmentVariableRepository : IHostEnvironmentVariableRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HostEnvironmentVariableRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<HostEnvironmentVariable>> GetByHostIdAsync(Guid hostId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.HostEnvironmentVariables
            .Where(v => v.HostEntryId == hostId)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Name)
            .ToListAsync(ct);
    }

    public async Task SetForHostAsync(Guid hostId, IEnumerable<HostEnvironmentVariable> vars, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Remove all existing environment variables for this host
        var existing = await db.HostEnvironmentVariables
            .Where(v => v.HostEntryId == hostId)
            .ToListAsync(ct);
        db.HostEnvironmentVariables.RemoveRange(existing);

        // Add the new set of environment variables
        var varsList = vars.ToList();
        for (var i = 0; i < varsList.Count; i++)
        {
            var variable = varsList[i];
            variable.HostEntryId = hostId;
            variable.SortOrder = i;
            variable.CreatedAt = DateTimeOffset.UtcNow;
            variable.UpdatedAt = DateTimeOffset.UtcNow;

            // Validate the variable
            var validationContext = new ValidationContext(variable);
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(variable, validationContext, validationResults, validateAllProperties: true))
            {
                throw new ValidationException(validationResults.First().ErrorMessage);
            }

            // Ensure the variable has a new ID
            if (variable.Id == Guid.Empty)
            {
                variable.Id = Guid.NewGuid();
            }

            db.HostEnvironmentVariables.Add(variable);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AddAsync(HostEnvironmentVariable variable, CancellationToken ct = default)
    {
        var validationContext = new ValidationContext(variable);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(variable, validationContext, validationResults, validateAllProperties: true))
        {
            throw new ValidationException(validationResults.First().ErrorMessage);
        }

        variable.CreatedAt = DateTimeOffset.UtcNow;
        variable.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Set sort order to be last for the host
        var maxOrder = await db.HostEnvironmentVariables
            .Where(v => v.HostEntryId == variable.HostEntryId)
            .MaxAsync(v => (int?)v.SortOrder, ct) ?? -1;
        variable.SortOrder = maxOrder + 1;

        // Clear navigation property to prevent EF from trying to insert existing hosts
        variable.Host = null!;

        db.HostEnvironmentVariables.Add(variable);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var variable = await db.HostEnvironmentVariables.FindAsync([id], ct);
        if (variable != null)
        {
            db.HostEnvironmentVariables.Remove(variable);
            await db.SaveChangesAsync(ct);
        }
    }
}
