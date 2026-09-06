# Aveline AI — Pricing Model

**Version 1.1 | 2026-09-06**

---

## Document Purpose

This document defines the proposed pricing model for Aveline AI, a boutique concierge and AI-assisted boutique operations platform.

The pricing model is designed around four principles:

1. **Flowers are the primary customer-facing usage unit.**
2. **AI infrastructure costs are measured internally rather than exposed as token quotas.**
3. **Every plan should allow customers to experience Aveline's core capabilities.**
4. **Paid plans primarily increase scale, automation, team capacity, and business intelligence.**

This document is a living draft and should be updated as real usage and cost data becomes available.

---

# Table of Contents

1. Executive Summary
2. Pricing Philosophy
3. The Flower System
4. Flower Calculation
5. Plan Overview
6. Plan Details
7. Free Tier Strategy
8. Staff and Customer Limits
9. Add-ons
10. Upgrades and Billing
11. Internal Cost Protection
12. Usage Tracking Architecture
13. Decision Flow
14. Implementation Requirements
15. Open Questions
16. Viva Explanation
17. Brand Positioning

---

# 1. Executive Summary

Aveline uses a hybrid SaaS pricing model built around:

- **Flower Credits** — the primary AI usage allowance.
- **Staff Seats** — the number of employees who can actively use Aveline.
- **Active Customer Limits** — the number of active customer profiles supported by a plan.
- **Features and Capabilities** — integrations, automation, analytics, API access, and business context differentiate higher plans.

The previous model also exposed monthly token caps to customers.

This has been revised.

## Revised principle

> **Customers pay for Aveline's AI capabilities through Flowers, not directly for tokens.**

Tokens remain an **internal infrastructure and cost-management metric**.

A customer should understand:

> "I have 750 Flowers this month."

They should not need to understand:

> "I have 500,000 LLM tokens."

---

# 2. Pricing Philosophy

## 2.1 What Aveline is selling

Aveline is not selling access to an LLM API.

It is selling:

- customer intelligence
- boutique assistance
- product intelligence
- recommendations
- workflow automation
- customer communication
- business-specific AI context

Therefore, the pricing model should represent **AI work performed by Aveline**, rather than exposing the underlying technical implementation.

---

## 2.2 The Four Pricing Dimensions

| Dimension        | Purpose                                          |
| ---------------- | ------------------------------------------------ |
| Flowers          | Measures AI usage                                |
| Staff Seats      | Determines team capacity                         |
| Active Customers | Determines customer-data scale                   |
| Features         | Determines business capability and plan maturity |

The dimensions serve different purposes.

Flowers measure **how much AI the customer uses**.

Staff and customer limits determine **how large the business using Aveline is**.

Features determine **what Aveline can do for that business**.

---

# 3. The Flower System

## 3.1 What is a Flower?

A Flower is Aveline's customer-facing unit of AI usage.

A Flower does **not** represent a literal API token count.

Instead:

> **A Flower represents a normalized unit of AI work performed by Aveline.**

The underlying AI usage and infrastructure cost are measured internally and converted into Flower usage.

This allows Aveline to change:

- AI providers
- models
- prompts
- agent architecture
- tool usage
- model routing

without forcing a redesign of the customer-facing pricing model.

---

## 3.2 Why Flowers?

Flowers provide a simple abstraction.

Instead of exposing:

```text
Input tokens
Output tokens
Vision tokens
Tool-result tokens
Model costs
```

Aveline exposes:

```text
Your bouquet
     ↓
750 Flowers
     ↓
AI work
```

This preserves the "Quiet Luxury" positioning while still allowing precise internal cost management.

---

# 4. Flower Calculation

## 4.1 Initial Normalization

For the initial implementation, Aveline may use a token-equivalent normalization.

Proposed baseline:

> **1 Flower ≈ 1,000 normalized AI usage units**

For example:

|     AI Usage | Flower Usage |
| -----------: | -----------: |
|    250 units | 0.25 Flowers |
|    500 units | 0.50 Flowers |
|  1,000 units |     1 Flower |
|  2,500 units |  2.5 Flowers |
|  5,000 units |    5 Flowers |
| 10,000 units |   10 Flowers |

These values are an implementation baseline, not a permanent pricing guarantee.

