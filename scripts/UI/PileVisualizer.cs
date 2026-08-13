using System;
using System.Collections.Generic;
using Godot;

public partial class PileVisualizer : HBoxContainer
{
    private const int MaximumUnitCount = 64;

    private readonly List<Control> _generatedUnits = new();
    private Control _unitTemplate = null!;
    private int _unitCount = 15;

    [Export(PropertyHint.Range, "0,64,1")]
    public int UnitCount
    {
        get => _unitCount;
        set
        {
            _unitCount = Math.Clamp(value, 0, MaximumUnitCount);

            if (IsNodeReady())
            {
                RebuildUnits();
            }
        }
    }

    public override void _Ready()
    {
        _unitTemplate = GetNode<Control>("UnitExample");
        RebuildUnits();
    }

    /// <summary>
    /// Updates the displayed pile size. Game-state code can call this method
    /// whenever the remaining unit count changes.
    /// </summary>
    public void SetUnitCount(int count)
    {
        UnitCount = count;
    }

    private void RebuildUnits()
    {
        ClearGeneratedUnits();
        _unitTemplate.Visible = _unitCount > 0;

        // UnitExample is the first displayed unit. Only the remaining units
        // are generated, so the authored scene always contains one example.
        for (int index = 1; index < _unitCount; index++)
        {
            var unit = _unitTemplate.Duplicate() as Control;

            if (unit is null)
            {
                GD.PushError("PileVisualizer could not duplicate UnitExample.");
                return;
            }

            unit.Name = $"Unit{index + 1:00}";
            unit.Visible = true;
            unit.MouseFilter = MouseFilterEnum.Ignore;

            AddChild(unit);
            _generatedUnits.Add(unit);
        }
    }

    private void ClearGeneratedUnits()
    {
        foreach (var unit in _generatedUnits)
        {
            if (GodotObject.IsInstanceValid(unit))
            {
                unit.Free();
            }
        }

        _generatedUnits.Clear();
    }
}
