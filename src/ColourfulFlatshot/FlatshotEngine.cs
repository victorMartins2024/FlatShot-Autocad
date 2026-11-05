using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.EditorInput;
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace ColourfulFlatshot
{
    public sealed class FlatshotOptions
    {
        public bool PreserveColors=true, PreserveLayers=true, ShowHidden=true;
        public short HiddenAci=8;
        public double Tolerance=0.01;
    }
    internal sealed class EdgeData
    {
        public Vertex A,B;
        public Vector3d Normal;
        public bool Candidate;
        public int Faces;
        public ObjectId Layer;
        public Color Color;
    }
    public sealed class FlatshotResult
    {
        public string BlockName;
        public int Objects, Visible, Hidden, Skipped;
        internal readonly HashSet<string> Emitted=new HashSet<string>();
    }
    public static class FlatshotEngine
    {
        private const int MaxTriangles=150000, MaxEdges=100000;
        public static FlatshotResult Generate(Database db,Transaction tr,Matrix3d worldToView,Matrix3d ucs,Point3d insertion,FlatshotOptions options,Action pulse)
        {
            if(pulse!=null)
            {
                pulse();
                var originalPulse=pulse;
                var cancellationTimer=Stopwatch.StartNew();
                pulse=delegate
                {
                    if(cancellationTimer.ElapsedMilliseconds<50)return;
                    originalPulse();cancellationTimer.Restart();
                };
            }
            var result=new FlatshotResult();
            var triangles=new List<Triangle>();
            var edges=new List<EdgeData>();
            var geometryCache=new Dictionary<GeometryKey,CachedGeometry>();
            var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
            var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
            foreach(ObjectId id in ms)
            {
                var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;
                if(entity!=null)Read(entity,tr,Matrix3d.Identity,ObjectId.Null,null,worldToView,options,triangles,edges,result,0,pulse,geometryCache);
            }
            if(edges.Count==0)throw new InvalidOperationException("Nenhuma geometria 3D compatível e visível encontrada no Model Space.");
            // A hierarchy visits only bounds crossed by each segment, without cell duplicates.
            var grid=new TriangleIndex(triangles,true);
            ObjectId hiddenType=db.ContinuousLinetype;
            if(options.ShowHidden)
            {
                var lt=(LinetypeTable)tr.GetObject(db.LinetypeTableId,OpenMode.ForRead);
                if(!lt.Has("HIDDEN"))
                {
                    string file=Convert.ToInt32(AcadApplication.GetSystemVariable("MEASUREMENT"))==1?"acadiso.lin":"acad.lin";
                    db.LoadLineTypeFile("HIDDEN",file);
                }
                hiddenType=lt["HIDDEN"];
            }
            bt.UpgradeOpen();
            var block=new BlockTableRecord {Name="CF_"+Guid.NewGuid().ToString("N").Substring(0,12)};
            bt.Add(block);tr.AddNewlyCreatedDBObject(block,true);
            double minX=double.MaxValue,minY=double.MaxValue;
            foreach(var edge in edges){minX=Math.Min(minX,Math.Min(edge.A.X,edge.B.X));minY=Math.Min(minY,Math.Min(edge.A.Y,edge.B.Y));}
            int processed=0;
            foreach(var edge in edges)
            {
                if((processed++%100)==0 && pulse!=null)pulse();
                var hidden=new List<Span>();
                foreach(var tri in grid.Query(edge.A,edge.B))
                {
                    Span span;
                    if(ProjectionMath.OccludedSpan(edge.A,edge.B,tri,options.Tolerance*0.1,out span))hidden.Add(span);
                }
                var merged=ProjectionMath.Merge(hidden);
                double start=0;
                foreach(var span in merged)
                {
                    Emit(block,tr,db,edge,start,span.Start,false,hiddenType,minX,minY,options,result);
                    if(options.ShowHidden)Emit(block,tr,db,edge,span.Start,span.End,true,hiddenType,minX,minY,options,result);
                    start=span.End;
                }
                Emit(block,tr,db,edge,start,1,false,hiddenType,minX,minY,options,result);
            }
            SuppressHiddenOverlap(block,tr,db,result,options.Tolerance*0.01,pulse);
            if(result.Visible+result.Hidden==0)throw new InvalidOperationException("A projeção não produziu linhas nesta vista.");
            var visibleIds=new ObjectIdCollection();
            var uniqueLines=new HashSet<string>();
            foreach(ObjectId id in block)
            {
                var line=(Line)tr.GetObject(id,OpenMode.ForRead);
                string ka=Key(new Vertex(line.StartPoint.X,line.StartPoint.Y,0),options.Tolerance*0.001),kb=Key(new Vertex(line.EndPoint.X,line.EndPoint.Y,0),options.Tolerance*0.001);
                string coordinates=string.CompareOrdinal(ka,kb)<0?ka+"/"+kb:kb+"/"+ka;
                string key=coordinates+"|"+line.LayerId+"|"+line.Color+"|"+line.LinetypeId;
                if(!uniqueLines.Add(key))
                {
                    bool isVisible=line.LinetypeId==db.ContinuousLinetype;
                    line.UpgradeOpen();line.Erase();
                    if(isVisible)result.Visible--;else result.Hidden--;
                    continue;
                }
                if(line.LinetypeId==db.ContinuousLinetype)visibleIds.Add(id);
            }
            var order=(DrawOrderTable)tr.GetObject(block.DrawOrderTableId,OpenMode.ForWrite);
            order.MoveToTop(visibleIds);
            ms.UpgradeOpen();
            var reference=new BlockReference(Point3d.Origin,block.ObjectId);
            reference.SetDatabaseDefaults(db);
            // Insertion is WCS; block XY axes follow the current UCS.
            reference.TransformBy(Matrix3d.AlignCoordinateSystem(Point3d.Origin,Vector3d.XAxis,Vector3d.YAxis,Vector3d.ZAxis,insertion,ucs.CoordinateSystem3d.Xaxis,ucs.CoordinateSystem3d.Yaxis,ucs.CoordinateSystem3d.Zaxis));
            ms.AppendEntity(reference);tr.AddNewlyCreatedDBObject(reference,true);
            result.BlockName=block.Name;
            return result;
        }
        private static void Emit(BlockTableRecord block,Transaction tr,Database db,EdgeData edge,double from,double to,bool hidden,ObjectId hiddenType,double x,double y,FlatshotOptions options,FlatshotResult result)
        {
            if(to-from<1e-9)return;
            Vertex a=Vertex.At(edge.A,edge.B,from),b=Vertex.At(edge.A,edge.B,to);
            if(Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y)<options.Tolerance*0.01)return;
            string ka=Key(new Vertex(a.X,a.Y,0),options.Tolerance*0.001),kb=Key(new Vertex(b.X,b.Y,0),options.Tolerance*0.001);
            string coordinates=string.CompareOrdinal(ka,kb)<0?ka+"/"+kb:kb+"/"+ka;
            string key=coordinates+"|"+hidden+"|"+(options.PreserveLayers?edge.Layer.ToString():"0")+"|"+(options.PreserveColors?edge.Color.ToString():"256");
            if(!result.Emitted.Add(key))return;
            var line=new Line(new Point3d(a.X-x,a.Y-y,0),new Point3d(b.X-x,b.Y-y,0));
            line.SetDatabaseDefaults(db);
            line.LayerId=options.PreserveLayers?edge.Layer:db.LayerZero;
            line.Color=hidden?Color.FromColorIndex(ColorMethod.ByAci,options.HiddenAci):(options.PreserveColors?edge.Color:Color.FromColorIndex(ColorMethod.ByAci,256));
            line.LinetypeId=hidden?hiddenType:db.ContinuousLinetype;
            block.AppendEntity(line);tr.AddNewlyCreatedDBObject(line,true);
            if(hidden)result.Hidden++;else result.Visible++;
        }
        private static void SuppressHiddenOverlap(BlockTableRecord block,Transaction tr,Database db,FlatshotResult result,double tolerance,Action pulse)
        {
            var visible=new List<Triangle>();
            var hidden=new List<Line>();
            foreach(ObjectId id in block)
            {
                var line=(Line)tr.GetObject(id,OpenMode.ForRead);
                if(line.LinetypeId!=db.ContinuousLinetype)hidden.Add(line);
                else visible.Add(new Triangle(new Vertex(line.StartPoint.X,line.StartPoint.Y,0),new Vertex(line.EndPoint.X,line.EndPoint.Y,0),new Vertex(line.StartPoint.X,line.StartPoint.Y,0)));
            }
            if(visible.Count==0 || hidden.Count==0)return;
            var grid=new TriangleIndex(visible);
            foreach(var line in hidden)
            {
                if(pulse!=null)pulse();
                var a=line.StartPoint;var b=line.EndPoint;
                double dx=b.X-a.X,dy=b.Y-a.Y,length2=dx*dx+dy*dy;
                double length=Math.Sqrt(length2);
                var overlap=new List<Span>();
                foreach(var candidate in grid.Query(new Vertex(a.X,a.Y,0),new Vertex(b.X,b.Y,0)))
                {
                    double cross1=Math.Abs((candidate.A.X-a.X)*dy-(candidate.A.Y-a.Y)*dx)/length;
                    double cross2=Math.Abs((candidate.B.X-a.X)*dy-(candidate.B.Y-a.Y)*dx)/length;
                    if(cross1>tolerance || cross2>tolerance)continue;
                    double t1=((candidate.A.X-a.X)*dx+(candidate.A.Y-a.Y)*dy)/length2;
                    double t2=((candidate.B.X-a.X)*dx+(candidate.B.Y-a.Y)*dy)/length2;
                    double lo=Math.Max(0,Math.Min(t1,t2)),hi=Math.Min(1,Math.Max(t1,t2));
                    if(hi-lo>1e-9)overlap.Add(new Span(lo,hi));
                }
                var merged=ProjectionMath.Merge(overlap);
                if(merged.Count==0)continue;
                double start=0;
                foreach(var span in merged)
                {
                    AppendHiddenRemainder(line,start,span.Start,block,tr,result,tolerance);
                    start=span.End;
                }
                AppendHiddenRemainder(line,start,1,block,tr,result,tolerance);
                line.UpgradeOpen();line.Erase();result.Hidden--;
            }
        }
        private static void AppendHiddenRemainder(Line source,double start,double end,BlockTableRecord block,Transaction tr,FlatshotResult result,double tolerance)
        {
            if((end-start)*source.Length<=tolerance)return;
            var line=(Line)source.Clone();
            line.StartPoint=source.StartPoint+(source.EndPoint-source.StartPoint)*start;
            line.EndPoint=source.StartPoint+(source.EndPoint-source.StartPoint)*end;
            block.AppendEntity(line);tr.AddNewlyCreatedDBObject(line,true);result.Hidden++;
        }
        private static void Read(Entity entity,Transaction tr,Matrix3d transform,ObjectId inheritedLayer,Color inheritedColor,Matrix3d view,FlatshotOptions options,List<Triangle> triangles,List<EdgeData> output,FlatshotResult result,int depth,Action pulse,Dictionary<GeometryKey,CachedGeometry> geometryCache)
        {
            if(depth>32)throw new InvalidOperationException("Blocos aninhados além do limite de 32 níveis.");
            if(!entity.Visible)return;
            var layer=(LayerTableRecord)tr.GetObject(entity.LayerId,OpenMode.ForRead);
            ObjectId effectiveLayer=layer.Name=="0" && !inheritedLayer.IsNull?inheritedLayer:entity.LayerId;
            var effectiveRecord=(LayerTableRecord)tr.GetObject(effectiveLayer,OpenMode.ForRead);
            if(layer.IsOff || layer.IsFrozen || effectiveRecord.IsOff || effectiveRecord.IsFrozen)return;
            Color color=entity.Color;
            if(color.ColorMethod==ColorMethod.ByLayer)color=effectiveRecord.Color;
            else if(color.ColorMethod==ColorMethod.ByBlock)color=inheritedColor??Color.FromColorIndex(ColorMethod.ByAci,7);
            var reference=entity as BlockReference;
            if(reference!=null)
            {
                var definition=(BlockTableRecord)tr.GetObject(reference.BlockTableRecord,OpenMode.ForRead);
                if(definition.IsFromExternalReference || definition.Name.StartsWith("CF_",StringComparison.OrdinalIgnoreCase)){result.Skipped++;return;}
                foreach(ObjectId id in definition)
                {
                    var child=tr.GetObject(id,OpenMode.ForRead) as Entity;
                    if(child!=null)Read(child,tr,transform*reference.BlockTransform,effectiveLayer,color,view,options,triangles,output,result,depth+1,pulse,geometryCache);
                }
                return;
            }
            bool supported=entity is Solid3d || entity is Autodesk.AutoCAD.DatabaseServices.Surface || entity is SubDMesh || entity is Face || entity is PolyFaceMesh || entity is PolygonMesh;
            if(!supported){result.Skipped++;return;}
            if(pulse!=null)pulse();
            if(entity is PolyFaceMesh || entity is PolygonMesh)
            {
                var exploded=new DBObjectCollection();
                try
                {
                    entity.Explode(exploded);
                    foreach(DBObject item in exploded)
                    {
                        var part=item as Entity;
                        if(part!=null)Read(part,tr,transform,effectiveLayer,color,view,options,triangles,output,result,depth+1,pulse,geometryCache);
                    }
                }
                finally {foreach(DBObject item in exploded)item.Dispose();}
                return;
            }
            double scale=Math.Max(transform.CoordinateSystem3d.Xaxis.Length,Math.Max(transform.CoordinateSystem3d.Yaxis.Length,transform.CoordinateSystem3d.Zaxis.Length));
            var cacheKey=new GeometryKey(entity.ObjectId,scale);
            CachedGeometry geometry;
            if(entity.ObjectId.IsNull || !geometryCache.TryGetValue(cacheKey,out geometry))
            {
                geometry=ExtractGeometry(entity,options.Tolerance,scale);
                if(!entity.ObjectId.IsNull)geometryCache.Add(cacheKey,geometry);
            }
            var vertices=geometry.Vertices;var faces=geometry.Faces;
            var projected=new Vertex[vertices.Length];
            for(int i=0;i<vertices.Length;i++){var p=vertices[i].TransformBy(transform).TransformBy(view);projected[i]=new Vertex(p.X,p.Y,p.Z);}
            var adjacency=new Dictionary<ulong,EdgeData>();
            var welded=new Dictionary<VertexKey,int>();
            var vertexIds=new int[projected.Length];
            for(int i=0;i<projected.Length;i++)
            {
                var key=new VertexKey(projected[i],options.Tolerance*0.001);
                int id;if(!welded.TryGetValue(key,out id)){id=welded.Count;welded.Add(key,id);}vertexIds[i]=id;
            }
            bool brepSource=geometry.HasBrep;
            foreach(var samples in geometry.Curves)
            for(int i=0;i<samples.Length-1;i++)
            {
                var a=samples[i].TransformBy(transform).TransformBy(view);
                var b=samples[i+1].TransformBy(transform).TransformBy(view);
                output.Add(new EdgeData {A=new Vertex(a.X,a.Y,a.Z),B=new Vertex(b.X,b.Y,b.Z),Layer=effectiveLayer,Color=color});
                if(output.Count>MaxEdges)throw new InvalidOperationException("Modelo excede 100.000 segmentos. Simplifique o modelo.");
            }
            for(int index=0;index<faces.Length;)
            {
                int count=faces[index++];if(count<3 || index+count>faces.Length)throw new InvalidOperationException("Malha com face inválida.");
                var polygon=new Vertex[count];
                var polygonIds=new int[count];
                for(int i=0;i<count;i++){int vertexIndex=Math.Abs(faces[index+i]);polygon[i]=projected[vertexIndex];polygonIds[i]=vertexIds[vertexIndex];}
                index+=count;
                Vector3d normal=Normal(polygon[0],polygon[1],polygon[2]);
                if(normal.Length<1e-12)continue;
                normal=normal.GetNormal();
                // AutoCAD faceter emits convex facets; triangulate each facet, keeping only perimeter edges.
                for(int i=1;i<count-1;i++)triangles.Add(new Triangle(polygon[0],polygon[i],polygon[i+1]));
                for(int i=0;i<count;i++)
                {
                    Vertex a=polygon[i],b=polygon[(i+1)%count];
                    uint ka=(uint)polygonIds[i],kb=(uint)polygonIds[(i+1)%count];
                    ulong key=ka<kb?((ulong)ka<<32)|kb:((ulong)kb<<32)|ka;
                    EdgeData previous;
                    if(adjacency.TryGetValue(key,out previous))
                    {
                        previous.Faces++;
                        // Smooth facets only contribute their silhouette; sharp features remain.
                        bool silhouette=(previous.Normal.Z>1e-9)!=(normal.Z>1e-9);
                        if(silhouette || (!brepSource && !(entity is Solid3d) && !(entity is Autodesk.AutoCAD.DatabaseServices.Surface) && previous.Normal.DotProduct(normal)<1-1e-8))previous.Candidate=true;
                    }
                    else adjacency.Add(key,new EdgeData {A=a,B=b,Normal=normal,Faces=1,Layer=effectiveLayer,Color=color});
                }
                if(triangles.Count>MaxTriangles)throw new InvalidOperationException("Modelo excede 150.000 triângulos. Simplifique o modelo.");
            }
            foreach(var pair in adjacency)if((!brepSource && pair.Value.Faces==1) || pair.Value.Candidate)output.Add(pair.Value);
            if(output.Count>MaxEdges)throw new InvalidOperationException("Modelo excede 100.000 arestas de projeção.");
            result.Objects++;
        }
        private sealed class CachedGeometry
        {
            public Point3d[] Vertices;
            public int[] Faces;
            public bool HasBrep;
            public readonly List<Point3d[]> Curves=new List<Point3d[]>();
        }
        private struct GeometryKey : IEquatable<GeometryKey>
        {
            private readonly ObjectId id;
            private readonly double scale;
            public GeometryKey(ObjectId source,double sourceScale){id=source;scale=sourceScale;}
            public bool Equals(GeometryKey other){return id==other.id && scale==other.scale;}
            public override bool Equals(object other){return other is GeometryKey && Equals((GeometryKey)other);}
            public override int GetHashCode(){unchecked{return id.GetHashCode()*397^scale.GetHashCode();}}
        }
        private static CachedGeometry ExtractGeometry(Entity entity,double tolerance,double scale)
        {
            Point3dCollection vertices;Int32Collection faces;
            var mesh=entity as SubDMesh;
            if(mesh!=null){vertices=mesh.SubDividedVertices;faces=mesh.SubDividedFaceArray;}
            else if(entity is Face)
            {
                var face=(Face)entity;vertices=new Point3dCollection();
                for(short i=0;i<4;i++)vertices.Add(face.GetVertexAt(i));
                faces=new Int32Collection(new[]{4,0,1,2,3});
            }
            else
            {
                var data=SubDMesh.GetObjectMesh(entity,new MeshFaceterData(tolerance,0.08,0,0,128,32,32,0));
                vertices=data.VertexArray;faces=data.FaceArray;
            }
            var geometry=new CachedGeometry {Vertices=new Point3d[vertices.Count],Faces=new int[faces.Count],HasBrep=entity is Solid3d || entity is Autodesk.AutoCAD.DatabaseServices.Surface};
            vertices.CopyTo(geometry.Vertices,0);faces.CopyTo(geometry.Faces,0);
            if(geometry.HasBrep)
            try
            {
                using(var brep=new Autodesk.AutoCAD.BoundaryRepresentation.Brep(entity))
                foreach(var edge in brep.Edges)
                using(edge)
                using(var curve=edge.GetCurveAsNurb())
                {
                    var interval=curve.GetInterval();
                    var samples=curve.GetSamplePoints(interval.LowerBound,interval.UpperBound,tolerance/Math.Max(scale,1e-9));
                    try
                    {
                        if(samples.Length>MaxEdges)throw new InvalidOperationException("Curva excede o limite de segmentos desta versão.");
                        var points=new Point3d[samples.Length];
                        for(int i=0;i<samples.Length;i++)points[i]=samples[i].Point;
                        geometry.Curves.Add(points);
                    }
                    finally {foreach(var sample in samples)sample.Dispose();}
                }
            }
            catch(Autodesk.AutoCAD.BoundaryRepresentation.Exception)
            {
                geometry.Curves.Clear();geometry.HasBrep=false;
            }
            return geometry;
        }
        private static Vector3d Normal(Vertex a,Vertex b,Vertex c)
        { return new Vector3d(b.X-a.X,b.Y-a.Y,b.Z-a.Z).CrossProduct(new Vector3d(c.X-a.X,c.Y-a.Y,c.Z-a.Z)); }
        private static string Key(Vertex p,double tolerance)
        {return Math.Round(p.X/tolerance)+","+Math.Round(p.Y/tolerance)+","+Math.Round(p.Z/tolerance);}
        private struct VertexKey : IEquatable<VertexKey>
        {
            private readonly double x,y,z;
            public VertexKey(Vertex p,double tolerance){x=Math.Round(p.X/tolerance);y=Math.Round(p.Y/tolerance);z=Math.Round(p.Z/tolerance);}
            public bool Equals(VertexKey other){return x==other.x && y==other.y && z==other.z;}
            public override bool Equals(object other){return other is VertexKey && Equals((VertexKey)other);}
            public override int GetHashCode(){unchecked{return (x.GetHashCode()*397^y.GetHashCode())*397^z.GetHashCode();}}
        }

        private sealed class TriangleIndex
        {
            private sealed class Node
            {
                public double MinX,MaxX,MinY,MaxY,MaxZ;
                public Node Left,Right;
                public int Start,Count;
            }
            private readonly Triangle[] triangles;
            private readonly Node root;
            private readonly bool depthFilter;
            private static readonly IComparer<Triangle> ByX=Comparer<Triangle>.Create((a,b)=>(a.MinX+a.MaxX).CompareTo(b.MinX+b.MaxX));
            private static readonly IComparer<Triangle> ByY=Comparer<Triangle>.Create((a,b)=>(a.MinY+a.MaxY).CompareTo(b.MinY+b.MaxY));
            public TriangleIndex(List<Triangle> source,bool useDepth=false)
            {
                triangles=source.ToArray();depthFilter=useDepth;
                if(triangles.Length>0)root=Build(0,triangles.Length);
            }
            private Node Build(int start,int count)
            {
                var node=new Node {MinX=double.MaxValue,MaxX=double.MinValue,MinY=double.MaxValue,MaxY=double.MinValue,MaxZ=double.MinValue,Start=start,Count=count};
                for(int i=start;i<start+count;i++)
                {
                    var t=triangles[i];node.MinX=Math.Min(node.MinX,t.MinX);node.MaxX=Math.Max(node.MaxX,t.MaxX);node.MinY=Math.Min(node.MinY,t.MinY);node.MaxY=Math.Max(node.MaxY,t.MaxY);node.MaxZ=Math.Max(node.MaxZ,t.MaxZ);
                }
                if(count<=8)return node;
                Array.Sort(triangles,start,count,node.MaxX-node.MinX>=node.MaxY-node.MinY?ByX:ByY);
                int half=count/2;node.Left=Build(start,half);node.Right=Build(start+half,count-half);node.Count=0;
                return node;
            }
            public IEnumerable<Triangle> Query(Vertex a,Vertex b)
            {
                if(root==null)yield break;
                var stack=new Node[64];int size=0;stack[size++]=root;
                double minZ=Math.Min(a.Z,b.Z);
                while(size>0)
                {
                    var node=stack[--size];
                    if(depthFilter && node.MaxZ<minZ)continue;
                    if(!Crosses(a,b,node.MinX,node.MaxX,node.MinY,node.MaxY))continue;
                    if(node.Count==0){stack[size++]=node.Left;stack[size++]=node.Right;continue;}
                    for(int i=node.Start;i<node.Start+node.Count;i++)
                    {
                        var t=triangles[i];
                        if((!depthFilter || t.MaxZ>=minZ) && Crosses(a,b,t.MinX,t.MaxX,t.MinY,t.MaxY))yield return t;
                    }
                }
            }
            private static bool Crosses(Vertex a,Vertex b,double minX,double maxX,double minY,double maxY)
            {
                double lo=0,hi=1;
                return Slab(a.X,b.X-a.X,minX-1e-8,maxX+1e-8,ref lo,ref hi)
                    && Slab(a.Y,b.Y-a.Y,minY-1e-8,maxY+1e-8,ref lo,ref hi);
            }
            private static bool Slab(double origin,double direction,double min,double max,ref double lo,ref double hi)
            {
                if(Math.Abs(direction)<1e-20)return origin>=min && origin<=max;
                double a=(min-origin)/direction,b=(max-origin)/direction;
                if(a>b){double swap=a;a=b;b=swap;}
                lo=Math.Max(lo,a);hi=Math.Min(hi,b);return hi>=lo;
            }
        }
    }
}


