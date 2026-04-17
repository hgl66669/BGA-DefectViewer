namespace BgaDefectViewer.Helpers;

public class CoordinateTransform
{
    private double _minX, _maxX, _minY, _maxY;
    private double _canvasWidth, _canvasHeight;
    private double _scale;
    private double _offsetX, _offsetY;
    private readonly double _margin = 10;

    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; } = 0;
    public double PanY { get; set; } = 0;

    public double MinX => _minX;
    public double MaxX => _maxX;
    public double MinY => _minY;
    public double MaxY => _maxY;

    /// <summary>目前有效縮放倍率（基礎比例 × Zoom），用於 LOD 判斷</summary>
    public double Scale => _scale * Zoom;

    public void SetBounds(double minX, double maxX, double minY, double maxY)
    {
        _minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY;
    }

    public void SetCanvasSize(double width, double height)
    {
        _canvasWidth = width; _canvasHeight = height;
        RecalcScale();
    }

    private void RecalcScale()
    {
        double dataW = _maxX - _minX;
        double dataH = _maxY - _minY;
        if (dataW <= 0 || dataH <= 0) return;

        double scaleX = (_canvasWidth - 2 * _margin) / dataW;
        double scaleY = (_canvasHeight - 2 * _margin) / dataH;
        _scale = Math.Min(scaleX, scaleY);
        _offsetX = (_canvasWidth - dataW * _scale) / 2.0;
        _offsetY = (_canvasHeight - dataH * _scale) / 2.0;
    }

    public (int px, int py) DataToScreen(double x, double y)
    {
        double sx = (x - _minX) * _scale * Zoom + _offsetX + PanX;
        double sy = (_maxY - y) * _scale * Zoom + _offsetY + PanY;
        return ((int)sx, (int)sy);
    }

    public (double x, double y) ScreenToData(double px, double py)
    {
        double x = (px - PanX - _offsetX) / (_scale * Zoom) + _minX;
        double y = _maxY - (py - PanY - _offsetY) / (_scale * Zoom);
        return (x, y);
    }

    public double BallRadiusPixels(double diameter)
    {
        return Math.Max(1, diameter * _scale * Zoom / 2.0);
    }

    /// <summary>Zoom=1, PanX=PanY=0 restores the auto-fit view.</summary>
    public void ResetToFit()
    {
        Zoom = 1.0;
        PanX = 0.0;
        PanY = 0.0;
    }

    /// <summary>Set zoom to targetZoom and pan so (dataX, dataY) appears at canvas center.</summary>
    public void CenterOn(double dataX, double dataY,
                         double canvasWidth, double canvasHeight,
                         double targetZoom)
    {
        Zoom = Math.Max(0.1, Math.Min(50, targetZoom));
        PanX = 0;
        PanY = 0;
        var (px, py) = DataToScreen(dataX, dataY); // Computed with new Zoom, PanX/Y=0
        PanX = canvasWidth  / 2.0 - px;
        PanY = canvasHeight / 2.0 - py;
    }
}
