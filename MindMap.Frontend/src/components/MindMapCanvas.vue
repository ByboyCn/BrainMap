<script setup>
import LogicFlow, { BezierEdge, BezierEdgeModel, RectNode, RectNodeModel, h } from '@logicflow/core'
import '@logicflow/core/es/index.css'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const DEFAULT_BG_COLOR = '#ffffff'
const DEFAULT_NODE_FILL = 'transparent'
const DEFAULT_NODE_STROKE = '#3a78ff'
const DEFAULT_EDGE_TYPE = 'mindmap-bezier'
const MIN_NODE_WIDTH = 140
const MIN_NODE_HEIGHT = 50
const EMPTY_NODE_WIDTH = 96
const EMPTY_NODE_HEIGHT = 60
const NODE_FONT_SIZE = 15
const NODE_LINE_HEIGHT = 20
const NODE_TEXT_HORIZONTAL_PADDING = 3
const NODE_TEXT_VERTICAL_PADDING = 3
const NODE_MAX_AUTO_WIDTH = 500
const DEFAULT_NEW_NODE_TEXT = '节点'
const OUT_ANCHOR_SUFFIX = '_right'
const IN_ANCHOR_SUFFIX = '_left'
const CHILD_HORIZONTAL_GAP = 80
const CHILD_VERTICAL_GAP = 10
const CHILD_VERTICAL_GAP_WITH_CHILDREN = 10
const NODE_BASE_SPAN = 50
const EDGE_CURVE_BASE_X = 68
const EDGE_CURVE_X_STEP = 18
const EDGE_CURVE_BASE_Y = 10
const EDGE_CURVE_Y_STEP = 18
const EDGE_CURVE_MAX_Y = 120
const DRAG_SLOT_THRESHOLD_X = 110
const DRAG_SLOT_THRESHOLD_Y = 60
const MINDMAP_NODE_TYPE = 'mindmap-node'
const PRESET_NODE_COLORS = [
  '#ffffff', '#f2f2f2', '#d9d9d9', '#bfbfbf', '#8c8c8c', '#595959', '#262626', '#000000',
  '#fff1f0', '#ffccc7', '#ffa39e', '#ff7875', '#ff4d4f', '#cf1322', '#a8071a', '#820014',
  '#fff7e6', '#ffd591', '#ffc069', '#ffa940', '#fa8c16', '#d46b08', '#ad4e00', '#873800',
  '#f6ffed', '#d9f7be', '#b7eb8f', '#95de64', '#73d13d', '#389e0d', '#237804', '#135200',
]
const { t } = useI18n()

function readNodeTextValue(textConfig) {
  if (typeof textConfig === 'string') return textConfig
  if (typeof textConfig?.value === 'string') return textConfig.value
  return ''
}

function estimateLineUnits(line) {
  if (!line) return 1
  return Array.from(line).reduce((sum, ch) => {
    const isAscii = ch.charCodeAt(0) <= 0x7f
    return sum + (isAscii ? 0.58 : 1)
  }, 0)
}

function calculateNodeAutoSize(textConfig) {
  const text = readNodeTextValue(textConfig)
  const normalizedText = String(text || '').trim()
  if (!normalizedText) {
    return {
      width: EMPTY_NODE_WIDTH,
      height: EMPTY_NODE_HEIGHT,
      isEmpty: true,
    }
  }

  const lines = String(text || '').split(/\r?\n/)
  const maxTextWidth = Math.max(1, NODE_MAX_AUTO_WIDTH - NODE_TEXT_HORIZONTAL_PADDING * 2)
  const maxUnitsPerLine = Math.max(1, maxTextWidth / NODE_FONT_SIZE)
  let lineCount = 0
  let maxUnits = 1

  lines.forEach((line) => {
    const units = Math.max(1, estimateLineUnits(line))
    const wrappedCount = Math.max(1, Math.ceil(units / maxUnitsPerLine))
    lineCount += wrappedCount
    if (wrappedCount > 1) {
      maxUnits = Math.max(maxUnits, maxUnitsPerLine)
    } else {
      maxUnits = Math.max(maxUnits, units)
    }
  })

  const textWidth = Math.ceil(Math.min(maxTextWidth, maxUnits * NODE_FONT_SIZE))
  const width = Math.min(NODE_MAX_AUTO_WIDTH, Math.max(MIN_NODE_WIDTH, textWidth + NODE_TEXT_HORIZONTAL_PADDING * 2))
  const height = Math.max(
    MIN_NODE_HEIGHT,
    lineCount * NODE_LINE_HEIGHT + NODE_TEXT_VERTICAL_PADDING * 2
  )
  return { width, height, isEmpty: false }
}

class MindMapNodeModel extends RectNodeModel {
  setAttributes() {
    const { width, height, isEmpty } = calculateNodeAutoSize(this.text)
    this.width = width
    this.height = height
    this.radius = isEmpty ? Math.floor(height / 2) : 8
    const anchorOffsetX = Math.max(1, Math.round(width / 2))
    const fillColor = this.properties?.fillColor || DEFAULT_NODE_FILL
    const strokeColor = this.properties?.strokeColor || DEFAULT_NODE_STROKE
    this.style = {
      ...this.style,
      fill: fillColor,
      stroke: strokeColor,
      radius: this.radius,
    }
    this.anchorsOffset = [
      { x: anchorOffsetX, y: 0, id: `${this.id}${OUT_ANCHOR_SUFFIX}` },
      { x: -anchorOffsetX, y: 0, id: `${this.id}${IN_ANCHOR_SUFFIX}` },
    ]
  }

  getConnectedSourceRules() {
    return [
      {
        message: '只能从右侧端点连线',
        validate: (_, __, sourceAnchor) => !!sourceAnchor?.id?.endsWith(OUT_ANCHOR_SUFFIX),
      },
    ]
  }

  getConnectedTargetRules() {
    return [
      {
        message: '只能连接到左侧端点',
        validate: (_, __, ___, targetAnchor) => !!targetAnchor?.id?.endsWith(IN_ANCHOR_SUFFIX),
      },
    ]
  }
}

function getNodeOutAnchorId(nodeId) {
  return `${nodeId}${OUT_ANCHOR_SUFFIX}`
}

function getNodeInAnchorId(nodeId) {
  return `${nodeId}${IN_ANCHOR_SUFFIX}`
}

function buildMindmapEdge(sourceNodeId, targetNodeId) {
  return {
    type: DEFAULT_EDGE_TYPE,
    sourceNodeId,
    targetNodeId,
    sourceAnchorId: getNodeOutAnchorId(sourceNodeId),
    targetAnchorId: getNodeInAnchorId(targetNodeId),
  }
}

