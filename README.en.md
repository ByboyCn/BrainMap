# MindMap + TODO Collaboration

中文版: [README.md](./README.md)

## Overview
This is a collaborative web app with two document types:
- MindMap: edit, share, realtime collaboration
- TODO: grouped list, sorting, archive, autosave

Tech stack:
- Backend: `ASP.NET Core` + `SQLite` + `SignalR` + `protobuf-net`
- Frontend: `Vue 3` + `Vite` + `LogicFlow` + `protobufjs`

## Implemented Features
1. User register/login
2. Create type selection in home console: `MindMap` or `TODO`
3. MindMap edit/share/realtime collaboration with cursor sync
4. TODO 4 groups: pending / completed / archived pending / archived completed
5. TODO sorting: natural, created time, planned start, planned end, start time, completed time
6. Bottom quick input for TODO (`Ctrl + /` to focus, `Enter` to submit)
7. Subtask-parent status sync: when all subtasks are done, parent task is auto-completed; if any subtask becomes pending, parent completion is reverted automatically
8. Frontend/backend business APIs use Protobuf (`/pb/*`)

## Local Development
### Start backend
```powershell
cd MindMap.Backend
dotnet run
```
Default URL: `http://localhost:5289`

### Start frontend
```powershell
cd MindMap.Frontend
npm install
npm run dev
```
Default URL: `http://localhost:5173`

## Main Routes
- Home: `/`
- MindMap editor: `/editor/:id`
- TODO page: `/todo/:id`
- Share page: `/share/:shareCode`

## Docker Deployment (External Caddy Reverse Proxy)
This repo assumes Caddy is already running in a separate container. `docker-compose.yml` does not publish service ports directly and uses external network `caddy_net`.

### 1) Create shared network (one-time)
```bash
docker network create caddy_net
```

### 2) Connect your Caddy container
```bash
docker network connect caddy_net <caddy_container_name>
```

### 3) Start this project
```bash
docker compose up -d --build
```

Notes:
- Frontend service: `frontend` (internal port `80`)
- Backend service: `backend` (internal port `5289`)
- SQLite volume: `mindmap_data` (persistent)

### 4) Caddyfile example
```caddy
your-domain.com {
  encode gzip zstd

  @api path /api/* /pb/* /hubs/*
  reverse_proxy @api backend:5289

  reverse_proxy frontend:80
}
```

Reload Caddy:
```bash
docker exec <caddy_container_name> caddy reload --config /etc/caddy/Caddyfile
```

## Notes
- Share-page cursor sync uses SignalR (`/hubs/share`).
- Both MindMap and TODO CRUD use protobuf endpoints.

