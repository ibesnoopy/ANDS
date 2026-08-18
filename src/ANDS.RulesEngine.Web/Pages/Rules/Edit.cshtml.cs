using System.ComponentModel.DataAnnotations;
using ANDS.RulesEngine.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ANDS.RulesEngine.Web.Pages.Rules;

public class EditModel : PageModel
{
    private const string DefinitionTemplate = """
        {
          "condition": {
            "type": "group",
            "group": "all",
            "conditions": [
              { "type": "comparison", "field": "order.total", "operator": "greaterThanOrEqual", "value": 1000 }
            ]
          },
          "actions": [
            { "type": "send-notification", "parameters": { "channel": "sales" } }
          ]
        }
        """;

    private readonly IRuleStore _store;

    public EditModel(IRuleStore store) => _store = store;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public bool IsNew { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public sealed class InputModel
    {
        [Required]
        [StringLength(200)]
        [Display(Name = "ID")]
        public string Id { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Display(Name = "Priority")]
        public int Priority { get; set; }

        [Display(Name = "Enabled")]
        public bool Enabled { get; set; } = true;

        [Required]
        [Display(Name = "Definition")]
        public string Definition { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(id))
        {
            IsNew = true;
            Input = new InputModel { Definition = DefinitionTemplate };
            return Page();
        }

        var record = await _store.FindAsync(id, cancellationToken);
        if (record is null)
            return NotFound();

        IsNew = false;
        Input = new InputModel
        {
            Id = record.Id,
            Name = record.Name,
            Description = record.Description,
            Priority = record.Priority,
            Enabled = record.Enabled,
            Definition = record.Definition
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        var model = new RuleEditModel(Input.Id, Input.Name, Input.Description, Input.Priority, Input.Enabled,
            Input.Definition);
        var user = User.Identity?.Name ?? "unknown";
        try
        {
            if (IsNew)
                await _store.CreateAsync(model, user, cancellationToken);
            else
                await _store.UpdateAsync(model, user, cancellationToken);
        }
        catch (RuleStoreValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }

        StatusMessage = $"Rule '{Input.Id}' was saved.";
        return RedirectToPage("/Rules/Index");
    }
}
