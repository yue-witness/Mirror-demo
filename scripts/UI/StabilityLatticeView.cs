using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Code-native visualization of the shared Stability Lattice. Ordinary anchor
/// nodes occupy crystalline rings around the central keystone anchor. Active
/// nodes dim from the outside inward as the game advances, while a provisional
/// player request is previewed without changing the underlying rule state.
/// </summary>
public partial class StabilityLatticeView : Control
{
    private static readonly Color ActiveGreen = new("31d894");
    private static readonly Color ActiveMint = new("b9ffe3");
    private static readonly Color GhostGreen = new("8ab9aa40");
    private static readonly Color PreviewGold = new("ffcb55");
    private static readonly Color WarningRed = new("ff5268");
    private static readonly Color Ink = new("14362f");

    private int _initialAnchors = 20;
    private int _remainingAnchors = 20;
    private int? _previewRequest;
    private bool _requestLocked;
    private bool _limitMode;
    private bool _resultMode;
    private RoundOutcome _result;
    private double _pulsePhase;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _pulsePhase = (_pulsePhase + delta * 2.4) % (Math.PI * 2.0);
        QueueRedraw();
    }

    public void ShowState(
        int initialAnchors,
        int remainingAnchors,
        int? previewRequest,
        bool requestLocked,
        bool limitMode)
    {
        _initialAnchors = Math.Max(1, initialAnchors);
        _remainingAnchors = Math.Clamp(remainingAnchors, 0, _initialAnchors);
        _previewRequest = previewRequest;
        _requestLocked = requestLocked;
        _limitMode = limitMode;
        _resultMode = false;
        QueueRedraw();
    }

    public void ShowResult(RoundOutcome result)
    {
        _remainingAnchors = 0;
        _previewRequest = null;
        _requestLocked = true;
        _resultMode = true;
        _result = result;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X < 80.0f || Size.Y < 80.0f)
        {
            return;
        }

        Vector2 center = new(Size.X * 0.5f, Size.Y * 0.48f);
        float radius = MathF.Min(Size.X * 0.42f, Size.Y * 0.42f);
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_pulsePhase);

        DrawLatticeFrame(center, radius, pulse);

        int satelliteCount = Math.Max(0, _initialAnchors - 1);
        List<List<Vector2>> rings = BuildRings(center, radius, satelliteCount);
        var vertices = new List<Vector2>();

        // Rings are returned outside-in so disengaged nodes naturally recede
        // toward the central keystone as the active count falls.
        foreach (List<Vector2> ring in rings)
        {
            vertices.AddRange(ring);
            DrawRingEdges(ring, vertices.Count - ring.Count);
        }

        int removedSatellites = Math.Clamp(
            _initialAnchors - _remainingAnchors,
            0,
            satelliteCount);
        int previewCount = Math.Clamp(
            _previewRequest ?? 0,
            0,
            _remainingAnchors);

        for (int index = 0; index < vertices.Count; index++)
        {
            bool active = index >= removedSatellites;
            bool previewed = active
                && index < removedSatellites + previewCount;
            DrawAnchor(vertices[index], active, previewed, pulse);
        }

        bool keystoneActive = _remainingAnchors > 0;
        bool previewTouchesKeystone = keystoneActive
            && previewCount >= _remainingAnchors;
        DrawKeystone(center, keystoneActive, previewTouchesKeystone, pulse);
    }

    private void DrawLatticeFrame(Vector2 center, float radius, float pulse)
    {
        Color frame = ActiveGreen with { A = 0.11f + pulse * 0.05f };
        Vector2[] diamond =
        {
            center + new Vector2(0.0f, -radius),
            center + new Vector2(radius, 0.0f),
            center + new Vector2(0.0f, radius),
            center + new Vector2(-radius, 0.0f),
            center + new Vector2(0.0f, -radius)
        };

        DrawPolyline(diamond, frame, 1.5f, antialiased: true);
        DrawLine(
            center + new Vector2(-radius * 0.72f, 0.0f),
            center + new Vector2(radius * 0.72f, 0.0f),
            frame,
            1.0f,
            antialiased: true);
        DrawLine(
            center + new Vector2(0.0f, -radius * 0.72f),
            center + new Vector2(0.0f, radius * 0.72f),
            frame,
            1.0f,
            antialiased: true);
    }

    private List<List<Vector2>> BuildRings(
        Vector2 center,
        float radius,
        int satelliteCount)
    {
        var counts = new List<int>();
        var radiusFactors = new List<float>();

        if (satelliteCount <= 8)
        {
            counts.Add(satelliteCount);
            radiusFactors.Add(0.62f);
        }
        else if (satelliteCount <= 20)
        {
            int inner = Math.Clamp((int)MathF.Round(satelliteCount * 0.32f), 4, 6);
            counts.Add(satelliteCount - inner);
            counts.Add(inner);
            radiusFactors.Add(0.88f);
            radiusFactors.Add(0.46f);
        }
        else
        {
            counts.Add(satelliteCount - 16);
            counts.Add(10);
            counts.Add(6);
            radiusFactors.Add(0.92f);
            radiusFactors.Add(0.66f);
            radiusFactors.Add(0.38f);
        }

        var rings = new List<List<Vector2>>();

        for (int ringIndex = 0; ringIndex < counts.Count; ringIndex++)
        {
            int count = counts[ringIndex];
            var ring = new List<Vector2>(count);
            float offset = ringIndex % 2 == 0 ? -Mathf.Pi * 0.5f : 0.0f;

            for (int index = 0; index < count; index++)
            {
                float angle = offset + Mathf.Tau * index / Math.Max(1, count);
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
                ring.Add(center + direction * radius * radiusFactors[ringIndex]);
            }

            rings.Add(ring);
        }

        return rings;
    }

    private void DrawRingEdges(List<Vector2> ring, int globalStartIndex)
    {
        if (ring.Count < 2)
        {
            return;
        }

        int removedSatellites = Math.Clamp(
            _initialAnchors - _remainingAnchors,
            0,
            Math.Max(0, _initialAnchors - 1));

        for (int index = 0; index < ring.Count; index++)
        {
            int next = (index + 1) % ring.Count;
            bool active = globalStartIndex + index >= removedSatellites
                && globalStartIndex + next >= removedSatellites;
            Color edge = active
                ? ActiveGreen with { A = 0.46f }
                : GhostGreen with { A = 0.16f };
            DrawLine(ring[index], ring[next], edge, active ? 2.0f : 1.0f, true);
        }
    }

    private void DrawAnchor(Vector2 position, bool active, bool previewed, float pulse)
    {
        Color fill = active ? ActiveMint : GhostGreen;
        Color outline = active ? ActiveGreen : GhostGreen;

        if (previewed)
        {
            fill = _requestLocked
                ? WarningRed.Lerp(PreviewGold, 0.35f)
                : PreviewGold;
            outline = _requestLocked ? WarningRed : new Color("b57900");
        }

        float glowRadius = active ? 10.0f + pulse * 2.0f : 7.0f;
        DrawCircle(position, glowRadius, outline with { A = active ? 0.16f : 0.05f });

        float size = active ? 6.5f : 4.5f;
        Vector2[] diamond =
        {
            position + new Vector2(0.0f, -size),
            position + new Vector2(size, 0.0f),
            position + new Vector2(0.0f, size),
            position + new Vector2(-size, 0.0f)
        };
        DrawColoredPolygon(diamond, fill);
        DrawPolyline(
            new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] },
            outline,
            active ? 1.6f : 1.0f,
            true);
    }

    private void DrawKeystone(
        Vector2 center,
        bool active,
        bool previewed,
        float pulse)
    {
        Color accent = _resultMode
            ? _result == RoundOutcome.PlayerWin
                ? ActiveGreen
                : _result == RoundOutcome.Draw
                    ? PreviewGold
                    : WarningRed
            : previewed || _remainingAnchors <= 3
                ? WarningRed
                : ActiveGreen;

        if (!active && !_resultMode)
        {
            accent = GhostGreen;
        }

        float glow = 29.0f + pulse * 5.0f;
        DrawCircle(center, glow, accent with { A = 0.10f + pulse * 0.06f });
        DrawCircle(center, 21.0f, new Color("f8fffc"));
        DrawCircle(center, 21.0f, accent, filled: false, width: 3.0f, antialiased: true);

        float size = 13.0f;
        Vector2[] crystal =
        {
            center + new Vector2(0.0f, -size),
            center + new Vector2(size * 0.72f, 0.0f),
            center + new Vector2(0.0f, size),
            center + new Vector2(-size * 0.72f, 0.0f)
        };
        DrawColoredPolygon(crystal, accent.Lerp(Colors.White, 0.42f));
        DrawPolyline(
            new[] { crystal[0], crystal[1], crystal[2], crystal[3], crystal[0] },
            accent,
            2.0f,
            true);

        string label = _resultMode
            ? _result == RoundOutcome.PlayerWin
                ? "LATTICE CLEARED"
                : _result == RoundOutcome.Draw
                    ? "LATTICE BALANCED"
                    : "SYNC LOST"
            : "KEYSTONE";
        DrawString(
            ThemeDB.FallbackFont,
            center + new Vector2(-64.0f, 43.0f),
            label,
            HorizontalAlignment.Center,
            128.0f,
            12,
            _resultMode ? accent : Ink);
    }
}