class MindMapNodeView extends RectNode {
  getShape() {
    const baseShape = super.getShape()
    const model = this.props.model
    const hasChildren = !!model?.properties?.hasChildren
    if (!hasChildren) return baseShape

    const x = Number(model.x || 0)
    const y = Number(model.y || 0)
    const width = Number(model.width || 140)
    const collapsed = !!model?.properties?.collapsed
    const icon = collapsed ? '+' : '-'
    const buttonX = x + width / 2 + 14
    const buttonY = y

    const emitToggle = (event) => {
      event?.stopPropagation?.()
      event?.preventDefault?.()
      model.graphModel.eventCenter.emit('node:toggle-collapse', {
        nodeId: model.id,
      })
    }

    return h('g', {}, [
      baseShape,
      h('circle', {
        cx: buttonX,
        cy: buttonY,
        r: 10,
        fill: '#ffffff',
        stroke: '#3a78ff',
        strokeWidth: 1.5,
        onMouseDown: emitToggle,
        onClick: emitToggle,
      }),
      h(
        'text',
        {
          x: buttonX,
          y: buttonY + 4,
          fill: '#2e74ff',
          fontSize: 14,
          textAnchor: 'middle',
          style: {
            pointerEvents: 'none',
            userSelect: 'none',
          },
        },
        icon
      ),
    ])
  }
}

class MindMapEdgeView extends BezierEdge {
  getLastTwoPoints() {
    const endPoint = this.props?.model?.endPoint
    const startPoint = this.props?.model?.startPoint
    if (!endPoint) {
      return super.getLastTwoPoints()
    }
    const endX = Number(endPoint.x || 0)
    const endY = Number(endPoint.y || 0)
    const startX = Number(startPoint?.x || endX - 1)
    const deltaX = Math.abs(endX - startX) > 1 ? Math.abs(endX - startX) : 1
    const tailX = endX - Math.max(1, Math.min(12, deltaX))
    return [{ x: tailX, y: endY }, { x: endX, y: endY }]
  }

  getEndArrow() {
    return null
  }
}

class MindMapEdgeModel extends BezierEdgeModel {
  setAttributes() {
    super.setAttributes()
    this.setHitable(false)
    this.draggable = false
    this.isShowAdjustPoint = false
  }
}

const props = defineProps({
  modelValue: {
    type: String,
    default: '{"nodes":[],"edges":[],"meta":{"backgroundColor":"#ffffff"}}',
  },
  readonly: {
    type: Boolean,
    default: false,
  },
  showToolbar: {
    type: Boolean,
    default: true,
  },
  height: {
    type: Number,
    default: 430,
  },
})

const emit = defineEmits(['update:modelValue', 'operation'])
const wrapperRef = ref(null)
const containerRef = ref(null)
const backgroundColor = ref(DEFAULT_BG_COLOR)
const selectedNodeId = ref('')
const selectedNodeIds = ref([])
const selectedNodeColor = ref(DEFAULT_NODE_FILL)
const zoomPercent = ref(100)
const isRightPanning = ref(false)
const isBoxSelecting = ref(false)
const boxSelectRectStyle = ref({})
const isDragPreviewVisible = ref(false)
const dragPreviewRectStyle = ref({})
const hasNodeSelection = computed(() => selectedNodeIds.value.length > 0 || !!selectedNodeId.value)
const showEditorWatermark = computed(() => !!props.showToolbar && !props.readonly)

let lf = null
let suppressSync = false
let nodeCounter = 1
let activeDragNodeId = ''
let activeDragDescendants = []
let activeDropParentId = ''
let internalSubtreeMove = false
let panLastClientX = 0
let panLastClientY = 0
let boxSelectStartClientX = 0
let boxSelectStartClientY = 0
let boxSelectCurrentClientX = 0
let boxSelectCurrentClientY = 0

function hideDragPreview() {
  isDragPreviewVisible.value = false
  dragPreviewRectStyle.value = {}
  activeDropParentId = ''
}

function graphPointToStagePoint(x, y) {
  if (!lf) return { x: 0, y: 0 }
  const transform = lf.getTransform() || {}
  const scaleX = Number(transform.SCALE_X || 1)
  const scaleY = Number(transform.SCALE_Y || scaleX)
  const translateX = Number(transform.TRANSLATE_X || 0)
  const translateY = Number(transform.TRANSLATE_Y || 0)
  return {
    x: x * scaleX + translateX,
    y: y * scaleY + translateY,
  }
}

function stagePointToGraphPoint(x, y) {
  if (!lf) return { x: 0, y: 0 }
  const transform = lf.getTransform() || {}
  const scaleX = Number(transform.SCALE_X || 1)
  const scaleY = Number(transform.SCALE_Y || scaleX)
  const translateX = Number(transform.TRANSLATE_X || 0)
  const translateY = Number(transform.TRANSLATE_Y || 0)
  return {
    x: (x - translateX) / scaleX,
    y: (y - translateY) / scaleY,
  }
}

function normalizeGraph(raw) {
  const nodes = (Array.isArray(raw?.nodes) ? raw.nodes : []).map((node) => ({
    ...node,
    type: MINDMAP_NODE_TYPE,
    properties: {
      ...(node.properties || {}),
      fillColor: node.properties?.fillColor || DEFAULT_NODE_FILL,
      strokeColor: node.properties?.strokeColor || DEFAULT_NODE_STROKE,
      collapsed: !!node.properties?.collapsed,
    },
  }))
  const edges = (Array.isArray(raw?.edges) ? raw.edges : []).map((edge) => ({
    ...edge,
    type: DEFAULT_EDGE_TYPE,
    sourceAnchorId: edge?.sourceNodeId ? (edge.sourceAnchorId || getNodeOutAnchorId(edge.sourceNodeId)) : edge.sourceAnchorId,
    targetAnchorId: edge?.targetNodeId ? (edge.targetAnchorId || getNodeInAnchorId(edge.targetNodeId)) : edge.targetAnchorId,
  }))
  return { nodes, edges }
}

function parseGraph(jsonText) {
  try {
    const graph = JSON.parse(jsonText || '{}')
    backgroundColor.value = graph?.meta?.backgroundColor || DEFAULT_BG_COLOR
    return normalizeGraph(graph)
  } catch {
    backgroundColor.value = DEFAULT_BG_COLOR
    return {
      nodes: [],
      edges: [],
    }
  }
}

function syncToModel() {
  if (!lf || suppressSync) return
  const graph = lf.getGraphData() || {}
  const nodes = (Array.isArray(graph.nodes) ? graph.nodes : []).map((node) => {
    const properties = { ...(node.properties || {}) }
    delete properties.hasChildren
    return {
      ...node,
      properties,
    }
  })
  const edges = Array.isArray(graph.edges) ? graph.edges : []
  emit(
    'update:modelValue',
    JSON.stringify({
      nodes,
      edges,
      meta: {
        backgroundColor: backgroundColor.value,
      },
    })
  )
}

function renderFromModel(source) {
  if (!lf) return
  suppressSync = true
  lf.render(parseGraph(source))
  suppressSync = false
  layoutAllTrees(true)
  syncToModel()
  refreshZoomPercent()
}

function focusCanvas() {
  wrapperRef.value?.focus()
}

function beginRightPan(event) {
  if (event.button !== 2) return
  event.preventDefault()
  focusCanvas()
  isRightPanning.value = true
  panLastClientX = event.clientX
  panLastClientY = event.clientY
}

function handleWindowMouseMove(event) {
  if (isRightPanning.value && lf) {
    const deltaX = event.clientX - panLastClientX
    const deltaY = event.clientY - panLastClientY
    if (deltaX || deltaY) {
      panLastClientX = event.clientX
      panLastClientY = event.clientY
      lf.translate(deltaX, deltaY)
    }
  }

  if (!isBoxSelecting.value) return
  boxSelectCurrentClientX = event.clientX
  boxSelectCurrentClientY = event.clientY
  updateBoxSelectRectStyle()
}

