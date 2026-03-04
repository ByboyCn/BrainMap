# MindMap Collaboration Demo

中文版: [README.md](./README.md)

## Overview

This is a collaborative mind map application:

- Backend: `ASP.NET Core` + `SQLite` + `SignalR` + `protobuf-net`
- Frontend: `Vue 3` + `Vite` + `LogicFlow` + `protobufjs`

## Implemented Features

1. User registration/login
2. Create and edit mind maps
3. Share mind maps for real-time collaboration
4. Show online users and other users' cursors on share page
5. Frontend/backend business APIs use Protobuf (`/pb/*`)

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

## Docker Deployment (External Caddy Reverse Proxy)

This project is configured for an existing standalone Caddy container.  
`docker-compose.yml` does not publish app ports directly; services join external network `caddy_net`.

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
- Auth/mind map/share CRUD flows mainly use Protobuf endpoints.
