# URLScra

**Paste URL. Choose engine. Get data.**

URLScra ialah aplikasi desktop Windows untuk mengekstrak data daripada halaman web, termasuk laman yang memerlukan JavaScript, dan memaparkan hasil sebelum eksport ke CSV.

## M2.1 engine

URLScra mempunyai tiga pilihan engine:

- **Auto** — cuba Fast HTML dahulu dan jatuh ke Browser jika hasil tidak mencukupi.
- **Fast HTML** — HTTP + AngleSharp sahaja; paling pantas untuk laman statik/server-rendered.
- **Browser** — Playwright Chromium, JavaScript rendering dan adaptive auto-scroll.

Browser mode melaporkan status seperti `Loading`, `Waiting`, `Auto-scroll` dan `Capturing DOM`. Analisis juga boleh dihentikan menggunakan butang **Cancel**.

## Output mode

- **Raw Data** — hasil individu seperti Text, Price, Image, Link dan Email.
- **Smart Records** — cuba menggabungkan Name + Price + Image + Link + Rating + Stock sebagai satu rekod.

## Search criteria

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

URLScra menyokong:

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
- product detection melalui Schema.org JSON-LD dan heuristik DOM
- JavaScript-rendered DOM melalui Playwright Chromium
- adaptive auto-scroll untuk kandungan lazy/infinite-style pages

## Setup Windows

Keperluan:

- Windows 10/11
- .NET SDK yang menyokong target `net8.0-windows`

### 1. Pasang Chromium Playwright sekali sahaja

```powershell
.\install-browser.ps1
```

Jika drive `D:` tersedia, browser disimpan di:

```text
D:\URLScra\PlaywrightBrowsers
```

### 2. Jalankan local pipeline

```powershell
.\pipeline.ps1
```

Pipeline lokal menjalankan:

```text
restore -> build Release -> run
```

Build sahaja:

```powershell
.\pipeline.ps1 -NoRun
```

Publish Windows x64:

```powershell
.\pipeline.ps1 -Publish
```

GitHub Actions/CI tidak digunakan semasa fasa pembangunan aktif supaya iterasi lebih cepat.

## Penggunaan yang bertanggungjawab

URLScra tidak direka untuk memintas CAPTCHA, anti-bot, login, paywall atau kawalan akses. Browser mode hanya merender halaman seperti browser biasa. Gunakan pada data dan laman yang anda dibenarkan akses serta hormati rate limit, hak cipta, privasi, robots/polisi laman dan terma perkhidmatan.