function endRightPan() {
  if (!isRightPanning.value) return
  isRightPanning.value = false
}

function clamp(value, min, max) {
  return Math.min(Math.max(value, min), max)
}

function updateBoxSelectRectStyle() {
  const canvasEl = containerRef.value
  if (!canvasEl) return
  const rect = canvasEl.getBoundingClientRect()
  const left = clamp(Math.min(boxSelectStartClientX, boxSelectCurrentClientX) - rect.left, 0, rect.width)
  const top = clamp(Math.min(boxSelectStartClientY, boxSelectCurrentClientY) - rect.top, 0, rect.height)
  const right = clamp(Math.max(boxSelectStartClientX, boxSelectCurrentClientX) - rect.left, 0, rect.width)
  const bottom = clamp(Math.max(boxSelectStartClientY, boxSelectCurrentClientY) - rect.top, 0, rect.height)
  boxSelectRectStyle.value = {
    left: `${left}px`,
    top: `${top}px`,
    width: `${Math.max(0, right - left)}px`,
    height: `${Math.max(0, bottom - top)}px`,
  }
}

function emitOperation(actionType, detail = {}) {
  if (!actionType) return
  emit('operation', {
    actionType,
    detail,
    atUnixMs: Date.now(),
  })
}

function isRectOverlap(a, b) {
  const overlapWidth = Math.min(a.right, b.right) - Math.max(a.left, b.left)
  const overlapHeight = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top)
  return overlapWidth > 0 && overlapHeight > 0
}

function getNodeIdsByBoxOverlap(left, top, right, bottom) {
  if (!lf) return []
  const graph = lf.getGraphData() || {}
  const nodes = Array.isArray(graph.nodes) ? graph.nodes : []
  const selectRect = { left, top, right, bottom }

  return nodes
    .map((node) => node?.id)
    .filter((id) => !!id)
    .filter((id) => {
      const model = lf.getNodeModelById(id)
      if (!model || model.visible === false) return false

      const width = Number(model.width || MIN_NODE_WIDTH)
      const height = Number(model.height || MIN_NODE_HEIGHT)
      const x = Number(model.x || 0)
      const y = Number(model.y || 0)

      const p1 = graphPointToStagePoint(x - width / 2, y - height / 2)
      const p2 = graphPointToStagePoint(x + width / 2, y + height / 2)

      const nodeRect = {
        left: Math.min(p1.x, p2.x),
        top: Math.min(p1.y, p2.y),
        right: Math.max(p1.x, p2.x),
        bottom: Math.max(p1.y, p2.y),
      }

      return isRectOverlap(selectRect, nodeRect)
    })
}

function getNodeCount() {
  if (!lf) return 0
  const graph = lf.getGraphData() || {}
  return Array.isArray(graph.nodes) ? graph.nodes.length : 0
}

function createMainNodeAt(graphX, graphY) {
  if (!lf || props.readonly) return null
  const added = lf.addNode({
    id: `n-${Date.now()}-${nodeCounter++}`,
    type: MINDMAP_NODE_TYPE,
    x: graphX,
    y: graphY,
    text: DEFAULT_NEW_NODE_TEXT,
    properties: {
      fillColor: selectedNodeColor.value || DEFAULT_NODE_FILL,
      strokeColor: DEFAULT_NODE_STROKE,
      collapsed: false,
    },
  })
  if (!added?.id) return null
  setSelectedNode(added.id, { replace: true })
  syncToModel()
  emitOperation('node_add', {
    nodeId: added.id,
    parentNodeId: '',
  })
  return added
}

function createMainNodeByCanvasCenter() {
  const canvasEl = containerRef.value
  if (!canvasEl) return null
  const centerX = canvasEl.clientWidth / 2
  const centerY = canvasEl.clientHeight / 2
  const point = stagePointToGraphPoint(centerX, centerY)
  return createMainNodeAt(point.x, point.y)
}

function beginBoxSelect(event) {
  if (props.readonly || !lf || !event || event.button !== 0) return
  const canvasEl = containerRef.value
  if (!canvasEl) return
  const rect = canvasEl.getBoundingClientRect()
  if (
    event.clientX < rect.left ||
    event.clientX > rect.right ||
    event.clientY < rect.top ||
    event.clientY > rect.bottom
  ) {
    return
  }
  isBoxSelecting.value = true
  boxSelectStartClientX = event.clientX
  boxSelectStartClientY = event.clientY
  boxSelectCurrentClientX = event.clientX
  boxSelectCurrentClientY = event.clientY
  updateBoxSelectRectStyle()
}

function endBoxSelect() {
  if (!isBoxSelecting.value || !lf) return
  const canvasEl = containerRef.value
  isBoxSelecting.value = false
  boxSelectRectStyle.value = {}
  if (!canvasEl) return

  const rect = canvasEl.getBoundingClientRect()
  const dragDistance =
    Math.abs(boxSelectCurrentClientX - boxSelectStartClientX) +
    Math.abs(boxSelectCurrentClientY - boxSelectStartClientY)
  if (dragDistance < 6) return

  const x1 = clamp(boxSelectStartClientX - rect.left, 0, rect.width)
  const y1 = clamp(boxSelectStartClientY - rect.top, 0, rect.height)
  const x2 = clamp(boxSelectCurrentClientX - rect.left, 0, rect.width)
  const y2 = clamp(boxSelectCurrentClientY - rect.top, 0, rect.height)
  const left = Math.min(x1, x2)
  const top = Math.min(y1, y2)
  const right = Math.max(x1, x2)
  const bottom = Math.max(y1, y2)
  const nodeIds = getNodeIdsByBoxOverlap(left, top, right, bottom)

  if (nodeIds.length === 0) {
    if (getNodeCount() === 0) {
      const point = stagePointToGraphPoint(x2, y2)
      createMainNodeAt(point.x, point.y)
      return
    }
    clearSelectedNodes()
    return
  }

  lf.clearSelectElements()
  nodeIds.forEach((nodeId, index) => {
    lf.selectElementById(nodeId, index > 0)
  })
  refreshSelectedState(nodeIds[nodeIds.length - 1])
}

function handleWindowMouseUp() {
  endRightPan()
  endBoxSelect()
  hideDragPreview()
}

function handleContextMenu(event) {
  event.preventDefault()
}

function getSelectedNodeIdsFromGraph() {
  if (!lf) return []
  const selected = lf.getSelectElements(true) || {}
  return (Array.isArray(selected.nodes) ? selected.nodes : [])
    .map((node) => node?.id)
    .filter((id) => typeof id === 'string' && id.length > 0)
}

function refreshSelectedState(preferredNodeId = '') {
  const ids = getSelectedNodeIdsFromGraph()
  selectedNodeIds.value = ids
  if (ids.length === 0) {
    selectedNodeId.value = ''
    return
  }
  if (preferredNodeId && ids.includes(preferredNodeId)) {
    selectedNodeId.value = preferredNodeId
  } else if (!ids.includes(selectedNodeId.value)) {
    selectedNodeId.value = ids[ids.length - 1]
  }
  const model = lf?.getModelById(selectedNodeId.value)
  selectedNodeColor.value = model?.properties?.fillColor || DEFAULT_NODE_FILL
}

