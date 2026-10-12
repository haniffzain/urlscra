# URLScra

**Paste URL. Choose data. Get results.**

URLScra ialah aplikasi desktop Windows untuk mengekstrak data daripada halaman web dan memaparkannya dalam UI sebelum eksport ke CSV.

## M1.2 search criteria

- All
- Text
- Title
- Price
- Image
- Video
- Audio
- Link
- Email
- Phone
- Document
- CSV
- JSON / JSON-LD
- Table
- Product
- Metadata
- Social
- Downloadable File

## Detectors

M1.2 menyokong HTML statik yang dihantar oleh server, termasuk:

- title dan teks halaman
- harga dan mata wang biasa
- imej termasuk lazy-load, srcset dan OpenGraph
- HTML5 video/audio, source, embed dan pautan media
- email daripada `mailto:` dan teks halaman
- nombor telefon daripada `tel:` dan teks halaman
- dokumen, CSV, JSON dan fail boleh dimuat turun
- HTML table sebagai row data
- metadata, canonical URL dan OpenGraph/Twitter metadata
- social links
- product detection asas melalui Schema.org JSON-LD dan heuristik DOM

## Build

Keperluan:

- Windows 10/11
- .NET SDK yang menyokong target `net8.0-windows`

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

## Batasan semasa

M1.2 membaca HTML awal yang dihantar oleh server. Kandungan yang hanya muncul selepas JavaScript dijalankan, infinite scroll atau interaksi browser akan ditambah dalam milestone browser-engine seterusnya.

URLScra tidak direka untuk memintas CAPTCHA, anti-bot, login atau kawalan akses. Gunakan hanya pada data dan laman yang anda dibenarkan akses dan hormati rate limit, hak cipta, privasi dan polisi laman.
