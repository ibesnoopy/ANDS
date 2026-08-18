using ANDS.RulesEngine.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ANDS.RulesEngine.Web.Pages.Rules;

public class IndexModel : PageModel
{
    private readonly IRuleStore _store;

    public IndexModel(IRuleStore store) => _store = store;

    public IReadOnlyList<RuleRecord> Rules { get; private set; } = Array.Empty<RuleRecord>();

    public IReadOnlyList<RuleAudit> Audit { get; private set; } = Array.Empty<RuleAudit>();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rules = await _store.ListAsync(cancellationToken);
        Audit = await _store.ListAuditAsync(10, cancellationToken);
    }

    public async Task<IActionResult> OnPostToggleAsync(string id, CancellationToken cancellationToken)
    {
        var record = await _store.FindAsync(id, cancellationToken);
        if (record is null)
            return NotFound();

        await _store.UpdateAsync(
            new RuleEditModel(record.Id, record.Name, record.Description, record.Priority, !record.Enabled,
                record.Definition),
            User.Identity?.Name ?? "unknown",
            cancellationToken);
        StatusMessage = $"Rule '{id}' is now {(record.Enabled ? "enabled" : "disabled")}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _store.DeleteAsync(id, User.Identity?.Name ?? "unknown", cancellationToken);
        }
        catch (RuleStoreValidationException)
        {
            return NotFound();
        }

        StatusMessage = $"Rule '{id}' was deleted.";
        return RedirectToPage();
    }
}