function clearSelectedNodes() {
  if (lf) {
    lf.clearSelectElements()
  }
  selectedNodeIds.value = []
  selectedNodeId.value = ''
}

function setSelectedNode(nodeId, options = {}) {
  if (!lf || !nodeId) {
    clearSelectedNodes()
    return
  }
  if (options.replace) {
    lf.clearSelectElements()
    lf.selectElementById(nodeId)
  } else {
    lf.selectElementById(nodeId, true)
  }
  refreshSelectedState(nodeId)
}

function addNodeWithParent(parentNode, x, y) {
  if (!lf || !parentNode) return null
  const added = lf.addNode({
    id: `n-${Date.now()}-${nodeCounter++}`,
    type: MINDMAP_NODE_TYPE,
    x,
    y,
    text: DEFAULT_NEW_NODE_TEXT,
    properties: {
      fillColor: selectedNodeColor.value || DEFAULT_NODE_FILL,
      strokeColor: DEFAULT_NODE_STROKE,
      collapsed: false,
    },
  })
  lf.addEdge(buildMindmapEdge(parentNode.id, added.id))
  setSelectedNode(added.id, { replace: true })
  layoutAllTrees()
  emitOperation('node_add', {
    nodeId: added.id,
    parentNodeId: parentNode.id,
  })
  return added
}

function addFreeNode() {
  if (!lf || props.readonly) return
  const graph = lf.getGraphData() || {}
  const nodes = Array.isArray(graph.nodes) ? graph.nodes : []
  const base = nodes[nodes.length - 1]
  const x = base ? base.x + 120 : 320
  const y = base ? base.y + 60 : 220
  const added = lf.addNode({
    id: `n-${Date.now()}-${nodeCounter++}`,
    type: MINDMAP_NODE_TYPE,
    x,
    y,
    text: DEFAULT_NEW_NODE_TEXT,
    properties: {
      fillColor: selectedNodeColor.value || DEFAULT_NODE_FILL,
      strokeColor: DEFAULT_NODE_STROKE,
      collapsed: false,
    },
  })
  syncToModel()
  if (added?.id) {
    emitOperation('node_add', {
      nodeId: added.id,
      parentNodeId: '',
    })
  }
}

function addChildNode() {
  if (!lf || props.readonly || !selectedNodeId.value) return
  const parentNode = lf.getDataById(selectedNodeId.value)
  if (!parentNode?.id) return
  if (isNodeCollapsed(parentNode.id)) {
    lf.setProperties(parentNode.id, { collapsed: false })
  }
  const x = getChildCenterXByParent(parentNode.id)
  const y = getAppendChildY(parentNode.id, parentNode.y)
  addNodeWithParent(parentNode, x, y)
}

function isRootNode(nodeId) {
  if (!lf || !nodeId) return false
  const edges = lf.getNodeEdges(nodeId) || []
  return !edges.some((edge) => edge.targetNodeId === nodeId)
}

function addSiblingNode() {
  if (!lf || props.readonly || !selectedNodeId.value) return
  if (isRootNode(selectedNodeId.value)) return

  const currentNode = lf.getDataById(selectedNodeId.value)
  if (!currentNode?.id) return

  const currentEdges = lf.getNodeEdges(currentNode.id) || []
  const parentEdge = currentEdges.find((edge) => edge.targetNodeId === currentNode.id)
  if (!parentEdge?.sourceNodeId) return

  const parentNode = lf.getDataById(parentEdge.sourceNodeId)
  if (!parentNode?.id) return

  const x = getChildCenterXByParent(parentNode.id)
  const y = getAppendChildY(parentNode.id, parentNode.y)
  addNodeWithParent(parentNode, x, y)
}

function collectSubtreeNodeIds(rootNodeId) {
  if (!lf || !rootNodeId) return []
  const queue = [rootNodeId]
  const result = []
  const visited = new Set()

  while (queue.length > 0) {
    const currentId = queue.shift()
    if (!currentId || visited.has(currentId)) continue
    visited.add(currentId)
    result.push(currentId)

    const edges = lf.getNodeEdges(currentId) || []
    edges
      .filter((edge) => edge.sourceNodeId === currentId)
      .forEach((edge) => {
        if (edge.targetNodeId && !visited.has(edge.targetNodeId)) {
          queue.push(edge.targetNodeId)
        }
      })
  }

  return result
}

function getDirectParentId(nodeId) {
  if (!lf || !nodeId) return ''
  const edges = lf.getNodeEdges(nodeId) || []
  const parentEdge = edges.find((edge) => edge.targetNodeId === nodeId)
  return parentEdge?.sourceNodeId || ''
}

function getDirectChildrenIds(nodeId) {
  if (!lf || !nodeId) return []
  const edges = lf.getNodeEdges(nodeId) || []
  return edges.filter((edge) => edge.sourceNodeId === nodeId).map((edge) => edge.targetNodeId)
}

function getAppendChildY(parentId, fallbackY = 0) {
  if (!lf || !parentId) return fallbackY
  const childYs = getDirectChildrenIds(parentId)
    .map((id) => lf.getDataById(id))
    .filter((node) => node?.id)
    .map((node) => Number(node.y || 0))
  if (childYs.length === 0) return fallbackY
  return Math.max(...childYs) + 1
}

function getNodeAnchorOffsetById(nodeId) {
  if (!lf || !nodeId) return Math.round(MIN_NODE_WIDTH / 2)
  const model = lf.getNodeModelById(nodeId)
  return Math.max(1, Math.round(Number(model?.width || MIN_NODE_WIDTH) / 2))
}

function handleNodeTextUpdate(event) {
  if (!lf) return
  const nodeId = event?.data?.id || event?.id || ''
  if (!nodeId) return
  layoutAllTrees()
}

function getChildCenterXByParent(parentId, childId = '') {
  if (!lf || !parentId) return 0
  const parentNode = lf.getDataById(parentId)
  if (!parentNode?.id) return 0
  const parentOutOffset = getNodeAnchorOffsetById(parentId)
  const childInOffset = childId ? getNodeAnchorOffsetById(childId) : Math.round(MIN_NODE_WIDTH / 2)
  return Number(parentNode.x || 0) + parentOutOffset + CHILD_HORIZONTAL_GAP + childInOffset
}

function getEdgeBetween(sourceNodeId, targetNodeId) {
  if (!lf || !sourceNodeId || !targetNodeId) return null
  const edges = lf.getNodeEdges(sourceNodeId) || []
  return edges.find((edge) => edge.sourceNodeId === sourceNodeId && edge.targetNodeId === targetNodeId) || null
}

function detachIncomingEdges(nodeId) {
  if (!lf || !nodeId) return
  const incomingEdges = (lf.getNodeEdges(nodeId) || []).filter((edge) => edge.targetNodeId === nodeId)
  incomingEdges.forEach((edge) => {
    if (edge?.id) {
      lf.deleteEdge(edge.id)
    }
  })
}

function setElementVisible(elementId, visible) {
  if (!lf || !elementId) return
  const model = lf.getModelById(elementId)
  if (!model) return
  if (model.visible === visible) return
  lf.updateAttributes(elementId, { visible })
}

