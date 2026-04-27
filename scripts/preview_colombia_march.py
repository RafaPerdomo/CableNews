import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import time

brands = [
    "Nexans", '"Centelsa by Nexans"', "Centelsa", '"Nexans Colombia"',
    '"INDECO by Nexans"', "Indeco", '"Madeco by Nexans"', "Madeco", "Ficap", "Incable"
]

date_range = "after:2026-03-01 before:2026-04-01"

output_file = r"C:\Users\Windows 10 Pro\.gemini\antigravity\brain\32f8b386-dae3-4f27-8064-a35d26f88ca6\colombia_march_preview.md"

articles = {}  # dedup by title

for brand in brands:
    query = f"{brand} {date_range}"
    encoded_query = urllib.parse.quote(query)
    url = f"https://news.google.com/rss/search?q={encoded_query}&hl=es-CO&gl=CO"
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=10) as response:
            xml_data = response.read()
            root = ET.fromstring(xml_data)
            items = root.findall(".//item")
            for item in items:
                title = item.find("title").text if item.find("title") is not None else ""
                link = item.find("link").text if item.find("link") is not None else ""
                pub_date = item.find("pubDate").text if item.find("pubDate") is not None else "Sin fecha"
                if title and title not in articles:
                    articles[title] = {"date": pub_date, "url": link, "brand": brand}
    except Exception as e:
        print(f"  Error en [{brand}]: {e}")
    time.sleep(0.4)

with open(output_file, "w", encoding="utf-8") as f:
    f.write("# Preview Colombia — Marzo 2026\n\n")
    f.write(f"> Rango: {date_range}  |  Noticias únicas encontradas: **{len(articles)}**\n\n")
    if articles:
        for title, meta in sorted(articles.items(), key=lambda x: x[1]["date"]):
            f.write(f"- **{meta['date'][:16]}** | [{title}]({meta['url']})\n")
    else:
        f.write("_No se encontraron noticias para este período._\n")

print(f"Preview generado: {len(articles)} artículos.")
