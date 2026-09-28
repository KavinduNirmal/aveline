# Tests: Tools

Place tool-level unit tests here.

## What belongs here

Test files for each tool group, for example:

- `test_customer_tools.py` — Tests for `search_customer_profile`, `get_customer_memory`, etc.
- `test_inventory_tools.py` — Tests for `analyze_product_image`, `search_inventory`, etc.
- `test_commerce_tools.py` — Tests for `calculate_margin`, `generate_payment_request`, etc.

## Testing approach

Tool tests should:
- Mock the database session (use `pytest-asyncio` + `AsyncMock`)
- Mock any external HTTP calls (use `respx` or `unittest.mock`)
- Assert that the tool returns the correct output type (matching the Pydantic schema)
- Assert that the tool raises meaningful errors on bad input