function isNodeCollapsed(nodeId) {
  if (!lf || !nodeId) return false
  const model = lf.getModelById(nodeId)
  return !!model?.properties?.collapsed
}

function hasChildren(nodeId) {
  return getDirectChildrenIds(nodeId).length > 0
}

function updateHasChildrenFlags() {
  if (!lf) return
  const graph = lf.getGraphData() || {}
  const nodes = Array.isArray(graph.nodes) ? graph.nodes : []
  nodes.forEach((node) => {
    const nodeId = node?.id
    if (!nodeId) return
    const childExists = hasChildren(nodeId)
    const currentHasChildren = !!lf.getModelById(nodeId)?.properties?.hasChildren
    if (childExists !== currentHasChildren) {
      lf.setProperties(nodeId, { hasChildren: childExists })
    }
  })
}

function hasExpandedChildren(nodeId) {
  if (!lf || !nodeId) return false
  return !isNodeCollapsed(nodeId) && hasChildren(nodeId)
}

function getVerticalGapByNode(nodeId) {
  return hasExpandedChildren(nodeId) ? CHILD_VERTICAL_GAP_WITH_CHILDREN : CHILD_VERTICAL_GAP
}

function getSubtreeSpan(nodeId) {
  if (!lf || !nodeId) return NODE_BASE_SPAN
  const selfHeight = Number(lf.getNodeModelById(nodeId)?.height || NODE_BASE_SPAN)
  const selfSpan = Math.max(NODE_BASE_SPAN, selfHeight)
  const childIds = getDirectChildrenIds(nodeId)
  if (childIds.length === 0 || isNodeCollapsed(nodeId)) {
    return selfSpan
  }

  let total = 0
  childIds.forEach((childId, index) => {
    total += getSubtreeSpan(childId)
    if (index < childIds.length - 1) {
      total += getVerticalGapByNode(childId)
    }
  })

  return Math.max(selfSpan, total)
}

function getProjectedChildPosition(parentId, draggedNodeId, draggedY) {
  if (!lf || !parentId || !draggedNodeId) return null
  const parent = lf.getDataById(parentId)
  if (!parent?.id) return null

  const existingChildren = getDirectChildrenIds(parentId)
    .filter((id) => id !== draggedNodeId)
    .map((id) => lf.getDataById(id))
    .filter((node) => node?.id)
    .map((node) => ({
      id: node.id,
      y: Number(node.y || 0),
      x: Number(node.x || 0),
      span: getSubtreeSpan(node.id),
      gapAfter: getVerticalGapByNode(node.id),
      isDragged: false,
    }))

  const draggedNode = lf.getDataById(draggedNodeId)
  if (!draggedNode?.id) return null
  existingChildren.push({
    id: draggedNode.id,
    y: Number(draggedY || draggedNode.y || 0),
    x: Number(draggedNode.x || 0),
    span: getSubtreeSpan(draggedNode.id),
    gapAfter: getVerticalGapByNode(draggedNode.id),
    isDragged: true,
  })

  const entries = existingChildren.sort((a, b) => (a.y - b.y) || (a.x - b.x))
  const totalSpan = entries.reduce(
    (sum, entry, index) => sum + entry.span + (index < entries.length - 1 ? entry.gapAfter : 0),
    0
  )

  let cursorY = Number(parent.y || 0) - totalSpan / 2
  for (let i = 0; i < entries.length; i += 1) {
    const entry = entries[i]
    const centerY = cursorY + entry.span / 2
    if (entry.isDragged) {
      return {
        x: getChildCenterXByParent(parentId, draggedNodeId),
        y: centerY,
      }
    }
    cursorY += entry.span + (i < entries.length - 1 ? entry.gapAfter : 0)
  }

  return {
    x: getChildCenterXByParent(parentId, draggedNodeId),
    y: Number(parent.y || 0),
  }
}

function getCurveLevelBySibling(index, siblingCount) {
  if (siblingCount <= 1) return 0
  const middle = (siblingCount - 1) / 2
  const rawDistance = Math.abs(index - middle)
  if (siblingCount % 2 === 0) {
    return Math.max(0, rawDistance - 0.5)
  }
  return rawDistance
}

function getCurveDirectionBySibling(index, siblingCount, parentY, childY) {
  const deltaY = Number(childY || 0) - Number(parentY || 0)
  if (Math.abs(deltaY) > 0.001) {
    return deltaY > 0 ? 1 : -1
  }
  if (siblingCount <= 1) return 0
  const middle = (siblingCount - 1) / 2
  if (index < middle) return -1
  if (index > middle) return 1
  return 0
}

function applySiblingCurve(edgeId, options) {
  if (!lf || !edgeId) return
  const edgeModel = lf.getModelById(edgeId)
  if (!edgeModel || typeof edgeModel.updatePath !== 'function') return

  const start = edgeModel.startPoint
  const end = edgeModel.endPoint
  if (!start || !end) return

  const level = getCurveLevelBySibling(options.index, options.siblingCount)
  const direction = getCurveDirectionBySibling(
    options.index,
    options.siblingCount,
    options.parentY,
    options.childY
  )

  const curveY =
    direction === 0 ? 0 : Math.min(EDGE_CURVE_MAX_Y, EDGE_CURVE_BASE_Y + level * EDGE_CURVE_Y_STEP)
  const idealCurveX = EDGE_CURVE_BASE_X + level * EDGE_CURVE_X_STEP
  const halfDx = Math.abs(Number(end.x || 0) - Number(start.x || 0)) / 2
  const maxCurveX = Math.max(2, halfDx - 2)
  const curveX = Math.max(2, Math.min(idealCurveX, maxCurveX))

  edgeModel.updatePath(
    {
      x: start.x + curveX,
      y: start.y + direction * curveY,
    },
    {
      x: end.x - curveX,
      y: end.y,
    }
  )
}

function setDescendantsVisible(parentId, visible) {
  if (!lf || !parentId) return
  const childIds = getDirectChildrenIds(parentId)
  childIds.forEach((childId) => {
    const edge = getEdgeBetween(parentId, childId)
    if (edge) {
      setElementVisible(edge.id, visible)
    }
    setElementVisible(childId, visible)
    setDescendantsVisible(childId, visible)
  })
}

function layoutChildren(parentId, skipSync = false) {
  if (!lf || !parentId) return
  const parent = lf.getDataById(parentId)
  if (!parent?.id) return
  const childIds = getDirectChildrenIds(parentId)
  if (childIds.length === 0) {
    if (!skipSync) syncToModel()
    return
  }

  if (isNodeCollapsed(parentId)) {
    setDescendantsVisible(parentId, false)
    if (!skipSync) syncToModel()
    return
  }

  const entries = childIds
    .map((id) => lf.getDataById(id))
    .filter((node) => node?.id)
    .sort((a, b) => (a.y - b.y) || (a.x - b.x))
    .map((node) => ({
      node,
      span: getSubtreeSpan(node.id),
      gapAfter: getVerticalGapByNode(node.id),
    }))

  if (entries.length === 0) {
    if (!skipSync) syncToModel()
    return
  }

  const totalSpan = entries.reduce(
    (sum, entry, index) => sum + entry.span + (index < entries.length - 1 ? entry.gapAfter : 0),
    0
  )
  let cursorY = parent.y - totalSpan / 2

  entries.forEach((entry, index) => {
    const centerY = cursorY + entry.span / 2
    const targetX = getChildCenterXByParent(parentId, entry.node.id)
    moveSubtreeTo(entry.node.id, targetX, centerY)

    const edge = getEdgeBetween(parentId, entry.node.id)
    if (edge) {
      setElementVisible(edge.id, true)
      applySiblingCurve(edge.id, {
        index,
        siblingCount: entries.length,
        parentY: parent.y,
        childY: centerY,
      })
    }
    setElementVisible(entry.node.id, true)

    layoutChildren(entry.node.id, true)
    cursorY += entry.span + (index < entries.length - 1 ? entry.gapAfter : 0)
  })

  if (!skipSync) {
    syncToModel()
  }
}

