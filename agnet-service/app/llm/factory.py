"""Chat model factory.

Selects an OpenAI or DeepSeek chat model at runtime based on the
``LLM_PROVIDER`` setting. Both providers are OpenAI-compatible; the factory
keeps the two paths explicit so provider-specific defaults can diverge later.
"""

import logging

from langchain_core.language_models.chat_models import BaseChatModel
from langchain_deepseek import ChatDeepSeek
from langchain_openai import ChatOpenAI

from app.core.config import Settings

logger = logging.getLogger("aveline.agent.llm")

_PROVIDER_OPENAI = "openai"
_PROVIDER_DEEPSEEK = "deepseek"


def create_chat_model(settings: Settings) -> BaseChatModel:
    """Return a configured chat model for the configured LLM provider.

    Args:
        settings: Application settings carrying ``llm_provider``, ``llm_api_key``,
            ``llm_base_url`` and ``llm_model``.

    Returns:
        A ``ChatOpenAI`` or ``ChatDeepSeek`` instance.

    Raises:
        ValueError: If ``llm_provider`` is not a supported value.
    """
    provider = settings.llm_provider.lower()
    kwargs = {
        "model": settings.llm_model,
        "api_key": settings.llm_api_key,
    }
    if settings.llm_base_url:
        kwargs["base_url"] = settings.llm_base_url

    if provider == _PROVIDER_OPENAI:
        logger.info("Creating OpenAI chat model (model=%s).", settings.llm_model)
        return ChatOpenAI(**kwargs)
    if provider == _PROVIDER_DEEPSEEK:
        logger.info("Creating DeepSeek chat model (model=%s).", settings.llm_model)
        return ChatDeepSeek(**kwargs)

    raise ValueError(f"Unsupported LLM provider: {settings.llm_provider!r}")
