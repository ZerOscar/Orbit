// 环形菜单的纯数学层：切片角度、命中测试、圆角扇形轮廓。
// 角度以 12 点方向为 0，顺时针增大，单位弧度。切片 i 的中心在 i·span。

using Avalonia;

namespace Orbit.App.Controls;

public enum DonutHitKind
{
    /// <summary>圆环之外。</summary>
    Outside,
    /// <summary>中心孔洞（取消 / 关闭）。</summary>
    Center,
    /// <summary>某个切片，Index 有效。</summary>
    Segment,
    /// <summary>没有切片时的圆环本身。</summary>
    EmptyRing,
}

public readonly record struct DonutHit(DonutHitKind Kind, int Index = -1);

public enum SegmentKind
{
    Line,
    Arc,
}

/// <param name="Clockwise">圆弧方向，仅 Kind=Arc 有意义。</param>
public readonly record struct PathSegment(SegmentKind Kind, Point To, double Radius = 0, bool Clockwise = true, bool LargeArc = false);

/// <summary>一个闭合切片的轮廓：起点 + 依次的线段/圆弧，最后一段回到起点。</summary>
public sealed record WedgePath(Point Start, IReadOnlyList<PathSegment> Segments);

public static class DonutGeometry
{
    /// <summary>完整一圈。</summary>
    public const double Turn = Math.PI * 2;

    /// <summary>圆角小于此值（像素）看不出差别，直接画尖角。</summary>
    public const double MinCornerRadius = 0.25;

    public static double Span(int count) => count <= 0 ? Turn : Turn / count;

    public static double MidAngle(int index, int count) => index * Span(count);

    /// <summary>把（顺时针距顶角度, 半径）转成相对圆心的坐标。</summary>
    public static Point PointAt(Point center, double radius, double angle) =>
        new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

    /// <summary>点相对圆心的顺时针距顶角度，归一化到 0..2π。</summary>
    public static double AngleOf(Point center, Point p)
    {
        var angle = Math.Atan2(p.X - center.X, center.Y - p.Y);
        return angle < 0 ? angle + Turn : angle;
    }

    /// <summary>给定角度落在第几个切片。切片 i 覆盖 [i·span - span/2, i·span + span/2)，所以第 0 片正对上方。</summary>
    public static int IndexAt(double angle, int count)
    {
        if (count <= 0)
            return -1;
        var span = Span(count);
        return (int)Math.Floor((angle + span / 2) / span) % count;
    }

    public static DonutHit HitTest(Point center, Point p, double innerRadius, double outerRadius, int count)
    {
        var distance = Distance(center, p);
        if (distance < innerRadius)
            return new DonutHit(DonutHitKind.Center);
        if (distance > outerRadius)
            return new DonutHit(DonutHitKind.Outside);
        if (count == 0)
            return new DonutHit(DonutHitKind.EmptyRing);
        return new DonutHit(DonutHitKind.Segment, IndexAt(AngleOf(center, p), count));
    }

    /// <summary>
    /// 一个切片的轮廓：内外两段同心圆弧 + 两条径向边，四个角按 <paramref name="cornerRadius"/> 倒圆。
    /// <paramref name="gap"/> 是切片之间的间隙（像素），按中径换算成角度，所以内外间隙看起来一样宽。
    /// 圆角放不下时会自动收缩到能放下的最大值，因此调用方可以随便传。
    /// </summary>
    public static WedgePath Wedge(Point center, double innerRadius, double outerRadius, double mid, double span,
        double gap, double cornerRadius = 0)
    {
        var midRadius = (innerRadius + outerRadius) / 2;
        // 间隙按弧长折算成角度，保证内外两侧的实际间隙相同。
        var half = Math.Max(0.0005, span / 2 - gap / (2 * Math.Max(midRadius, 1)));
        var startAngle = mid - half;
        var endAngle = mid + half;

        var radius = ResolveCornerRadius(innerRadius, outerRadius, half, cornerRadius);
        if (radius <= MinCornerRadius)
        {
            return new WedgePath(PointAt(center, outerRadius, startAngle),
            [
                new PathSegment(SegmentKind.Arc, PointAt(center, outerRadius, endAngle), outerRadius, Clockwise: true,
                    LargeArc: half * 2 > Math.PI),
                new PathSegment(SegmentKind.Line, PointAt(center, innerRadius, endAngle)),
                new PathSegment(SegmentKind.Arc, PointAt(center, innerRadius, startAngle), innerRadius, Clockwise: false,
                    LargeArc: half * 2 > Math.PI),
            ]);
        }

        // 圆角的圆心必须同时与圆弧和径向边相切，于是它落在「半径 outer-r」的圆上，且离径向边 r。
        // 在半径 R 处，距离径向线 r 对应的角度是 asin(r/R)，这就是径向边上的切点相对拐角的角度偏移。
        var deltaOuter = Math.Asin(Math.Min(1, radius / (outerRadius - radius)));
        var outerArcFrom = startAngle + deltaOuter;
        var outerArcTo = endAngle - deltaOuter;

        // 只圆外圈的两个角：内圈留尖角，和外弧连起来才是干净的扇形轮廓。
        // 内外都倒圆的话，八个切片会在内圈排出一圈小圆点，像串珠。
        return new WedgePath(PointAt(center, outerRadius - radius, startAngle),
        [
            // 外圈起点侧圆角
            new PathSegment(SegmentKind.Arc, PointAt(center, outerRadius, outerArcFrom), radius, Clockwise: true),
            // 外弧
            new PathSegment(SegmentKind.Arc, PointAt(center, outerRadius, outerArcTo), outerRadius, Clockwise: true,
                LargeArc: outerArcTo - outerArcFrom > Math.PI),
            // 外圈终点侧圆角
            new PathSegment(SegmentKind.Arc, PointAt(center, outerRadius - radius, endAngle), radius, Clockwise: true),
            // 径向边（外侧 → 内侧）
            new PathSegment(SegmentKind.Line, PointAt(center, innerRadius, endAngle)),
            // 内弧（逆时针走回起点侧）
            new PathSegment(SegmentKind.Arc, PointAt(center, innerRadius, startAngle), innerRadius, Clockwise: false,
                LargeArc: endAngle - startAngle > Math.PI),
        ]);
    }

    /// <summary>
    /// 找出能同时满足「不超过切片一半厚度」和「不越过切片中线」的最大圆角，二分逼近。
    /// 直接取 cornerRadius 会让窄切片上的圆角互相穿透，必须钳制。
    /// </summary>
    internal static double ResolveCornerRadius(double innerRadius, double outerRadius, double halfSpan, double cornerRadius)
    {
        var limit = Math.Min(cornerRadius, (outerRadius - innerRadius) / 2);
        if (limit <= MinCornerRadius)
            return 0;
        if (Fits(limit))
            return limit;

        double low = 0, high = limit;
        for (var i = 0; i < 16; i++)
        {
            var mid = (low + high) / 2;
            if (Fits(mid))
                low = mid;
            else
                high = mid;
        }
        return low;

        // 只约束外圈：内圈是尖角，不需要为它留圆角的余量。
        bool Fits(double r) => Math.Asin(Math.Min(1, r / (outerRadius - r))) <= halfSpan;
    }

    public static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
