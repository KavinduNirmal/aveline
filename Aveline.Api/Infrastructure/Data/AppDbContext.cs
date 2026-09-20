using Aveline.Api.Modules.Admin.Models;
using Aveline.Api.Modules.Attendance.Models;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Home.Models;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    // Visual Intelligence Module (Slice 2)
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryImage> InventoryImages => Set<InventoryImage>();
    public DbSet<CustomerMatch> CustomerMatches => Set<CustomerMatch>();
    public DbSet<OutfitComposition> OutfitCompositions => Set<OutfitComposition>();
    public DbSet<OutfitItem> OutfitItems => Set<OutfitItem>();
    public DbSet<SourcingRequest> SourcingRequests => Set<SourcingRequest>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    // Commerce Module (Slice 3)
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ApprovalQueueEntry> ApprovalQueue => Set<ApprovalQueueEntry>();
    public DbSet<DeliveryPlan> DeliveryPlans => Set<DeliveryPlan>();
    public DbSet<BusinessRule> BusinessRules => Set<BusinessRule>();

    public DbSet<AdminApprovalRequest> AdminApprovalRequests => Set<AdminApprovalRequest>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();

    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    public DbSet<UsageAccount> UsageAccounts => Set<UsageAccount>();

    public DbSet<BlossomConversionRule> BlossomConversionRules => Set<BlossomConversionRule>();

    public DbSet<BlossomPriceEntry> BlossomPriceEntries => Set<BlossomPriceEntry>();

    public DbSet<BlossomLedgerEntry> BlossomLedgerEntries => Set<BlossomLedgerEntry>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Aveline's own revenue journal (S-50). Append-only; see <c>IncomeLedgerEntry</c>.</summary>
    public DbSet<Modules.Revenue.Models.IncomeLedgerEntry> IncomeLedgerEntries =>
        Set<Modules.Revenue.Models.IncomeLedgerEntry>();

    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();

    public DbSet<PlanEntitlementOverride> PlanEntitlementOverrides => Set<PlanEntitlementOverride>();

    public DbSet<OrganizationSubscription> OrganizationSubscriptions => Set<OrganizationSubscription>();

    /// <summary>Daily per-organization subscription snapshot backing the S-47 trend.</summary>
    public DbSet<Aveline.Api.Modules.Analytics.Models.OrganizationSubscriptionSnapshot>
        OrganizationSubscriptionSnapshots =>
        Set<Aveline.Api.Modules.Analytics.Models.OrganizationSubscriptionSnapshot>();

    public DbSet<DailyBillingMetric> DailyBillingMetrics => Set<DailyBillingMetric>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<Modules.ApiAccess.Models.ApiKey> ApiKeys => Set<Modules.ApiAccess.Models.ApiKey>();

    // Statistics Module (Phase 4)
    public DbSet<Modules.Statistics.Models.AgentWorkflowRun> AgentWorkflowRuns =>
        Set<Modules.Statistics.Models.AgentWorkflowRun>();

    public DbSet<Modules.Statistics.Models.AgentStepRun> AgentStepRuns =>
        Set<Modules.Statistics.Models.AgentStepRun>();

    public DbSet<Modules.Statistics.Models.DailyAgentMetric> DailyAgentMetrics =>
        Set<Modules.Statistics.Models.DailyAgentMetric>();

    // Statistics Module (Phase 5 — API consumption statistics, M7)
    public DbSet<Modules.Statistics.Models.ApiRequestMetric> ApiRequestMetrics =>
        Set<Modules.Statistics.Models.ApiRequestMetric>();

    public DbSet<Modules.Statistics.Models.ApiRequestLog> ApiRequestLogs =>
        Set<Modules.Statistics.Models.ApiRequestLog>();

    public DbSet<Modules.Statistics.Models.ApiQuotaUsage> ApiQuotaUsage =>
        Set<Modules.Statistics.Models.ApiQuotaUsage>();

    // Statistics Module (Phase 6 — system statistics and alerts, M8)
    public DbSet<Modules.Statistics.Models.SystemMetricSample> SystemMetricSamples =>
        Set<Modules.Statistics.Models.SystemMetricSample>();

    public DbSet<Modules.Statistics.Models.SystemAlertRule> SystemAlertRules =>
        Set<Modules.Statistics.Models.SystemAlertRule>();

    public DbSet<Modules.Statistics.Models.SystemAlert> SystemAlerts =>
        Set<Modules.Statistics.Models.SystemAlert>();

    public DbSet<IntegrationCredential> IntegrationCredentials => Set<IntegrationCredential>();

    public DbSet<InboundMessageLog> InboundMessageLogs => Set<InboundMessageLog>();

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    public DbSet<NotificationRecord> NotificationRecords => Set<NotificationRecord>();

    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    public DbSet<UserDeviceToken> UserDeviceTokens => Set<UserDeviceToken>();

    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<SignOffDecision> SignOffDecisions => Set<SignOffDecision>();

    /// <summary>The thread's per-user read markers (D5 = B).</summary>
    public DbSet<ConversationReadState> ConversationReadStates => Set<ConversationReadState>();

    /// <summary>A thread message's attachments (D8).</summary>
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();

    // Customer Concierge Module (Slice 1)
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerPreference> CustomerPreferences => Set<CustomerPreference>();
    public DbSet<CustomerEvent> CustomerEvents => Set<CustomerEvent>();
    public DbSet<CustomerMemory> CustomerMemories => Set<CustomerMemory>();
    public DbSet<CustomerInteraction> CustomerInteractions => Set<CustomerInteraction>();
    public DbSet<CustomerConsent> CustomerConsents => Set<CustomerConsent>();
    public DbSet<CustomerTag> CustomerTags => Set<CustomerTag>();

    // Home module: the persisted half of the derived focus feed.
    public DbSet<FocusDismissal> FocusDismissals => Set<FocusDismissal>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
