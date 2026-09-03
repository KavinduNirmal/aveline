# Boutique Concierge AI (Aveline) - Project Context Brief

---

## Project Overview

You are being engaged to build **Aveline AI**, a full-stack platform for semi-luxury boutiques in Sri Lanka. The system solves a real problem: boutique owners and associates manage customer relationships, inventory, sourcing, payments, and deliveries through WhatsApp messages, phone calls, and in-person visits. They have no integrated system to remember customer preferences, track inventory intelligence, or automate business workflows.

The platform consists of a Flutter mobile app for boutique associates and managers, a React web dashboard for boutique owners, an ASP.NET Core Web API as the mandatory public backend, PostgreSQL as the database, and a Python-based Agentic AI subsystem using LangGraph.

This is a university group assignment (SE3090) for 3 students. Each student owns one vertical slice of the system. The system must demonstrate a complete cross-platform workflow: Flutter triggers a request, ASP.NET Core processes it, the Agentic AI subsystem plans and executes, PostgreSQL persists state, React shows approvals, and the Flutter app receives status updates.

---

## Business Context

### The Problem

A boutique in Colombo receives customer inquiries via WhatsApp and Instagram. Customers send messages like:

- "Hi, I have a wedding on Saturday. Do you have anything bluish in my size?"
- "Can you get this dress for me? I saw it on Pinterest." (with reference image)
- "I like the 4th one. Can you reserve it?"

The associate must:

1. Remember who the customer is (size, style, budget, past purchases).
2. Search inventory for matching items.
3. Check if items are in stock.
4. Respond with options.
5. Put holds on items.
6. Generate payment requests.
7. Arrange delivery.
8. Get owner approval for high-value orders.

All of this happens manually, with no system support.

### The Solution

Boutique Concierge AI automates this workflow. The system:

- **Remembers every customer** through a semantic memory layer (vector embeddings).
- **Understands every product** through visual analysis and structured data.
- **Matches customers to products** based on preferences, past purchases, and upcoming events.
- **Automates the commerce workflow** (pricing, payments, approvals, deliveries).
- **Pauses for human approval** when a decision is too big to auto-approve.

---

## User Roles

| Role          | Description                                                                         | Platform            |
| ------------- | ----------------------------------------------------------------------------------- | ------------------- |
| **Owner**     | Owns the boutique. Reviews approvals, monitors analytics, manages business rules.   | React Web           |
| **Manager**   | Manages daily operations. Approves orders, handles sourcing decisions.              | React Web + Flutter |
| **Associate** | Interacts with customers on the floor and via WhatsApp. Uses the Flutter app daily. | Flutter Mobile      |
| **Customer**  | Interacts via WhatsApp/Instagram. No direct app access.                             | External            |

---

## The Three Vertical Slices

### Slice 1: Customer Concierge & Memory (Student 1)

**Domain:** Customer relationships, communication, and memory.

**Core Problem:** "Who is this customer? What do they want? How do we respond?"

**Key Responsibilities:**

- Receive and parse WhatsApp messages (inbound).
- Identify the customer from phone number.
- Extract structured intent from messages (occasion, color, size, budget, item).
- Build and maintain customer profiles.
- Store semantic memory (preferences, events, complaints, facts).
- Generate interaction briefs for staff.
- Draft personalized responses for employee review.
- Send WhatsApp messages to customers.

**Database Tables:**

- Customers (id, phone_number, email, full_name, status, total_spent, visit_count, last_visit_at, created_at, created_by)
- Customer_Preferences (id, customer_id, preference_type, preference_value, is_explicit, source, confidence, created_at)
- Customer_Events (id, customer_id, event_type, event_date, description, created_at)
- Customer_Memory (id, customer_id, content, embedding (vector), category, source, metadata (JSONB), created_at)
- Customer_Interactions (id, customer_id, channel, direction, message_content, parsed_intent (JSONB), staff_member_id, created_at)