function layoutAllTrees(skipSync = false) {
  if (!lf) return
  updateHasChildrenFlags()
  const graph = lf.getGraphData() || {}
  const nodes = Array.isArray(graph.nodes) ? graph.nodes : []
  const roots = nodes.filter((node) => !getDirectParentId(node.id))
  roots.forEach((node) => {
    setElementVisible(node.id, true)
    layoutChildren(node.id, true)
  })
  if (!skipSync) {
    syncToModel()
  }
}

function hasEdge(sourceNodeId, targetNodeId) {
  if (!lf || !sourceNodeId || !targetNodeId) return false
  const edges = lf.getNodeEdges(sourceNodeId) || []
  return edges.some((edge) => edge.sourceNodeId === sourceNodeId && edge.targetNodeId === targetNodeId)
}

function moveSubtreeTo(nodeId, x, y) {
  if (!lf || !nodeId) return
  const current = lf.getDataById(nodeId)
  if (!current?.id) return
  const deltaX = x - current.x
  const deltaY = y - current.y
  if (!deltaX && !deltaY) return
  const subtreeIds = collectSubtreeNodeIds(nodeId)
  lf.graphModel.moveNodes(subtreeIds, deltaX, deltaY, true)
}

function findDropTargetSlot(draggedNodeId, x, y) {
  if (!lf || !draggedNodeId) return null
  const subtreeSet = new Set(collectSubtreeNodeIds(draggedNodeId))
  const graph = lf.getGraphData() || {}
  const nodes = Array.isArray(graph.nodes) ? graph.nodes : []
  let best = null

  for (let i = 0; i < nodes.length; i += 1) {
    const candidate = nodes[i]
    if (!candidate?.id || candidate.id === draggedNodeId || subtreeSet.has(candidate.id)) continue
    const projected = getProjectedChildPosition(candidate.id, draggedNodeId, y)
    if (!projected) continue

    const dx = Math.abs(projected.x - x)
    const dy = Math.abs(projected.y - y)
    if (dx > DRAG_SLOT_THRESHOLD_X || dy > DRAG_SLOT_THRESHOLD_Y) continue

    const score = dx + dy * 1.4
    if (!best || score < best.score) {
      best = {
        parentId: candidate.id,
        projected,
        score,
      }
    }
  }

  return best
}

function updateDragPreview(draggedNodeId, x, y) {
  if (!lf || !draggedNodeId) {
    hideDragPreview()
    return
  }
  const slot = findDropTargetSlot(draggedNodeId, x, y)
  if (!slot?.parentId || !slot?.projected) {
    hideDragPreview()
    return
  }
  activeDropParentId = slot.parentId

  const draggedModel = lf.getNodeModelById(draggedNodeId)
  const nodeWidth = Number(draggedModel?.width || 140)
  const nodeHeight = Number(draggedModel?.height || 50)
  const stageCenter = graphPointToStagePoint(slot.projected.x, slot.projected.y)
  const transform = lf.getTransform() || {}
  const scaleX = Number(transform.SCALE_X || 1)
  const scaleY = Number(transform.SCALE_Y || scaleX)
  const width = nodeWidth * scaleX
  const height = nodeHeight * scaleY

  dragPreviewRectStyle.value = {
    left: `${stageCenter.x - width / 2}px`,
    top: `${stageCenter.y - height / 2}px`,
    width: `${width}px`,
    height: `${height}px`,
  }
  isDragPreviewVisible.value = true
}

function reparentNode(draggedNodeId, targetParentId) {
  if (!lf || !draggedNodeId || !targetParentId || draggedNodeId === targetParentId) return
  const subtreeSet = new Set(collectSubtreeNodeIds(draggedNodeId))
  if (subtreeSet.has(targetParentId)) return

  const targetParent = lf.getDataById(targetParentId)
  if (!targetParent?.id) return

  const incomingEdges = (lf.getNodeEdges(draggedNodeId) || []).filter((edge) => edge.targetNodeId === draggedNodeId)
  incomingEdges.forEach((edge) => {
    if (edge.sourceNodeId !== targetParentId) {
      lf.deleteEdge(edge.id)
    }
  })

  if (!hasEdge(targetParentId, draggedNodeId)) {
    lf.addEdge(buildMindmapEdge(targetParentId, draggedNodeId))
  }

  layoutAllTrees()
  emitOperation('node_reparent', {
    nodeId: draggedNodeId,
    parentNodeId: targetParentId,
  })
}

function deleteSelectedSubtree() {
  if (!lf || props.readonly) return
  const selectedIds = selectedNodeIds.value.length
    ? selectedNodeIds.value.slice()
    : selectedNodeId.value
      ? [selectedNodeId.value]
      : []
  if (selectedIds.length === 0) return

  const nodeIdsToDelete = new Set()
  selectedIds.forEach((nodeId) => {
    collectSubtreeNodeIds(nodeId).forEach((subId) => nodeIdsToDelete.add(subId))
  })
  if (nodeIdsToDelete.size === 0) return
  const deletedNodeIds = Array.from(nodeIdsToDelete)

  deletedNodeIds
    .reverse()
    .forEach((nodeId) => {
      if (lf.getDataById(nodeId)?.id) {
        lf.deleteNode(nodeId)
      }
    })

  clearSelectedNodes()
  const graph = lf.getGraphData() || {}
  const nodes = Array.isArray(graph.nodes) ? graph.nodes : []
  if (nodes.length === 0) {
    lf.addNode({
      id: 'root',
      type: MINDMAP_NODE_TYPE,
      x: 320,
      y: 220,
      text: t('canvas.rootTopic'),
      properties: {
        fillColor: DEFAULT_NODE_FILL,
        strokeColor: DEFAULT_NODE_STROKE,
        collapsed: false,
      },
    })
    syncToModel()
  } else {
    layoutAllTrees()
  }
  emitOperation('node_delete', {
    count: deletedNodeIds.length,
    nodeIds: deletedNodeIds.slice(0, 20),
  })
}

function toggleSelectedCollapse() {
  if (!lf || props.readonly || !selectedNodeId.value) return
  const nodeId = selectedNodeId.value
  if (!hasChildren(nodeId)) return
  lf.setProperties(nodeId, { collapsed: !isNodeCollapsed(nodeId) })
  layoutAllTrees()
}

