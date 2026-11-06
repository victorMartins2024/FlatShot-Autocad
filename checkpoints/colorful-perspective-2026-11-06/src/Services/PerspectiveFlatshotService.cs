using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.BoundaryRepresentation;
using ColorFlatshot.Models;
using AcApplication=Autodesk.AutoCAD.ApplicationServices.Application;
using BEdge=Autodesk.AutoCAD.BoundaryRepresentation.Edge;

namespace ColorFlatshot.Services
{
    public sealed class PerspectiveFlatshotService
    {
        sealed class Candidate { public P3 A,B;public int Owner,Chain; }
        sealed class MeshEdge { public P3 A,B;public readonly List<bool> Front=new List<bool>(); }
        sealed class Output { public P2 A,B;public int Owner,Chain;public bool Hidden; }
        static P3 Point(Point3d p){return new P3(p.X,p.Y,p.Z);}
        static Point3d Point(P3 p){return new Point3d(p.X,p.Y,p.Z);}

        public static PerspectiveCamera CaptureCamera(Document doc,ViewTableRecord current)
        {
            if(!current.PerspectiveEnabled)throw new InvalidOperationException("A vista não está em perspectiva.");
            int number=Convert.ToInt32(AcApplication.GetSystemVariable("CVPORT"));
            var gs=doc.GraphicsManager.GetCurrent3dAcGsView(number);
            if(gs==null||!gs.IsPerspective)gs=doc.GraphicsManager.GetCurrentAcGsView(number);
            if(gs==null||!gs.IsPerspective)
                throw new InvalidOperationException("A câmera em perspectiva ainda não está disponível. Use REGEN na vista em perspectiva e tente novamente.");
            var eye=Point(gs.Position);var forward=Point(gs.Target)-eye;
            if(forward.Length<1e-10||gs.FieldWidth<=0||gs.FieldHeight<=0)throw new InvalidOperationException("Parâmetros da câmera inválidos.");
            double near=Math.Max(1e-6,forward.Length*1e-7),far=double.MaxValue;
            if(current.FrontClipEnabled&&!current.FrontClipAtEye)near=Math.Max(near,forward.Length-current.FrontClipDistance);
            if(current.BackClipEnabled)far=forward.Length-current.BackClipDistance;
            if(far<=near)throw new InvalidOperationException("Os planos de recorte da câmera não deixam uma região projetável.");
            return new PerspectiveCamera((gs.ProjectionMatrix*gs.ViewingMatrix).ToArray(),eye,forward,
                gs.FieldWidth/2,gs.FieldHeight/2,near,far);
        }