---

## 4.2 Actual AI Usage

An Aveline workflow may involve multiple model calls and tools.

Example:

```text
Customer asks:
"Find something suitable for Maya."

        ↓

LLM call
800 tokens

        ↓

Customer lookup tool
Database query + model interpretation
400 tokens

        ↓

Inventory search
600 tokens

        ↓

Recommendation reasoning
1,200 tokens

        ↓

Final response
700 tokens

────────────────────
Total normalized usage
3,700 units

3,700 / 1,000
= 3.7 Flowers
```

The workflow therefore consumes approximately:

> **3.7 Flowers**

---

## 4.3 Tool Calls

A tool call does not automatically cost Flowers simply because a tool was called.

For example:

```text
SearchInventory()
```

is not itself necessarily an AI-token expense.

However, if the result is returned to an LLM and contributes to AI usage, the associated model usage is included in the Flower calculation.

Therefore:

> **Flowers measure AI work, not the number of API/database calls.**

Infrastructure costs such as database queries, Redis operations, or external APIs should be tracked separately.

---

## 4.4 Model Cost Normalization

Token counts alone do not necessarily represent real AI costs.

Different models may have different pricing.

Therefore, Aveline should eventually move toward:

```text
Actual model usage
        ↓
Model-specific cost calculation
        ↓
Normalized AI cost
        ↓
Flower units
```

This means the customer-facing Flower system remains stable even if Aveline changes AI providers or models.

The exact normalization formula should be determined after collecting real production usage data.

---

# 5. Plan Overview

|                      |      Seed |        Bloom |       Orchid |          Rose |
| -------------------- | --------: | -----------: | -----------: | ------------: |
| **Price**            |      Free | LKR 3,500/mo | LKR 9,000/mo | LKR 20,000/mo |
| **Flowers**          |       150 |          750 |        2,000 |         5,000 |
| **Staff**            |         1 |            3 |           10 |            25 |
| **Active Customers** |        50 |          250 |        1,000 |         5,000 |
| Memory Agent         |       Yes |          Yes |          Yes |           Yes |
| Visual Agent         |   Limited |          Yes |          Yes |           Yes |
| Commerce Agent       |   Limited |      Limited |          Yes |           Yes |
| WhatsApp             |   Limited |          Yes |          Yes |           Yes |
| Custom AI Context    |        No |        Basic |         Full |          Full |
| Automation           |        No |        Basic |     Advanced |      Advanced |
| Analytics            |     Basic |        Basic |     Advanced |      Advanced |
| API Access           |        No |           No |           No |           Yes |
| Custom Agents        |        No |           No |           No |           Yes |
| Support              | Community |        Email |     Priority |     Dedicated |

---

# 6. Plan Details

## 6.1 Seed — Free

### Purpose

**Discover Aveline.**

Seed is designed to let a boutique experience the important parts of Aveline before committing to a subscription.

### Limits

- Free
- 150 Flowers/month
- 1 staff member
- 50 active customers

### Capabilities

- Memory Agent
- Limited Visual Agent
- Limited Commerce capabilities
- Limited WhatsApp usage
- Basic customer intelligence
- Basic analytics

### Restrictions

- No custom AI context
- No advanced automation
- No API access
- No custom agents
- Community support

### Product philosophy

The free tier should demonstrate the **complete Aveline concept at small scale**.

It should not intentionally remove every feature that makes Aveline valuable.

The purpose is:

> **Let the customer experience the product, then charge for scale.**

---

# 6.2 Bloom — Popular

### Price

**LKR 3,500/month**

### Limits

- 750 Flowers/month
- 3 staff members
- 250 active customers

### Capabilities

- Memory Agent
- Visual Agent
- Basic Commerce capabilities
- WhatsApp integration
- Basic custom AI context
- Basic automation
- Basic analytics
- Email support

### Custom Context

Customers can configure business rules such as:

- maximum discount percentage
- delivery rules
- basic pricing rules
- store preferences
- basic brand terminology

### Best for

A small but established boutique with several employees that wants Aveline to become part of its daily workflow.

---

# 6.3 Orchid — Scale

### Price

**LKR 9,000/month**

### Limits

- 2,000 Flowers/month
- 10 staff members
- 1,000 active customers

