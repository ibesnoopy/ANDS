using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ANDS.RulesEngine.Web.Data;

public sealed record RuleEditModel(
    string Id,
    string Name,
    string? Description,
    int Priority,
    bool Enabled,
    string Definition);

public interface IRuleStore
{
    Task<IReadOnlyList<RuleRecord>> ListAsync(CancellationToken cancellationToken = default);

    Task<RuleRecord?> FindAsync(string id, CancellationToken cancellationToken = default);

    Task CreateAsync(RuleEditModel model, string user, CancellationToken cancellationToken = default);

    Task UpdateAsync(RuleEditModel model, string user, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, string user, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuleAudit>> ListAuditAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when a submitted definition is not a valid rule.
/// </summary>
public sealed class RuleStoreValidationException : Exception
{
    public RuleStoreValidationException(string message)
        : base(message)
    {
    }
}

public sealed class RuleStore : IRuleStore
{
    private readonly RulesDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RuleStore(RulesDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<RuleRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        await _context.Rules
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

    public Task<RuleRecord?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        _context.Rules.FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public async Task CreateAsync(RuleEditModel model, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var definition = NormalizeDefinition(model);
        if (await _context.Rules.AnyAsync(rule => rule.Id == model.Id, cancellationToken))
            throw new RuleStoreValidationException($"A rule with ID '{model.Id}' already exists.");

        var now = _timeProvider.GetUtcNow();
        _context.Rules.Add(new RuleRecord
        {
            Id = model.Id.Trim(),
            Name = model.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            Priority = model.Priority,
            Enabled = model.Enabled,
            Definition = definition,
            UpdatedAt = now,
            UpdatedBy = user
        });
        AddAudit(model.Id.Trim(), "created", definition, user, now);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RuleEditModel model, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var definition = NormalizeDefinition(model);
        var record = await FindAsync(model.Id, cancellationToken)
                     ?? throw new RuleStoreValidationException($"Rule '{model.Id}' was not found.");

        var now = _timeProvider.GetUtcNow();
        record.Name = model.Name.Trim();
        record.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        record.Priority = model.Priority;
        record.Enabled = model.Enabled;
        record.Definition = definition;
        record.UpdatedAt = now;
        record.UpdatedBy = user;
        AddAudit(record.Id, "updated", definition, user, now);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, string user, CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(id, cancellationToken)
                     ?? throw new RuleStoreValidationException($"Rule '{id}' was not found.");
        _context.Rules.Remove(record);
        AddAudit(record.Id, "deleted", null, user, _timeProvider.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleAudit>> ListAuditAsync(int take, CancellationToken cancellationToken = default) =>
        await _context.RuleAudits
            .OrderByDescending(audit => audit.ChangedAt)
            .ThenByDescending(audit => audit.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Parses the submitted definition, validates it with the engine's own rule
    /// validation, and returns the canonical JSON to persist.
    /// </summary>
    public static string NormalizeDefinition(RuleEditModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Id))
            throw new RuleStoreValidationException("Id is required.");
        if (string.IsNullOrWhiteSpace(model.Name))
            throw new RuleStoreValidationException("Name is required.");

        var options = RuleJsonSerializer.CreateOptions();
        RuleDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<RuleDefinition>(model.Definition ?? string.Empty, options);
        }
        catch (JsonException exception)
        {
            throw new RuleStoreValidationException($"Definition is not valid JSON: {exception.Message}");
        }

        if (definition is null)
            throw new RuleStoreValidationException("Definition is required.");

        var rule = new Rule
        {
            Id = model.Id.Trim(),
            Name = model.Name.Trim(),
            Description = model.Description,
            Priority = model.Priority,
            Enabled = model.Enabled,
            Condition = definition.Condition,
            Actions = definition.Actions ?? Array.Empty<RuleAction>()
        };

        try
        {
            rule.Validate();
        }
        catch (RuleValidationException exception)
        {
            throw new RuleStoreValidationException(exception.Message);
        }

        return JsonSerializer.Serialize(
            new RuleDefinition { Condition = rule.Condition, Actions = rule.Actions },
            options);
    }

    private void AddAudit(string ruleId, string action, string? definition, string user, DateTimeOffset changedAt) =>
        _context.RuleAudits.Add(new RuleAudit
        {
            RuleId = ruleId,
            Action = action,
            Definition = definition,
            ChangedBy = user,
            ChangedAt = changedAt
        });
}
