"""Load the canonical universal system prompt shipped with the agent service.

The universal prompt is the single source of truth at
``agnet-service/app/prompts/SYSTEM_PROMPT.md``. It lives inside the service (not
in ``.agents/brain``, which is reserved for coding-agent context) so it does not
interfere with coding agents. It is read once and cached for the process lifetime
so runtime prompt assembly does not hit the filesystem on every request.
"""

import logging
from functools import lru_cache
from pathlib import Path

logger = logging.getLogger("aveline.agent.prompts")

# agnet-service/app/prompts/loader.py -> the prompt file sits beside this module.
_DEFAULT_PROMPT_PATH = Path(__file__).resolve().parent / "SYSTEM_PROMPT.md"


def _read_file(path: Path) -> str:
    """Read and return the raw contents of ``path``."""
    return path.read_text(encoding="utf-8")


@lru_cache(maxsize=1)
def _cached_load(path: str) -> str:
    """Read the prompt file at ``path`` and cache the result."""
    return _read_file(Path(path))


def load_system_prompt(path: Path | None = None) -> str:
    """Return the universal system prompt, reading it once and caching it.

    Args:
        path: Optional explicit path to the prompt file. Defaults to the
            canonical ``SYSTEM_PROMPT.md`` shipped beside this module.

    Returns:
        The raw markdown contents of the universal system prompt.

    Raises:
        FileNotFoundError: If the prompt file does not exist.
    """
    target = path or _DEFAULT_PROMPT_PATH
    return _cached_load(str(target))


def clear_cache() -> None:
    """Clear the cached prompt (used by tests)."""
    _cached_load.cache_clear()
