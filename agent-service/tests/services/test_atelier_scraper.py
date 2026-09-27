"""Unit tests for HTML catalog and product card parsing in AtelierScraperService."""

from app.services.atelier_scraper import AtelierScraperService


def test_parse_html_catalog_extracts_product_cards():
    html = """
    <html>
      <body>
        <div class="product-grid">
          <div class="product-card">
            <a href="/products/emerald-raw-silk">
              <img src="/images/silk1.jpg" alt="Emerald Green Raw Silk Fabric" />
            </a>
            <h3 class="product-title">Emerald Green Raw Silk Fabric</h3>
            <span class="price">$180.00</span>
          </div>
          <div class="product-item">
            <a href="/products/mulberry-silk-crepe">
              <img data-src="/images/silk2.jpg" alt="Mulberry Silk Crepe" />
            </a>
            <h3 class="title">Mulberry Silk Crepe</h3>
            <span class="product-price">LKR 45,000</span>
          </div>
        </div>
      </body>
    </html>
    """
    scraper = AtelierScraperService()
    items = scraper.parse_html_catalog(html, "https://maisondesoie.com")

    assert len(items) == 2
    assert items[0]["title"] == "Emerald Green Raw Silk Fabric"
    assert items[0]["price"] == "$180.00"
    assert items[0]["product_url"] == "https://maisondesoie.com/products/emerald-raw-silk"
    assert items[0]["image_url"] == "https://maisondesoie.com/images/silk1.jpg"

    assert items[1]["title"] == "Mulberry Silk Crepe"
    assert items[1]["price"] == "LKR 45,000"
    assert items[1]["product_url"] == "https://maisondesoie.com/products/mulberry-silk-crepe"
    assert items[1]["image_url"] == "https://maisondesoie.com/images/silk2.jpg"


def test_parse_html_catalog_extracts_json_ld_products():
    html = """
    <html>
      <head>
        <script type="application/ld+json">
        {
          "@context": "https://schema.org/",
          "@type": "Product",
          "name": "Bespoke Banarasi Brocade Saree",
          "image": "https://atelier.com/img/saree.jpg",
          "offers": {
            "@type": "Offer",
            "priceCurrency": "USD",
            "price": "650"
          }
        }
        </script>
      </head>
      <body><div>No raw product cards here</div></body>
    </html>
    """
    scraper = AtelierScraperService()
    items = scraper.parse_html_catalog(html, "https://atelier.com")

    assert len(items) == 1
    assert items[0]["title"] == "Bespoke Banarasi Brocade Saree"
    assert items[0]["price"] == "USD 650"
    assert items[0]["image_url"] == "https://atelier.com/img/saree.jpg"


def test_parse_html_catalog_extracts_opengraph_metadata_fallback():
    html = """
    <html>
      <head>
        <meta property="og:title" content="Handloom Tussar Silk Dupatta" />
        <meta property="og:image" content="https://atelier.com/img/dupatta.jpg" />
        <meta property="og:price:amount" content="120.00" />
        <meta property="og:price:currency" content="EUR" />
      </head>
      <body><div>Details page</div></body>
    </html>
    """
    scraper = AtelierScraperService()
    items = scraper.parse_html_catalog(html, "https://atelier.com")

    assert len(items) == 1
    assert items[0]["title"] == "Handloom Tussar Silk Dupatta"
    assert items[0]["price"] == "EUR 120.00"
    assert items[0]["image_url"] == "https://atelier.com/img/dupatta.jpg"