**Agent:** Customer Memory Agent

**Agent Responsibilities:**

- Parse raw messages into structured intent.
- Extract explicit facts and inferred preferences.
- Perform semantic search over customer memories using pgvector.
- Generate concise interaction briefs.
- Draft customer responses.

**Tools:**

- search_customer_profile(customer_id)
- get_customer_memory(customer_id, query)
- save_customer_memory(customer_id, content, category)
- extract_entities_from_message(message_text)
- generate_interaction_brief(customer_id)
- send_whatsapp_message(customer_id, message)

**API Endpoints (minimum):**

- GET /api/customers (paginated, filterable)
- GET /api/customers/{id}
- POST /api/customers
- PUT /api/customers/{id}
- GET /api/customers/phone/{phoneNumber}
- POST /api/customer-interactions
- GET /api/customer-interactions/{customerId}
- GET /api/customer-interactions/{customerId}/recent
- POST /api/customer-memory
- GET /api/customer-memory/{customerId}
- GET /api/customer-memory/{customerId}/search?query=...
- POST /api/customer-memory/{customerId}/brief
- POST /api/whatsapp/webhook
- POST /api/whatsapp/send

---

### Slice 2: Visual Intelligence & Sourcing (Student 2)

**Domain:** Catalog, inventory, visual analysis, and sourcing.

**Core Problem:** "Do we have it? If not, can we get it?"

**Key Responsibilities:**

- Analyze product photos (new arrivals and reference images).
- Extract product attributes (color, fabric, style, occasion, price range).
- Match new arrivals to customer profiles.
- Compose complete outfits from available inventory.
- Create sourcing requests when items aren't in store.
- Search supplier catalogs for sourcing options.
- Generate inventory demand signals.

**Database Tables:**

- Inventory_Items (id, item_name, description, category, color, fabric, style, sizes, price, cost, stock_quantity, status, created_at)
- Inventory_Images (id, item_id, image_url, is_primary, analysis_result (JSONB), created_at)
- Outfit_Compositions (id, name, occasion, total_price, created_at)
- Outfit_Items (id, outfit_id, item_id, quantity)
- Sourcing_Requests (id, customer_id, reference_image_url, item_description, supplier_id, estimated_cost, proposed_markup, proposed_price, status, created_at, created_by)
- Suppliers (id, supplier_name, contact_info (JSONB), api_endpoint, minimum_order, delivery_time_days, created_at)
- Customer_Matches (id, customer_id, item_id, match_confidence, match_reason, created_at, employee_acted)

**Agent:** Visual Insight Agent

**Agent Responsibilities:**

- Analyze product images and extract attributes.
- Search inventory for matching items.
- Match customers to new arrivals.
- Compose outfits based on customer preferences and occasion.
- Search supplier databases.
- Create sourcing requests.

**Tools:**

- analyze_product_image(image_url)
- search_inventory(criteria)
- match_customers_to_item(item_id)
- compose_outfit(customer_id, occasion)
- search_supplier_catalog(query)
- create_sourcing_request(customer_id, image_url)
- check_stock(item_id)

**API Endpoints (minimum):**

- GET /api/inventory (paginated, filterable)
- GET /api/inventory/{id}
- POST /api/inventory
- PUT /api/inventory/{id}
- PATCH /api/inventory/{id}/status
- GET /api/inventory/low-stock
- POST /api/inventory-images
- GET /api/inventory-images/{itemId}
- POST /api/inventory-images/analyze
- GET /api/customer-matching/{itemId}
- POST /api/customer-matching/{itemId}/generate
- GET /api/customer-matching/{customerId}/items
- POST /api/customer-matching/{itemId}/notify
- POST /api/outfits/compose
- GET /api/outfits
- GET /api/outfits/{id}
- POST /api/sourcing-requests
- GET /api/sourcing-requests
- GET /api/sourcing-requests/{id}
- PUT /api/sourcing-requests/{id}/status
- GET /api/sourcing-requests/{id}/supplier-options
- GET /api/suppliers
- POST /api/suppliers
- GET /api/suppliers/{id}/catalog

