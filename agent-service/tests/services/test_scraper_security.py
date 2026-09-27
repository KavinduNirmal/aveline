"""Unit tests for Atelier Web Scraper SSRF security validation."""

from app.services.atelier_scraper import AtelierScraperService


def test_is_safe_url_blocks_localhost_and_loopback():
    scraper = AtelierScraperService()
    assert scraper.is_safe_url("http://localhost:8000/shop") is False
    assert scraper.is_safe_url("http://127.0.0.1:5000/catalog") is False
    assert scraper.is_safe_url("http://[::1]/products") is False


def test_is_safe_url_blocks_private_subnets():
    scraper = AtelierScraperService()
    assert scraper.is_safe_url("http://10.0.0.1/search") is False
    assert scraper.is_safe_url("http://192.168.1.10/search") is False
    assert scraper.is_safe_url("http://172.16.0.5/catalog") is False
    assert scraper.is_safe_url("http://169.254.169.254/latest/meta-data/") is False


def test_is_safe_url_blocks_non_http_schemes():
    scraper = AtelierScraperService()
    assert scraper.is_safe_url("file:///etc/passwd") is False
    assert scraper.is_safe_url("ftp://example.com/items") is False
    assert scraper.is_safe_url("javascript:alert(1)") is False


def test_is_safe_url_accepts_valid_public_domain():
    scraper = AtelierScraperService()
    assert scraper.is_safe_url("https://www.maisondesoie.com/catalog") is True
    assert scraper.is_safe_url("https://atelier-paris.fr/shop?s=silk") is True


def test_is_safe_url_enforces_whitelist_when_provided():
    scraper = AtelierScraperService(allowed_domains=["maisondesoie.com", "partner-atelier.org"])
    assert scraper.is_safe_url("https://maisondesoie.com/shop") is True
    assert scraper.is_safe_url("https://subdomain.maisondesoie.com/search") is True
    assert scraper.is_safe_url("https://partner-atelier.org/catalog") is True
    assert scraper.is_safe_url("https://malicious-site.com/search") is False
