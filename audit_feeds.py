import json
import urllib.request
import urllib.error
import xml.etree.ElementTree as ET
import time

def check_feed(name, url):
    print(f"Checking {name}: {url}")
    try:
        req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = resp.read()
            # Try to parse XML
            try:
                root = ET.fromstring(data)
                items = root.findall(".//item")
                first_title = items[0].find("title") if items else None
                details = f"OK ({len(items)} items, First: {first_title.text[:60] if first_title is not None else 'N/A'})"
                return "PASS", details
            except ET.ParseError as pe:
                return "MALFORMED_XML", str(pe)
    except urllib.error.HTTPError as e:
        return "FAIL", f"HTTP {e.code}"
    except Exception as e:
        return "FAIL", str(e)[:100]

def main():
    with open('CableNews.Worker/appsettings.json', 'r', encoding='utf-8') as f:
        config = json.load(f)
    
    results = []
    countries = config.get('NewsAgent', {}).get('Countries', [])
    for country in countries:
        country_name = country.get('Name', 'Unknown')
        feeds = country.get('ExtraRssFeeds', [])
        for url in feeds:
            status, details = check_feed(f"{country_name} - {url[:30]}", url)
            results.append((f"{country_name} - {url}", status, details))
            time.sleep(0.3) # Be nice

    print(f"\n{'Feed':<60} {'Status':<15} {'Details'}")
    print("-" * 120)
    for name, status, details in results:
        print(f"{name:<60} {status:<15} {details}")

if __name__ == "__main__":
    main()
