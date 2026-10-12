# URLScra

**Extract. Preview. Save.**

URLScra ialah aplikasi desktop Windows untuk mengekstrak data daripada halaman web, termasuk laman yang memerlukan JavaScript, memaparkan hasil dalam UI dan membenarkan pengguna preview atau menyimpan resource yang dipilih.

## M2.3 workspace

UI menggunakan tema dark high-contrast dengan command bar, results table, preview workspace dan status footer.

URLScra mempunyai tiga pilihan engine:

- **Auto** — cuba Fast HTML dahulu dan jatuh ke Browser jika hasil tidak mencukupi.
- **Fast HTML** — HTTP + AngleSharp sahaja; paling pantas untuk laman statik/server-rendered.
- **Browser** — Playwright Chromium, JavaScript rendering dan adaptive auto-scroll.

Browser mode melaporkan status seperti `Loading`, `Waiting`, `Auto-scroll` dan `Capturing DOM`. Analisis juga boleh dihentikan menggunakan butang **Cancel**.

### Preview workspace

- **Image** — thumbnail dalam senarai dan preview besar di panel kanan.
- **Video** — direct video cuba dimainkan dengan player native WPF; stream/embed dipaparkan sebagai URL untuk dibuka secara luaran.
- **PDF** — panel PDF khas dengan Open, Copy URL dan Save As.
- **Save As** — resource yang dipilih boleh dimuat turun secara manual.
- **Open / Copy URL** — tersedia pada panel preview.

URLScra juga mempunyai ikon vector dalam UI dan ikon Windows `.ico` yang dijana oleh local pipeline untuk EXE/taskbar.

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
- PDF
- Document
- CSV
- JSON / JSON-LD
- Table
- Product
- Metadata
- Social
- Downloadable File

`PDF` dikesan secara khusus sebagai fail `.pdf`, manakala `Document` digunakan untuk DOC/DOCX, XLS/XLSX, PPT/PPTX, TXT, RTF, ODT, ODS dan ODP.

## Detectors

URLScra menyokong:

- title dan teks halaman
- harga dan mata wang biasa
- imej termasuk lazy-load, srcset dan OpenGraph
- HTML5 video/audio, source, embed dan pautan media
- email daripada `mailto:` dan teks halaman
- nombor telefon daripada `tel:` dan teks halaman
- PDF, dokumen, CSV, JSON dan fail boleh dimuat turun
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

### First run M2

Pipeline menjana ikon app, restore dependency, build, dan boleh memasang Chromium Playwright:

```powershell
.\pipeline.ps1 -SetupBrowser
```

Jika drive `D:` tersedia, browser disimpan di:

```text
D:\URLScra\PlaywrightBrowsers
```

### Run biasa selepas setup

```powershell
.\pipeline.ps1
```

Build sahaja:

```powershell
.\pipeline.ps1 -NoRun
```

Publish Windows x64:

```powershell
.\pipeline.ps1 -Publish
```

Jika hanya mahu memasang semula Chromium:

```powershell
.\install-browser.ps1
```

GitHub Actions/CI tidak digunakan semasa fasa pembangunan aktif supaya iterasi lebih cepat. Local pipeline ialah gate pembangunan semasa.

## Penggunaan yang bertanggungjawab

URLScra tidak direka untuk memintas CAPTCHA, anti-bot, login, paywall atau kawalan akses. Browser mode hanya merender halaman seperti browser biasa. Gunakan pada data dan laman yang anda dibenarkan akses serta hormati rate limit, hak cipta, privasi, robots/polisi laman dan terma perkhidmatan.
