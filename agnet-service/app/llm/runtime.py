"""Runtime decision of whether the agent workflow should invoke an LLM.

The model factory (``app/llm/factory.py``) can build a chat model, but nothing decides when
to actually use one. This module is that gate: an LLM is only produced when it is explicitly
enabled (``agent_llm_enabled``) **and** a provider key + model are configured. Otherwise it
returns ``None`` and the workflow runs fully deterministically (rule-based). This keeps CI and
local development green when no ``LLM_API_KEY``/``LLM_MODEL`` are present.
"""

import logging

from langchain_core.language_models.chat_models import BaseChatModel

from app.core.config import Settings
from app.llm.factory import create_chat_model

logger = logging.getLogger("aveline.agent.llm.runtime")


def memory_llm_or_none(settings: Settings) -> BaseChatModel | None:
    """Return a configured chat model for the memory agent, or ``None`` to stay rule-based.

    Args:
        settings: Application settings carrying ``agent_llm_enabled``, ``llm_api_key`` and
            ``llm_model``.

    Returns:
        A chat model when LLM use is enabled and a key + model are configured, else ``None``.
    """
    if not settings.agent_llm_enabled:
        logger.info("LLM disabled for agent workflows (agent_llm_enabled=false); using rule-based mode.")
        return None
    if not settings.llm_api_key or not settings.llm_model:
        logger.info("LLM not configured (missing api key or model); using rule-based mode.")
        return None
    return create_chat_model(settings)


def visual_llm_or_none(settings: Settings) -> BaseChatModel | None:
    """Return a configured chat model for the visual insight agent, or ``None`` to stay rule-based.

    Args:
        settings: Application settings carrying ``agent_llm_enabled``, ``llm_api_key`` and
            ``llm_model``.

    Returns:
        A chat model when LLM use is enabled and a key + model are configured, else ``None``.
    """
    if not settings.agent_llm_enabled:
        logger.info("LLM disabled for visual agent workflows (agent_llm_enabled=false); using rule-based mode.")
        return None
    if not settings.llm_api_key or not settings.llm_model:
        logger.info("LLM not configured (missing api key or model); using rule-based mode.")
        return None
    return create_chat_model(settings)


workflow_llm_or_none = memory_llm_or_none
