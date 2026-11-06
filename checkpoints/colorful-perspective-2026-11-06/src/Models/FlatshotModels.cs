using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace ColorFlatshot.Models
{
    public enum ProjectionView { Current, Top, Front, Right, Isometric }
    public sealed class FlatshotOptions
    {
        public ProjectionView View { get; set; }
        public bool IncludeHiddenLines { get; set; }
        public bool IncludeTangencies { get; set; }
        public bool InsertAsBlock { get; set; }
        public double PerspectiveTolerance { get; set; }
        public FlatshotOptions() { InsertAsBlock = true; PerspectiveTolerance = 0.1; }
    }

    // Geometry is an owned, detached copy. Never modify the user's source entity.
    public sealed class CadEntityModel : IDisposable
    {
        public Entity Geometry { get; private set; }
        public ObjectId SourceId { get; private set; }
        public string SourceHandle { get; private set; }
        public ObjectId LayerId { get; private set; }
        public Color Color { get; private set; }
        public CadEntityModel(Entity geometry, ObjectId source, string handle, ObjectId layer, Color color)
        { Geometry = geometry; SourceId = source; SourceHandle = handle; LayerId = layer; Color = color; }
        public void Dispose() { Geometry.Dispose(); }
    }

    public sealed class FlatshotResult
    {
        public int SourceCount { get; set; }
        public int CurveCount { get; set; }
        public string BlockName { get; set; }
        public List<string> Warnings { get; private set; }
        public FlatshotResult() { Warnings = new List<string>(); }
    }
}