---

### Slice 3: Commerce Validation & Optimization (Student 3)

**Domain:** Pricing, payments, negotiations, and delivery.

**Core Problem:** "Should we accept this order? Is it profitable? How do we deliver?"

**Key Responsibilities:**

- Calculate profit margins for orders.
- Apply loyalty rules and discounts.
- Generate payment requests (deposits).
- Validate payments in real-time.
- Check approval thresholds.
- Manage the approval workflow (pause/resume).
- Plan and optimize delivery routes.
- Book couriers via third-party API.

**Database Tables:**

- Orders (id, customer_id, order_type, status, subtotal, discount, total, margin, created_at, created_by)
- Order_Items (id, order_id, item_id, quantity, unit_price)
- Payments (id, order_id, amount, payment_type, payment_method, status, transaction_id, created_at, confirmed_at)
- Approval_Queue (id, order_id, approval_type, status, threshold_exceeded, requested_by, decided_by, decision_comment, created_at, decided_at)
- Delivery_Plans (id, order_id, courier_service, route_optimized (JSONB), estimated_cost, estimated_eta, status, created_at)
- Business_Rules (id, rule_name, rule_type, rule_value (JSONB), is_active, updated_at)

**Agent:** Commerce Agent

**Agent Responsibilities:**

- Calculate margins and apply discounts.
- Generate and validate payment requests.
- Check thresholds and trigger approvals.
- Plan deliveries and book couriers.
- Apply deterministic business rules.
- Manage the approval state machine.

**Tools:**

- calculate_margin(order_id)
- get_customer_loyalty_tier(customer_id)
- generate_payment_request(order_id, amount)
- validate_payment(payment_id)
- apply_discount(order_id, discount_percent)
- check_approval_threshold(order_id)
- book_courier(delivery_details)
- validate_business_rules(order_id)
- pause_for_approval(order_id)

**API Endpoints (minimum):**

- POST /api/orders
- GET /api/orders (paginated, filterable)
- GET /api/orders/{id}
- PUT /api/orders/{id}
- PATCH /api/orders/{id}/status
- POST /api/orders/{id}/calculate-margin
- POST /api/orders/{id}/apply-discount
- POST /api/orders/{id}/validate
- POST /api/payments
- GET /api/payments/{orderId}
- GET /api/payments/{id}
- POST /api/payments/{id}/validate
- POST /api/payments/{id}/refund
- GET /api/approvals
- GET /api/approvals/{id}
- POST /api/approvals/{id}/approve
- POST /api/approvals/{id}/reject
- POST /api/approvals/{id}/revise
- GET /api/approvals/pending
- POST /api/deliveries
- GET /api/deliveries/{orderId}
- GET /api/deliveries
- POST /api/deliveries/{id}/book
- POST /api/deliveries/optimize
- GET /api/business-rules
- POST /api/business-rules
- PUT /api/business-rules/{id}

---

## The Complete Workflow (Cross-Platform Evidence)

This is the **mandatory minimum assessed workflow** that must work end-to-end:

1. **Flutter (Associate App):** The associate receives a WhatsApp message from a customer.
2. **ASP.NET Core Backend:** The backend records the interaction and calls the Agentic AI service.
3. **Agentic AI (LangGraph):** The workflow executes:
   - Customer Memory Agent parses the message, identifies the customer, retrieves memories, and drafts a response.
   - Visual Insight Agent searches inventory, finds matching items, and optionally creates a sourcing request.
   - Commerce Agent calculates pricing, checks thresholds, and triggers an approval interrupt if needed.
