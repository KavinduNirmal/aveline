# Core — Configuration & Constants

This folder contains app-wide configuration, settings, and constants.

## What belongs here

- **`config.py`** — A `pydantic-settings` `BaseSettings` class that reads all
  configuration from environment variables (`.env` file in development, real env vars
  in production). This is the **single source of truth** for all configuration.

  ```python
  from pydantic_settings import BaseSettings

  class Settings(BaseSettings):
      database_url: str
      openai_api_key: str
      aveline_api_base_url: str
      # ... other settings

      class Config:
          env_file = ".env"

  settings = Settings()
  ```

- **`constants.py`** — Application-wide constants (e.g., approval threshold amount,
  memory category names, agent thread prefixes).

- **`logging.py`** — Logging configuration (structured JSON logging for production).

## Rules

- Never import secrets or credentials directly — always go through `settings`
- Never commit `.env` files — only `.env.example` is committed
- Do not put any business logic here

## What does NOT belong here

- Business logic
- Database models
- Agent or tool code
