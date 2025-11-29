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
    public DbSet<AutomationExecution> AutomationExecutions => Set<AutomationExecution>();
    public DbSet<S_Note> Notes => Set<S_Note>();
    public DbSet<J_Note_Link> NoteLinks => Set<J_Note_Link>();
    public DbSet<S_Event> Events => Set<S_Event>();
    public DbSet<J_Event_Link> EventLinks => Set<J_Event_Link>();
    public DbSet<F_File> Files => Set<F_File>();
    public DbSet<F_FileLink> FileLinks => Set<F_FileLink>();
    public DbSet<F_Record> Records => Set<F_Record>();
    public DbSet<NoteSearchEntry> NoteSearch => Set<NoteSearchEntry>();
    public DbSet<RecordSearchEntry> RecordSearch => Set<RecordSearchEntry>();
    public DbSet<FileSearchEntry> FileSearch => Set<FileSearchEntry>();
    public DbSet<SemanticSearchEntry> SemanticSearch => Set<SemanticSearchEntry>();
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
        modelBuilder.Entity<S_Module>(builder =>
        {
            builder.Property(m => m.Name).IsRequired().HasMaxLength(128);
            builder.Property(m => m.Description).HasMaxLength(1024);
            builder.HasMany(m => m.EntityTypes).WithOne().HasForeignKey(e => e.ModuleId);
            builder.HasMany(m => m.Reports).WithOne().HasForeignKey(r => r.ModuleId);
            builder.HasMany(m => m.AutomationRules).WithOne().HasForeignKey(r => r.ModuleId);
        });

        modelBuilder.Entity<S_EntityType>(builder =>
        {
            builder.Property(e => e.Name).IsRequired().HasMaxLength(128);
            builder.Property(e => e.PluralName).IsRequired().HasMaxLength(128);
            builder.Property(e => e.Icon).HasMaxLength(64);
            builder.HasMany(e => e.Fields).WithOne().HasForeignKey(f => f.EntityTypeId);
            builder.HasMany(e => e.Relations).WithOne().HasForeignKey(r => r.EntityTypeId);
        });

        modelBuilder.Entity<S_Field>(builder =>
        {
            builder.Property(f => f.Name).IsRequired().HasMaxLength(128);
            builder.Property(f => f.Label).IsRequired().HasMaxLength(128);
            builder.Property(f => f.DataType).HasConversion<string>().HasMaxLength(32);
            builder.Property(f => f.LookupTarget).HasMaxLength(128);
            builder.Property(f => f.OptionsJson).HasMaxLength(4000);
            builder.Property(f => f.DefaultValue).HasMaxLength(1024);
        });

        modelBuilder.Entity<S_Relation>(builder =>
        {
            builder.Property(r => r.FromField).IsRequired().HasMaxLength(128);
            builder.Property(r => r.ToEntity).IsRequired().HasMaxLength(128);
            builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<S_ReportDefinition>(builder =>
        {
            builder.Property(r => r.Name).IsRequired().HasMaxLength(128);
            builder.Property(r => r.QueryDefinition).IsRequired().HasMaxLength(4000);
            builder.Property(r => r.Visualization).HasMaxLength(128);
        });

        modelBuilder.Entity<S_AutomationRule>(builder =>
        {
            builder.Property(a => a.Name).IsRequired().HasMaxLength(128);
            builder.Property(a => a.Trigger).HasConversion<string>().HasMaxLength(32);
            builder.Property(a => a.TriggerFilter).IsRequired().HasMaxLength(256);
            builder.Property(a => a.IsEnabled).HasDefaultValue(true);
            builder.HasMany(a => a.Conditions).WithOne().HasForeignKey(c => c.AutomationRuleId);
            builder.HasMany(a => a.Actions).WithOne().HasForeignKey(c => c.AutomationRuleId);
        });

        modelBuilder.Entity<AutomationCondition>(builder => builder.Property(c => c.Expression).IsRequired().HasMaxLength(1024));

        modelBuilder.Entity<AutomationAction>(builder =>
        {
            builder.Property(a => a.ActionType).HasConversion<string>().HasMaxLength(64);
            builder.Property(a => a.ParametersJson).IsRequired().HasMaxLength(4000);
        });

        modelBuilder.Entity<AutomationExecution>(builder =>
        {
            builder.Property(e => e.Trigger).IsRequired().HasMaxLength(128);
            builder.Property(e => e.PayloadSnapshot).IsRequired().HasMaxLength(4000);
            builder.Property(e => e.Outcome).IsRequired().HasMaxLength(512);
            builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            builder.Property(e => e.StartedAt).IsRequired();
            builder.HasOne<S_AutomationRule>().WithMany().HasForeignKey(e => e.RuleId);
            builder.HasIndex(e => e.RuleId);
            builder.HasIndex(e => e.StartedAt);
        });

        modelBuilder.Entity<S_Note>(builder =>
        {
            builder.Property(n => n.Title).IsRequired().HasMaxLength(256);
            builder.Property(n => n.Source).HasConversion<string>().HasMaxLength(32);
            builder.Property(n => n.JournalContext).HasMaxLength(512);
            builder.HasMany(n => n.Links).WithOne().HasForeignKey(l => l.NoteId);
            builder.HasIndex(n => n.CreatedAt);
        });

        modelBuilder.Entity<J_Note_Link>(builder =>
        {
            builder.Property(l => l.TargetType).IsRequired().HasMaxLength(128);
        });

        modelBuilder.Entity<S_Event>(builder =>
        {
            builder.Property(e => e.Title).IsRequired().HasMaxLength(256);
            builder.Property(e => e.Description).HasMaxLength(1024);
            builder.HasMany(e => e.Links).WithOne().HasForeignKey(l => l.EventId);
        });

        modelBuilder.Entity<J_Event_Link>(builder => builder.Property(l => l.TargetType).IsRequired().HasMaxLength(128));

        modelBuilder.Entity<F_File>(builder =>
        {
            builder.Property(f => f.FileName).IsRequired().HasMaxLength(256);
            builder.Property(f => f.MimeType).IsRequired().HasMaxLength(128);
            builder.Property(f => f.StoragePath).IsRequired().HasMaxLength(512);
            builder.Property(f => f.ThumbnailPath).HasMaxLength(512);
            builder.Property(f => f.Sha256).IsRequired().HasMaxLength(128);
            builder.HasMany<F_FileLink>().WithOne().HasForeignKey(l => l.FileId);
        });

        modelBuilder.Entity<F_FileLink>(builder =>
        {
            builder.Property(l => l.TargetType).IsRequired().HasMaxLength(128);
            builder.Property(l => l.Relation).HasMaxLength(64);
        });

        modelBuilder.Entity<F_Record>(builder =>
        {
            builder.Property(r => r.DataJson).IsRequired();
            builder.HasIndex(r => new { r.EntityTypeId, r.CreatedAt });
        });

        modelBuilder.Entity<S_VisionAnalysis>(builder =>
        {
            builder.Property(v => v.AnalysisType).HasConversion<string>().HasMaxLength(32);
            builder.Property(v => v.ResultJson).IsRequired();
        });

        modelBuilder.Entity<S_HistoryEvent>(builder =>
        {
            builder.Property(h => h.Title).IsRequired().HasMaxLength(256);
            builder.Property(h => h.Description).HasMaxLength(1024);
            builder.HasMany(h => h.Links).WithOne().HasForeignKey(l => l.SourceId);
        });

        modelBuilder.Entity<S_Link>(builder =>
        {
            builder.Property(l => l.SourceType).IsRequired().HasMaxLength(128);
            builder.Property(l => l.TargetType).IsRequired().HasMaxLength(128);
            builder.Property(l => l.Relation).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<DashboardWidget>(builder =>
        {
            builder.Property(w => w.Title).IsRequired().HasMaxLength(128);
            builder.Property(w => w.WidgetType).IsRequired().HasMaxLength(64);
            builder.Property(w => w.ConfigurationJson).IsRequired().HasMaxLength(4000);
        });

        modelBuilder.Entity<TemplatePackage>(builder =>
        {
            builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
            builder.Property(t => t.Description).HasMaxLength(512);
            builder.Property(t => t.Payload).IsRequired();
            builder.Property(t => t.Version).IsRequired().HasMaxLength(32);
        });

        modelBuilder.Entity<MarketplaceItem>(builder =>
        {
            builder.Property(m => m.Name).IsRequired().HasMaxLength(128);
            builder.Property(m => m.Category).IsRequired().HasMaxLength(64);
            builder.Property(m => m.PackagePath).IsRequired().HasMaxLength(512);
        });

        modelBuilder.Entity<PredictionInsight>(builder =>
        {
            builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(32);
            builder.Property(p => p.Message).IsRequired().HasMaxLength(512);
            builder.Property(p => p.TargetType).HasMaxLength(128);
        });

        modelBuilder.Entity<UserPersona>(builder =>
        {
            builder.Property(p => p.Name).IsRequired().HasMaxLength(128);
            builder.Property(p => p.Tone).HasConversion<string>().HasMaxLength(32);
            builder.Property(p => p.StyleNotes).HasMaxLength(1024);
        });

        modelBuilder.Entity<STable>(builder =>
        {
            builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
            builder.Property(t => t.DisplayName).IsRequired().HasMaxLength(128);
            builder.Property(t => t.Description).HasMaxLength(512);
            builder.HasMany(t => t.Fields).WithOne().HasForeignKey(f => f.TableId);
            builder.HasMany(t => t.Views).WithOne().HasForeignKey(v => v.TableId);
            builder.HasIndex(t => t.Name).IsUnique();
        });

        modelBuilder.Entity<SFieldDefinition>(builder =>
        {
            builder.Property(f => f.Name).IsRequired().HasMaxLength(128);
            builder.Property(f => f.Label).IsRequired().HasMaxLength(128);
            builder.Property(f => f.DataType).HasConversion<string>().HasMaxLength(32);
            builder.Property(f => f.DefaultValue).HasMaxLength(1024);
            builder.Property(f => f.LookupTarget).HasMaxLength(128);
            builder.HasIndex(f => new { f.TableId, f.Name }).IsUnique();
        });

        modelBuilder.Entity<SViewDefinition>(builder =>
        {
            builder.Property(v => v.Name).IsRequired().HasMaxLength(128);
            builder.Property(v => v.QueryDefinition).IsRequired().HasMaxLength(4000);
            builder.Property(v => v.Visualization).HasMaxLength(128);
            builder.HasIndex(v => new { v.TableId, v.Name }).IsUnique();
        });

        modelBuilder.Entity<NoteSearchEntry>()
            .HasNoKey()
            .ToView("NoteSearch");

        modelBuilder.Entity<RecordSearchEntry>()
            .HasNoKey()
            .ToView("RecordSearch");

        modelBuilder.Entity<FileSearchEntry>()
            .HasNoKey()
            .ToView("FileSearch");

        modelBuilder.Entity<SemanticSearchEntry>(builder =>
        {
            builder.Property(e => e.TargetType).IsRequired().HasMaxLength(64);
            builder.Property(e => e.Title).IsRequired().HasMaxLength(256);
            builder.Property(e => e.Content).IsRequired();
            builder.Property(e => e.EmbeddingJson).HasMaxLength(16000);
            builder.HasIndex(e => new { e.TargetType, e.TargetId }).IsUnique();
            builder.HasIndex(e => e.IndexedAt);
        });

        base.OnModelCreating(modelBuilder);
    }
}
