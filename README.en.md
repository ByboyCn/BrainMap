# MindMap Collaboration Demo

中文版: [README.md](./README.md)

## Overview

This is a collaborative mind map application:

- Backend: `ASP.NET Core` + `SQLite` + `SignalR` + `protobuf-net`
- Frontend: `Vue 3` + `Vite` + `LogicFlow` + `protobufjs`

## Implemented Features

1. User registration/login
2. Create and edit mind maps
3. Generate share links and open share pages
4. Show online users on share page (including self), but cursor positions exclude self
5. Frontend/backend business APIs use Protobuf binary payloads (`/pb/*`)

## Local Development

### 1) Start backend

```powershell
cd MindMap.Backend
dotnet run
```

Default URL: `http://localhost:5289`

### 2) Start frontend

```powershell
cd MindMap.Frontend
npm install
npm run dev
```

Default URL: `http://localhost:5173`

## Run with Docker

The project includes:

- Root: `docker-compose.yml`
- Backend: `MindMap.Backend/Dockerfile`
- Frontend: `MindMap.Frontend/Dockerfile`

Run from project root:

```bash
docker compose up -d --build
```

Then open:

- Frontend: `http://localhost:5173`
- Backend: `http://localhost:5289`

Notes:

- SQLite data is persisted via Docker volume `mindmap_data`.

## LAN Testing

- Backend can bind to `0.0.0.0:5289`
- Frontend Vite dev/preview can bind to `0.0.0.0:5173`
- Access via machine IP, for example:
  - `http://192.168.1.20:5173`

## Notes

- Online cursor sync on share page is implemented with SignalR.
- Auth/mind map/share CRUD flows mainly use Protobuf endpoints.
