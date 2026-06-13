# OCR-Based Search Using Elasticsearch (10M+ Documents)

Large-scale document search system built with ASP.NET Core 9, SQL Server, Elasticsearch, RabbitMQ, and Tesseract OCR.

## Architecture

- **DocumentSearch.Api** — upload, search, folder management, backfill
- **DocumentSearch.Worker** — async ingestion (PdfPig + Tesseract) and Elasticsearch indexing
- **SQL Server** — document/folder metadata
- **Local filesystem** — documents live under `Documents/` (configurable via `Storage:RootPath`)
- **Elasticsearch** — full-text search with folder-scoped filters
- **RabbitMQ** — ingestion queue

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Elasticsearch + RabbitMQ)
- SQL Server or LocalDB
- [Tesseract tessdata](https://github.com/tesseract-ocr/tessdata) — download `eng.traineddata` into `tessdata/` at the solution root

## Quick start

### 1. Start infrastructure

```powershell
cd DocumentSearch
docker compose up -d
```

### 2. Download Tesseract language data

```powershell
mkdir tessdata -Force
Invoke-WebRequest -Uri "https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata" -OutFile "tessdata/eng.traineddata"
```

Copy `tessdata` to the API and Worker output folders, or set `Ocr:TesseractDataPath` in `appsettings.json`.

### 3. Apply database migration (automatic on startup)

The API runs migrations and seeds a root folder on first launch.

### 4. Run services

**Terminal 1 — API** (opens the web UI in your browser):

```powershell
dotnet watch run --project src/DocumentSearch.Api --launch-profile http
```

Open **http://localhost:5016** — search box, folder tree, drag-and-drop upload.

Terminal 2 — Worker:

```powershell
dotnet run --project src/DocumentSearch.Worker
```

## API endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/search?q=invoice&folderId={guid}&includeSubfolders=true` | Full-text search |
| GET | `/api/folders` | Folder tree |
| POST | `/api/folders` | Create folder `{ "name": "contracts", "parentFolderId": "..." }` |
| POST | `/api/documents/upload` | Upload file (form: `folderId`, `file`) |
| GET | `/api/documents/{id}` | Document metadata |
| GET | `/api/documents/{id}/status` | Index status |
| GET | `/api/documents/{id}/download` | Download original file |
| POST | `/api/admin/bulk-ingest?sourceDirectory=C:\Docs` | Bulk ingest existing folder |

## Configuration

Edit `src/DocumentSearch.Api/appsettings.json`:

- `ConnectionStrings:Default` — SQL Server connection
- `Storage:RootPath` — where files are stored on disk (default: `./Documents`)

Place your PDFs, images, and text files in that folder (use subfolders as needed). New uploads are saved under the same root, mirroring the folder tree in the app.

To index files already on disk **without copying them**, point backfill at the storage root:

```powershell
curl -X POST "http://localhost:5016/api/admin/bulk-ingest?sourceDirectory=C:\Path\To\Your\Documents"
```
- `Elasticsearch:Url` — default `http://localhost:9200`
- `RabbitMQ:Host` — default `localhost`
- `Ocr:TesseractDataPath` — path to tessdata folder
- `Ingestion:MaxParallelJobs` — worker concurrency (default 4)

## Folder-scoped search

Documents are indexed with `ancestorFolderIds` for fast filtering:

- `includeSubfolders=true` — search within folder and all descendants
- `includeSubfolders=false` — search exact folder only

## Bulk backfill (10M+ documents)

Use the backfill endpoint to scan an existing directory tree:

```powershell
curl -X POST "http://localhost:5000/api/admin/backfill?sourceDirectory=D:\Archive"
```

Files are uploaded, queued, and processed by the worker. Run multiple worker instances or increase `MaxParallelJobs` for throughput.

## Services

| Service | URL |
|---------|-----|
| API | https://localhost:7001 (see launchSettings) |
| Elasticsearch | http://localhost:9200 |
| Kibana | http://localhost:5601 |
| RabbitMQ Management | http://localhost:15672 (guest/guest) |
