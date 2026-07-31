Imports System.Drawing
Imports System.Windows.Forms
Imports ChartKit.Abstractions

Namespace UI
    Public Class ChartQueryEventArgs
        Inherits EventArgs

        Public ReadOnly Property Symbol As String
        Public ReadOnly Property SymbolName As String
        Public ReadOnly Property Interval As CandleInterval
        Public ReadOnly Property TotalCount As Integer
        Public ReadOnly Property VisibleCount As Integer

        Public Sub New(symbol As String, symbolName As String, interval As CandleInterval,
                       totalCount As Integer, visibleCount As Integer)
            Me.Symbol = symbol
            Me.SymbolName = symbolName
            Me.Interval = interval
            Me.TotalCount = totalCount
            Me.VisibleCount = visibleCount
        End Sub
    End Class

    Public Class ChartDateEventArgs
        Inherits EventArgs
        Public ReadOnly Property TradingDate As Date

        Public Sub New(tradingDate As Date)
            Me.TradingDate = tradingDate.Date
        End Sub
    End Class

    Public Class ChartToolbar
        Inherits UserControl

        Private Class TimeframeOption
            Public Property Caption As String
            Public Property Interval As CandleInterval
            Public Overrides Function ToString() As String
                Return Caption
            End Function
        End Class

        Private ReadOnly _symbol As New TextBox()
        Private ReadOnly _symbolName As New TextBox()
        Private ReadOnly _timeframe As New ComboBox()
        Private ReadOnly _visibleCount As New NumericUpDown()
        Private ReadOnly _totalCount As New NumericUpDown()
        Private ReadOnly _datePicker As New DateTimePicker()
        Private ReadOnly _queryButton As New Button()
        Private ReadOnly _moreButton As New Button()
        Private ReadOnly _dateButton As New Button()
        Private ReadOnly _backtestButton As New Button()
        Private ReadOnly _status As New Label()

        Public Event QueryRequested As EventHandler(Of ChartQueryEventArgs)
        Public Event MoreRequested As EventHandler(Of ChartQueryEventArgs)
        Public Event DateRequested As EventHandler(Of ChartDateEventArgs)
        Public Event VisibleCountChanged As EventHandler(Of EventArgs)
        Public Event BacktestRequested As EventHandler(Of EventArgs)
        Private _updatingValues As Boolean
        Private _moreBatchCount As Integer = 200

        Public Sub New()
            Dock = DockStyle.Top
            Height = 38
            BackColor = Color.Black
            ForeColor = Color.White
            Padding = New Padding(6, 5, 6, 4)

            Dim flow As New FlowLayoutPanel() With {
                .Dock = DockStyle.Fill, .WrapContents = False, .AutoScroll = True,
                .BackColor = BackColor, .Padding = New Padding(0), .Margin = New Padding(0)}
            Controls.Add(flow)

            ConfigureTextBox(_symbol, 70)
            ConfigureTextBox(_symbolName, 105)
            _symbolName.ForeColor = Color.White

            _timeframe.DropDownStyle = ComboBoxStyle.DropDownList
            _timeframe.Width = 82
            ConfigureDarkInput(_timeframe)
            _timeframe.Items.AddRange(New Object() {
                Opt("1틱", CandleInterval.Tick1), Opt("3틱", CandleInterval.Tick3),
                Opt("5틱", CandleInterval.Tick5), Opt("10틱", CandleInterval.Tick10),
                Opt("30틱", CandleInterval.Tick30), Opt("60틱", CandleInterval.Tick60),
                Opt("120틱", CandleInterval.Tick120), Opt("240틱", CandleInterval.Tick240),
                Opt("360틱", CandleInterval.Tick360), Opt("720틱", CandleInterval.Tick720),
                Opt("1분", CandleInterval.Min1), Opt("3분", CandleInterval.Min3),
                Opt("5분", CandleInterval.Min5), Opt("15분", CandleInterval.Min15),
                Opt("30분", CandleInterval.Min30), Opt("60분", CandleInterval.Min60),
                Opt("일봉", CandleInterval.Day), Opt("주봉", CandleInterval.Week),
                Opt("월봉", CandleInterval.Month)})
            _timeframe.SelectedIndex = 9

            ConfigureNumber(_visibleCount, 1, 100000, 100, 72)
            ConfigureNumber(_totalCount, 10, 100000, 200, 76)
            AddHandler _visibleCount.ValueChanged, AddressOf OnVisibleCountValueChanged

            _datePicker.Format = DateTimePickerFormat.Custom
            _datePicker.CustomFormat = "yyyy-MM-dd"
            _datePicker.Width = 108
            ConfigureDarkInput(_datePicker)

            ConfigureButton(_queryButton, "조회", 52)
            ConfigureButton(_moreButton, "추가조회", 68)
            ConfigureButton(_dateButton, "이동", 48)
            ConfigureButton(_backtestButton, "백테스트", 70)
            AddHandler _queryButton.Click, AddressOf OnQueryClick
            AddHandler _moreButton.Click, AddressOf OnMoreClick
            AddHandler _dateButton.Click, AddressOf OnDateClick
            AddHandler _backtestButton.Click,
                Sub(sender As Object, e As EventArgs) RaiseEvent BacktestRequested(Me, EventArgs.Empty)

            _status.AutoSize = True
            _status.ForeColor = Color.White
            _status.Padding = New Padding(7, 6, 0, 0)

            AddField(flow, "종목코드", _symbol)
            AddField(flow, "종목명", _symbolName)
            AddField(flow, "주기", _timeframe)
            AddField(flow, "화면표시", _visibleCount)
            AddField(flow, "총캔들", _totalCount)
            flow.Controls.Add(_queryButton)
            flow.Controls.Add(_moreButton)
            AddField(flow, "일자", _datePicker)
            flow.Controls.Add(_dateButton)
            flow.Controls.Add(_backtestButton)
            flow.Controls.Add(_status)
        End Sub

        Public Sub Initialize(symbol As String, symbolName As String, interval As CandleInterval,
                              totalCount As Integer, visibleCount As Integer)
            _updatingValues = True
            _symbol.Text = symbol
            _symbolName.Text = symbolName
            _totalCount.Value = Math.Max(_totalCount.Minimum, Math.Min(_totalCount.Maximum, totalCount))
            _moreBatchCount = CInt(_totalCount.Value)
            _visibleCount.Value = Math.Max(_visibleCount.Minimum, Math.Min(_visibleCount.Maximum, visibleCount))
            For i = 0 To _timeframe.Items.Count - 1
                Dim item = DirectCast(_timeframe.Items(i), TimeframeOption)
                If item.Interval = interval Then _timeframe.SelectedIndex = i : Exit For
            Next
            _updatingValues = False
        End Sub

        Public ReadOnly Property VisibleCount As Integer
            Get
                Return CInt(_visibleCount.Value)
            End Get
        End Property

        Public Sub SetVisibleCount(value As Integer)
            Dim bounded = Math.Max(CInt(_visibleCount.Minimum), Math.Min(CInt(_visibleCount.Maximum), value))
            If CInt(_visibleCount.Value) = bounded Then Return
            _updatingValues = True
            Try
                _visibleCount.Value = bounded
            Finally
                _updatingValues = False
            End Try
        End Sub

        Public Sub SetTotalCount(value As Integer)
            Dim bounded = Math.Max(CInt(_totalCount.Minimum), Math.Min(CInt(_totalCount.Maximum), value))
            If CInt(_totalCount.Value) = bounded Then Return
            _updatingValues = True
            Try
                _totalCount.Value = bounded
            Finally
                _updatingValues = False
            End Try
        End Sub

        Private Sub OnVisibleCountValueChanged(sender As Object, e As EventArgs)
            If _updatingValues Then Return
            RaiseEvent VisibleCountChanged(Me, EventArgs.Empty)
        End Sub

        Public Sub SetBusy(isBusy As Boolean)
            _queryButton.Enabled = Not isBusy
            _moreButton.Enabled = Not isBusy
            _dateButton.Enabled = Not isBusy
            _backtestButton.Enabled = Not isBusy
            _queryButton.Text = If(isBusy, "조회중", "조회")
        End Sub

        Public Sub SetStatus(text As String, Optional isError As Boolean = False)
            _status.Text = text
            _status.ForeColor = If(isError, Color.OrangeRed, Color.White)
        End Sub

        Public Sub SetSymbolName(symbolName As String)
            _symbolName.Text = If(symbolName, "")
        End Sub

        Public Sub SetDateRange(firstDate As Date?, lastDate As Date?)
            If Not firstDate.HasValue OrElse Not lastDate.HasValue Then Return
            Dim minDate = firstDate.Value.Date
            Dim maxDate = lastDate.Value.Date
            If minDate > maxDate Then
                Dim swap = minDate : minDate = maxDate : maxDate = swap
            End If
            '' 기존 Value가 새 범위 밖에 있어도 속성 설정 순서 때문에 예외가 나지 않게 초기화한다.
            _datePicker.MinDate = DateTimePicker.MinimumDateTime
            _datePicker.MaxDate = DateTimePicker.MaximumDateTime
            _datePicker.MinDate = minDate
            _datePicker.MaxDate = maxDate
            _datePicker.Value = maxDate
        End Sub

        Private Sub OnQueryClick(sender As Object, e As EventArgs)
            Dim code = _symbol.Text.Trim()
            If code.Length = 0 Then
                SetStatus("종목코드를 입력하세요.", True)
                _symbol.Focus()
                Return
            End If
            Dim optionItem = TryCast(_timeframe.SelectedItem, TimeframeOption)
            If optionItem Is Nothing Then Return
            _moreBatchCount = CInt(_totalCount.Value)
            RaiseEvent QueryRequested(Me, New ChartQueryEventArgs(
                code, _symbolName.Text.Trim(), optionItem.Interval,
                CInt(_totalCount.Value), CInt(_visibleCount.Value)))
        End Sub

        Private Sub OnMoreClick(sender As Object, e As EventArgs)
            Dim code = _symbol.Text.Trim()
            Dim optionItem = TryCast(_timeframe.SelectedItem, TimeframeOption)
            If code.Length = 0 OrElse optionItem Is Nothing Then Return
            RaiseEvent MoreRequested(Me, New ChartQueryEventArgs(
                code, _symbolName.Text.Trim(), optionItem.Interval,
                _moreBatchCount, CInt(_visibleCount.Value)))
        End Sub

        Private Sub OnDateClick(sender As Object, e As EventArgs)
            RaiseEvent DateRequested(Me, New ChartDateEventArgs(_datePicker.Value))
        End Sub

        Private Shared Function Opt(caption As String, interval As CandleInterval) As TimeframeOption
            Return New TimeframeOption With {.Caption = caption, .Interval = interval}
        End Function

        Private Shared Sub ConfigureTextBox(box As TextBox, width As Integer)
            box.Width = width
            box.BorderStyle = BorderStyle.FixedSingle
            ConfigureDarkInput(box)
        End Sub

        Private Shared Sub ConfigureNumber(box As NumericUpDown, min As Decimal, max As Decimal,
                                           value As Decimal, width As Integer)
            box.Minimum = min : box.Maximum = max : box.Value = value : box.Width = width
            box.TextAlign = HorizontalAlignment.Right
            ConfigureDarkInput(box)
        End Sub

        Private Shared Sub ConfigureButton(button As Button, text As String, width As Integer)
            button.Text = text : button.Width = width : button.Height = 25
            button.Margin = New Padding(3, 0, 6, 0)
            button.BackColor = Color.Black
            button.ForeColor = Color.White
            button.FlatStyle = FlatStyle.Flat
            button.FlatAppearance.BorderColor = Color.DimGray
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 35, 35)
        End Sub

        Private Shared Sub AddField(flow As FlowLayoutPanel, caption As String, control As Control)
            Dim label As New Label() With {
                .Text = caption, .AutoSize = True, .ForeColor = Color.White,
                .Padding = New Padding(5, 5, 2, 0), .Margin = New Padding(0)}
            control.Height = 25
            control.Margin = New Padding(0, 0, 5, 0)
            flow.Controls.Add(label)
            flow.Controls.Add(control)
        End Sub

        Private Shared Sub ConfigureDarkInput(control As Control)
            control.BackColor = Color.Black
            control.ForeColor = Color.White
        End Sub
    End Class
End Namespace
