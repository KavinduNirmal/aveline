# Tests: Agents

Place agent graph tests here.

## What belongs here

One test file per agent:

- `test_customer_memory_agent.py` — Tests the Customer Memory Agent graph
- `test_visual_insight_agent.py` — Tests the Visual Insight Agent graph
- `test_commerce_agent.py` — Tests the Commerce Agent graph, including the approval interrupt

## Testing approach

Test at the graph level by:
1. Mocking all tool functions (so no real DB or API calls)
2. Invoking the compiled graph with a sample state
3. Asserting on the final state and which tools were called

For the Commerce Agent, specifically test:
- That `pause_for_approval` is reached when threshold is exceeded
- That the graph resumes correctly when an approval decision is provided