### Capabilities

- Memory Agent
- Visual Agent
- Commerce Agent
- WhatsApp integration
- Full custom AI context
- Advanced automation
- Advanced analytics
- Priority support

### Custom Context

The boutique can define:

- brand voice
- pricing rules
- discount policies
- delivery policies
- customer service rules
- product terminology
- store-specific procedures
- business-specific AI instructions

### Best for

Established boutiques where Aveline is becoming an operational assistant rather than simply an AI feature.

---

# 6.4 Rose — Scale / Multi-Branch

### Price

**LKR 20,000/month**

### Limits

- 5,000 Flowers/month
- 25 staff members
- 5,000 active customers

### Capabilities

Everything in Orchid, plus:

- API access
- Custom agents
- Advanced automation
- Multi-branch support
- Advanced analytics
- Dedicated support

### Best for

Larger boutique businesses and boutique groups operating multiple locations.

---

# 6.5 Enterprise

Enterprise pricing is not included in the standard pricing ladder.

It is intended for businesses requiring:

- more than 25 staff
- more than 5,000 active customers
- more than 5,000 Flowers
- extensive multi-branch operations
- custom integrations
- SLAs
- dedicated infrastructure
- specialized support
- custom AI deployments

Pricing is negotiated according to requirements.

---

# 7. Free Tier Strategy

The free tier is one of the most important changes to the original pricing model.

## Previous approach

The original Seed plan restricted:

- Visual AI
- Commerce
- WhatsApp

This meant a new customer could potentially experience Aveline without experiencing the features that make it distinctive.

## Revised approach

Seed should provide **limited access to the core product experience**.

For example:

```text
Seed

Memory
✓

Visual
Limited

Commerce
Limited

WhatsApp
Limited

Flowers
150/month

Staff
1

Customers
50
```

The limitations should be based primarily on **volume**, rather than completely disabling the capabilities.

---

## Example onboarding experience

A new boutique could:

1. Create an account.
2. Add several customers.
3. Add several products.
4. Ask Aveline to analyze a product.
5. Ask Aveline to match it with a customer.
6. Generate a recommendation.
7. Test a small number of WhatsApp interactions.
8. Experience the workflow.
9. Reach the Flower limit.
10. Upgrade to Bloom.

The customer should understand what Aveline does **before** being asked to pay.

---

# 8. Staff and Customer Limits

## 8.1 Staff

Staff limits prevent a large team from using a low-cost plan.

Current proposed limits:

```text
Seed     → 1
Bloom    → 3
Orchid   → 10
Rose     → 25
Enterprise → Custom
```

Future versions may introduce additional staff seats as paid add-ons.

---

# 8.2 Active Customers

Customer limits should apply to **active customers**, not necessarily every historical customer record.

This prevents businesses from being penalized for maintaining historical customer data.

An active customer definition should be established technically.

Possible definition:

> A customer who has interacted with the boutique, placed an order, or otherwise maintained an active relationship within a defined period.

Historical records should remain accessible without necessarily counting toward the active customer limit.

---

# 9. Add-ons

Customers should eventually be able to purchase additional Flowers without immediately changing plans.

## Proposed Flower Packs

| Pack          |     Price |
| ------------- | --------: |
| 100 Flowers   |   LKR 500 |
| 500 Flowers   | LKR 2,000 |
| 1,000 Flowers | LKR 3,500 |

These values are initial proposals and should be validated against actual AI costs.

---

## Upgrade Incentive

Add-ons should not be cheaper than permanently upgrading when a customer repeatedly needs additional usage.

Example:

```text
Bloom
LKR 3,500
750 Flowers

Customer repeatedly buys:
1,000 additional Flowers
```

Eventually the customer should recognize that upgrading to Orchid provides better value.

This creates a natural upgrade path.

---

# 10. Upgrades and Billing

## Monthly Billing

Customers are billed monthly.

## Annual Billing

Annual billing may provide:

> **2 months free**

Equivalent to paying for 10 months and receiving 12 months of service.

The exact annual discount should be finalized after pricing validation.

---

## Mid-Cycle Upgrade

When upgrading during a billing period:

- The plan change should take effect immediately.
- Remaining usage should be handled through a defined proration policy.
- Additional Flower allowance should become available immediately.

