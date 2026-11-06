using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ColorFlatshot.Models;

namespace ColorFlatshot.Services
{
    internal sealed class EntityCollector
    {
        private readonly Transaction transaction;
        private readonly List<string> warnings;
        private int visited;
        public EntityCollector(Transaction transaction, List<string> warnings)
        { this.transaction = transaction; this.warnings = warnings; }

        public List<CadEntityModel> Collect(IEnumerable<ObjectId> ids)
        {
            var result = new List<CadEntityModel>();
            try
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || id.IsErased) continue;
                    var entity = transaction.GetObject(id, OpenMode.ForRead) as Entity;
                    if (entity != null) Visit(entity, id, id.Handle.ToString(), null, null, result, 0, Matrix3d.Identity);
                }
                return result;
            }
            catch { foreach (var item in result) item.Dispose(); throw; }
        }

        private void Visit(Entity entity, ObjectId source, string handle, ObjectId? parentLayer,
            Color parentColor, List<CadEntityModel> result, int depth, Matrix3d transform)
        {
            if (++visited > 100000) throw new InvalidOperationException("Seleção excede 100.000 componentes. Divida em lotes.");
            if (depth > 32) { warnings.Add("Bloco com profundidade excessiva: " + handle); return; }
            var ownLayer = (LayerTableRecord)transaction.GetObject(entity.LayerId.IsNull && parentLayer.HasValue ? parentLayer.Value : entity.LayerId, OpenMode.ForRead);
            ObjectId layerId = (ownLayer.Name == "0" || entity.LayerId.IsNull) && parentLayer.HasValue ? parentLayer.Value : entity.LayerId;
            var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
            if (!entity.Visible || ownLayer.IsOff || ownLayer.IsFrozen || layer.IsOff || layer.IsFrozen) return;
            Color color = entity.Color;
            if (color.IsByBlock) color = parentColor ?? layer.Color;
            Color effectiveColor = color.IsByLayer ? layer.Color : color;

            if (entity is Solid3d || entity is Autodesk.AutoCAD.DatabaseServices.Surface || entity is Body || entity is Region)
            {
                var copy = (Entity)entity.Clone();
                try { copy.TransformBy(transform); result.Add(new CadEntityModel(copy, source, handle, layerId, color)); }
                catch { copy.Dispose(); throw; }
                return;
            }
            var block = entity as BlockReference;
            if (block != null)
            {
                var definition = (BlockTableRecord)transaction.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                if (definition.IsFromExternalReference || definition.IsFromOverlayReference)
                { warnings.Add("Referência externa ignorada: " + handle); return; }
                foreach (ObjectId id in definition)
                {
                    var child = transaction.GetObject(id, OpenMode.ForRead) as Entity;
                    if (child != null) Visit(child, source, handle, layerId, effectiveColor, result, depth + 1, transform * block.BlockTransform);
                }
                return;
            }
            // Custom Plant objects expose their graphics through Explode, when supported
            // by the loaded Plant object enabler. Ordinary 2D entities are excluded.
            if (block == null && (entity is Curve || entity is DBText || entity is MText || entity is Hatch || entity is DBPoint))
                return;
            var children = new DBObjectCollection();
            try
            {
                entity.Explode(children);
                if (children.Count == 0) warnings.Add("Sem geometria 3D acessível: " + handle);
                foreach (DBObject child in children)
                {
                    var childEntity = child as Entity;
                    if (childEntity != null) Visit(childEntity, source, handle, layerId, effectiveColor, result, depth + 1, transform);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            { warnings.Add("Não foi possível decompor " + handle + ": " + ex.ErrorStatus); }
            finally { foreach (DBObject child in children) child.Dispose(); children.Dispose(); }
        }
    }
}