4. **PostgreSQL:** The workflow state is persisted (checkpointing via LangGraph). Business data is updated.
5. **React (Owner Dashboard):** The owner sees the pending approval, reviews the proposal, and approves/rejects.
6. **Backend Resumes:** The workflow resumes, updates the database, and sends a WhatsApp message to the customer.
7. **Flutter (Associate App):** The associate sees the updated order status.

This must be demonstrated: **Flutter → API → Agent → Database → React → API → Flutter**.

---

## Technical Architecture

### Technology Stack (Mandatory)

| Layer           | Technology                |
| --------------- | ------------------------- |
| Backend         | ASP.NET Core Web API (C#) |
| Database        | PostgreSQL with pgvector  |
| Web Frontend    | React (Vite)              |
| Mobile Frontend | Flutter (Dart)            |
| Agentic AI      | LangGraph (Python)        |
| Auth            | Clerk (JWT)               |
| Cache           | Redis                     |
| CI/CD           | GitHub Actions            |

### Architecture Pattern

**Modular Monolith.** The backend is a single ASP.NET Core Web API project with clean domain boundaries. Each student owns one vertical slice (controllers, services, DTOs, models). There are no microservices.

**Agentic AI Service:** A separate Python service (FastAPI + LangGraph) that is called internally by ASP.NET Core via HTTP. It must NOT be called directly by React or Flutter.

**Third-Party Integrations (via ASP.NET Core backend):**

- WhatsApp Business API (messaging)
- Payment Gateway (PayHere, Stripe, or similar)
- Courier API (Uber, PickMe, or local courier)
- Image Recognition API (for visual analysis)

**Database Strategy:**

- Single PostgreSQL instance with pgvector extension.
- Business tables (structured data).
- Customer_Memory table with `vector` column (semantic search).
- Workflow state tables (LangGraph checkpointing).
- Workflow logs table (audit trail).

---

## Agentic AI Requirements (Mandatory)

### Minimum Assured Workflow

The system must demonstrate:

1. Receive a domain objective (customer message).
2. Create a structured multi-step plan.
3. Delegate to distinct agent roles.
4. Call allow-listed tools with validated inputs and structured outputs.
5. Persist workflow state.
6. Apply deterministic checks (schema validation, business rules).
7. Pause a high-impact action for approval by an authorized user.
8. Produce an auditable result or safe failure.

### Distinct Agents (3 Required)

1. **Customer Memory Agent:** Understands the customer. Parses messages, builds profiles, retrieves memories, drafts responses.
2. **Visual Insight Agent:** Understands the product. Analyzes images, matches customers to items, composes outfits, creates sourcing requests.
3. **Commerce Agent:** Makes the deal work. Calculates margins, validates payments, manages approvals, books deliveries.

Each agent must have:

- Identifiable responsibility.
- Defined input/output contract.
- Controlled tool permissions.
- Visible participation in the workflow.
- Tests and documentation.

### Human-in-the-Loop

At least one high-impact action must pause for human approval. This is the Approval Queue managed by the Commerce Agent. The React dashboard displays pending approvals. The owner approves, rejects, or requests revision.

---

## Testing Requirements

The system must have:

- **Backend Tests:** Unit tests, service-layer tests, controller tests, API integration tests.
- **Database Tests:** PostgreSQL integration tests, migration tests, constraint tests.
- **React Tests:** Component tests, form validation tests, protected route tests.
- **Flutter Tests:** Unit tests, widget tests, navigation tests, API integration tests.
- **End-to-End Test:** At least one complete workflow (Flutter → API → Agent → DB → React → API → Flutter).
- **Performance Tests:** Concurrent requests, response time, database response time, agent latency.
- **Agent Evaluation:** Golden cases for the complete workflow, including correct planning/delegation, tool selection, structured outputs, validation, approval enforcement, and safe failure.

---

## CI/CD Requirements

- GitHub Actions workflow that restores, builds, and runs backend tests on every push/PR to main.
- Additional pipelines for frontend build and Flutter analyze are encouraged.
- Evidence of task allocation, PRs, code reviews, and conflict resolution.

---

## Deployment Requirements

- ASP.NET Core API deployed to a cloud platform (Azure, Railway, Render).
- PostgreSQL deployed securely with migrations.
- React deployed to Vercel/Netlify.
- Flutter APK generated and submitted.
- Agentic AI service deployed or run locally with setup instructions.

---

## Documentation Requirements

- README with project overview, setup, and architecture.
- Architecture Decision Records (ADRs) for:
  - Monolith vs. microservices.
  - Agent framework selection.
  - Database strategy.
  - State management (React and Flutter).
  - Deployment platform.
- Consolidated report with Group Report and Individual Reports.
- AI usage logs (per student).
- AI usage declaration (group).

---

## Development Principles

1. **Vertical Slicing:** Each student builds their full stack (database, API, React, Flutter, agent) for their domain.
2. **No "Project Manager" roles:** Every student writes code, tests, and documentation.
3. **Integrated System:** React and Flutter must use the same API, database, and auth.
4. **Cross-Platform Evidence:** At least one workflow must span Flutter → API → Agent → DB → React → API → Flutter.
5. **Business Logic Before AI:** The system must work as a basic management tool even without the AI layer.
6. **Security:** JWT auth, role-based authorization, secret protection, prompt injection resistance, safe failure.

---

## What You Should Know Before Starting

1. **This is a university assignment.** The grading criteria include: component design, integrated architecture, agent orchestration, documentation, deployment, testing, and individual contribution.

2. **Time constraint:** 4 weeks. Prioritize the core workflow over nice-to-have features.

3. **Team size:** 3 students. Each owns one slice. Parallel development is essential.

4. **The AI agent is not a chatbot.** It must perform actions (update databases, create records, trigger payments) and pause for human approval.

5. **Clerk handles authentication.** You don't need to build auth from scratch. Use Clerk's JWT tokens.

6. **pgvector is essential.** It enables semantic search for customer memory.

7. **LangGraph checkpointing** allows the workflow to pause/resume across the human approval step.

8. **The React dashboard is for staff (owner/manager).** The Flutter app is for associates. The customer never uses either.

---

## Project Priorities (If Time Is Limited)

### Must-Have (Non-Negotiable)

- All 3 database schemas (Customers, Inventory, Orders).
- All 3 agents (Memory, Visual, Commerce).
- The complete cross-platform workflow (Flutter → API → Agent → DB → React → API → Flutter).
- Human-in-the-loop approval (pause/resume).
- JWT auth via Clerk.
- Basic tests + CI.
- Deployment of API, React, and APK build.

### Should-Have (High Value)

- WhatsApp integration.
- Image analysis for new arrivals.
- Customer matching to new arrivals.
- Payment request generation.
- Delivery booking via courier API.
- ADRs + documentation.

### Nice-to-Have (Only If Time Remains)

- Outfit composition.
- Negotiation agent.
- Demand signals dashboard.
- Redis caching.
- Docker/Kubernetes.
- Multi-channel (Instagram).

---

## Communication Protocol

You should:

1. **Ask clarifying questions** if any requirement is ambiguous.
2. **Propose a plan** before implementing major features.
3. **Build incrementally** with tests.
4. **Commit code** with clear messages.
5. **Flag risks** early (e.g., "This feature might take longer than expected").

Do NOT:

1. **Add features** not requested.
2. **Change the architecture** without discussing.
3. **Skip tests** for time.
4. **Commit secrets** or credentials.
5. **Assume** implementation details without confirming.

---

## Final Notes

This project must feel like a **real product**, not a tech demo. The professors will ask:

- "Why did you choose this architecture?"
- "Explain how your agent works."
- "Show me the workflow from Flutter to React."
- "What happens if the payment fails?"
- "How do you handle a new customer?"

Prepare for these questions. Build something you can explain, modify, and debug.
