Imports SkiaSharp

Namespace Core
    '' 좌표 변환 전담. 원본 변환 공식을 그대로 이전.
    Public Class CoordinateMapper
        Private ReadOnly _main As SKRect
        Private ReadOnly _volume As SKRect
        Private ReadOnly _startIndex As Integer
        Private ReadOnly _candleWidth As Single
        Private ReadOnly _gap As Single
        Private ReadOnly _priceHigh As Single
        Private ReadOnly _priceLow As Single
        Private ReadOnly _volumeMax As Long

        Public Sub New(main As SKRect, volume As SKRect, startIndex As Integer,
                       candleWidth As Single, gap As Single,
                       priceHigh As Single, priceLow As Single, volumeMax As Long)
            _main = main
            _volume = volume
            _startIndex = startIndex
            _candleWidth = candleWidth
            _gap = gap
            _priceHigh = priceHigh
            _priceLow = priceLow
            _volumeMax = volumeMax
        End Sub

        Public Function IndexToX(i As Integer) As Single
            Return _main.Left + (i - _startIndex) * (_candleWidth + _gap) + _candleWidth / 2
        End Function

        Public Function PriceToY(price As Single) As Single
            If _priceHigh = _priceLow Then Return _main.MidY
            Return _main.Top + (_priceHigh - price) / (_priceHigh - _priceLow) * _main.Height
        End Function

        Public Function VolumeToY(vol As Long) As Single
            If _volumeMax = 0 Then Return _volume.Bottom
            Return _volume.Bottom - CSng(vol) / _volumeMax * _volume.Height
        End Function

        Public Function XToIndex(x As Single) As Integer
            Return _startIndex + CInt(Math.Floor((x - _main.Left) / (_candleWidth + _gap)))
        End Function

        '' ===== 원본 YToPrice 그대로 =====
        Public Function YToPrice(y As Single) As Single
            If _main.Height = 0 Then Return 0
            Return _priceHigh - (y - _main.Top) / _main.Height * (_priceHigh - _priceLow)
        End Function

        '' ===== 원본 YToVolume 그대로 =====
        Public Function YToVolume(y As Single) As Single
            If _volume.Height <= 0 Then Return 0
            Dim ratio = (_volume.Bottom - y) / _volume.Height
            ratio = Math.Max(0, Math.Min(1, ratio))
            Return CSng(_volumeMax * ratio)
        End Function
    End Class
End Namespace