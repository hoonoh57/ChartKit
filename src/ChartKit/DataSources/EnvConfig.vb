Imports System.IO

Namespace DataSources

    '' 의존성 없는 .env 파서 + 키움 설정 홀더.
    '' 탐색 순서: 실행폴더 -> 상위 -> 상위상위 의 .env
    Public Class EnvConfig
        Private Shared ReadOnly _map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Private Shared _loaded As Boolean = False

        '' .env 탐색: (1) 환경변수 CHARTKIT_ENV_PATH  (2) 고정 후보경로들
        ''            (3) 실행폴더에서 상위로 5단계
        Private Shared ReadOnly _candidatePaths As String() = {}

        Public Shared Sub EnsureLoaded()
            If _loaded Then Return
            _loaded = True

            '' (1) 환경변수로 명시 지정 시 최우선
            Dim envOverride = Environment.GetEnvironmentVariable("CHARTKIT_ENV_PATH")
            If Not String.IsNullOrEmpty(envOverride) AndAlso File.Exists(envOverride) Then
                LoadFile(envOverride)
                _sourcePath = envOverride
                Return
            End If

            '' (2) 고정 후보경로
            For Each cand In _candidatePaths
                If File.Exists(cand) Then
                    LoadFile(cand)
                    _sourcePath = cand
                    Return
                End If
            Next

            '' (3) 실행폴더 -> 상위 5단계
            Dim dir = AppDomain.CurrentDomain.BaseDirectory
            For hop = 0 To 5
                Dim p = Path.Combine(dir, ".env")
                If File.Exists(p) Then
                    LoadFile(p)
                    _sourcePath = p
                    Return
                End If
                Dim parent = Directory.GetParent(dir)
                If parent Is Nothing Then Exit For
                dir = parent.FullName
            Next
        End Sub

        '' 어느 .env 를 읽었는지 (진단용)
        Private Shared _sourcePath As String = "(not found)"
        Public Shared ReadOnly Property SourcePath As String
            Get
                EnsureLoaded()
                Return _sourcePath
            End Get
        End Property

        Private Shared Sub LoadFile(path As String)
            For Each raw In File.ReadAllLines(path)
                Dim line = raw.Trim().TrimStart(ChrW(&HFEFF))
                If line.Length = 0 OrElse line.StartsWith("#") Then Continue For
                Dim eq = line.IndexOf("="c)
                If eq <= 0 Then Continue For
                Dim k = line.Substring(0, eq).Trim()
                Dim v = line.Substring(eq + 1).Trim()
                If v.Length >= 2 AndAlso ((v.StartsWith("""") AndAlso v.EndsWith("""")) OrElse (v.StartsWith("'") AndAlso v.EndsWith("'"))) Then
                    v = v.Substring(1, v.Length - 2)
                End If
                _map(k) = v
            Next
        End Sub

        Public Shared Function [Get](key As String, Optional dflt As String = "") As String
            EnsureLoaded()
            Dim v As String = Nothing
            If _map.TryGetValue(key, v) AndAlso Not String.IsNullOrEmpty(v) Then Return v
            Dim env = Environment.GetEnvironmentVariable(key)
            If Not String.IsNullOrEmpty(env) Then Return env
            Return dflt
        End Function

        '' 전용키가 비어 있으면 공용키로 폴백. VB 2항 If 는 Nothing 만 판정하므로 별도 함수가 필요하다.
        Friend Shared Function Pick(specific As String, common As String) As String
            If Not String.IsNullOrWhiteSpace(specific) Then Return specific.Trim()
            If common Is Nothing Then Return ""
            Return common.Trim()
        End Function

        Public Shared Function GetBool(key As String, Optional dflt As Boolean = False) As Boolean
            Dim v = [Get](key, "").Trim().ToLowerInvariant()
            If v = "" Then Return dflt
            Return (v = "1" OrElse v = "true" OrElse v = "yes" OrElse v = "y" OrElse v = "on")
        End Function

        '' 모의/실 분기된 최종 appkey/secretkey (core.py Config 동일 로직)
        Public Shared ReadOnly Property IsMock As Boolean
            Get
                Return GetBool("KIWOOM_MOCK", False)
            End Get
        End Property

        Public Shared ReadOnly Property AppKey As String
            Get
                Dim common = [Get]("KIWOOM_APP_KEY", "")
                If IsMock Then Return Pick([Get]("KIWOOM_MOCK_APP_KEY", ""), common)
                Return Pick([Get]("KIWOOM_REAL_APP_KEY", ""), common)
            End Get
        End Property

        Public Shared ReadOnly Property SecretKey As String
            Get
                Dim common = [Get]("KIWOOM_SECRET_KEY", "")
                If IsMock Then Return Pick([Get]("KIWOOM_MOCK_SECRET_KEY", ""), common)
                Return Pick([Get]("KIWOOM_REAL_SECRET_KEY", ""), common)
            End Get
        End Property

        Public Shared ReadOnly Property RestHost As String
            Get
                Return If(IsMock, "https://mockapi.kiwoom.com", "https://api.kiwoom.com")
            End Get
        End Property

        Public Shared ReadOnly Property AdjustPrice As String
            Get
                Return [Get]("KIWOOM_ADJUST_PRICE", "1")
            End Get
        End Property

        Public Shared ReadOnly Property DefaultSymbol As String
            Get
                Return [Get]("DEFAULT_SYMBOL", "005930")
            End Get
        End Property
    End Class

End Namespace