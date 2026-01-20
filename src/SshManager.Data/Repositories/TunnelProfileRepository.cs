using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SshManager.Core.Models;

namespace SshManager.Data.Repositories;

/// <summary>
/// Repository implementation for managing tunnel profile configurations.
/// </summary>
public sealed class TunnelProfileRepository : ITunnelProfileRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public TunnelProfileRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<TunnelProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TunnelProfiles
            .Include(p => p.Nodes)
            .Include(p => p.Edges)
                .ThenInclude(e => e.SourceNode)
            .Include(p => p.Edges)
                .ThenInclude(e => e.TargetNode)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<TunnelProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TunnelProfiles
            .Include(p => p.Nodes)
            .Include(p => p.Edges)
                .ThenInclude(e => e.SourceNode)
            .Include(p => p.Edges)
                .ThenInclude(e => e.TargetNode)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task AddAsync(TunnelProfile profile, CancellationToken ct = default)
    {
        var validationContext = new ValidationContext(profile);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(profile, validationContext, validationResults, validateAllProperties: true))
        {
            throw new ValidationException(validationResults.First().ErrorMessage);
        }

        profile.CreatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.TunnelProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TunnelProfile profile, CancellationToken ct = default)
    {
        var validationContext = new ValidationContext(profile);
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(profile, validationContext, validationResults, validateAllProperties: true))
        {
            throw new ValidationException(validationResults.First().ErrorMessage);
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Fetch the existing profile with all related data
        var existingProfile = await db.TunnelProfiles
            .Include(p => p.Nodes)
            .Include(p => p.Edges)
            .FirstOrDefaultAsync(p => p.Id == profile.Id, ct);

        if (existingProfile == null)
        {
            throw new InvalidOperationException($"Tunnel profile with ID {profile.Id} not found.");
        }

        // Update scalar properties
        existingProfile.DisplayName = profile.DisplayName;
        existingProfile.Description = profile.Description;
        existingProfile.UpdatedAt = profile.UpdatedAt;

        // Update nodes collection
        // Remove nodes that no longer exist
        var nodesToRemove = existingProfile.Nodes
            .Where(n => !profile.Nodes.Any(pn => pn.Id == n.Id))
            .ToList();
        foreach (var node in nodesToRemove)
        {
            db.Set<TunnelNode>().Remove(node);
        }

        // Add or update nodes
        foreach (var node in profile.Nodes)
        {
            var existingNode = existingProfile.Nodes.FirstOrDefault(n => n.Id == node.Id);
            if (existingNode != null)
            {
                // Update existing node
                existingNode.NodeType = node.NodeType;
                existingNode.HostId = node.HostId;
                existingNode.Label = node.Label;
                existingNode.X = node.X;
                existingNode.Y = node.Y;
                existingNode.LocalPort = node.LocalPort;
                existingNode.RemotePort = node.RemotePort;
                existingNode.RemoteHost = node.RemoteHost;
                existingNode.BindAddress = node.BindAddress;
            }
            else
            {
                // Add new node
                node.TunnelProfileId = existingProfile.Id;
                existingProfile.Nodes.Add(node);
            }
        }

        // Update edges collection
        // Remove edges that no longer exist
        var edgesToRemove = existingProfile.Edges
            .Where(e => !profile.Edges.Any(pe => pe.Id == e.Id))
            .ToList();
        foreach (var edge in edgesToRemove)
        {
            db.Set<TunnelEdge>().Remove(edge);
        }

        // Add or update edges
        foreach (var edge in profile.Edges)
        {
            var existingEdge = existingProfile.Edges.FirstOrDefault(e => e.Id == edge.Id);
            if (existingEdge != null)
            {
                // Update existing edge properties
                existingEdge.SourceNodeId = edge.SourceNodeId;
                existingEdge.TargetNodeId = edge.TargetNodeId;
            }
            else
            {
                // Add new edge
                edge.TunnelProfileId = existingProfile.Id;
                existingProfile.Edges.Add(edge);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Find the profile with its related data
        var profile = await db.TunnelProfiles
            .Include(p => p.Nodes)
            .Include(p => p.Edges)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (profile != null)
        {
            // EF Core will handle cascade delete of nodes and edges
            // based on the relationship configuration in AppDbContext
            db.TunnelProfiles.Remove(profile);
            await db.SaveChangesAsync(ct);
        }
    }
}
