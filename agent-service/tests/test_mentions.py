"""Tests for entity-mention extraction (ADR-019).

``extract_mentions`` pulls explicit ``@name`` and ``#phone`` references out of a Salon message so
the shared customer resolver can look the entity up exactly instead of guessing from free text.
"""

from app.customer_resolution.mentions import extract_mentions


def test_customer_mention_greedy_name_to_end():
    mentions = extract_mentions("Please check @Samantha Arias for us")
    assert mentions.customer == "Samantha Arias"


def test_customer_mention_stops_at_prose_after_name():
    # "@jason smith dropped by" -> the name is "jason smith", not the trailing prose.
    mentions = extract_mentions("@jason smith dropped by next week")
    assert mentions.customer == "jason smith"


def test_customer_mention_lowercase():
    mentions = extract_mentions("any events for @samantha arias?")
    assert mentions.customer == "samantha arias"


def test_customer_mention_stops_at_punctuation():
    mentions = extract_mentions("@Samantha Arias, could you call her?")
    assert mentions.customer == "Samantha Arias"


def test_customer_mention_allows_hyphen_and_apostrophe_in_name():
    mentions = extract_mentions("@O'Brien-Hart visited yesterday")
    assert mentions.customer == "O'Brien-Hart"


def test_customer_mention_only_when_word_follows():
    assert extract_mentions("say hi to @ ").customer is None


def test_phone_mention():
    mentions = extract_mentions("reach #0771234567 please")
    assert mentions.phone == "0771234567"


def test_phone_mention_e164():
    mentions = extract_mentions("call #+94771234567 today")
    assert mentions.phone == "+94771234567"


def test_no_mention():
    mentions = extract_mentions("A new customer dropped by next week")
    assert mentions.customer is None
    assert mentions.phone is None


def test_escaped_at_is_literal():
    mentions = extract_mentions("email \\@noreply is not a customer")
    assert mentions.customer is None
