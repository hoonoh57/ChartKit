Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports ChartKit.Core.Backtesting
Imports ChartKit.DataSources

Namespace UI
    Public NotInheritable Class ClipboardCaptureForm
        Inherits Form

        Private ReadOnly _sourceText As New TextBox()
        Private ReadOnly _grid As New DataGridView()
        Private ReadOnly _paste As New Button With {.Text = "클립보드 붙여넣기"}
        Private ReadOnly _parse As New Button With {.Text = "파싱하기"}
        Private ReadOnly _apply As New Button With {.Text = "정상 종목 적용", .Enabled = False}
        Private ReadOnly _cancel As New Button With {.Text = "닫기"}
        Private ReadOnly _status As New Label With {.AutoSize = True}
        Private ReadOnly _resolver As New CybosSymbolNameResolver()
        Private _accepted As New List(Of CaptureRecord)()

        Public ReadOnly Property AcceptedRecords As IReadOnlyList(Of CaptureRecord)
            Get
                Return _accepted
            End Get
        End Property

        Public Sub New()
            Text = "키움 1516 클립보드 포착"
            Width = 1180
            Height = 760
            StartPosition = FormStartPosition.CenterParent
            BackColor = Color.FromArgb(20, 23, 29)
            ForeColor = Color.White
            BuildUi()
            PasteClipboard()
        End Sub

        Private Sub BuildUi()
            Dim toolbar As New FlowLayoutPanel With {
                .Dock = DockStyle.Top, .Height = 42, .BackColor = Color.Black,
                .Padding = New Padding(6), .WrapContents = False}
            toolbar.Controls.AddRange(New Control() {_paste, _parse, _apply, _cancel, _status})

            _sourceText.Dock = DockStyle.Top
            _sourceText.Height = 270
            _sourceText.Multiline = True
            _sourceText.ScrollBars = ScrollBars.Both
            _sourceText.WordWrap = False
            _sourceText.BackColor = Color.Black
            _sourceText.ForeColor = Color.White
            _sourceText.Font = New Font("Malgun Gothic", 9.0F)

            _grid.Dock = DockStyle.Fill
            _grid.ReadOnly = True
            _grid.AllowUserToAddRows = False
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            _grid.BackgroundColor = BackColor
            _grid.EnableHeadersVisualStyles = False
            _grid.DefaultCellStyle.BackColor = BackColor
            _grid.DefaultCellStyle.ForeColor = Color.White
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.Black
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White

            AddHandler _paste.Click, Sub(sender, e) PasteClipboard()
            AddHandler _parse.Click, Async Sub(sender, e) Await ParseAsync()
            AddHandler _apply.Click,
                Sub(sender, e)
                    DialogResult = DialogResult.OK
                    Close()
                End Sub
            AddHandler _cancel.Click, Sub(sender, e) Close()

            Controls.Add(_grid)
            Controls.Add(_sourceText)
            Controls.Add(toolbar)
        End Sub

        Private Sub PasteClipboard()
            Try
                If Clipboard.ContainsText() Then
                    _sourceText.Text = Clipboard.GetText(TextDataFormat.UnicodeText)
                    _status.Text = "클립보드 내용을 붙였습니다."
                Else
                    _status.Text = "클립보드에 텍스트가 없습니다."
                End If
            Catch ex As Exception
                _status.Text = "클립보드 읽기 실패: " & ex.Message
            End Try
        End Sub

        Private Async Function ParseAsync() As Task
            Dim rows = KiwoomPerformanceClipboardParser.Parse(_sourceText.Text)
            If rows.Count = 0 Then
                MessageBox.Show(Me,
                    "키움 성과검증 표의 종목 행을 찾지 못했습니다. 표를 다시 복사해 주세요.",
                    "클립보드 포착", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            SetBusy(True, $"{rows.Count}개 종목명을 server32에서 확인 중...")
            Try
                Dim viewRows As New List(Of Object)()
                Dim accepted As New List(Of CaptureRecord)()
                Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
                For index = 0 To rows.Count - 1
                    Dim row = rows(index)
                    _status.Text = $"{index + 1}/{rows.Count} {row.StockName} 확인 중..."
                    Dim resolution = Await _resolver.ResolveAsync(row.StockName)
                    If resolution.IsSuccess AndAlso seen.Add(resolution.Symbol) Then
                        accepted.Add(New CaptureRecord With {.Symbol = resolution.Symbol})
                    End If
                    viewRows.Add(New With {
                        .종목명 = row.StockName,
                        .종목코드 = resolution.Symbol,
                        .일분 = row.Return1Minute,
                        .삼분 = row.Return3Minutes,
                        .기간수익률 = row.ReturnPeriod,
                        .최고수익률 = row.MaximumReturn,
                        .거래량 = row.Volume,
                        .상태 = If(resolution.IsSuccess, "정상", resolution.ErrorMessage)})
                Next
                _accepted = accepted
                _grid.DataSource = viewRows
                _apply.Enabled = accepted.Count > 0
                _status.Text = $"파싱 {rows.Count}개 / 정상 {accepted.Count}개 / 제외 {rows.Count - accepted.Count}개"
            Finally
                SetBusy(False, _status.Text)
            End Try
        End Function

        Private Sub SetBusy(value As Boolean, message As String)
            _paste.Enabled = Not value
            _parse.Enabled = Not value
            _apply.Enabled = Not value AndAlso _accepted.Count > 0
            _status.Text = message
            UseWaitCursor = value
        End Sub
    End Class
End Namespace
