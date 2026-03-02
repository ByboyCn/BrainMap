# 脑图协作 Demo

English version: [README.en.md](./README.en.md)

## 项目简介

这是一个支持多人分享协作的脑图应用：

- 后端：`ASP.NET Core` + `SQLite` + `SignalR` + `protobuf-net`
- 前端：`Vue 3` + `Vite` + `LogicFlow` + `protobufjs`

## 已实现功能

1. 用户注册/登录
2. 新增与编辑脑图
3. 生成分享链接并进入分享页
4. 分享页显示在线用户（含自己），鼠标位置仅显示他人
5. 前后端业务接口通过 Protobuf 二进制通信（`/pb/*`）

## 本地开发启动

### 1) 启动后端

```powershell
cd MindMap.Backend
dotnet run
```

默认地址：`http://localhost:5289`

### 2) 启动前端

```powershell
cd MindMap.Frontend
npm install
npm run dev
```

默认地址：`http://localhost:5173`

## Docker 启动

项目已提供以下文件：

- 根目录：`docker-compose.yml`
- 后端：`MindMap.Backend/Dockerfile`
- 前端：`MindMap.Frontend/Dockerfile`

在项目根目录执行：

```bash
docker compose up -d --build
```

启动后访问：

- 前端：`http://localhost:5173`
- 后端：`http://localhost:5289`

说明：

- 后端 SQLite 数据通过 Docker 卷 `mindmap_data` 持久化。

## 局域网测试

- 后端可绑定 `0.0.0.0:5289`
- 前端 Vite 开发/预览可绑定 `0.0.0.0:5173`
- 可通过机器 IP 访问，例如：
  - `http://192.168.1.20:5173`

## 备注

- 分享页在线鼠标使用 SignalR 实时同步。
- 认证/脑图/分享相关 CRUD 主要走 Protobuf 接口。
