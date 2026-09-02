using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Code-native visualization of the shared Stability Lattice. Ordinary anchor
/// nodes orbit a generated energy-core texture on several inclined planes.
/// The central energy core is decorative and never contributes to the rule
/// count. Active orbiting nodes dim from the outside inward as the game
/// advances, while a provisional request previews the counted nodes only.
/// </summary>
public partial class StabilityLatticeView : Control
{
    public const string EnergyCoreTexturePath =
        "res://assets/vfx/lattice_energy_core_v2.png";

    private static readonly Color ActiveGreen = new("39ff3a");
    private static readonly Color ActiveMint = new("c5ffc6");
    private static readonly Color GhostGreen = new("74a97540");
    private static readonly Color PreviewGold = new("ffcb55");
    private static readonly Color TutorCyan = new("66f4ff");
    private static readonly Color WarningRed = new("ff5268");
    private static readonly Color OrbitCyan = new("66f4ff");

    private static readonly OrbitSpec[] OrbitSpecs =
    {
        new(0.94f, 0.35f, 0.18f, 0.16f, -0.45f),
        new(0.73f, 0.30f, -0.72f, -0.22f, 0.82f),
        new(0.51f, 0.23f, 1.03f, 0.31f, 2.10f)
    };

    private Texture2D _energyCoreTexture = null!;
    private int _initialAnchors = 20;
    private int _remainingAnchors = 20;
    private int? _previewRequest;
    private bool _requestLocked;
    private bool _limitMode;
    private bool _resultMode;
    private RoundOutcome _result;
    private double _elapsedSeconds;
    private TransientPresentation _transientPresentation;
    private int _tutorActionCount;
    private int _limitPlayerCount;
    private int _limitTutorCount;
    private float _transientProgress;
    private float _transientDuration = 1.0f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _energyCoreTexture = ResourceLoader.Load<Texture2D>(EnergyCoreTexturePath)
            ?? throw new InvalidOperationException(
                $"Missing Stability Lattice core texture: {EnergyCoreTexturePath}");
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _elapsedSeconds = (_elapsedSeconds + delta) % 3600.0;

