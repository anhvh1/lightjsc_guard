# LightJSC

Production-ready .NET 8 solution for ingesting i-PRO AI Face metadata via RTSP ONVIF MediaInput, matching against Active Guard watchlist (SQL Server, read-only) or a local watchlist stored in Postgres, and dispatching face events to webhook subscribers.

## Projects
- `LightJSC.Api`: ASP.NET Core Web API + hosting background workers
- `LightJSC.Core`: Domain models, interfaces, options
- `LightJSC.Infrastructure`: Postgres EF Core, Active Guard SQL, RTSP metadata client, parsing, vector index
- `LightJSC.Workers`: Background services (registry, sync, matching, webhook), metrics
- `LightJSC.Tests`: Unit tests for parser, dedup, cosine similarity

## Prerequisites
- .NET 8 SDK
- PostgreSQL (local)
- SQL Server (Active Guard, read-only)
- RTSP camera network reachable from host

## Configuration (IIS/dev)
The app reads from `appsettings.json` and environment variables. Keep real passwords out of source control and set them at deploy time.

### Required secrets
- `ConnectionStrings:Postgres`
- `ConnectionStrings:ActiveGuard` (only when `Watchlist:Source=ActiveGuard`)
- `Encryption:Base64Key` (32-byte base64 key for AES-GCM)
- `Webhook:HmacSecret`

Example (PowerShell):
```powershell
# Generate 32-byte base64 key
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$base64 = [Convert]::ToBase64String($bytes)
$base64

# Set secrets in User Secrets
cd LightJSC.Api

dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=ipro_face_ingestor;Username=postgres;Password=YOUR_PASSWORD"
dotnet user-secrets set "ConnectionStrings:ActiveGuard" "Server=192.168.100.8\\AISYSTEM;Database=aicam;User Id=ipro;Password=YOUR_PASSWORD"
dotnet user-secrets set "Encryption:Base64Key" "<base64-from-above>"
dotnet user-secrets set "Webhook:HmacSecret" "your-hmac-secret"
```

### RTSP camera credentials
Create cameras using API:
```http
POST /api/v1/cameras
{
  "cameraId": "CAM01",
  "ipAddress": "192.168.100.58",
  "rtspUsername": "admin",
  "rtspPassword": "YOUR_PASSWORD",
  "rtspProfile": "def_profile1",
  "rtspPath": "/ONVIF/MediaInput",
  "enabled": true
}
```

Test RTSP metadata:
```
POST /api/v1/cameras/CAM01/test-rtsp
```

### Local watchlist (Postgres) + enrollment (i-PRO CGI)
Set local watchlist source in `LightJSC.Api/appsettings.json`:
```json
"Watchlist": {
  "Source": "Local",
  "SyncIntervalSeconds": 20,
  "FullRefreshMinutes": 10,
  "UsePerItemThreshold": true
}
```

Create a person:
```http
POST /api/v1/persons
{
  "code": "E001",
  "firstName": "Nguyen",
  "lastName": "An",
  "gender": "Male",
  "age": 30,
  "remarks": "HQ",
  "category": "Staff",
  "isActive": true
}
```

Enroll a face using i-PRO CGI (Digest auth is used with the camera credentials stored in `/api/v1/cameras`):
```http
POST /api/v1/persons/{personId}/enroll
{
  "cameraId": "CAM01",
  "imageBase64": "<base64-jpeg-or-data-uri>",
  "storeFaceImage": true
}
```

The face template is saved in Postgres and used for matching immediately.

### Face detection model (SCRFD ONNX)
The person upload/enroll flow uses a local SCRFD ONNX model with keypoints (bnkps) to detect and align faces.

Default path (development):
```
LightJSC.Api/models/scrfd.onnx
```

Download (PowerShell):
```powershell
New-Item -ItemType Directory -Force -Path .\LightJSC.Api\models | Out-Null
$uri = "https://huggingface.co/okaris/antelopev2/resolve/main/scrfd_10g_bnkps.onnx?download=true"
Invoke-WebRequest -Uri $uri -OutFile .\LightJSC.Api\models\scrfd.onnx
```