function applyNodeColor(color = selectedNodeColor.value) {
  if (!lf || props.readonly) return
  const targetIds = selectedNodeIds.value.length
    ? selectedNodeIds.value.slice()
    : selectedNodeId.value
      ? [selectedNodeId.value]
      : []
  if (targetIds.length === 0) return

  targetIds.forEach((nodeId) => {
    lf.setProperties(nodeId, {
      fillColor: color,
      strokeColor: DEFAULT_NODE_STROKE,
    })
  })
  syncToModel()
}

function applyPresetNodeColor(color) {
  selectedNodeColor.value = color
  applyNodeColor(color)
}

function fitView() {
  if (!lf) return
  lf.fitView(40, 40)
  refreshZoomPercent()
}

function refreshZoomPercent() {
  if (!lf) return
  const transform = lf.getTransform()
  const scale = Number(transform?.SCALE_X || 1)
  zoomPercent.value = Math.max(1, Math.round(scale * 100))
}

function isDomTextEditingTarget(target) {
  const element =
    target instanceof HTMLElement
      ? target
      : target instanceof Node
        ? target.parentElement
        : null
  if (!(element instanceof HTMLElement)) return false
  if (element.classList.contains('lf-text-input')) return true
  return !!element.closest('.lf-text-input,[contenteditable="true"],input,textarea')
}

function insertLineBreakIntoLfInput() {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return
  const range = selection.getRangeAt(0)
  range.deleteContents()
  const lineBreak = document.createTextNode('\n')
  range.insertNode(lineBreak)
  range.setStartAfter(lineBreak)
  range.collapse(true)
  selection.removeAllRanges()
  selection.addRange(range)
}

function handleWindowKeydownCapture(event) {
  if (props.readonly) return
  if (event.key !== 'Enter' || event.altKey || event.ctrlKey || event.metaKey) return
  const target =
    event.target instanceof HTMLElement
      ? event.target
      : event.target instanceof Node
        ? event.target.parentElement
        : null
  const editable = target?.closest?.('.lf-text-input')
  if (!(editable instanceof HTMLElement)) return

  event.preventDefault()
  event.stopPropagation()
  insertLineBreakIntoLfInput()
  editable.dispatchEvent(new Event('input', { bubbles: true }))
}

function isPrintableInputKey(event) {
  return event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey
}

function placeCaretAtEnd(element) {
  const selection = window.getSelection()
  if (!selection) return
  const range = document.createRange()
  range.selectNodeContents(element)
  range.collapse(false)
  selection.removeAllRanges()
  selection.addRange(range)
}

function activateTextEditorAndInsert(char) {
  if (!lf || !selectedNodeId.value) return
  lf.editText(selectedNodeId.value)

  requestAnimationFrame(() => {
    const editable = wrapperRef.value?.querySelector('.lf-text-input')
    if (!(editable instanceof HTMLElement)) return
    editable.focus()
    editable.textContent = char
    placeCaretAtEnd(editable)
    editable.dispatchEvent(new Event('input', { bubbles: true }))
  })
}

function getNodeTextValue(node) {
  if (!node) return ''
  if (typeof node.text === 'string') return node.text
  if (typeof node.text?.value === 'string') return node.text.value
  return ''
}

function getGraphStructureSignature(graph) {
  const nodes = (Array.isArray(graph?.nodes) ? graph.nodes : [])
    .map((node) => ({
      id: node?.id || '',
      text: getNodeTextValue(node),
      properties: {
        fillColor: node?.properties?.fillColor || '',
        strokeColor: node?.properties?.strokeColor || '',
        collapsed: !!node?.properties?.collapsed,
      },
    }))
    .sort((a, b) => a.id.localeCompare(b.id))

  const edges = (Array.isArray(graph?.edges) ? graph.edges : [])
    .map((edge) => ({
      sourceNodeId: edge?.sourceNodeId || '',
      targetNodeId: edge?.targetNodeId || '',
      sourceAnchorId: edge?.sourceAnchorId || '',
      targetAnchorId: edge?.targetAnchorId || '',
    }))
    .sort((a, b) => {
      const keyA = `${a.sourceNodeId}->${a.targetNodeId}:${a.sourceAnchorId}:${a.targetAnchorId}`
      const keyB = `${b.sourceNodeId}->${b.targetNodeId}:${b.sourceAnchorId}:${b.targetAnchorId}`
      return keyA.localeCompare(keyB)
    })

  return JSON.stringify({ nodes, edges })
}

function undoOnceForNodeChange() {
  if (!lf || !lf.history?.undoAble?.()) return
  const before = lf.getGraphData() || {}
  const beforeSignature = getGraphStructureSignature(before)

  lf.undo()

  const after = lf.getGraphData() || {}
  const afterSignature = getGraphStructureSignature(after)

  if (beforeSignature === afterSignature && lf.history?.undoAble?.()) {
    lf.undo()
  }

  refreshSelectedState()
  syncToModel()
}

function redoOnceForNodeChange() {
  if (!lf || !lf.history?.redoAble?.()) return
  lf.redo()
  refreshSelectedState()
  syncToModel()
}

function handleKeydown(event) {
  if (props.readonly) return
  if (isDomTextEditingTarget(event.target)) return
  const key = String(event.key || '').toLowerCase()

  if ((event.ctrlKey || event.metaKey) && !event.shiftKey && key === 'z') {
    event.preventDefault()
    event.stopPropagation()
    undoOnceForNodeChange()
    return
  }

  if ((event.ctrlKey || event.metaKey) && (key === 'y' || (event.shiftKey && key === 'z'))) {
    event.preventDefault()
    event.stopPropagation()
    redoOnceForNodeChange()
    return
  }

  if (!event.ctrlKey && !event.metaKey && !event.altKey && key === 'n') {
    event.preventDefault()
    createMainNodeByCanvasCenter()
    return
  }

  if (!selectedNodeId.value) return

  if (isPrintableInputKey(event)) {
    event.preventDefault()
    activateTextEditorAndInsert(event.key)
    return
  }

  if (event.key === 'Tab') {
    event.preventDefault()
    addChildNode()
    return
  }

  if (event.key === 'Enter') {
    event.preventDefault()
    addSiblingNode()
    return
  }

  if (event.key === 'Delete' || event.key === 'Backspace') {
    event.preventDefault()
    deleteSelectedSubtree()
  }
}

defineExpose({
  addFreeNode,
  addChildNode,
  addSiblingNode,
  fitView,
  getGraphJson: () =>
    lf
      ? JSON.stringify({
          ...(lf.getGraphData() || {}),
          meta: {
            backgroundColor: backgroundColor.value,
          },
        })
      : props.modelValue,
})