        if (_transientPresentation is TransientPresentation.PlayerRemoval
            or TransientPresentation.TutorRemoval
            or TransientPresentation.LimitReveal)
        {
            _transientProgress = Math.Clamp(
                _transientProgress + (float)delta / _transientDuration,
                0.0f,
                1.0f);
        }

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
        ClearTransientPresentation();
        QueueRedraw();
    }

    /// <summary>
    /// Marks the anchors selected by the Tutor without mutating game state.
    /// The cyan treatment distinguishes the Tutor's intention from the
    /// player's gold provisional request.
    /// </summary>
    public void ShowTutorSelection(int choice)
    {
        _transientPresentation = TransientPresentation.TutorSelection;
        _tutorActionCount = Math.Clamp(choice, 0, _remainingAnchors);
        _limitPlayerCount = 0;
        _limitTutorCount = 0;
        _transientProgress = 0.0f;
        QueueRedraw();
    }

    /// <summary>
    /// Pulls the player's confirmed orbiting anchors into the decorative core
    /// before the rule state is mutated and the Tutor begins evaluating.
    /// </summary>
    public void AnimatePlayerRemoval(int choice, float durationSeconds)
    {
        _transientPresentation = TransientPresentation.PlayerRemoval;
        _limitPlayerCount = Math.Clamp(choice, 0, _remainingAnchors);
        _limitTutorCount = 0;
        _tutorActionCount = 0;
        _transientDuration = Math.Max(0.01f, durationSeconds);
        _transientProgress = 0.0f;
        QueueRedraw();
    }

    /// <summary>
    /// Pulls the Tutor-marked anchors toward the core while fading them out.
    /// The controller applies the rule state only after this visual completes.
    /// </summary>
    public void AnimateTutorRemoval(float durationSeconds)
    {
        if (_tutorActionCount <= 0)
        {
            return;
        }

        _transientPresentation = TransientPresentation.TutorRemoval;
        _transientDuration = Math.Max(0.01f, durationSeconds);
        _transientProgress = 0.0f;
        QueueRedraw();
    }

    /// <summary>
    /// Reveals both hidden Limit Bash requests in different colours, then
    /// fades the marked anchors together before the rule state is committed.
    /// </summary>
    public void ShowLimitReveal(
        int playerChoice,
        int tutorChoice,
        float durationSeconds)
    {
        _transientPresentation = TransientPresentation.LimitReveal;
        _limitTutorCount = Math.Clamp(
            tutorChoice,
            0,
            _remainingAnchors);
        _limitPlayerCount = Math.Clamp(
            playerChoice,
            0,
            Math.Max(0, _remainingAnchors - _limitTutorCount));
        _tutorActionCount = 0;
        _transientDuration = Math.Max(0.01f, durationSeconds);
        _transientProgress = 0.0f;
        QueueRedraw();
    }

    /// <summary>
    /// Reveals the Tutor's sealed request without moving either side's nodes.
    /// </summary>
    public void ShowLimitTutorSelection(int tutorChoice)
    {
        _transientPresentation = TransientPresentation.TutorSelection;
        _tutorActionCount = Math.Clamp(tutorChoice, 0, _remainingAnchors);
        _limitPlayerCount = 0;
        _limitTutorCount = 0;
        _transientProgress = 0.0f;
        QueueRedraw();
    }

    public int GetTutorMarkedAnchorCount()
    {
        return _transientPresentation == TransientPresentation.None
            ? 0
            : _tutorActionCount + _limitTutorCount;
    }

    public int GetPlayerRevealMarkedAnchorCount()
    {
        return _limitPlayerCount;
    }

    public int GetDisplayedOrbitingAnchorCount()
    {
        return _remainingAnchors;
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
        float radius = MathF.Min(Size.X * 0.43f, Size.Y * 0.43f);
        float pulse = 0.5f
            + 0.5f * Mathf.Sin((float)_elapsedSeconds * 2.15f);

        // Every counted anchor is now visible on an orbit. The core remains a
        // stable visual reference and is deliberately excluded from gameplay.
        int satelliteCount = Math.Max(0, _initialAnchors);
        int removedSatellites = Math.Clamp(
            _initialAnchors - _remainingAnchors,
            0,
            satelliteCount);
        int previewCount = Math.Clamp(
            _previewRequest ?? 0,
            0,
            _remainingAnchors);

        List<OrbitRing> rings = BuildOrbitRings(satelliteCount);
        DrawOrbitalField(center, radius, rings, pulse);

        int globalIndex = 0;
        foreach (OrbitRing ring in rings)
        {
            for (int index = 0; index < ring.Count; index++)
            {
                bool active = globalIndex >= removedSatellites;
                bool previewed = active
                    && globalIndex < removedSatellites + previewCount;
                int activeIndex = globalIndex - removedSatellites;
                AnchorMark transientMark = ResolveTransientMark(
                    active,
                    activeIndex);
                float transientAlpha = ResolveTransientAlpha(transientMark);
                float pullToCore = ResolvePullToCore(transientMark);
                DrawOrbitingAnchor(
                    center,
                    radius,
                    ring,
                    index,
                    globalIndex,
                    active,
                    previewed,
                    transientMark,
                    transientAlpha,
                    pullToCore,
                    pulse);
                globalIndex++;
            }
        }

        bool coreActive = !_resultMode;
        DrawEnergyCore(
            center,
            coreActive,
            previewed: false,
            AnchorMark.None,
            transientAlpha: 1.0f,
            pulse);
    }

    private void DrawOrbitalField(
        Vector2 center,
        float radius,
        IReadOnlyList<OrbitRing> rings,
        float pulse)
    {
        float fieldRadius = radius * (1.02f + pulse * 0.015f);
        Color field = ActiveGreen with { A = 0.055f + pulse * 0.025f };
        DrawCircle(
            center,
            fieldRadius,
            field,
            filled: false,
            width: 1.0f,
            antialiased: true);
        DrawCircle(
            center,
            fieldRadius * 0.82f,
            OrbitCyan with { A = 0.035f },
            filled: false,
            width: 1.0f,
            antialiased: true);

        for (int index = 0; index < rings.Count; index++)
        {
            DrawOrbitPath(center, radius, rings[index], index, pulse);
        }
    }

    private static List<OrbitRing> BuildOrbitRings(int satelliteCount)
    {
        var counts = new List<int>();

        if (satelliteCount <= 8)
        {
            counts.Add(satelliteCount);
        }
        else if (satelliteCount <= 14)
        {
            int outer = (int)MathF.Ceiling(satelliteCount * 0.56f);
            counts.Add(outer);
            counts.Add(satelliteCount - outer);
        }
        else
        {
            int outer = (int)MathF.Ceiling(satelliteCount * 0.42f);
            int middle = (int)MathF.Ceiling(satelliteCount * 0.33f);
            counts.Add(outer);
            counts.Add(middle);
            counts.Add(satelliteCount - outer - middle);
        }

        var rings = new List<OrbitRing>(counts.Count);
        for (int index = 0; index < counts.Count; index++)
        {
            OrbitSpec spec = OrbitSpecs[Math.Min(index, OrbitSpecs.Length - 1)];
            rings.Add(new OrbitRing(counts[index], spec));
        }

        return rings;
    }

    private void DrawOrbitPath(
        Vector2 center,
        float radius,
        OrbitRing ring,
        int ringIndex,
        float pulse)
    {
        const int SegmentCount = 72;
        var points = new Vector2[SegmentCount + 1];
        float precession = Mathf.Sin(
            (float)_elapsedSeconds * 0.08f + ringIndex) * 0.035f;

        for (int segment = 0; segment <= SegmentCount; segment++)
        {
            float angle = Mathf.Tau * segment / SegmentCount;
            points[segment] = CalculateOrbitPosition(
                center,
                radius,
                ring.Spec,
                angle,
                precession);
        }

        float baseAlpha = ringIndex == 0 ? 0.20f : 0.14f;
        Color pathColor = (_limitMode ? OrbitCyan : ActiveGreen) with
        {
            A = baseAlpha + pulse * 0.035f
        };
        DrawPolyline(points, pathColor, ringIndex == 0 ? 1.4f : 1.1f, true);

        Vector2 scanPosition = CalculateOrbitPosition(
            center,
            radius,
            ring.Spec,
            (float)_elapsedSeconds * ring.Spec.Speed + ring.Spec.Phase,
            precession);
        DrawCircle(scanPosition, 2.2f, OrbitCyan with { A = 0.5f });
    }

    private void DrawOrbitingAnchor(
        Vector2 center,
        float radius,
        OrbitRing ring,
        int ringIndex,
        int globalIndex,
        bool active,
        bool previewed,
        AnchorMark transientMark,
        float transientAlpha,
        float pullToCore,
        float pulse)
    {
        float baseAngle = Mathf.Tau * ringIndex / Math.Max(1, ring.Count)
            + ring.Spec.Phase;
        float angle = baseAngle
            + (float)_elapsedSeconds * ring.Spec.Speed;
        float precession = Mathf.Sin(
            (float)_elapsedSeconds * 0.08f + globalIndex * 0.31f) * 0.035f;
        Vector2 position = CalculateOrbitPosition(
            center,
            radius,
            ring.Spec,
            angle,
            precession);

        if (transientMark != AnchorMark.None && pullToCore > 0.0f)
        {
            position = position.Lerp(center, pullToCore);
        }

        Color trailColor = transientMark == AnchorMark.None
            ? ResolveAnchorOutline(active, previewed)
            : ResolveMarkColor(transientMark);
        Vector2 previous = position;
        for (int trailIndex = 1; trailIndex <= 4; trailIndex++)
        {
            float trailAngle = angle - ring.Spec.Speed * trailIndex * 0.19f;
            Vector2 trailPosition = CalculateOrbitPosition(
                center,
                radius,
                ring.Spec,
                trailAngle,
                precession);
            float alpha = (active ? 0.30f / trailIndex : 0.035f / trailIndex)
                * transientAlpha;
            DrawLine(
                previous,
                trailPosition,
                trailColor with { A = alpha },
                active ? 2.0f : 1.0f,
                true);
            previous = trailPosition;
        }

        float depth = 0.5f + 0.5f * Mathf.Sin(angle);
        DrawAnchor(
            position,
            active,
            previewed,
            transientMark,
            transientAlpha,
            pulse,
            depth);
    }

    private static Vector2 CalculateOrbitPosition(
        Vector2 center,
        float radius,
        OrbitSpec spec,
        float angle,
        float precession)
    {
        Vector2 ellipse = new(
            Mathf.Cos(angle) * radius * spec.RadiusXFactor,
            Mathf.Sin(angle) * radius * spec.RadiusYFactor);
        return center + ellipse.Rotated(spec.Tilt + precession);
    }

    private void DrawAnchor(
        Vector2 position,
        bool active,
        bool previewed,
        AnchorMark transientMark,
        float transientAlpha,
        float pulse,
        float depth)
    {
        Color fill = active ? ActiveMint : GhostGreen;
        Color outline = ResolveAnchorOutline(active, previewed);

        if (previewed)
        {
            fill = _requestLocked
                ? WarningRed.Lerp(PreviewGold, 0.35f)
                : PreviewGold;
        }

        if (transientMark != AnchorMark.None)
        {
            outline = ResolveMarkColor(transientMark);
            fill = outline.Lerp(Colors.White, 0.54f);
        }

        float depthScale = Mathf.Lerp(0.78f, 1.12f, depth);
        float glowRadius = (active ? 12.0f + pulse * 3.0f : 6.0f)
            * depthScale;
        DrawCircle(
            position,
            glowRadius,
            outline with
            {
                A = (active ? 0.30f : 0.045f) * transientAlpha
            });

        float size = (active ? 8.0f : 4.2f) * depthScale;
        Vector2[] diamond =
        {
            position + new Vector2(0.0f, -size),
            position + new Vector2(size, 0.0f),
            position + new Vector2(0.0f, size),
            position + new Vector2(-size, 0.0f)
        };
        DrawColoredPolygon(
            diamond,
            fill with { A = fill.A * transientAlpha });
        DrawPolyline(
            new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] },
            outline with { A = outline.A * transientAlpha },
            active ? 2.1f : 1.0f,
            true);
        if (active)
        {
            DrawCircle(
                position,
                2.0f * depthScale,
                Colors.White with { A = 0.9f * transientAlpha });
        }
    }

    private Color ResolveAnchorOutline(bool active, bool previewed)
    {
        if (previewed)
        {
            return _requestLocked ? WarningRed : new Color("b57900");
        }

        return active ? ActiveGreen : GhostGreen;
    }

    private void DrawEnergyCore(
        Vector2 center,
        bool active,
        bool previewed,
        AnchorMark transientMark,
        float transientAlpha,
        float pulse)
    {
        Color accent = transientMark != AnchorMark.None
            ? ResolveMarkColor(transientMark)
            : _resultMode
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

        float outerGlow = 30.0f + pulse * 4.0f;
        DrawCircle(
            center,
            outerGlow,
            accent with
            {
                A = (0.07f + pulse * 0.05f) * transientAlpha
            });
        DrawCircle(
            center,
            23.0f + pulse * 1.5f,
            accent with { A = 0.15f * transientAlpha },
            filled: false,
            width: 1.8f,
            antialiased: true);

        float textureSize = 72.0f + pulse * 3.0f;
        float textureAlpha = (active || _resultMode ? 0.62f : 0.14f)
            * transientAlpha;
        Rect2 textureRect = new(
            center - Vector2.One * textureSize * 0.5f,
            Vector2.One * textureSize);
        DrawTextureRect(
            _energyCoreTexture,
            textureRect,
            tile: false,
            modulate: Colors.White with { A = textureAlpha });

        DrawCircle(
            center,
            12.0f + pulse,
            accent with { A = 0.28f * transientAlpha },
            filled: false,
            width: 2.0f,
            antialiased: true);
        DrawCircle(
            center,
            3.0f + pulse * 1.5f,
            accent.Lerp(Colors.White, 0.58f) with
            {
                A = transientAlpha
            });
    }

    private AnchorMark ResolveTransientMark(bool active, int activeIndex)
    {
        if (!active || activeIndex < 0)
        {
            return AnchorMark.None;
        }

        if (_transientPresentation == TransientPresentation.PlayerRemoval)
        {
            return activeIndex < _limitPlayerCount
                ? AnchorMark.Player
                : AnchorMark.None;
        }

        if (_transientPresentation is TransientPresentation.TutorSelection
            or TransientPresentation.TutorRemoval)
        {
            return activeIndex < _tutorActionCount
                ? AnchorMark.Tutor
                : AnchorMark.None;
        }

        if (_transientPresentation == TransientPresentation.LimitReveal)
        {
            // Keep the already-revealed cyan Tutor anchors stationary when the
            // player's gold request joins the combined extraction.
            if (activeIndex < _limitTutorCount)
            {
                return AnchorMark.Tutor;
            }

            if (activeIndex < _limitTutorCount + _limitPlayerCount)
            {
                return AnchorMark.Player;
            }
        }

        return AnchorMark.None;
    }

    private float ResolveTransientAlpha(AnchorMark mark)
    {
        if (mark == AnchorMark.None
            || _transientPresentation is TransientPresentation.None
                or TransientPresentation.TutorSelection)
        {
            return 1.0f;
        }

        return 1.0f - Mathf.SmoothStep(0.0f, 1.0f, _transientProgress);
    }

    private float ResolvePullToCore(AnchorMark mark)
    {
        if (mark == AnchorMark.None
            || _transientPresentation is not (
                TransientPresentation.PlayerRemoval
                or TransientPresentation.TutorRemoval
                or TransientPresentation.LimitReveal))
        {
            return 0.0f;
        }

        return Mathf.SmoothStep(0.0f, 0.34f, _transientProgress);
    }

    private static Color ResolveMarkColor(AnchorMark mark)
    {
        return mark == AnchorMark.Player ? PreviewGold : TutorCyan;
    }

    private void ClearTransientPresentation()
    {
        _transientPresentation = TransientPresentation.None;
        _tutorActionCount = 0;
        _limitPlayerCount = 0;
        _limitTutorCount = 0;
        _transientProgress = 0.0f;
        _transientDuration = 1.0f;
    }

    private enum TransientPresentation
    {
        None,
        TutorSelection,
        PlayerRemoval,
        TutorRemoval,
        LimitReveal
    }

    private enum AnchorMark
    {
        None,
        Player,
        Tutor
    }

    private readonly struct OrbitSpec
    {
        public OrbitSpec(
            float radiusXFactor,
            float radiusYFactor,
            float tilt,
            float speed,
            float phase)
        {
            RadiusXFactor = radiusXFactor;
            RadiusYFactor = radiusYFactor;
            Tilt = tilt;
            Speed = speed;
            Phase = phase;
        }

        public float RadiusXFactor { get; }
        public float RadiusYFactor { get; }
        public float Tilt { get; }
        public float Speed { get; }
        public float Phase { get; }
    }

    private sealed class OrbitRing
    {
        public OrbitRing(int count, OrbitSpec spec)
        {
            Count = count;
            Spec = spec;
        }

        public int Count { get; }
        public OrbitSpec Spec { get; }
    }
}
