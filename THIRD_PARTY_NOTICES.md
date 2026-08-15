# Third-party notices

## EFT: Where Am I

- Repository: https://github.com/karpitony/eft-where-am-i
- Files consulted: `UserControls/ServerLocation.cs`, `Classes/SettingsHandler.cs`
- License: MIT
- Copyright: Copyright (c) 2024 karpitony, Copyright (c) 2026 supnoel

The referenced project detects the last server address from EFT `*application*.log` files using an `Ip:` log pattern and locates the EFT log directory through installation metadata and common paths. Tarkov Server Guard reimplements those ideas in a smaller standalone application and retains this attribution.

The MIT license text for the referenced project is available at:
https://github.com/karpitony/eft-where-am-i/blob/main/LICENSE

```text
MIT License

Copyright (c) 2024 karpitony
Copyright (c) 2026 supnoel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## DB-IP Lite City

- Database: DB-IP Lite City
- Creator and source: DB-IP.com, https://db-ip.com/db/download/ip-to-city-lite
- License: Creative Commons Attribution 4.0 International, https://creativecommons.org/licenses/by/4.0/
- Changes: the downloaded gzip archive is decompressed and stored locally without modifying its database records.

DB-IP Lite City is a reduced-coverage and reduced-accuracy edition of DB-IP's commercial location database. It is updated monthly and supplies estimated country, subdivision, and city data. Tarkov Server Guard displays `DB-IP.com (CC BY 4.0)` beside the local region-lookup controls and provides the source and license links in the accompanying tooltip and this notice.

The application stores the active database and one recovery copy under `%LOCALAPPDATA%\TarkovServerGuard\DbIpLite`. A monthly gzip download is about 60–70 MB and expands to about 130 MB; keeping one recovery copy uses about 260 MB, and temporary download and decompression files plus the atomic replacement process can require roughly 500 MB of free space during an update. Future monthly sizes may differ. On the first region lookup and when a newer monthly edition is due, it may download the complete DB-IP Lite City MMDB gzip file from `https://download.db-ip.com/free/`. A normal HTTPS file download exposes the user's public network address and the application's User-Agent to the download server. Detected game server IPs, game or launcher logs, account information, SID values, and local paths are not included in this download request.

Before activation, the application enforces compressed and decompressed size limits, validates the MMDB v2 structure and city database type, and commits through a temporary file. A failed or malformed update is discarded and the previous usable database remains available. DB-IP provides the Lite data as-is; a returned city is an estimate and must not be treated as the exact physical server location.

For servers blocked by this application, the server IP, data-center code, returned location, and block timestamp may also be stored locally so that `서버차단현황` remains useful after game logs are deleted. This local metadata is removed when the corresponding managed block is successfully removed.

## Velopack

- Component: Velopack 1.2.0 runtime, updater, and packaging format
- Project: https://github.com/velopack/velopack
- Authors: Velopack Ltd, Caelan Sayler, Kevin Bost
- License: MIT, https://licenses.nuget.org/MIT
- Copyright: Copyright © Velopack Ltd. All rights reserved.

Velopack is distributed with installable and portable builds to check, download, apply, and restart into a newer Tarkov Server Guard release. The application uses the fixed public GitHub repository `Spirit-Schema/tarkov-server-guard`, requests stable releases without a GitHub access token, and leaves the current application usable if an update check or download fails.

## Newtonsoft.Json

- Component: Newtonsoft.Json 13.0.4
- Project: https://www.newtonsoft.com/json
- License: MIT
- Copyright: Copyright (c) 2007 James Newton-King

Newtonsoft.Json is the .NET Framework JSON dependency used by Velopack 1.2.0. Tarkov Server Guard's own local record files continue to use the .NET Framework serializer.

The following MIT terms apply to the Velopack and Newtonsoft.Json components above:

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The copyright notices above and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
