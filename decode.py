import base64
import re
import sys

b64 = "CBMihgFBVV95cUxNTGFxYVlCVEo1NkUxVFVvXzBTU3RqWkFLYVFiTXVjcjRoMVhNLXpDUzhvQVBDSE5sQU0wTHlvWW5NRDVDM1NzQUpIYVl1dEZya01XTWN4S0tPQ1pDQWJxbWJFM0N2bHVhOXdjZktkaElXanhlR2dFOHRjeWZ5THI0dU1OdWJYdw"
pad = "=" * (4 - len(b64) % 4)
decoded = base64.urlsafe_b64decode(b64 + pad)
urls = re.findall(b"(?i)https?://[^\x00-\x1F\x7F\"\']+", decoded)
for u in urls:
    print(u.decode("utf-8", errors="ignore"))
