import pytest

from app.core.config import Settings
from app.llm.factory import create_chat_model


def test_openai_provider_returns_chat_openai():
    settings = Settings(
        llm_provider="openai",
        llm_api_key="sk-openai",
        llm_model="gpt-4o",
    )
    model = create_chat_model(settings)
    assert model.__class__.__name__ == "ChatOpenAI"
    assert model.model_name == "gpt-4o"
    assert model.openai_api_key.get_secret_value() == "sk-openai"


def test_openai_provider_honors_custom_base_url():
    settings = Settings(
        llm_provider="openai",
        llm_api_key="sk-openai",
        llm_base_url="https://gateway.example.com/v1",
        llm_model="gpt-4o",
    )
    model = create_chat_model(settings)
    assert model.openai_api_base == "https://gateway.example.com/v1"


def test_deepseek_provider_returns_chat_deepseek():
    settings = Settings(
        llm_provider="deepseek",
        llm_api_key="sk-deepseek",
        llm_model="deepseek-v4-flash",
    )
    model = create_chat_model(settings)
    assert model.__class__.__name__ == "ChatDeepSeek"
    assert model.model_name == "deepseek-v4-flash"
    assert model.openai_api_key.get_secret_value() == "sk-deepseek"


def test_deepseek_provider_honors_custom_base_url():
    settings = Settings(
        llm_provider="deepseek",
        llm_api_key="sk-deepseek",
        llm_base_url="https://api.deepseek.com",
        llm_model="deepseek-v4-flash",
    )
    model = create_chat_model(settings)
    assert model.openai_api_base == "https://api.deepseek.com"


def test_deepseek_thinking_disabled_by_default():
    """Thinking is off by default: the model asks DeepSeek to skip the thinking pass."""
    settings = Settings(
        llm_provider="deepseek",
        llm_api_key="sk-deepseek",
        llm_model="deepseek-v4-flash",
    )
    model = create_chat_model(settings)
    assert model.extra_body == {"thinking": {"type": "disabled"}}


def test_deepseek_thinking_enabled_omits_extra_body():
    settings = Settings(
        llm_provider="deepseek",
        llm_api_key="sk-deepseek",
        llm_model="deepseek-v4-flash",
        llm_thinking_enabled=True,
    )
    model = create_chat_model(settings)
    assert model.extra_body is None


def test_openai_thinking_flag_is_ignored():
    """The thinking flag only applies to DeepSeek; OpenAI models are unaffected."""
    settings = Settings(
        llm_provider="openai",
        llm_api_key="sk-openai",
        llm_model="gpt-4o",
        llm_thinking_enabled=False,
    )
    model = create_chat_model(settings)
    assert model.extra_body is None


def test_unknown_provider_raises_value_error():
    settings = Settings(llm_provider="anthropic", llm_api_key="sk-x", llm_model="m")
    with pytest.raises(ValueError):
        create_chat_model(settings)
