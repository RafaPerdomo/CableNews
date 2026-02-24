import urllib.request
import urllib.parse
import json

url = "https://news.google.com/rss/articles/CBMihgFBVV95cUxNTGFxYVlCVEo1NkUxVFVvXzBTU3RqWkFLYVFiTXVjcjRoMVhNLXpDUzhvQVBDSE5sQU0wTHlvWW5NRDVDM1NzQUpIYVl1dEZya01XTWN4S0tPQ1pDQWJxbWJFM0N2bHVhOXdjZktkaElXanhlR2dFOHRjeWZ5THI0dU1OdWJYdw?oc=5&hl=es-419&gl=CO&ceid=CO:es-419"

req_data = f'[[["Fbv4je","[\\"{url}\\"]",null,"generic"]]]'
payload = urllib.parse.urlencode({"f.req": req_data}).encode("utf-8")

headers = {
    "Content-Type": "application/x-www-form-urlencoded;charset=utf-8",
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
}

req = urllib.request.Request("https://news.google.com/_/DotsSplashUi/data/batchexecute", data=payload, headers=headers)
try:
    with urllib.request.urlopen(req) as response:
        html = response.read().decode('utf-8')
        print(html)
except Exception as e:
    print("Error:", e)