        public FlatshotResult Generate(Document doc,IEnumerable<ObjectId> ids,FlatshotOptions options,
            PerspectiveCamera camera,Point3d insertion,Matrix3d outputUcs)
        {
            if(!doc.Database.TileMode)throw new InvalidOperationException("Execute na aba Model.");
            if(options.PerspectiveTolerance<=0||double.IsNaN(options.PerspectiveTolerance)||double.IsInfinity(options.PerspectiveTolerance))
                throw new InvalidOperationException("Informe uma tolerância de perspectiva maior que zero.");
            var db=doc.Database;var result=new FlatshotResult();
            using(var tr=db.TransactionManager.StartTransaction())
            {
                var sources=new EntityCollector(tr,result.Warnings).Collect(ids);
                try
                {
                    if(sources.Count==0)return result;
                    var triangles=new List<ProjectedTriangle>();var candidates=new List<Candidate>();int chain=0;
                    double tolerance=options.PerspectiveTolerance;
                    for(int owner=0;owner<sources.Count;owner++)
                    {
                        doc.Editor.WriteMessage("\nPerspectiva: preparando componente {0}/{1}…",owner+1,sources.Count);
                        int firstBoundary=candidates.Count;
                        AddEdges(sources[owner].Geometry,owner,camera,tolerance,options.IncludeTangencies,candidates,ref chain);
                        var boundaries=candidates.Skip(firstBoundary).ToArray();
                        AddMesh(sources[owner].Geometry,owner,camera,tolerance,triangles,candidates,boundaries,ref chain);
                        if(triangles.Count>2000000||candidates.Count>2000000)
                            throw new InvalidOperationException("Modelo muito detalhado para esta tolerância. Aumente a tolerância ou gere por subconjuntos.");
                    }
                    var index=new TriangleIndex(triangles);var output=new List<Output>();
                    var unique=new HashSet<string>();double bias=tolerance*1.5;
                    foreach(var candidate in candidates)
                    {
                        var a=candidate.A;var b=candidate.B;if(!camera.ClipSegment(ref a,ref b))continue;
                        var pa=camera.Project(a);var pb=camera.Project(b);if(pa.Distance(pb)<1e-9)continue;
                        foreach(var span in index.Classify(pa,pb,bias))
                        {
                            if(span.Hidden&&!options.IncludeHiddenLines)continue;
                            var start=P2.Lerp(pa,pb,span.Start);var end=P2.Lerp(pa,pb,span.End);
                            if(start.Distance(end)<1e-8)continue;
                            string ka=Key2(start),kb=Key2(end);
                            string key=candidate.Owner+":"+span.Hidden+":"+(StringComparer.Ordinal.Compare(ka,kb)<0?ka+"/"+kb:kb+"/"+ka);
                            if(unique.Add(key))output.Add(new Output{A=start,B=end,Owner=candidate.Owner,Chain=candidate.Chain,Hidden=span.Hidden});
                        }
                    }
                    if(output.Count==0){result.Warnings.Add("Nenhuma aresta visível à frente da câmera e dentro dos planos de recorte.");return result;}
                    double minX=output.Min(x=>Math.Min(x.A.X,x.B.X)),minY=output.Min(x=>Math.Min(x.A.Y,x.B.Y));
                    var cs=outputUcs.CoordinateSystem3d;
                    var placement=Matrix3d.AlignCoordinateSystem(Point3d.Origin,Vector3d.XAxis,Vector3d.YAxis,Vector3d.ZAxis,insertion,cs.Xaxis,cs.Yaxis,cs.Zaxis);
                    var space=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForWrite);
                    BlockTableRecord target=space,definition=null;
                    if(options.InsertAsBlock)
                    {
                        var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForWrite);
                        definition=new BlockTableRecord{Name="COLORFLATSHOT_PERSPECTIVE_"+Guid.NewGuid().ToString("N")};
                        bt.Add(definition);tr.AddNewlyCreatedDBObject(definition,true);target=definition;result.BlockName=definition.Name;
                    }
                    ObjectId hiddenType=ObjectId.Null;
                    if(output.Any(x=>x.Hidden))hiddenType=HiddenLinetype(db,tr);
                    // Join consecutive sampled curve segments into editable polylines.
                    foreach(var group in output.GroupBy(x=>new {x.Owner,x.Chain,x.Hidden}))
                    {
                        var points=new List<P2>();
                        Action flush=()=>
                        {
                            if(points.Count<2){points.Clear();return;}
                            var entity=new Polyline();entity.SetDatabaseDefaults(db);
                            for(int i=0;i<points.Count;i++)entity.AddVertexAt(i,new Point2d(points[i].X-minX,points[i].Y-minY),0,0,0);
                            entity.LayerId=sources[group.Key.Owner].LayerId;entity.Color=sources[group.Key.Owner].Color;
                            if(group.Key.Hidden)entity.LinetypeId=hiddenType;
                            if(!options.InsertAsBlock)entity.TransformBy(placement);
                            target.AppendEntity(entity);tr.AddNewlyCreatedDBObject(entity,true);result.CurveCount++;points.Clear();
                        };
                        foreach(var segment in group)
                        {
                            if(points.Count>0&&points[points.Count-1].Distance(segment.A)>1e-7)flush();
                            if(points.Count==0)points.Add(segment.A);points.Add(segment.B);
                        }
                        flush();
                    }
                    if(definition!=null)
                    {
                        var reference=new BlockReference(Point3d.Origin,definition.ObjectId);reference.LayerId=db.LayerZero;reference.TransformBy(placement);
                        space.AppendEntity(reference);tr.AddNewlyCreatedDBObject(reference,true);
                    }
                    result.SourceCount=sources.Count;
                    result.Warnings.Add("Perspectiva real da câmera. Curvas e silhuetas aproximadas por segmentos; tolerância: "+tolerance.ToString("G6")+" unidades. Ocultação calculada entre os componentes selecionados.");
                    tr.Commit();return result;
                }
                finally{foreach(var source in sources)source.Dispose();}
            }
        }
        static string Key2(P2 p){return Math.Round(p.X/1e-7)+","+Math.Round(p.Y/1e-7);}
        static string Key3(P3 p,double tolerance){return Math.Round(p.X/tolerance)+","+Math.Round(p.Y/tolerance)+","+Math.Round(p.Z/tolerance);}
        static void AddMesh(Entity entity,int owner,PerspectiveCamera camera,double tolerance,
            List<ProjectedTriangle> triangles,List<Candidate> candidates,Candidate[] boundaries,ref int chain)
        {
            var settings=new MeshFaceterData{FaceterDevSurface=tolerance,FaceterDevNormal=2,FaceterGridRatio=0,
                FaceterMaxEdgeLength=0,FaceterMaxGrid=0,FaceterMinUGrid=0,FaceterMinVGrid=0,FaceterMeshType=2};
            var data=SubDMesh.GetObjectMesh(entity,settings);
            P3[] vertices;int[] faces;
            using(var nativeVertices=data.VertexArray)
            {
                vertices=nativeVertices.Cast<Point3d>().Select(Point).ToArray();faces=data.FaceArray.ToArray();
            }
            if(vertices.Length==0||faces.Length==0)throw new InvalidOperationException("Não foi possível tesselar um componente para perspectiva.");
            double weld=Math.Max(1e-9,(entity.GeometricExtents.MaxPoint-entity.GeometricExtents.MinPoint).Length*1e-9);
            var edges=new Dictionary<string,MeshEdge>();
            for(int cursor=0;cursor<faces.Length;)
            {
                int count=faces[cursor++];
                if(count<3||cursor+count>faces.Length)throw new InvalidOperationException("Malha de perspectiva inválida.");
                var polygon=new P3[count];
                for(int i=0;i<count;i++){int id=faces[cursor++];if(id<0||id>=vertices.Length)throw new InvalidOperationException("Índice de malha inválido.");polygon[i]=vertices[id];}
                var normal=(polygon[1]-polygon[0]).Cross(polygon[2]-polygon[0]);
                bool front=normal.Dot(camera.Eye-polygon[0])>=0;
                for(int i=0;i<count;i++)
                {
                    var a=polygon[i];var b=polygon[(i+1)%count];string ka=Key3(a,weld),kb=Key3(b,weld);
                    string key=StringComparer.Ordinal.Compare(ka,kb)<0?ka+"/"+kb:kb+"/"+ka;
                    MeshEdge edge;if(!edges.TryGetValue(key,out edge)){edge=new MeshEdge{A=a,B=b};edges.Add(key,edge);}edge.Front.Add(front);
                }
                var clipped=camera.ClipPolygon(polygon);
                for(int i=1;i+1<clipped.Count;i++)triangles.Add(new ProjectedTriangle(camera.Project(clipped[0]),camera.Project(clipped[i]),camera.Project(clipped[i+1])));
            }
            foreach(var edge in edges.Values)
                if((edge.Front.Count==1||edge.Front.Any(x=>x)&&edge.Front.Any(x=>!x))&&!CoveredBoundary(edge.A,edge.B,boundaries,tolerance*2))
                    candidates.Add(new Candidate{A=edge.A,B=edge.B,Owner=owner,Chain=chain++});
        }
        static bool CoveredBoundary(P3 a,P3 b,Candidate[] boundaries,double tolerance)
        {
            var middle=(a+b)*0.5;
            foreach(var chain in boundaries.GroupBy(x=>x.Chain))
            {
                bool ca=false,cb=false,cm=false;
                foreach(var segment in chain)
                {
                    ca|=Distance(a,segment.A,segment.B)<=tolerance;
                    cb|=Distance(b,segment.A,segment.B)<=tolerance;
                    cm|=Distance(middle,segment.A,segment.B)<=tolerance;
                    if(ca&&cb&&cm)return true;
                }
            }
            return false;
        }
        static double Distance(P3 p,P3 a,P3 b)
        {
            var d=b-a;double length=d.Dot(d);double t=length<1e-30?0:Math.Max(0,Math.Min(1,(p-a).Dot(d)/length));return (p-(a+d*t)).Length;
        }
        static void AddEdges(Entity entity,int owner,PerspectiveCamera camera,double tolerance,bool includeTangencies,List<Candidate> result,ref int chain)
        {
            using(var brep=new Brep(entity))
            foreach(BEdge edge in brep.Edges)
            using(edge)
            using(var curve=edge.Curve)
            {
                var interval=curve.GetInterval();double start=interval.LowerBound,end=interval.UpperBound;
                if(double.IsInfinity(start)||double.IsInfinity(end))throw new InvalidOperationException("Aresta sem intervalo finito.");
                if(!includeTangencies&&Tangent(edge,curve.EvaluatePoint((start+end)/2)))continue;
                var first=Point(curve.EvaluatePoint(start));var last=Point(curve.EvaluatePoint(end));
                bool linear=(last-first).Length>1e-10&&Distance(Point(curve.EvaluatePoint((start+end)/2)),first,last)<1e-9&&
                    Distance(Point(curve.EvaluatePoint(start+(end-start)*.25)),first,last)<1e-9;
                int id=chain++;int seeds=linear?1:16;
                for(int i=0;i<seeds;i++)Sample(curve,start+(end-start)*i/seeds,start+(end-start)*(i+1)/seeds,owner,id,camera,tolerance,result,0);
            }
        }
        static bool Tangent(BEdge edge,Point3d middle)
        {
            var normals=new List<Vector3d>();
            foreach(BoundaryLoop loop in edge.Loops)
            using(loop)
            using(var face=loop.Face)
            using(var surface=face.Surface)
            using(var closest=surface.GetClosestPointTo(middle))
            {
                var normal=closest.GetNormal().GetNormal();if(!face.IsOrientToSurface)normal=-normal;normals.Add(normal);
            }
            return normals.Count>=2&&normals.All(n=>n.DotProduct(normals[0])>1-1e-8);
        }
        static void Sample(Curve3d curve,double start,double end,int owner,int chain,PerspectiveCamera camera,double tolerance,List<Candidate> result,int depth)
        {
            var a=Point(curve.EvaluatePoint(start));var b=Point(curve.EvaluatePoint(end));double middle=(start+end)/2;
            var m=Point(curve.EvaluatePoint(middle));double error=(m-(a+b)*0.5).Length;
            if(camera.Depth(a)>camera.Near&&camera.Depth(b)>camera.Near&&camera.Depth(m)>camera.Near)
            {
                var pa=camera.Project(a);var pb=camera.Project(b);var pm=camera.Project(m);
                double dx=pb.X-pa.X,dy=pb.Y-pa.Y,den=dx*dx+dy*dy;
                double t=den<1e-30?0:Math.Max(0,Math.Min(1,((pm.X-pa.X)*dx+(pm.Y-pa.Y)*dy)/den));
                error=Math.Max(error,pm.Distance(P2.Lerp(pa,pb,t)));
            }
            if(error>tolerance&&depth<18)
            {
                Sample(curve,start,middle,owner,chain,camera,tolerance,result,depth+1);
                Sample(curve,middle,end,owner,chain,camera,tolerance,result,depth+1);return;
            }
            if(result.Count>2000000)throw new InvalidOperationException("Amostragem muito detalhada. Aumente a tolerância.");
            result.Add(new Candidate{A=a,B=b,Owner=owner,Chain=chain});
        }
        static ObjectId HiddenLinetype(Database db,Transaction tr)
        {
            var table=(LinetypeTable)tr.GetObject(db.LinetypeTableId,OpenMode.ForRead);const string name="CF_PERSPECTIVE_HIDDEN";
            if(table.Has(name))return table[name];table.UpgradeOpen();
            var record=new LinetypeTableRecord{Name=name,AsciiDescription="Colorful FlatShot - ocultas em perspectiva",PatternLength=4.5,NumDashes=2};
            record.SetDashLengthAt(0,3);record.SetDashLengthAt(1,-1.5);
            var id=table.Add(record);tr.AddNewlyCreatedDBObject(record,true);return id;
        }
    }
}
