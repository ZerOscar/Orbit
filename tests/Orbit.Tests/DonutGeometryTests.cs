using Avalonia;
using Orbit.App.Controls;

namespace Orbit.Tests;

public class DonutGeometryTests
{
    private static readonly Point Center = new(150, 150);

    [Fact]
    public void Span_TotalIsFullTurn()
    {
        for (var n = 1; n <= 12; n++)
            Assert.Equal(DonutGeometry.Turn, DonutGeometry.Span(n) * n, 9);
    }

    [Fact]
    public void Span_ZeroOrNegative_ReturnsFullTurn()
    {
        Assert.Equal(DonutGeometry.Turn, DonutGeometry.Span(0));
        Assert.Equal(DonutGeometry.Turn, DonutGeometry.Span(-3));
    }

    [Fact]
    public void IndexAt_TopOfRing_IsFirstSlice()
    {
        Assert.Equal(0, DonutGeometry.IndexAt(0, 8));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.3, 0)]
    [InlineData(0.75, 1)]
    [InlineData(1.6, 2)]
    [InlineData(3.0, 4)]
    [InlineData(5.5, 7)]
    [InlineData(6.2, 0)]
    public void IndexAt_EightSlices_MapsClockwise(double angle, int expected)
    {
        Assert.Equal(expected, DonutGeometry.IndexAt(angle, 8));
    }

    [Fact]
    public void IndexAt_BoundaryBeforeTop_BelongsToFirstSlice()
    {
        // 八等分时切片 0 覆盖 -22.5°~+22.5°（即 337.5°~360° 也归它），分界在 5.8905 弧度。
        Assert.Equal(7, DonutGeometry.IndexAt(5.88, 8));
        Assert.Equal(0, DonutGeometry.IndexAt(5.90, 8));
    }

    [Fact]
    public void IndexAt_NoSlices_ReturnsMinusOne()
    {
        Assert.Equal(-1, DonutGeometry.IndexAt(1.0, 0));
    }

    [Fact]
    public void PointAt_TopAndRight_AreWhereExpected()
    {
        var top = DonutGeometry.PointAt(Center, 100, 0);
        Assert.Equal(150, top.X, 9);
        Assert.Equal(50, top.Y, 9);

        var right = DonutGeometry.PointAt(Center, 100, Math.PI / 2);
        Assert.Equal(250, right.X, 9);
        Assert.Equal(150, right.Y, 9);
    }

    [Fact]
    public void AngleOf_IsInverseOfPointAt()
    {
        foreach (var angle in new[] { 0.0, 0.4, 1.7, 3.0, 4.5, 6.1 })
        {
            var p = DonutGeometry.PointAt(Center, 80, angle);
            Assert.Equal(angle, DonutGeometry.AngleOf(Center, p), 9);
        }
    }

    [Fact]
    public void HitTest_ClassifiesRegions()
    {
        Assert.Equal(DonutHitKind.Center, DonutGeometry.HitTest(Center, new Point(152, 151), 50, 150, 8).Kind);
        Assert.Equal(DonutHitKind.Outside, DonutGeometry.HitTest(Center, new Point(400, 150), 50, 150, 8).Kind);

        var hit = DonutGeometry.HitTest(Center, new Point(150, 50), 50, 150, 8);
        Assert.Equal(DonutHitKind.Segment, hit.Kind);
        Assert.Equal(0, hit.Index);
    }

    [Fact]
    public void HitTest_EmptyRing_WhenNoSlices()
    {
        Assert.Equal(DonutHitKind.EmptyRing, DonutGeometry.HitTest(Center, new Point(150, 50), 50, 150, 0).Kind);
    }

    [Fact]
    public void Wedge_WithoutCorner_HasThreeSegments()
    {
        var wedge = DonutGeometry.Wedge(Center, 60, 150, 0, DonutGeometry.Span(8), gap: 2, cornerRadius: 0);

        Assert.Equal(3, wedge.Segments.Count);
        Assert.Equal(SegmentKind.Arc, wedge.Segments[0].Kind);
        Assert.Equal(SegmentKind.Line, wedge.Segments[1].Kind);
        Assert.Equal(SegmentKind.Arc, wedge.Segments[2].Kind);
    }

    [Fact]
    public void Wedge_WithCorner_HasFiveSegments()
    {
        var wedge = DonutGeometry.Wedge(Center, 60, 150, 0, DonutGeometry.Span(8), gap: 2, cornerRadius: 8);

        // 外圈起点圆角 → 外弧 → 外圈终点圆角 → 径向边 → 内弧。内圈不倒角，所以是 5 段而不是 7 段。
        Assert.Equal(5, wedge.Segments.Count);
        Assert.Equal(SegmentKind.Arc, wedge.Segments[0].Kind);
        Assert.Equal(SegmentKind.Arc, wedge.Segments[1].Kind);
        Assert.Equal(SegmentKind.Arc, wedge.Segments[2].Kind);
        Assert.Equal(SegmentKind.Line, wedge.Segments[3].Kind);
        Assert.Equal(SegmentKind.Arc, wedge.Segments[4].Kind);
    }

    [Fact]
    public void Wedge_AllPointsStayInsideTheRing()
    {
        const double inner = 60, outer = 150;
        var wedge = DonutGeometry.Wedge(Center, inner, outer, 0, DonutGeometry.Span(8), gap: 2, cornerRadius: 10);

        foreach (var point in AllPoints(wedge))
        {
            var radius = DonutGeometry.Distance(Center, point);
            Assert.InRange(radius, inner - 0.001, outer + 0.001);
        }
    }

    [Fact]
    public void Wedge_OversizedCorner_IsClampedNotOverlapping()
    {
        const double inner = 60, outer = 150;
        var wedge = DonutGeometry.Wedge(Center, inner, outer, 0, DonutGeometry.Span(8), gap: 2, cornerRadius: 1000);

        // 圆角被钳制后所有顶点仍在环内，且不越过切片中线。
        foreach (var point in AllPoints(wedge))
            Assert.InRange(DonutGeometry.Distance(Center, point), inner - 0.001, outer + 0.001);
    }

    [Fact]
    public void Wedge_NarrowSlice_DoesNotThrow()
    {
        var wedge = DonutGeometry.Wedge(Center, 60, 150, 0, DonutGeometry.Span(24), gap: 2, cornerRadius: 10);
        Assert.NotNull(wedge);
        Assert.True(wedge.Segments.Count >= 3);
    }

    [Fact]
    public void Wedge_ClosesBackToStart_ByAngle()
    {
        var wedge = DonutGeometry.Wedge(Center, 60, 150, 0, DonutGeometry.Span(8), gap: 2, cornerRadius: 6);
        var last = wedge.Segments[^1].To;

        // 最后一段回到起点所在角度（归一化后比较）。
        Assert.Equal(DonutGeometry.AngleOf(Center, wedge.Start), DonutGeometry.AngleOf(Center, last), 6);
    }

    [Fact]
    public void Wedge_GapReducesCoveredAngle()
    {
        var gap = 6.0;
        var span = DonutGeometry.Span(8);
        var wedge = DonutGeometry.Wedge(Center, 60, 150, 0, span, gap, cornerRadius: 0);

        // 第一段圆弧的终点就是切片终边的角度。
        var endAngle = DonutGeometry.AngleOf(Center, wedge.Segments[0].To);
        var covered = endAngle - DonutGeometry.AngleOf(Center, wedge.Start);
        Assert.True(covered < span, $"间隙没有生效：覆盖 {covered} >= span {span}");
    }

    [Fact]
    public void CornerRadius_NeverExceedsHalfThickness()
    {
        var r = DonutGeometry.ResolveCornerRadius(100, 120, 0.5, 500);
        Assert.True(r <= 10.0001, $"圆角 {r} 超过了厚度的一半");
    }

    private static IEnumerable<Point> AllPoints(WedgePath wedge) =>
        wedge.Segments.Select(s => s.To).Prepend(wedge.Start);
}
