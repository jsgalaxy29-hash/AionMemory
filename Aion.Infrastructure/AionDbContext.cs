using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure;

// Audit rapide (2024-05-15) :
// - OK : schéma métamodèle (modules, entity types, automation, notes, agenda, fichiers) déjà exposé.
// - Manque identifié : persistance des tables STable/SField/SView et application automatique des migrations.
// - Action : consolidation ci-dessous avec support SQLCipher et migrations.
public class AionDbContext : DbContext
{
    public AionDbContext(DbContextOptions<AionDbContext> options) : base(options)
    {
    }

    public DbSet<S_Module> Modules => Set<S_Module>();
    public DbSet<S_EntityType> EntityTypes => Set<S_EntityType>();
    public DbSet<S_Field> Fields => Set<S_Field>();
    public DbSet<S_Relation> Relations => Set<S_Relation>();
    public DbSet<S_ReportDefinition> Reports => Set<S_ReportDefinition>();
    public DbSet<S_AutomationRule> AutomationRules => Set<S_AutomationRule>();
    public DbSet<AutomationCondition> AutomationConditions => Set<AutomationCondition>();
    public DbSet<AutomationAction> AutomationActions => Set<AutomationAction>();
    public DbSet<S_Note> Notes => Set<S_Note>();
    public DbSet<J_Note_Link> NoteLinks => Set<J_Note_Link>();
    public DbSet<S_Event> Events => Set<S_Event>();
    public DbSet<J_Event_Link> EventLinks => Set<J_Event_Link>();
    public DbSet<F_File> Files => Set<F_File>();
    public DbSet<F_FileLink> FileLinks => Set<F_FileLink>();
    public DbSet<F_Record> Records => Set<F_Record>();
    public DbSet<NoteSearchEntry> NoteSearch => Set<NoteSearchEntry>();
    public DbSet<RecordSearchEntry> RecordSearch => Set<RecordSearchEntry>();
    public DbSet<STable> Tables => Set<STable>();
    public DbSet<SFieldDefinition> TableFields => Set<SFieldDefinition>();
    public DbSet<SViewDefinition> TableViews => Set<SViewDefinition>();
    public DbSet<S_VisionAnalysis> VisionAnalyses => Set<S_VisionAnalysis>();
    public DbSet<S_HistoryEvent> HistoryEvents => Set<S_HistoryEvent>();
    public DbSet<S_Link> Links => Set<S_Link>();
    public DbSet<DashboardWidget> Widgets => Set<DashboardWidget>();
    public DbSet<TemplatePackage> Templates => Set<TemplatePackage>();
    public DbSet<MarketplaceItem> Marketplace => Set<MarketplaceItem>();
    public DbSet<PredictionInsight> Predictions => Set<PredictionInsight>();
    public DbSet<UserPersona> Personas => Set<UserPersona>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<S_Module>()
            .HasMany(m => m.EntityTypes)
            .WithOne()
            .HasForeignKey(e => e.ModuleId);

        modelBuilder.Entity<S_Module>()
            .HasMany(m => m.Reports)
            .WithOne()
            .HasForeignKey(r => r.ModuleId);

        modelBuilder.Entity<S_Module>()
            .HasMany(m => m.AutomationRules)
            .WithOne()
            .HasForeignKey(r => r.ModuleId);

        modelBuilder.Entity<STable>()
            .HasMany(t => t.Fields)
            .WithOne()
            .HasForeignKey(f => f.TableId);

        modelBuilder.Entity<STable>()
            .HasMany(t => t.Views)
            .WithOne()
            .HasForeignKey(v => v.TableId);

        modelBuilder.Entity<S_EntityType>()
            .HasMany(e => e.Fields)
            .WithOne()
            .HasForeignKey(f => f.EntityTypeId);

        modelBuilder.Entity<S_EntityType>()
            .HasMany(e => e.Relations)
            .WithOne()
            .HasForeignKey(r => r.EntityTypeId);

        modelBuilder.Entity<S_AutomationRule>()
            .HasMany(a => a.Conditions)
            .WithOne()
            .HasForeignKey(c => c.AutomationRuleId);

        modelBuilder.Entity<S_AutomationRule>()
            .HasMany(a => a.Actions)
            .WithOne()
            .HasForeignKey(c => c.AutomationRuleId);

        modelBuilder.Entity<S_Note>()
            .HasMany(n => n.Links)
            .WithOne()
            .HasForeignKey(l => l.NoteId);

        modelBuilder.Entity<S_Event>()
            .HasMany(e => e.Links)
            .WithOne()
            .HasForeignKey(l => l.EventId);

        modelBuilder.Entity<F_File>()
            .HasMany<F_FileLink>()
            .WithOne()
            .HasForeignKey(l => l.FileId);

        modelBuilder.Entity<S_HistoryEvent>()
            .HasMany(h => h.Links)
            .WithOne()
            .HasForeignKey(l => l.SourceId);

        modelBuilder.Entity<F_Record>()
            .HasIndex(r => new { r.EntityTypeId, r.CreatedAt });

        modelBuilder.Entity<S_Note>()
            .HasIndex(n => n.CreatedAt);

        modelBuilder.Entity<NoteSearchEntry>()
            .HasNoKey()
            .ToView("NoteSearch");

        modelBuilder.Entity<RecordSearchEntry>()
            .HasNoKey()
            .ToView("RecordSearch");

        base.OnModelCreating(modelBuilder);
    }
}
