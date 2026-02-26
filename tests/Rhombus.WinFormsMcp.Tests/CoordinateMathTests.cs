using C5T8fBtWY.WinFormsMcp.Server.Utilities;

namespace C5T8fBtWY.WinFormsMcp.Tests;

/// <summary>
/// Unit tests for CoordinateMath utility class.
/// </summary>
public class CoordinateMathTests
{
    [Test]
    public void PixelToHimetric_At96Dpi_ConvertsCorrectly()
    {
        // At 96 DPI: 1 pixel = 2540/96 ≈ 26.458 HIMETRIC
        var (hx, hy) = CoordinateMath.PixelToHimetric(100, 200, 96, 96);

        // Expected: 100 * 2540 / 96 = 2645
        // Expected: 200 * 2540 / 96 = 5291
        Assert.That(hx, Is.EqualTo(2645).Within(1));
        Assert.That(hy, Is.EqualTo(5291).Within(1));
    }

    [Test]
    public void PixelToHimetric_At144Dpi_ConvertsCorrectly()
    {
        // At 144 DPI (150% scaling): 1 pixel = 2540/144 ≈ 17.64 HIMETRIC
        var (hx, hy) = CoordinateMath.PixelToHimetric(100, 200, 144, 144);

        // Expected: 100 * 2540 / 144 ≈ 1763
        // Expected: 200 * 2540 / 144 ≈ 3527
        Assert.That(hx, Is.EqualTo(1763).Within(1));
        Assert.That(hy, Is.EqualTo(3527).Within(1));
    }

    [Test]
    public void HimetricToPixel_At96Dpi_ConvertsCorrectly()
    {
        var (px, py) = CoordinateMath.HimetricToPixel(2540, 5080, 96, 96);

        // Expected: 2540 * 96 / 2540 = 96
        // Expected: 5080 * 96 / 2540 = 192
        Assert.That(px, Is.EqualTo(96));
        Assert.That(py, Is.EqualTo(192));
    }

    [Test]
    public void PixelToHimetric_And_HimetricToPixel_AreReversible()
    {
        var (hx, hy) = CoordinateMath.PixelToHimetric(150, 250, 96, 96);
        var (px, py) = CoordinateMath.HimetricToPixel(hx, hy, 96, 96);

        Assert.That(px, Is.EqualTo(150).Within(1));
        Assert.That(py, Is.EqualTo(250).Within(1));
    }

    [Test]
    public void PixelToHimetric_HandlesZeroDpi_UsesStandard()
    {
        // Should fall back to 96 DPI and not throw
        var (hx, hy) = CoordinateMath.PixelToHimetric(100, 100, 0, 0);
        Assert.That(hx, Is.GreaterThan(0));
        Assert.That(hy, Is.GreaterThan(0));
    }

    [Test]
    public void WindowToScreen_ConvertsCorrectly()
    {
        var (sx, sy) = CoordinateMath.WindowToScreen(50, 75, 100, 200);
        Assert.That(sx, Is.EqualTo(150)); // 100 + 50
        Assert.That(sy, Is.EqualTo(275)); // 200 + 75
    }

    [Test]
    public void ScreenToWindow_ConvertsCorrectly()
    {
        var (wx, wy) = CoordinateMath.ScreenToWindow(150, 275, 100, 200);
        Assert.That(wx, Is.EqualTo(50));
        Assert.That(wy, Is.EqualTo(75));
    }

    [Test]
    public void WindowToScreen_And_ScreenToWindow_AreReversible()
    {
        var (sx, sy) = CoordinateMath.WindowToScreen(30, 40, 500, 300);
        var (wx, wy) = CoordinateMath.ScreenToWindow(sx, sy, 500, 300);

        Assert.That(wx, Is.EqualTo(30));
        Assert.That(wy, Is.EqualTo(40));
    }

    [Test]
    public void GetScaleFactor_At96Dpi_Returns1()
    {
        var scale = CoordinateMath.GetScaleFactor(96);
        Assert.That(scale, Is.EqualTo(1.0));
    }

    [Test]
    public void GetScaleFactor_At144Dpi_Returns1_5()
    {
        var scale = CoordinateMath.GetScaleFactor(144);
        Assert.That(scale, Is.EqualTo(1.5));
    }

    [Test]
    public void GetScaleFactor_At192Dpi_Returns2()
    {
        var scale = CoordinateMath.GetScaleFactor(192);
        Assert.That(scale, Is.EqualTo(2.0));
    }

    [Test]
    public void GetScaleFactor_HandlesZeroDpi_Returns1()
    {
        var scale = CoordinateMath.GetScaleFactor(0);
        Assert.That(scale, Is.EqualTo(1.0));
    }

    [Test]
    public void ScaleForDpi_At144Dpi_ScalesCorrectly()
    {
        var scaled = CoordinateMath.ScaleForDpi(100, 144);
        Assert.That(scaled, Is.EqualTo(150)); // 100 * 1.5
    }

    [Test]
    public void Lerp_At0_ReturnsStart()
    {
        var result = CoordinateMath.Lerp(0, 100, 0.0);
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void Lerp_At1_ReturnsEnd()
    {
        var result = CoordinateMath.Lerp(0, 100, 1.0);
        Assert.That(result, Is.EqualTo(100));
    }

    [Test]
    public void Lerp_AtHalf_ReturnsMidpoint()
    {
        var result = CoordinateMath.Lerp(0, 100, 0.5);
        Assert.That(result, Is.EqualTo(50));
    }

    [Test]
    public void Distance_BetweenPoints_CalculatesCorrectly()
    {
        // 3-4-5 triangle
        var dist = CoordinateMath.Distance(0, 0, 3, 4);
        Assert.That(dist, Is.EqualTo(5.0).Within(0.001));
    }

    [Test]
    public void Distance_SamePoint_ReturnsZero()
    {
        var dist = CoordinateMath.Distance(50, 50, 50, 50);
        Assert.That(dist, Is.EqualTo(0.0));
    }

    [Test]
    public void Clamp_ValueInRange_ReturnsValue()
    {
        Assert.That(CoordinateMath.Clamp(50, 0, 100), Is.EqualTo(50));
    }

    [Test]
    public void Clamp_ValueBelowMin_ReturnsMin()
    {
        Assert.That(CoordinateMath.Clamp(-10, 0, 100), Is.EqualTo(0));
    }

    [Test]
    public void Clamp_ValueAboveMax_ReturnsMax()
    {
        Assert.That(CoordinateMath.Clamp(150, 0, 100), Is.EqualTo(100));
    }
}
