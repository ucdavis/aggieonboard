using Microsoft.EntityFrameworkCore;
using Server.Core.Domain;

namespace Server.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    private const int KeyLen = 60;
    private const int NameLen = 200;
    private const int EmailLen = 320;
    private const int EnumLen = 40;

    public DbSet<StepDefinition> StepDefinitions => Set<StepDefinition>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<StaffMember> Staff => Set<StaffMember>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<CandidateStep> CandidateSteps => Set<CandidateStep>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<TaskNotification> TaskNotifications => Set<TaskNotification>();
    public DbSet<SentMessage> SentMessages => Set<SentMessage>();
    public DbSet<CandidateAccessToken> CandidateAccessTokens => Set<CandidateAccessToken>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<AppointmentType> AppointmentTypes => Set<AppointmentType>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<WeeklyHours> WeeklyHours => Set<WeeklyHours>();
    public DbSet<ScheduleSession> ScheduleSessions => Set<ScheduleSession>();
    public DbSet<Closure> Closures => Set<Closure>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as their names, so the tables read without a lookup and reordering an enum is safe.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(EnumLen);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StepDefinition>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(KeyLen);
            e.Property(x => x.AuditReference).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(NameLen);
            e.Property(x => x.RequestMessageKey).HasMaxLength(KeyLen);
            e.Property(x => x.AcknowledgmentMessageKey).HasMaxLength(KeyLen);
            e.Property(x => x.VendorMessageKey).HasMaxLength(KeyLen);
            e.Property(x => x.DeadlineQuestion).HasMaxLength(1000);
            e.Property(x => x.WatchForSender).HasMaxLength(NameLen);
            e.Property(x => x.WatchForFromAddress).HasMaxLength(EmailLen);
            e.Property(x => x.WatchForSubject).HasMaxLength(NameLen);
            e.Property(x => x.TaskLabel).HasMaxLength(NameLen);
            e.Property(x => x.TaskSummary).HasMaxLength(1000);
            e.OwnsOne(x => x.Deadline, d =>
            {
                d.Property(r => r.Anchor).HasColumnName("DeadlineAnchor");
                d.Property(r => r.Amount).HasColumnName("DeadlineAmount");
                d.Property(r => r.Unit).HasColumnName("DeadlineUnit");
            });
            e.Navigation(x => x.Deadline).IsRequired();
        });

        modelBuilder.Entity<MessageTemplate>(e =>
        {
            e.HasIndex(x => new { x.Key, x.Version }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(KeyLen);
            e.Property(x => x.AuditReference).HasMaxLength(20);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(EmailLen);
            e.HasMany(x => x.Sections).WithOne().HasForeignKey(s => s.MessageTemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageTemplateSection>(e => e.ToTable("MessageTemplateSections"));

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(KeyLen);
            e.Property(x => x.Value).HasMaxLength(2000);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.OpenQuestion).HasMaxLength(1000);
        });

        modelBuilder.Entity<Holiday>(e =>
        {
            e.HasKey(x => x.Date);
            e.Property(x => x.Name).HasMaxLength(NameLen);
        });

        modelBuilder.Entity<StaffMember>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Name).HasMaxLength(NameLen);
            e.Property(x => x.Email).HasMaxLength(EmailLen);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Ignore(x => x.ContactDetails);
        });

        modelBuilder.Entity<Candidate>(e =>
        {
            e.HasIndex(x => x.Email);
            e.Property(x => x.FirstName).HasMaxLength(NameLen);
            e.Property(x => x.LastName).HasMaxLength(NameLen);
            e.Property(x => x.Email).HasMaxLength(EmailLen);
            e.Property(x => x.PositionTitle).HasMaxLength(NameLen);
            e.Property(x => x.Department).HasMaxLength(NameLen);
            e.Property(x => x.HiringManagerContact).HasMaxLength(500);
            e.Property(x => x.EmployeeId).HasMaxLength(20);
            e.Property(x => x.I9RepresentativeName).HasMaxLength(NameLen);
            e.Property(x => x.I9RepresentativeEmail).HasMaxLength(EmailLen);
            e.Property(x => x.FingerprintAtiNumber).HasMaxLength(Server.Core.Journey.CandidateAnswers.AtiNumberMax);

            // Staff are never deleted out from under a candidate; deactivate them instead.
            e.HasOne(x => x.Recruiter).WithMany().HasForeignKey(x => x.RecruiterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ServiceChannel).WithMany().HasForeignKey(x => x.ServiceChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.UnitHr).WithMany().HasForeignKey(x => x.UnitHrId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Qualifications).WithOne().HasForeignKey(q => q.CandidateId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Steps).WithOne(s => s.Candidate).HasForeignKey(s => s.CandidateId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CandidateQualification>(e =>
        {
            e.ToTable("CandidateQualifications");
            e.Property(x => x.Name).HasMaxLength(NameLen);
            e.Property(x => x.DocumentToSubmit).HasMaxLength(500);
        });

        modelBuilder.Entity<CandidateStep>(e =>
        {
            e.HasIndex(x => new { x.CandidateId, x.StepDefinitionId }).IsUnique();
            e.HasOne(x => x.Step).WithMany().HasForeignKey(x => x.StepDefinitionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CompletedBy).WithMany().HasForeignKey(x => x.CompletedById).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.StaffNote).HasMaxLength(2000);
        });

        modelBuilder.Entity<TaskNotification>(e =>
        {
            e.HasIndex(x => new { x.SettledAt, x.OpenedAt });
            e.Property(x => x.Outcome).HasMaxLength(500);
            e.HasOne<Candidate>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CandidateStep).WithMany().HasForeignKey(x => x.CandidateStepId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.HasIndex(x => new { x.CandidateId, x.At });
            e.Property(x => x.ActorName).HasMaxLength(NameLen);
            e.Property(x => x.Action).HasMaxLength(KeyLen);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.HasOne<Candidate>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.ActorStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SentMessage>(e =>
        {
            e.HasIndex(x => new { x.CandidateId, x.SentAt });
            e.Property(x => x.MessageKey).HasMaxLength(KeyLen);
            e.Property(x => x.AuditReference).HasMaxLength(20);
            e.Property(x => x.ToAddress).HasMaxLength(EmailLen);
            e.Property(x => x.ReplyTo).HasMaxLength(EmailLen);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.SentByName).HasMaxLength(NameLen);
            e.HasOne<Candidate>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.MessageTemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.SentByStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CandidateAccessToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.CandidateId, x.CreatedAt });
            e.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
            e.HasOne<Candidate>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(KeyLen);
            e.Property(x => x.Name).HasMaxLength(NameLen);
        });

        modelBuilder.Entity<TeamMembership>(e =>
        {
            e.HasKey(x => new { x.TeamId, x.StaffMemberId });
            e.HasOne(x => x.Team).WithMany(t => t.Members).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.StaffMember).WithMany(s => s.Teams).HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppointmentType>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(KeyLen);
            e.Property(x => x.Name).HasMaxLength(NameLen);
            e.Property(x => x.StepKey).HasMaxLength(KeyLen);
            e.Property(x => x.WindowQuestion).HasMaxLength(1000);
            e.OwnsOne(x => x.Earliest, d =>
            {
                d.Property(r => r.Anchor).HasColumnName("EarliestAnchor");
                d.Property(r => r.Amount).HasColumnName("EarliestAmount");
                d.Property(r => r.Unit).HasColumnName("EarliestUnit");
            });
            e.OwnsOne(x => x.Latest, d =>
            {
                d.Property(r => r.Anchor).HasColumnName("LatestAnchor");
                d.Property(r => r.Amount).HasColumnName("LatestAmount");
                d.Property(r => r.Unit).HasColumnName("LatestUnit");
            });
            e.Navigation(x => x.Earliest).IsRequired();
            e.Navigation(x => x.Latest).IsRequired();
            e.HasMany(x => x.Locations).WithOne(l => l.AppointmentType).HasForeignKey(l => l.AppointmentTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Location>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(NameLen);
            e.Property(x => x.Address).HasMaxLength(500);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Hours).WithOne().HasForeignKey(h => h.LocationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleSession>(e =>
        {
            e.HasIndex(x => new { x.LocationId, x.Start });
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Closure>(e =>
        {
            e.HasIndex(x => new { x.LocationId, x.Date }).IsUnique();
            e.Property(x => x.Reason).HasMaxLength(NameLen);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Appointment>(e =>
        {
            e.HasIndex(x => new { x.LocationId, x.Start });
            e.HasIndex(x => new { x.CandidateId, x.AppointmentTypeId });
            e.Property(x => x.BookedBy).HasMaxLength(NameLen);
            e.Property(x => x.StatusChangedBy).HasMaxLength(NameLen);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Ignore(x => x.IsActive);
            e.HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AppointmentType).WithMany().HasForeignKey(x => x.AppointmentTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(modelBuilder);
    }
}
