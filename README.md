# 脑图与 TODO 协作 Demo

English version: [README.en.md](./README.en.md)

## 项目简介
这是一个支持多人协作的在线应用：
- 脑图：支持编辑、分享、多人实时同步
- TODO：支持分组、排序、归档、自动保存

技术栈：
- 后端：`ASP.NET Core` + `SQLite` + `SignalR` + `protobuf-net`
- 前端：`Vue 3` + `Vite` + `LogicFlow` + `protobufjs`

## 已实现功能
1. 用户注册/登录
2. 控制台可选择创建“脑图”或“TODO”
3. 脑图编辑、分享、在线协作、鼠标位置同步
4. TODO 四分组展示：未完成 / 已完成 / 归档未完成 / 归档已完成
5. TODO 排序：自然排序、创建时间、预计开始时间、预计完成时间、开始时间、完成时间
6. TODO 底部快捷输入框（`Ctrl + /` 聚焦，`Enter` 提交）
7. 前后端业务接口通过 Protobuf（`/pb/*`）通信

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

## 主要页面路由
- 首页：`/`
- 脑图编辑页：`/editor/:id`
- TODO 页面：`/todo/:id`
- 分享页：`/share/:shareCode`

## Docker 部署（外部 Caddy 反代）
项目默认按“已有 Caddy 单独运行”设计，`docker-compose.yml` 不直接暴露前后端端口，而是接入外部网络 `caddy_net`。

### 1) 创建共享网络（仅一次）
```bash
docker network create caddy_net
```

### 2) 把 Caddy 容器接入该网络
```bash
docker network connect caddy_net <caddy容器名>
```

### 3) 启动本项目
```bash
docker compose up -d --build
```

说明：
- 前端服务名：`frontend`（容器内端口 `80`）
- 后端服务名：`backend`（容器内端口 `5289`）
- SQLite 卷：`mindmap_data`（持久化）

### 4) Caddyfile 示例
```caddy
your-domain.com {
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
- 分享页在线光标使用 SignalR 实时同步（`/hubs/share`）。
- 脑图和 TODO 都走 Protobuf 接口进行读写。