The exact financial calculation should be implemented by the billing provider rather than manually calculated wherever possible.

---

## Downgrades

Downgrades should generally take effect at the beginning of the next billing cycle.

If the customer currently exceeds the lower plan's:

- staff limit
- active customer limit
- Flower allowance

the downgrade should require resolution before taking effect.

---

## Refunds

Proposed policy:

- Full refund within the first 7 days of a new subscription.
- No standard refunds after the 7-day period.
- Exceptional cases may be handled manually.

Final policy should be reviewed against applicable payment-provider and consumer-protection requirements.

---

# 11. Internal Cost Protection

## 11.1 Token Caps Are No Longer Customer-Facing

Aveline should not expose monthly token quotas such as:

```text
30,000 tokens
150,000 tokens
500,000 tokens
2,000,000 tokens
```

These are implementation details.

The customer interacts with:

> **Flowers**

The Aveline team monitors:

> **Tokens + actual AI cost**

---

## 11.2 Why?

A customer should not encounter:

> "You still have 300 Flowers, but your token quota is exhausted."

This would make the Flower system confusing and undermine its purpose.

---

## 11.3 Internal Safety Controls

Aveline should still maintain internal usage protection.

For example:

```text
AI Request
    ↓
Check Flower balance
    ↓
Allow request
    ↓
Execute workflow
    ↓
Record actual usage
    ↓
Calculate actual cost
    ↓
Update Flower usage
```

Separately:

```text
Abnormally high AI cost
        ↓
Internal safety threshold
        ↓
Throttle / suspend workflow
        ↓
Notify Aveline administrators
```

The internal safety system exists to protect Aveline from:

- runaway agent loops
- accidental recursive workflows
- unexpectedly expensive model calls
- malicious usage
- implementation bugs
- external API failures

It should not normally be presented as a second customer-facing quota.

---

# 12. Usage Tracking Architecture

The backend should maintain a usage record for every AI workflow.

## Suggested model

```text
AIUsageRecord

id
organization_id
request_id
workflow_id

provider
model

input_tokens
output_tokens
cached_tokens

actual_cost

flower_units

created_at
```

The organization-level usage account can maintain:

```text
UsageAccount

organization_id

monthly_flower_limit
flower_used
flower_remaining

active_customer_count
staff_count

current_period_start
current_period_end

status
```

---

## Usage Flow

```text
                AI REQUEST
                    │
                    ▼
           Check Flower balance
                    │
                    ▼
              Run workflow
                    │
          ┌─────────┴─────────┐
          ▼                   ▼
       LLM calls            Tools
          │                   │
          └─────────┬─────────┘
                    ▼
              Record usage
                    │
                    ▼
          Calculate AI cost
                    │
                    ▼
          Convert to Flowers
                    │
                    ▼
          Deduct Flower usage
```

---

# 13. Decision Flow

The customer-facing decision flow should primarily be based on business scale and desired capabilities.

```text
Does the boutique want to try Aveline?
        │
       YES
        ↓
      SEED
        │
        ▼
Needs regular usage + WhatsApp + small team?
        │
       YES
        ↓
      BLOOM
        │
        ▼
Needs full AI capabilities + automation?
        │
       YES
        ↓
     ORCHID
        │
        ▼
Needs API + custom agents + multiple branches?
        │
       YES
        ↓
       ROSE
        │
        ▼
Exceeds Rose limits?
        │
       YES
        ↓
    ENTERPRISE
```

---

# 14. Implementation Requirements

## 14.1 UsageTracker

The UsageTracker service should be responsible for:

- checking Flower availability
- recording AI usage
- calculating Flower consumption
- maintaining monthly usage
- preventing usage beyond limits
- recording model/provider information
- recording actual token usage
- calculating actual AI cost
- supporting usage analytics
- detecting abnormal usage

---

## 14.2 Flower Calculation

Initial implementation:

```text
normalized_usage =
    input_tokens
    + output_tokens
    + applicable cached/multimodal usage

flower_usage =
    normalized_usage / 1,000
```

This should be treated as an initial implementation.

After collecting real usage data, Aveline should evaluate:

```text
AI cost
      ↓
Cost normalization
      ↓
Flower value
```

to ensure each plan remains profitable.

---

