Imports System.Windows.Forms
Imports System.Drawing

Namespace UI
    '' 모든 신호 속성을 하나의 PropertyGrid 로 편집하는 다이얼로그
    Public Class SignalPropertyDialog
        Inherits Form

        Private ReadOnly _grid As PropertyGrid
        Public Property Rule As ChartKit.Core.SignalRule

        Public Sub New(rule As ChartKit.Core.SignalRule)
            Me.Rule = rule
            Me.Text = "신호 검색 설정 (속성)"
            Me.Width = 380
            Me.Height = 460
            Me.StartPosition = FormStartPosition.CenterParent
            Me.MinimizeBox = False
            Me.MaximizeBox = False
            Me.FormBorderStyle = FormBorderStyle.FixedDialog

            _grid = New PropertyGrid With {
                .Dock = DockStyle.Fill,
                .SelectedObject = Me.Rule,
                .PropertySort = PropertySort.Categorized,
                .ToolbarVisible = False
            }

            Dim pnl As New Panel With {.Dock = DockStyle.Bottom, .Height = 44}
            Dim btnOk As New Button With {.Text = "확인", .DialogResult = DialogResult.OK, .Width = 90, .Top = 8, .Left = 180}
            Dim btnCancel As New Button With {.Text = "취소", .DialogResult = DialogResult.Cancel, .Width = 90, .Top = 8, .Left = 278}
            pnl.Controls.Add(btnOk)
            pnl.Controls.Add(btnCancel)

            Me.Controls.Add(_grid)
            Me.Controls.Add(pnl)
            Me.AcceptButton = btnOk
            Me.CancelButton = btnCancel
        End Sub
    End Class
End Namespace