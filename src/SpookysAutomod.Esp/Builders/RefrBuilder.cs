using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SpookysAutomod.Esp.Builders;

/// <summary>
/// Fluent builder for placing a PlacedObject (REFR) into a Cell.
///
/// Pattern: caller resolves the target Cell via a link cache (or already has
/// it from the mod), constructs a RefrBuilder against that cell, sets the
/// base form + position, and Build() returns the placed REFR (already
/// attached to the cell's Persistent or Temporary collection).
///
/// Persistence is determined by which cell collection holds the REFR -
/// `cell.Persistent` survives saves and supports cross-session FormID
/// references; `cell.Temporary` is regenerated on cell load. XMarker proxy
/// use cases (Phase 3 M2) want Persistent.
/// </summary>
public class RefrBuilder
{
    private readonly SkyrimMod _mod;
    private readonly ICell     _targetCell;
    private FormKey  _baseFormKey;
    private string?  _editorId;
    private P3Float  _position = new(0f, 0f, 0f);
    private P3Float  _rotation = new(0f, 0f, 0f);
    private float?   _scale;
    private bool     _persistent = true;

    public RefrBuilder(SkyrimMod mod, ICell targetCell, string? editorId = null)
    {
        _mod        = mod;
        _targetCell = targetCell;
        _editorId   = editorId;
    }

    /// <summary>
    /// Set the base record this REFR places. For an XMarker REFR, that's
    /// Skyrim.esm:0x00000033. For an XMarkerHeading, 0x0000003B.
    /// </summary>
    public RefrBuilder WithBase(FormKey baseFormKey)
    {
        _baseFormKey = baseFormKey;
        return this;
    }

    /// <summary>
    /// World position. For interior cells, this is cell-local. For exterior
    /// cells (worldspaces), this is worldspace coordinates.
    /// </summary>
    public RefrBuilder AtPosition(float x, float y, float z)
    {
        _position = new P3Float(x, y, z);
        return this;
    }

    /// <summary>
    /// Rotation in radians (X, Y, Z). Default is (0, 0, 0).
    /// </summary>
    public RefrBuilder WithRotation(float x, float y, float z)
    {
        _rotation = new P3Float(x, y, z);
        return this;
    }

    /// <summary>
    /// Optional uniform scale. Vanilla default omits the XSCL subrecord
    /// (engine uses 1.0); only set if you need non-default.
    /// </summary>
    public RefrBuilder WithScale(float scale)
    {
        _scale = scale;
        return this;
    }

    /// <summary>
    /// Place into the cell's Temporary collection instead of Persistent.
    /// Temporary REFRs don't survive cell unload + cosave; use for
    /// generated content that's recreated on cell load. Default is
    /// Persistent which is what most use cases want.
    /// </summary>
    public RefrBuilder AsTemporary()
    {
        _persistent = false;
        return this;
    }

    /// <summary>
    /// Build the PlacedObject, add it to the target cell, and return it.
    /// </summary>
    public PlacedObject Build()
    {
        var refr = new PlacedObject(_mod.GetNextFormKey(), _mod.SkyrimRelease);
        if (!string.IsNullOrEmpty(_editorId))
        {
            refr.EditorID = _editorId;
        }
        refr.Base.SetTo(_baseFormKey);
        refr.Placement = new Placement
        {
            Position = _position,
            Rotation = _rotation
        };
        if (_scale.HasValue)
        {
            refr.Scale = _scale.Value;
        }

        if (_persistent)
        {
            _targetCell.Persistent.Add(refr);
        }
        else
        {
            _targetCell.Temporary.Add(refr);
        }
        return refr;
    }
}