## 14.3 Minimum Charge

Very small AI requests may produce inconveniently small Flower values.

The system may therefore define minimum billing increments such as:

```text
0.1 Flower
0.25 Flower
0.5 Flower
1 Flower
```

The final granularity should be selected based on actual usage patterns.

---

## 14.4 Agent Integration

Every AI workflow should return usage metadata to the UsageTracker.

Example:

```text
{
    "workflow_id": "...",
    "model": "...",
    "input_tokens": 1200,
    "output_tokens": 800,
    "cached_tokens": 0,
    "actual_cost": 0.00
}
```

The UsageTracker then determines the corresponding Flower consumption.

---

# 15. Open Questions

| Question                                                        | Status                                                              |
| --------------------------------------------------------------- | ------------------------------------------------------------------- |
| What is the exact monetary value of one Flower?                 | To validate                                                         |
| Should Flowers be calculated from tokens or normalized AI cost? | Initial token-equivalent implementation; cost normalization planned |
| What is the minimum Flower billing increment?                   | To determine                                                        |
| Should unused Flowers roll over?                                | To validate                                                         |
| Should Flower add-ons expire?                                   | To determine                                                        |
| Exact annual pricing?                                           | To determine                                                        |
| Exact proration policy?                                         | To determine                                                        |
| Exact active-customer definition?                               | To determine                                                        |
| Additional staff seat pricing?                                  | Future                                                              |
| Exact Seed WhatsApp allowance?                                  | To determine                                                        |
| Exact Seed Visual/Commerce allowance?                           | To determine                                                        |
| Enterprise pricing?                                             | Future                                                              |
| Internal abnormal-usage threshold?                              | To determine                                                        |

---

# 16. Viva Explanation

### Professor: "How do you price Aveline?"

Aveline uses a hybrid SaaS pricing model.

The primary usage unit is called a **Flower**. Flowers represent a normalized amount of AI work performed by Aveline rather than exposing technical concepts such as API tokens to boutique owners.

Each subscription provides a monthly Flower allowance, while staff and active-customer limits determine the scale of the business that can use the platform.

Higher plans also unlock capabilities such as WhatsApp integration, advanced AI context, automation, analytics, API access, and custom agents.

Internally, Aveline records token usage and actual AI costs for every workflow. This allows us to monitor profitability and protect against abnormal usage without making customers manage technical token quotas.

The important separation is:

> **Flowers are the commercial abstraction; tokens are an internal cost metric.**

This allows Aveline to change AI models or providers without changing the customer-facing pricing model.

---

# 17. Brand Positioning

Aveline's pricing should maintain the brand's "Quiet Luxury" identity.

## Seed

**A small beginning.**

The customer gets their first experience with Aveline.

## Bloom

**The boutique begins to grow.**

Aveline becomes part of everyday customer and product interactions.

## Orchid

**A mature boutique intelligence system.**

Aveline becomes an operational assistant for an established team.

## Rose

**Aveline at scale.**

Designed for larger and multi-branch boutique businesses.

## Pricing Page Copy

> **Every boutique starts as a seed.**
>
> Aveline helps it bloom.
>
> Choose the level of intelligence that fits your boutique.

---

# Final Pricing Philosophy

The pricing system should follow one fundamental principle:

> **Customers pay for the value and scale of Aveline, while Aveline internally manages the cost of delivering that value.**

The customer sees:

```text
Flowers
Staff
Customers
Features
```

Aveline sees:

```text
Tokens
Models
Tool usage
Infrastructure
Actual AI cost
Profit margin
```

This separation should remain fundamental to the pricing architecture.

---

## Next Steps

1. Define the initial Flower normalization formula.
2. Instrument all AI workflows with usage tracking.
3. Measure actual token consumption per workflow.
4. Calculate actual AI cost per workflow.
5. Validate proposed Flower allowances against real usage.
6. Finalize Seed's limited Visual, Commerce, and WhatsApp allowances.
7. Define active-customer rules.
8. Implement the UsageTracker backend service.
9. Integrate UsageTracker with the agent service.
10. Implement billing and Flower allocation.
11. Build the pricing page.
12. Test the pricing model with several hypothetical boutique usage profiles.

**Status: Pricing model proposed — pending usage-cost validation.**
