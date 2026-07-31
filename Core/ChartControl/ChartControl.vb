Imports System.Linq
Imports System.Windows.Forms
Imports SkiaSharp
Imports SkiaSharp.Views.Desktop
Imports ChartKit.Abstractions
Imports ChartKit.Models
Imports ChartKit.Core.Strategies

Namespace Core
    Public Partial Class ChartControl
        Inherits UserControl

        Private Const MARGIN_LEFT As Single = 10
        Private Const MARGIN_RIGHT As Single = 80
        Private Const RIGHT_DRAG_PADDING_BARS As Integer = 12
        Private Const DEFAULT_INITIAL_VISIBLE_BARS As Integer = 100
        Private Const DEFAULT_INITIAL_CANDLE_WIDTH As Single = 8
        Private Const DEFAULT_INITIAL_GAP As Single = 2
        Private Const FRAME_INTERVAL_MS As Integer = 16
        Private Const CANDLE_RING_CAPACITY As Integer = 100000

        Private WithEvents _sk As SKControl
        Private ReadOnly _theme As ChartTheme = ChartTheme.CreateDefault()
        Private ReadOnly _registry As New LayerRegistry()
        Private ReadOnly _indicatorEngine As New IndicatorEngine()
        Private _candles As New CandleRingBuffer(CANDLE_RING_CAPACITY)
        Private ReadOnly _vs As New ChartViewState()
        Private _viewportInitialized As Boolean

        Private _mouseInside As Boolean
        Private WithEvents _frameTimer As Timer
        Private WithEvents _stateSaveTimer As Timer
        Private _isRestoringState As Boolean
        Private _needsRepaint As Boolean = True

        Public Event VisibleCandleCountChanged As EventHandler(Of EventArgs)
        Public Event CandleCountChanged As EventHandler(Of EventArgs)

        Private _mainRect As SKRect
        Private _volumeRect As SKRect
        Private _panelRects As New List(Of SKRect)()
        Private _panelBaselines As New Dictionary(Of Integer, List(Of Single))()
        Private _panelZones As New Dictionary(Of Integer, PanelZoneState)()
        Private _shadeRules As New List(Of OverlayShadeRule)()
        Private _signalRules As New List(Of SignalRule)()
        Private _strategyCapture As StrategyCapture
        Private _strategyReentryOptions As New StrategyReentryLockOptions()
        Private _pctAxisMode As Integer
        Private _lastRestoreCount As Integer = -1
        Private _panelRatios As New List(Of Single)()
        Private _isPanelResizing As Boolean
        Private _resizePanelSlot As Integer = -1
        Private _resizeStartY As Single
        Private _resizeStartRatio As Single
        Private Const PANEL_HIT As Single = 4.0F
        Private Const PANEL_MIN_RATIO As Single = 0.06F
        Private _priceHigh As Single
        Private _priceLow As Single
        Private _volumeMax As Long

        Private _isDragging As Boolean
        Private _isDraggingPrice As Boolean
        Private _dragStartX As Integer
        Private _dragStartY As Integer
        Private _dragStartIndex As Integer
        Private _dragStartMaxP As Single
        Private _dragStartMinP As Single
        Private _manualMaxP As Single
        Private _manualMinP As Single
        Private _isAutoScaleY As Boolean = True
        Private _lastMouseX As Single
        Private _lastMouseY As Single

        Public ReadOnly Property LastRestoreIndicatorCount As Integer
            Get
                Return _lastRestoreCount
            End Get
        End Property

        Public ReadOnly Property Layers As LayerRegistry
            Get
                Return _registry
            End Get
        End Property

        Public Sub New()
            _sk = New SKControl() With {.Dock = DockStyle.Fill}
            Controls.Add(_sk)

            AddHandler _sk.MouseMove, AddressOf OnGLMouseMove
            AddHandler _sk.MouseDown, AddressOf OnGLMouseDown
            AddHandler _sk.MouseUp, AddressOf OnGLMouseUp
            AddHandler _sk.MouseWheel, AddressOf OnGLMouseWheel
            AddHandler _sk.MouseEnter, AddressOf OnGLMouseEnter
            AddHandler _sk.MouseLeave, AddressOf OnGLMouseLeave
            AddHandler _sk.KeyDown, AddressOf OnGLKeyDown

            _frameTimer = New Timer() With {.Interval = FRAME_INTERVAL_MS, .Enabled = True}
            AddHandler _frameTimer.Tick, AddressOf OnFrameTimer

            _stateSaveTimer = New Timer() With {.Interval = 500, .Enabled = False}
            AddHandler _stateSaveTimer.Tick, AddressOf OnStateSaveTimer
        End Sub

        Private Sub ScheduleStateSave()
            If _isRestoringState OrElse IsDisposed OrElse _stateSaveTimer Is Nothing Then Return
            _stateSaveTimer.Stop()
            _stateSaveTimer.Start()
        End Sub

        Private Sub OnStateSaveTimer(sender As Object, e As EventArgs)
            _stateSaveTimer.Stop()
            SaveState()
        End Sub
    End Class
End Namespace
