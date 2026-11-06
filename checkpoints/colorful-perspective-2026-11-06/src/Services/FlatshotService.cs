using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ColorFlatshot.Models;

namespace ColorFlatshot.Services
{
    public sealed class FlatshotService
    {
        public FlatshotResult Generate(Database database, IEnumerable<ObjectId> sourceIds,
            FlatshotOptions options, Matrix3d worldToView, Point3d insertionPoint,
            Matrix3d? outputUcs = null)
        {
            if (database.TileMode == false)
                throw new InvalidOperationException("Execute no espaço Model (aba Modelo).");
            var result = new FlatshotResult();
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var sources = new EntityCollector(transaction, result.Warnings).Collect(sourceIds);
                var generated = new List<Entity>();
                try
                {
                    if (sources.Count == 0) return result;
                    Extents3d? bounds = null;
                    foreach (var source in sources)
                    {
                        source.Geometry.TransformBy(worldToView);
                        var extents = source.Geometry.GeometricExtents;
                        if (!bounds.HasValue) bounds = extents;
                        else { var combined = bounds.Value; combined.AddExtents(extents); bounds = combined; }
                    }
                    var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                    var box = bounds.Value;
                    double margin = Math.Max(1.0, box.MinPoint.DistanceTo(box.MaxPoint) * 0.05);
                    double z = box.MaxPoint.Z + margin;
                    using (var section = new Section(new Point3dCollection(new[] {
                        new Point3d(box.MinPoint.X - margin, 0, z),
                        new Point3d(box.MaxPoint.X + margin, 0, z) }), Vector3d.YAxis, -Vector3d.ZAxis))
                    {
                        section.State = SectionState.Plane;
                        space.AppendEntity(section);
                        transaction.AddNewlyCreatedDBObject(section, true);
                        var settings = (SectionSettings)transaction.GetObject(section.Settings, OpenMode.ForWrite);
                        settings.CurrentSectionType = SectionType.Section2d;
                        settings.SetVisibility(SectionType.Section2d, SectionGeometry.BackgroundGeometry, true);
                        settings.SetVisibility(SectionType.Section2d, SectionGeometry.ForegroundGeometry, false);
                        settings.SetHiddenLine(SectionType.Section2d, SectionGeometry.BackgroundGeometry, options.IncludeHiddenLines);
                        foreach (var source in sources)
                        {
                            // Fail atomically if the kernel rejects a valid source: no partial success.
                            Array boundary, fill, background, foreground, tangencies;
                            section.GenerateSectionGeometry(source.Geometry, out boundary, out fill,
                                out background, out foreground, out tangencies);
                            try
                            {
                                CopyCurves(background, source, generated);
                                if (options.IncludeTangencies) CopyCurves(tangencies, source, generated);
                            }
                            finally
                            {
                                DisposeArrays(boundary, fill, background, foreground, tangencies);
                            }
                        }
                        section.Erase();
                    }
                    if (generated.Count == 0) return result;

                    // Native FLATSHOT creates the 2D drawing in the current UCS XY
                    // plane. Using WCS here makes a face-aligned TOP look edge-on.
                    var anchor = new Vector3d(-box.MinPoint.X, -box.MinPoint.Y, 0);
                    var axes = (outputUcs ?? Matrix3d.Identity).CoordinateSystem3d;
                    var placement = Matrix3d.AlignCoordinateSystem(Point3d.Origin,
                        Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                        insertionPoint, axes.Xaxis, axes.Yaxis, axes.Zaxis);
                    BlockTableRecord target = space;
                    BlockTableRecord definition = null;
                    if (options.InsertAsBlock)
                    {
                        var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForWrite);
                        result.BlockName = "COLORFLATSHOT_" + Guid.NewGuid().ToString("N");
                        definition = new BlockTableRecord { Name = result.BlockName, Origin = Point3d.Origin };
                        table.Add(definition);
                        transaction.AddNewlyCreatedDBObject(definition, true);
                        target = definition;
                    }
                    foreach (var curve in generated)
                    {
                        curve.TransformBy(Matrix3d.Displacement(anchor));
                        if (!options.InsertAsBlock) curve.TransformBy(placement);
                        target.AppendEntity(curve);
                        transaction.AddNewlyCreatedDBObject(curve, true);
                    }
                    if (definition != null)
                    {
                        var reference = new BlockReference(Point3d.Origin, definition.ObjectId);
                        reference.TransformBy(placement);
                        var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
                        reference.LayerId = layers["0"];
                        space.AppendEntity(reference);
                        transaction.AddNewlyCreatedDBObject(reference, true);
                    }
                    result.SourceCount = sources.Count;
                    result.CurveCount = generated.Count;
                    transaction.Commit();
                    return result;
                }
                finally
                {
                    foreach (var entity in generated) entity.Dispose();
                    foreach (var source in sources) source.Dispose();
                }
            }
        }

        private static void CopyCurves(Array array, CadEntityModel source, List<Entity> result)
        {
            if (array == null) return;
            foreach (object value in array)
            {
                var curve = value as Curve;
                if (curve == null) continue;
                var copy = (Curve)curve.Clone();
                try
                {
                    // The section engine emits geometry on its section plane. Orthogonal
                    // projection guarantees Z=0, including arcs and splines, without tessellation.
                    using (var plane = new Plane(Point3d.Origin, Vector3d.ZAxis))
                    {
                        var flat = copy.GetOrthoProjectedCurve(plane);
                        if (flat == null) continue;
                        try { flat.LayerId = source.LayerId; flat.Color = source.Color; result.Add(flat); }
                        catch { flat.Dispose(); throw; }
                    }
                }
                finally { copy.Dispose(); }
            }
        }

        private static void DisposeArrays(params Array[] arrays)
        {
            foreach (var array in arrays)
                if (array != null) foreach (object value in array)
                { var disposable = value as IDisposable; if (disposable != null) disposable.Dispose(); }
        }

        public static Matrix3d GetWorldToView(ProjectionView view, ViewTableRecord current)
        {
            if (view == ProjectionView.Current && current.PerspectiveEnabled)
                throw new InvalidOperationException("Desative a perspectiva (PERSPECTIVE = 0). O FlatShot acompanha a vista paralela atual do ViewCube.");
            Vector3d direction;
            double twist = 0;
            Point3d target = Point3d.Origin;
            switch (view)
            {
                case ProjectionView.Top: direction = Vector3d.ZAxis; break;
                case ProjectionView.Front: direction = -Vector3d.YAxis; break;
                case ProjectionView.Right: direction = Vector3d.XAxis; break;
                case ProjectionView.Isometric: direction = new Vector3d(1, -1, 1); break;
                default: direction = current.ViewDirection; twist = current.ViewTwist; target = current.Target; break;
            }
            var viewToWorld = Matrix3d.PlaneToWorld(direction);
            viewToWorld = Matrix3d.Displacement(target.GetAsVector()) * viewToWorld;
            viewToWorld = Matrix3d.Rotation(-twist, direction, target) * viewToWorld;
            return viewToWorld.Inverse();
        }
    }
}
