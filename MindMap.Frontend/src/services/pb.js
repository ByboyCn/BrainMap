import protobuf from 'protobufjs'
import { getApiBaseUrl, getSession } from './api'

const schema = `
syntax = "proto3";
package mindmap;

message PbAuthRequest {
  string userName = 1;
  string password = 2;
}

message PbAuthResponse {
  bool success = 1;
  string message = 2;
  string userId = 3;
  string userName = 4;
  string token = 5;
}

message PbEmptyRequest {}

message PbMindMapIdRequest {
  string mapId = 1;
}

message PbCreateMindMapRequest {
  string title = 1;
  string contentJson = 2;
}

message PbUpdateMindMapRequest {
  string mapId = 1;
  string title = 2;
  string contentJson = 3;
}

message PbCreateShareRequest {
  string mapId = 1;
  bool requireLogin = 2;
}

message PbShareCodeRequest {
  string shareCode = 1;
}

message PbUpdateSharedRequest {
  string shareCode = 1;
  string contentJson = 2;
}

message PbTodoIdRequest {
  string todoId = 1;
}

message PbCreateTodoRequest {
  string title = 1;
  string contentJson = 2;
}

message PbUpdateTodoRequest {
  string todoId = 1;
  string title = 2;
  string contentJson = 3;
}

message PbMindMapSummary {
  string id = 1;
  string title = 2;
  int64 updatedAtUnixMs = 3;
  string shareCode = 4;
}

message PbMindMapListResponse {
  bool success = 1;
  string message = 2;
  repeated PbMindMapSummary maps = 3;
}

message PbMindMapDetailResponse {
  bool success = 1;
  string message = 2;
  string id = 3;
  string title = 4;
  string contentJson = 5;
  int64 updatedAtUnixMs = 6;
  string shareCode = 7;
  bool shareRequireLogin = 8;
}

message PbShareResponse {
  bool success = 1;
  string message = 2;
  string shareCode = 3;
  string relativeUrl = 4;
  bool requireLogin = 5;
}

message PbStatusResponse {
  bool success = 1;
  string message = 2;
}

message PbTodoSummary {
  string id = 1;
  string title = 2;
  int64 updatedAtUnixMs = 3;
}

message PbTodoListResponse {
  bool success = 1;
  string message = 2;
  repeated PbTodoSummary todos = 3;
}

message PbTodoDetailResponse {
  bool success = 1;
  string message = 2;
  string id = 3;
  string title = 4;
  string contentJson = 5;
  int64 updatedAtUnixMs = 6;
}
`

const root = protobuf.parse(schema).root

function type(name) {
  return root.lookupType(`mindmap.${name}`)
}

function toPlain(decodedType, payload) {
  return decodedType.toObject(payload, {
    defaults: true,
    longs: Number,
  })
}

async function pbRequest(path, reqType, resType, body, auth = false) {
  const requestType = type(reqType)
  const responseType = type(resType)
  const encoded = requestType.encode(requestType.create(body || {})).finish()

  const headers = {
    'Content-Type': 'application/x-protobuf',
    Accept: 'application/x-protobuf',
  }

  if (auth) {
    const token = getSession()?.token
    if (token) headers.Authorization = `Bearer ${token}`
  }

  const response = await fetch(`${getApiBaseUrl()}${path}`, {
    method: 'POST',
    headers,
    body: encoded,
  })

  if (!response.ok) {
    throw new Error(`request failed (${response.status})`)
  }

  const buffer = new Uint8Array(await response.arrayBuffer())
  const decoded = responseType.decode(buffer)
  return toPlain(responseType, decoded)
}

function assertSuccess(result) {
  if (!result.success) {
    throw new Error(result.message || 'request failed')
  }
  return result
}

export async function pbRegister(userName, password) {
  const result = await pbRequest('/pb/auth/register', 'PbAuthRequest', 'PbAuthResponse', { userName, password })
  return assertSuccess(result)
}

export async function pbLogin(userName, password) {
  const result = await pbRequest('/pb/auth/login', 'PbAuthRequest', 'PbAuthResponse', { userName, password })
  return assertSuccess(result)
}

export async function pbListMaps() {
  const result = await pbRequest('/pb/mindmaps/list', 'PbEmptyRequest', 'PbMindMapListResponse', {}, true)
  return assertSuccess(result).maps
}

export async function pbCreateMap(title, contentJson) {
  const result = await pbRequest('/pb/mindmaps/create', 'PbCreateMindMapRequest', 'PbMindMapDetailResponse', { title, contentJson }, true)
  return assertSuccess(result)
}

export async function pbGetMap(mapId) {
  const result = await pbRequest('/pb/mindmaps/get', 'PbMindMapIdRequest', 'PbMindMapDetailResponse', { mapId }, true)
  return assertSuccess(result)
}

export async function pbUpdateMap(mapId, title, contentJson) {
  const result = await pbRequest('/pb/mindmaps/update', 'PbUpdateMindMapRequest', 'PbMindMapDetailResponse', { mapId, title, contentJson }, true)
  return assertSuccess(result)
}

export async function pbDeleteMap(mapId) {
  const result = await pbRequest('/pb/mindmaps/delete', 'PbMindMapIdRequest', 'PbStatusResponse', { mapId }, true)
  return assertSuccess(result)
}

export async function pbCreateShare(mapId, requireLogin = false) {
  const result = await pbRequest('/pb/mindmaps/share', 'PbCreateShareRequest', 'PbShareResponse', { mapId, requireLogin }, true)
  return assertSuccess(result)
}

export async function pbGetShared(shareCode) {
  const result = await pbRequest('/pb/share/get', 'PbShareCodeRequest', 'PbMindMapDetailResponse', { shareCode }, true)
  return assertSuccess(result)
}

export async function pbUpdateShared(shareCode, contentJson) {
  const result = await pbRequest('/pb/share/update', 'PbUpdateSharedRequest', 'PbStatusResponse', { shareCode, contentJson }, true)
  return assertSuccess(result)
}

export async function pbListTodos() {
  const result = await pbRequest('/pb/todos/list', 'PbEmptyRequest', 'PbTodoListResponse', {}, true)
  return assertSuccess(result).todos
}

export async function pbCreateTodo(title, contentJson) {
  const result = await pbRequest('/pb/todos/create', 'PbCreateTodoRequest', 'PbTodoDetailResponse', { title, contentJson }, true)
  return assertSuccess(result)
}

export async function pbGetTodo(todoId) {
  const result = await pbRequest('/pb/todos/get', 'PbTodoIdRequest', 'PbTodoDetailResponse', { todoId }, true)
  return assertSuccess(result)
}

export async function pbUpdateTodo(todoId, title, contentJson) {
  const result = await pbRequest('/pb/todos/update', 'PbUpdateTodoRequest', 'PbTodoDetailResponse', { todoId, title, contentJson }, true)
  return assertSuccess(result)
}

export async function pbDeleteTodo(todoId) {
  const result = await pbRequest('/pb/todos/delete', 'PbTodoIdRequest', 'PbStatusResponse', { todoId }, true)
  return assertSuccess(result)
}
