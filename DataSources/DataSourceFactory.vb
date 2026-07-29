Imports ChartKit.Abstractions

Namespace DataSources

    Public Enum DataSourceKind
        Random = 0
        KiwoomRest = 1
        Server32 = 2
        Database = 3
    End Enum

    '' 데이터소스 선택/생성 지점. 차트와 무관.
    Public Module DataSourceFactory
        Public Function Create(kind As DataSourceKind) As ICandleDataSource
            Select Case kind
                Case DataSourceKind.Random
                    Return New RandomDataSource(42)
                Case DataSourceKind.KiwoomRest
                    Throw New NotImplementedException("키움 REST 데이터소스는 아직 구현되지 않았습니다.")
                Case DataSourceKind.Server32
                    Throw New NotImplementedException("Server32 데이터소스는 아직 구현되지 않았습니다.")
                Case DataSourceKind.Database
                    Throw New NotImplementedException("DB 데이터소스는 아직 구현되지 않았습니다.")
                Case Else
                    Return New RandomDataSource(42)
            End Select
        End Function
    End Module

End Namespace