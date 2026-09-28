"""Structured (JSON) logging setup for the agent service.

Emits single-line JSON records to stdout so logs can be shipped to ELK,
Application Insights, or any JSON log pipeline. Extra fields passed via
``extra={...}`` (e.g. ``user_id``) are included as top-level keys.
"""

import datetime
import json
import logging
import sys


class JsonFormatter(logging.Formatter):
    """Format a log record as a single-line JSON object."""

    # Standard logging.LogRecord attributes that are never emitted as extras.
    _reserved = frozenset(
        {
            "name",
            "msg",
            "args",
            "levelname",
            "levelno",
            "pathname",
            "filename",
            "module",
            "exc_info",
            "exc_text",
            "stack_info",
            "lineno",
            "funcName",
            "created",
            "msecs",
            "relativeCreated",
            "thread",
            "threadName",
            "processName",
            "process",
            "taskName",
            "asctime",
            "message",
        }
    )

    def format(self, record: logging.LogRecord) -> str:
        payload: dict = {
            "timestamp": datetime.datetime.now(datetime.UTC).isoformat(),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
        }
        for key, value in record.__dict__.items():
            if key in payload or key in self._reserved:
                continue
            if isinstance(value, (str, int, float, bool, list, dict)) or value is None:
                payload[key] = value
        if record.exc_info:
            payload["exception"] = self.formatException(record.exc_info)
        return json.dumps(payload, default=str)


def configure_logging(level: int = logging.INFO, log_format: str = "json") -> None:
    """Configure root logging to stdout with a JSON (or plain text) formatter.

    Args:
        level: Minimum level to emit.
        log_format: ``"json"`` (default) for structured output, ``"text"`` for
            human-readable local development logs.
    """
    handler = logging.StreamHandler(sys.stdout)
    if log_format == "json":
        handler.setFormatter(JsonFormatter())
    else:
        handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(name)s %(message)s"))
    logging.basicConfig(level=level, handlers=[handler], force=True)