onMounted(() => {
  lf = new LogicFlow({
    container: containerRef.value,
    grid: true,
    keyboard: { enabled: true },
    isSilentMode: props.readonly,
    stopMoveGraph: true,
  })
  if (lf?.history) {
    lf.history.waitTime = 360
  }
  lf.updateEditConfig({
    adjustEdge: false,
    adjustEdgeMiddle: false,
    adjustEdgeStartAndEnd: false,
    adjustEdgeStart: false,
    adjustEdgeEnd: false,
    edgeTextEdit: false,
    edgeTextDraggable: false,
    edgeSelectedOutline: false,
  })

  lf.register({
    type: MINDMAP_NODE_TYPE,
    view: MindMapNodeView,
    model: MindMapNodeModel,
  })

  lf.register({
    type: DEFAULT_EDGE_TYPE,
    view: MindMapEdgeView,
    model: MindMapEdgeModel,
  })

  lf.setTheme({
    bezier: {
      stroke: '#7a93c7',
      endArrowType: 'none',
    },
    nodeText: {
      fontSize: 15,
      overflowMode: 'autoWrap',
    },
    inputText: {
      fontSize: 15,
      background: 'transparent',
      border: 'none',
      boxShadow: 'none',
      padding: '3px',
      borderRadius: '0',
    },
  })

  renderFromModel(props.modelValue)
  layoutAllTrees()
  refreshZoomPercent()

  const events = ['history:change', 'node:dragstop', 'node:add', 'node:delete', 'edge:add', 'edge:delete']
  events.forEach((eventName) => lf.on(eventName, syncToModel))
  lf.on('node:text:update', handleNodeTextUpdate)
  lf.on('graph:transform', ({ transform }) => {
    const scale = Number(transform?.SCALE_X || 1)
    zoomPercent.value = Math.max(1, Math.round(scale * 100))
  })

  lf.on('node:dragstart', ({ data }) => {
    if (props.readonly) return
    activeDragNodeId = data?.id || ''
    activeDragDescendants = activeDragNodeId ? collectSubtreeNodeIds(activeDragNodeId).slice(1) : []
    if (activeDragNodeId) {
      detachIncomingEdges(activeDragNodeId)
    }
    hideDragPreview()
  })

  lf.on('node:drag', ({ data, deltaX, deltaY }) => {
    if (props.readonly || internalSubtreeMove) return
    if (!activeDragNodeId || data?.id !== activeDragNodeId) return

    const draggedNode = lf.getDataById(activeDragNodeId)
    if (draggedNode?.id) {
      updateDragPreview(activeDragNodeId, Number(draggedNode.x || 0), Number(draggedNode.y || 0))
    } else {
      hideDragPreview()
    }

    if (!activeDragDescendants.length) return

    const moveX = Number(deltaX || 0)
    const moveY = Number(deltaY || 0)
    if (!moveX && !moveY) return

    internalSubtreeMove = true
    lf.graphModel.moveNodes(activeDragDescendants, moveX, moveY, true)
    internalSubtreeMove = false
  })

  lf.on('node:drop', ({ data }) => {
    if (props.readonly) return
    const draggedId = data?.id || activeDragNodeId
    const targetIdFromPreview = activeDropParentId
    hideDragPreview()
    if (!draggedId) return

    const draggedNode = lf.getDataById(draggedId)
    if (draggedNode?.id) {
      const targetId = targetIdFromPreview || findDropTargetSlot(draggedId, draggedNode.x, draggedNode.y)?.parentId || ''
      if (targetId) {
        reparentNode(draggedId, targetId)
      } else {
        layoutAllTrees()
      }
    }

    activeDragNodeId = ''
    activeDragDescendants = []
    internalSubtreeMove = false
  })

  lf.on('node:click', ({ data, e }) => {
    const nodeId = data?.id || ''
    if (!nodeId) return
    const hasModifier = !!(e?.ctrlKey || e?.metaKey || e?.shiftKey)
    if (hasModifier) {
      if (selectedNodeIds.value.includes(nodeId)) {
        lf.deselectElementById(nodeId)
        refreshSelectedState()
      } else {
        setSelectedNode(nodeId, { replace: false })
      }
    } else if (selectedNodeIds.value.length > 1 && selectedNodeIds.value.includes(nodeId)) {
      refreshSelectedState(nodeId)
    } else {
      setSelectedNode(nodeId, { replace: true })
    }
    focusCanvas()
  })
  lf.on('node:toggle-collapse', ({ nodeId }) => {
    if (props.readonly || !nodeId || !hasChildren(nodeId)) return
    lf.setProperties(nodeId, { collapsed: !isNodeCollapsed(nodeId) })
    setSelectedNode(nodeId, { replace: false })
    layoutAllTrees()
    focusCanvas()
  })

  lf.on('blank:click', () => {
    clearSelectedNodes()
    focusCanvas()
  })
  lf.on('blank:mousedown', ({ e }) => {
    beginBoxSelect(e)
  })

  window.addEventListener('mousemove', handleWindowMouseMove)
  window.addEventListener('mouseup', handleWindowMouseUp)
  window.addEventListener('keydown', handleWindowKeydownCapture, true)
})

onBeforeUnmount(() => {
  window.removeEventListener('mousemove', handleWindowMouseMove)
  window.removeEventListener('mouseup', handleWindowMouseUp)
  window.removeEventListener('keydown', handleWindowKeydownCapture, true)
  if (lf) {
    lf.destroy()
    lf = null
  }
})

watch(
  () => props.modelValue,
  (nextValue) => {
    if (!lf || suppressSync) return
    const current = JSON.stringify({
      ...(lf.getGraphData() || {}),
      meta: { backgroundColor: backgroundColor.value },
    })
    if (nextValue !== current) {
      renderFromModel(nextValue)
    }
  }
)
</script>

<template>
  <div
    ref="wrapperRef"
    class="mindmap-canvas-wrap"
    :class="{ 'is-right-panning': isRightPanning }"
    tabindex="0"
    @keydown="handleKeydown"
    @mousedown="beginRightPan"
    @contextmenu="handleContextMenu"
  >
    <div v-if="showToolbar && !readonly" class="mindmap-toolbar">
      <button type="button" @click="addFreeNode">{{ t('canvas.addNode') }}</button>
      <button type="button" @click="addChildNode" :disabled="!selectedNodeId">{{ t('canvas.addChild') }}</button>
      <button type="button" @click="addSiblingNode" :disabled="!selectedNodeId || isRootNode(selectedNodeId)">
        {{ t('canvas.addSibling') }}
      </button>
      <button type="button" @click="fitView">{{ t('canvas.fitView') }}</button>
      <label class="color-picker">
        {{ t('canvas.background') }}
        <input v-model="backgroundColor" type="color" @input="syncToModel" />
      </label>
      <button type="button" :disabled="!selectedNodeId" @click="deleteSelectedSubtree">{{ t('canvas.deleteSubtree') }}</button>
    </div>
    <div class="mindmap-stage">
      <div
        ref="containerRef"
        class="mindmap-canvas"
        :style="{ height: `${height}px`, backgroundColor }"
        :class="{ readonly }"
      />
      <div v-if="isBoxSelecting" class="box-select-mask" :style="boxSelectRectStyle" />
      <div v-if="isDragPreviewVisible" class="drag-preview-mask" :style="dragPreviewRectStyle" />
      <div v-if="!readonly && hasNodeSelection" class="node-color-palette" @mousedown.stop>
        <button
          v-for="color in PRESET_NODE_COLORS"
          :key="color"
          type="button"
          class="palette-color"
          :class="{ active: color === selectedNodeColor }"
          :style="{ backgroundColor: color }"
          @mousedown.prevent
          @click="applyPresetNodeColor(color)"
        />
      </div>
      <div class="zoom-indicator">{{ t('canvas.zoom') }}: {{ zoomPercent }}%</div>
      <div v-if="showEditorWatermark" class="canvas-watermark">BrainMap</div>
    </div>
  </div>
</template>




