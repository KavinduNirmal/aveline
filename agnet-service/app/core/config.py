from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Application-wide settings, read from environment variables."""

    app_name: str = "Aveline Agent Service"
    version: str = "0.1.0"
    internal_api_token: str = ""
    api_base_url: str = "http://localhost:5000"
    redis_url: str = "redis://localhost:6379/0"
    # Event types this service subscribes to (Redis Pub/Sub, ADR-014).
    subscribe_event_types: list[str] = []

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")


@lru_cache
def get_settings() -> Settings:
    return Settings()