Download (curl):
```bash
mkdir -p LightJSC.Api/models
curl -L "https://huggingface.co/okaris/antelopev2/resolve/main/scrfd_10g_bnkps.onnx?download=true" -o LightJSC.Api/models/scrfd.onnx
```

If you store the model elsewhere, update `FaceDetection:ModelPath` in `LightJSC.Api/appsettings.json` or `LightJSC.Api/appsettings.Development.json`.

## IIS deployment
- Put real connection strings into the deployed `appsettings.json` on the server (not in source control), or set environment variables via IIS `web.config`/App Pool.
- Required keys: `ConnectionStrings__Postgres`, `Encryption__Base64Key`, `Webhook__HmacSecret` (and `ConnectionStrings__ActiveGuard` when `Watchlist__Source=ActiveGuard`).

## Run locally
```powershell
dotnet restore

dotnet ef database update -p LightJSC.Infrastructure/LightJSC.Infrastructure.csproj -s LightJSC.Api/LightJSC.Api.csproj

dotnet run --project LightJSC.Api/LightJSC.Api.csproj
```

`dotnet ef` reads `LightJSC.Api/appsettings.json` for Postgres. Optional override: set `IPRO_POSTGRES_CONNECTION`.

Open:
- Health: `http://localhost:5000/health/live`, `http://localhost:5000/health/ready`
- Metrics: `http://localhost:5000/metrics`
- Swagger: `http://localhost:5000/swagger`
- Docs (ReDoc): `http://localhost:5000/docs/index.html`

## Frontend (React)
The UI project lives in `LightJSC.Web`.

```powershell
cd LightJSC.Web

npm install
npm run dev
```

Optional API base URL:
- Set `VITE_API_BASE_URL` in `LightJSC.Web/.env` (example: `http://localhost:5177`).
- A template is available at `LightJSC.Web/.env.example`.
- If not set, Vite dev server proxies `/api`, `/health`, and `/metrics` to `http://localhost:5177`.

## Subscriber console (webhook receiver)
The webhook subscriber project is `LightJSC.Subscriber` and includes a realtime UI that splits watchlist matches vs unknown faces.

Run locally:
```powershell
dotnet run --project LightJSC.Subscriber/LightJSC.Subscriber.csproj
```

Webhook endpoint:
- `POST http://localhost:5180/api/v1/webhooks/face`

SignalR stream:
- `http://localhost:5180/hubs/faces`

Set the same HMAC secret used by the ingestor:
- `Subscriber:HmacSecret` in `LightJSC.Subscriber/appsettings.json` or env `Subscriber__HmacSecret`
- If you want to enforce signature, set `Subscriber:RequireSignature=true`

## Docker (optional)
```powershell
# Build and run with postgres + optional prometheus/grafana

docker compose up --build
```

Environment variables (compose):
- `ConnectionStrings__Postgres`
- `ConnectionStrings__ActiveGuard` (only when `Watchlist__Source=ActiveGuard`)
- `Encryption__Base64Key`
- `Webhook__HmacSecret`

## Notes and assumptions
- Active Guard watchlist query assumes columns:
  - `face_matching_list.list_id`, `similarity`, `valid_flag`, `matching_status`, `expire_date`, `update_datetime`
  - `face_matching_image.list_id`, `feature_value`, `image_representive`
  If schema differs, set `ActiveGuardSchema` in `LightJSC.Api/appsettings.json` to map column names.
- i-PRO CGI enrollment uses Digest authentication and the same camera credentials stored for RTSP.
- RTSP metadata parsing supports XML ONVIF MetadataStream and key=value dump. The RTSP client uses TCP interleaved RTP and expects metadata payloads to be UTF-8.
- RTSP authentication supports Basic and Digest.

## Testing
```powershell
dotnet test
```

