# 脑图协作 Demo

English version: [README.en.md](./README.en.md)

## 项目简介

这是一个支持多人协作的脑图应用：

- 后端：`ASP.NET Core` + `SQLite` + `SignalR` + `protobuf-net`
- 前端：`Vue 3` + `Vite` + `LogicFlow` + `protobufjs`

## 已实现功能

1. 用户注册/登录
2. 新增与编辑脑图
3. 分享脑图并多人实时协作
4. 分享页展示在线用户和他人鼠标位置
5. 前后端业务接口通过 Protobuf（`/pb/*`）通信

## 本地开发

### 启动后端

```powershell
cd MindMap.Backend
dotnet run
```

默认地址：`http://localhost:5289`

### 启动前端

```powershell
cd MindMap.Frontend
npm install
npm run dev
```

默认地址：`http://localhost:5173`

## Docker 部署（外部 Caddy 反代）

项目默认按“已有 Caddy 单独运行”设计，`docker-compose.yml` 不再直接暴露前后端端口，而是接入外部网络 `caddy_net`。

### 1) 创建共享网络（仅一次）

```bash
docker network create caddy_net
```

### 2) 把你的 Caddy 容器接入该网络

```bash
docker network connect caddy_net <caddy容器名>
```

### 3) 启动本项目

```bash
docker compose up -d --build
```

说明：

- 前端容器名：`frontend`（内部端口 `80`）
- 后端容器名：`backend`（内部端口 `5289`）
- SQLite 数据卷：`mindmap_data`（持久化）

### 4) Caddyfile 反代示例

```caddy
你的域名 {
  encode gzip zstd

  @api path /api/* /pb/* /hubs/*
  reverse_proxy @api backend:5289

  reverse_proxy frontend:80
}
```

应用后重载 Caddy：

```bash
docker exec <caddy容器名> caddy reload --config /etc/caddy/Caddyfile
```

## 备注

- 分享页在线鼠标使用 SignalR 实时同步（`/hubs/share`）。
- 认证/脑图/分享相关 CRUD 主要走 Protobuf 接口。
