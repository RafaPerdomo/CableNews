import requests, re, json
from urllib.parse import quote

url = "https://news.google.com/rss/articles/CBMi2AFBVV95cUxPeU5vYVdnXzZ2bjVWZUp0ZkF1dU9Sa3BEekY0bHhmenhMZzEtQm9kenBjVWxmb0NaR09MUVQ4Yl9YOGpnRDJfeG0zN1NzSWR2M2ZOdC0xUGpjUG5pV3dTVUM4bHlNUm5xR3RONUtDcWNRblNMUzc2YTd1QkFINWFiM3lLOHl3Z3RFSzl6U19LQzhrYXdDaTJyVEFUMHRGZDZBS1BabnJSVERXbmQ5OUhSdWV0akJVYzNfVTRUenVoamJzX1RacW9NYzRucGh4bUVmMENEMUE5d3CSAfMBQVVfeXFMT1JYdk12M1lRU1dobWVOcGRLaWh3WXU0UFd3dWFrXzJnSl8wV1hKX2Z1c3U3a1V3MmhvckIzQXphV0NGejhNeWVuV2ViSHhNclBSVGUya19XQkM3clh5eVZaX2dXZXVlaElWREkzQmRFVVdQSG9lNVprTW1CckI3Z0J0NC12WkU5VTl2ODZvNm9DaFo2LVEyRGxBSGpydTlza3lvN1pzVFJIZ1BkUjJ4QmZfYzNiU0duUGtoQ2RGWGJjQWYwa1dWckR2Q2xDMjRxTlo1enJGeHpDTnpNUURmNUhCSEExNDVDclc5d3M2VUdwZno4?oc=5"

base64str = url.split("articles/")[-1].split("?")[0]
headers = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36",
    "Accept": "text/html,application/xhtml+xml"
}
r = requests.get(f"https://news.google.com/articles/{base64str}", headers=headers)
html = r.text
sg = re.search(r'data-n-a-sg="([^"]+)"', html).group(1)
ts = re.search(r'data-n-a-ts="([^"]+)"', html).group(1)

payload_inner = f'["garturlreq",[["X","X",["X","X"],null,null,1,1,"US:en",null,1,null,null,null,null,null,0,1],"X","X",1,[1,1,1],1,1,null,0,0,null,0],"{base64str}",{ts},"{sg}"]'
req_data = json.dumps([[["Fbv4je", payload_inner, None, "generic"]]])
resp = requests.post(
    "https://news.google.com/_/DotsSplashUi/data/batchexecute",
    headers={**headers, "Content-Type": "application/x-www-form-urlencoded"},
    data=f"f.req={quote(req_data)}"
)
raw = resp.text

# Find the data after the first blank line
split_pos = raw.find("\n\n")
json_str = raw[split_pos:].strip()
parsed = json.loads(json_str)

print("Top-level array length:", len(parsed))
for i, item in enumerate(parsed):
    print(f"  [{i}] -> {str(item)[:120]}")

# Navigate into the nested JSON
for i, item in enumerate(parsed):
    if isinstance(item, list):
        for j, sub in enumerate(item):
            if isinstance(sub, str) and sub.startswith('['):
                try:
                    inner = json.loads(sub)
                    print(f"\n  parsed[{i}][{j}] as JSON -> {str(inner)[:200]}")
                    if isinstance(inner, list) and len(inner) >= 2:
                        url_candidate = inner[1] if isinstance(inner[1], str) and inner[1].startswith('http') else None
                        if url_candidate:
                            print(f"\n  ✅ FOUND URL at parsed[{i}][{j}][1] -> {url_candidate}")
                except:
                    pass
