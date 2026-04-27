import urllib.request
import xml.etree.ElementTree as ET
import time

feeds = {
    "CO - Valora Analitik": "https://www.valoraanalitik.com/feed/",
    "CO - La Republica": "https://www.larepublica.co/arcio/rss/",
    "CO - Forbes CO": "https://forbes.co/feed",
    "CO - El Tiempo Economia": "https://www.eltiempo.com/rss/economia.xml",
    "CO - Semana": "https://www.semana.com/rss",
    "PE - El Comercio": "https://elcomercio.pe/arcio/rss/",
    "PE - Infobae Peru": "https://www.infobae.com/feeds/rss/peru/",
    "CL - La Tercera": "https://www.latercera.com/feed/",
    "CL - EMOL Economia": "https://www.emol.com/rss/economia.xml",
    "CL - Forbes Chile": "https://forbes.cl/feed",
    "BR - InfoMoney": "https://www.infomoney.com.br/feed/",
    "BR - Exame": "https://exame.com/feed/",
    "AMERICAS - BNamericas": "https://www.bnamericas.com/en/rss",
}

results = []
for name, url in feeds.items():
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = resp.read()
            root = ET.fromstring(data)
            items = root.findall(".//item")
            status = f"OK - {len(items)} items"
            if items:
                first_title = items[0].find("title")
                status += f" | Latest: {first_title.text[:60] if first_title is not None else 'N/A'}"
            results.append((name, "PASS", status))
    except Exception as e:
        results.append((name, "FAIL", str(e)[:100]))
    time.sleep(0.3)

print(f"\n{'Feed':<30} {'Status':<6} {'Details'}")
print("-" * 120)
for name, status, details in results:
    print(f"{name:<30} {status:<6} {details}")
