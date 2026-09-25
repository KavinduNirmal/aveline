using Aveline.Api.Modules.Admin.Models;
using Aveline.Api.Modules.Analytics.Models;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.Attendance.Models;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Handbook.Models;
using Aveline.Api.Modules.Home.Models;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Privacy.Models;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Models;
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
    public DbSet<CatalogTag> CatalogTags => Set<CatalogTag>();
    public DbSet<InventoryItemTag> InventoryItemTags => Set<InventoryItemTag>();
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

    /// <summary>
    /// The boutique's own takings journal. Deliberately a **different table** from
    /// <see cref="IncomeLedgerEntries"/>, which records what Aveline billed the shop.
    /// </summary>
    public DbSet<BoutiqueSaleEntry> BoutiqueSaleEntries => Set<BoutiqueSaleEntry>();

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
    public DbSet<IncomeLedgerEntry> IncomeLedgerEntries => Set<IncomeLedgerEntry>();

    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();

    public DbSet<PlanEntitlementOverride> PlanEntitlementOverrides => Set<PlanEntitlementOverride>();

    public DbSet<OrganizationSubscription> OrganizationSubscriptions => Set<OrganizationSubscription>();

    // Payments module (plan §6.3): the provider-neutral charge record and the webhook inbox.
    public DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();

    public DbSet<PaymentProviderEvent> PaymentProviderEvents => Set<PaymentProviderEvent>();

    /// <summary>Daily per-organization subscription snapshot backing the S-47 trend.</summary>
    public DbSet<OrganizationSubscriptionSnapshot> OrganizationSubscriptionSnapshots =>
        Set<OrganizationSubscriptionSnapshot>();

    public DbSet<DailyBillingMetric> DailyBillingMetrics => Set<DailyBillingMetric>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    // Statistics Module (Phase 4)
    public DbSet<AgentWorkflowRun> AgentWorkflowRuns => Set<AgentWorkflowRun>();

    public DbSet<AgentStepRun> AgentStepRuns => Set<AgentStepRun>();

    public DbSet<DailyAgentMetric> DailyAgentMetrics => Set<DailyAgentMetric>();

    // Statistics Module (Phase 5 — API consumption statistics, M7)
    public DbSet<ApiRequestMetric> ApiRequestMetrics => Set<ApiRequestMetric>();

    public DbSet<ApiRequestLog> ApiRequestLogs => Set<ApiRequestLog>();

    public DbSet<ApiQuotaUsage> ApiQuotaUsage => Set<ApiQuotaUsage>();

    // Statistics Module (Phase 6 — system statistics and alerts, M8)
    public DbSet<SystemMetricSample> SystemMetricSamples => Set<SystemMetricSample>();

    public DbSet<SystemAlertRule> SystemAlertRules => Set<SystemAlertRule>();

    public DbSet<SystemAlert> SystemAlerts => Set<SystemAlert>();

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

    /// <summary>Append-only consent history (plan §3.2).</summary>
    public DbSet<ConsentAuditEntry> ConsentAuditEntries => Set<ConsentAuditEntry>();

    // Privacy module (plan §3.3, §7.3): the durable data-subject-request log and the consent
    // tombstone that survives erasure (Q-4).
    public DbSet<DataSubjectRequest> DataSubjectRequests => Set<DataSubjectRequest>();

    public DbSet<PrivacyErasureTombstone> PrivacyErasureTombstones => Set<PrivacyErasureTombstone>();

    // Home module: the persisted half of the derived focus feed.
    public DbSet<FocusDismissal> FocusDismissals => Set<FocusDismissal>();

    // Handbook module (ADR-025): the global, non-tenant knowledge corpus the agent retrieves from.
    public DbSet<HandbookChunk> HandbookChunks => Set<HandbookChunk>();

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
