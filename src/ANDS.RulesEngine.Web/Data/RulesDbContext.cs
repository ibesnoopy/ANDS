using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ANDS.RulesEngine.Web.Data;

public class RulesDbContext : IdentityDbContext<IdentityUser>
{
    public RulesDbContext(DbContextOptions<RulesDbContext> options)
        : base(options)
    {
    }

    public DbSet<RuleRecord> Rules => Set<RuleRecord>();

    public DbSet<RuleAudit> RuleAudits => Set<RuleAudit>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RuleRecord>(entity =>
        {
            entity.ToTable("Rules");
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Id).HasMaxLength(200);
            entity.Property(rule => rule.Name).HasMaxLength(200).IsRequired();
            entity.Property(rule => rule.Description).HasMaxLength(1000);
            entity.Property(rule => rule.Definition).IsRequired();
            entity.Property(rule => rule.UpdatedBy).HasMaxLength(256);
            entity.HasIndex(rule => new { rule.Priority, rule.Id });
        });

        builder.Entity<RuleAudit>(entity =>
        {
            entity.ToTable("RuleAudits");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.RuleId).HasMaxLength(200).IsRequired();
            entity.Property(audit => audit.Action).HasMaxLength(20).IsRequired();
            entity.Property(audit => audit.ChangedBy).HasMaxLength(256).IsRequired();
            entity.HasIndex(audit => new { audit.RuleId, audit.ChangedAt });
        });
    }
}
