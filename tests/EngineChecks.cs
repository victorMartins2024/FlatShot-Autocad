using System;
using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using ColourfulFlatshot;
using Vertex=ColourfulFlatshot.Vertex;
using A=Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class EngineChecks
{
    static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    [CommandMethod("CFS_ENGINE_CHECKS")]
    public void Run()
    {
        var log=new System.Text.StringBuilder();
        string report=Path.Combine(Path.GetDirectoryName(typeof(EngineChecks).Assembly.Location),"engine-checks.txt");
        try
        {
            Span interval;
            var triangle=new Triangle(new Vertex(0,0,10),new Vertex(10,0,10),new Vertex(0,10,10));
            Assert(ProjectionMath.OccludedSpan(new Vertex(-5,2,0),new Vertex(15,2,0),triangle,1e-6,out interval),"Partial occlusion missing");
            Assert(Math.Abs(interval.Start-0.25)<1e-6 && Math.Abs(interval.End-0.65)<1e-6,"Wrong clipped interval");
            Assert(!ProjectionMath.OccludedSpan(new Vertex(1,2,10),new Vertex(5,2,10),triangle,1e-6,out interval),"Coplanar edge occluded");
            log.AppendLine("PASS analytic partial overlap and coplanar depth");
            using(var db=new Database(true,true))
            {
                db.DisableUndoRecording(false);
                var previous=HostApplicationServices.WorkingDatabase;HostApplicationServices.WorkingDatabase=db;
                try
                {
                    ObjectId redLayer,blueLayer;
                    using(var tr=db.TransactionManager.StartTransaction())
                    {
                        var lt=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForWrite);
                        var r=new LayerTableRecord {Name="CFS_RED",Color=Color.FromRgb(240,40,20)};
                        redLayer=lt.Add(r);tr.AddNewlyCreatedDBObject(r,true);
                        var b=new LayerTableRecord {Name="CFS_BLUE",Color=Color.FromColorIndex(ColorMethod.ByAci,5)};
                        blueLayer=lt.Add(b);tr.AddNewlyCreatedDBObject(b,true);
                        var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
                        var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
                        for(int i=0;i<2;i++)
                        {
                            var solid=new Solid3d();solid.SetDatabaseDefaults(db);solid.CreateBox(10,10,10);
                            solid.TransformBy(Matrix3d.Displacement(new Vector3d(i*5,i*2,i*20)));
                            solid.LayerId=i==0?redLayer:blueLayer;solid.Color=Color.FromColorIndex(ColorMethod.ByAci,256);
                            ms.AppendEntity(solid);tr.AddNewlyCreatedDBObject(solid,true);
                        }
                        tr.Commit();
                    }
                    using(var tr=db.TransactionManager.StartTransaction())
                    {
                        var result=FlatshotEngine.Generate(db,tr,Matrix3d.Identity,Matrix3d.Identity,new Point3d(100,200,0),new FlatshotOptions(),null);
                        Assert(result.Objects==2 && result.Visible>0 && result.Hidden>0,"Projection empty or no hidden lines");
                        var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
                        var block=(BlockTableRecord)tr.GetObject(bt[result.BlockName],OpenMode.ForRead);
                        bool clippedVisible=false,clippedHidden=false,foundBlue=false;
                        foreach(ObjectId id in block)
                        {
                            var line=(Line)tr.GetObject(id,OpenMode.ForRead);
                            Assert(Math.Abs(line.StartPoint.Z)<1e-9 && Math.Abs(line.EndPoint.Z)<1e-9,"Nonplanar output");
                            Assert(line.LayerId==redLayer || line.LayerId==blueLayer,"Source layer lost");
                            bool hidden=line.Linetype=="HIDDEN";
                            if(hidden)Assert(line.ColorIndex==8,"Wrong hidden ACI");
                            else if(line.LayerId==redLayer)Assert(line.Color.Red==240 && line.Color.Green==40,"True color lost");
                            else {Assert(line.Color.ColorIndex==5,"Blue ACI lost");foundBlue=true;}
                            double x1=Math.Min(line.StartPoint.X,line.EndPoint.X),x2=Math.Max(line.StartPoint.X,line.EndPoint.X);
                            if(line.LayerId==redLayer && Math.Abs(line.StartPoint.Y-10)<1e-5 && Math.Abs(line.EndPoint.Y-10)<1e-5)
                            {
                                if(!hidden && Math.Abs(x1)<1e-5 && Math.Abs(x2-5)<1e-5)clippedVisible=true;
                                if(hidden && Math.Abs(x1-5)<1e-5 && Math.Abs(x2-10)<1e-5)clippedHidden=true;
                            }
                        }
                        Assert(clippedVisible && clippedHidden && foundBlue,"Cross-solid partial occlusion did not split correctly");
                        log.AppendLine("PASS two overlapping solids: XY projection, source layers, RGB/ACI, HIDDEN/ACI8, partial cross-solid occlusion");
                        tr.Commit();
                    }
                    string abortedName=null;
                    using(var tr=db.TransactionManager.StartTransaction())
                    {
                        var result=FlatshotEngine.Generate(db,tr,Matrix3d.Identity,Matrix3d.Identity,Point3d.Origin,new FlatshotOptions {ShowHidden=false,PreserveColors=false,PreserveLayers=false},null);
                        abortedName=result.BlockName;
                        Assert(result.Objects==2 && result.Hidden==0,"Prior output reprocessed or hidden option ignored");
                        var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
                        var block=(BlockTableRecord)tr.GetObject(bt[result.BlockName],OpenMode.ForRead);
                        foreach(ObjectId id in block){var e=(Entity)tr.GetObject(id,OpenMode.ForRead);Assert(e.LayerId==db.LayerZero && e.ColorIndex==256,"Disable preservation ignored");}
                        // Abort on purpose; no output may remain.
                    }
                    using(var tr=db.TransactionManager.StartTransaction())
                    {
                        var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
                        Assert(!bt.Has(abortedName) || bt[abortedName].IsErased,"Aborted output block persisted");
                    }
                    bool cancelled=false;
                    try {using(var tr=db.TransactionManager.StartTransaction()){FlatshotEngine.Generate(db,tr,Matrix3d.Identity,Matrix3d.Identity,Point3d.Origin,new FlatshotOptions(),delegate {throw new OperationCanceledException();});}}
                    catch(OperationCanceledException){cancelled=true;}
                    Assert(cancelled,"Cancellation pulse ignored");
                    log.AppendLine("PASS hidden off, preservation off, prior CF result excluded, verified rollback, cancellation");
                }
                finally {HostApplicationServices.WorkingDatabase=previous;}
            }
            CheckCurvesAndBlocks(log);
            log.AppendLine("ALL CHECKS PASSED");
        }
        catch(System.Exception ex){log.AppendLine("FAIL: "+ex);}
        File.WriteAllText(report,log.ToString());
        A.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n"+log);
    }
    static void CheckCurvesAndBlocks(System.Text.StringBuilder log)
    {
        using(var db=new Database(true,true))
        {
            db.DisableUndoRecording(false);
            var previous=HostApplicationServices.WorkingDatabase;HostApplicationServices.WorkingDatabase=db;
            try
            {
                ObjectId blockSource;
                using(var tr=db.TransactionManager.StartTransaction())
                {
                    var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForWrite);
                    var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
                    var definition=new BlockTableRecord {Name="CHECK_LOCAL"};bt.Add(definition);tr.AddNewlyCreatedDBObject(definition,true);
                    var inner=new Solid3d();inner.SetDatabaseDefaults(db);inner.CreateBox(2,4,6);inner.Color=Color.FromColorIndex(ColorMethod.ByAci,0);
                    definition.AppendEntity(inner);tr.AddNewlyCreatedDBObject(inner,true);
                    var reference=new BlockReference(new Point3d(110,0,0),definition.ObjectId) {Rotation=0.4,ScaleFactors=new Scale3d(2),Color=Color.FromRgb(21,180,90)};
                    ms.AppendEntity(reference);tr.AddNewlyCreatedDBObject(reference,true);blockSource=reference.ObjectId;
                    var sphere=new Solid3d();sphere.SetDatabaseDefaults(db);sphere.CreateSphere(5);sphere.TransformBy(Matrix3d.Displacement(new Vector3d(30,0,0)));ms.AppendEntity(sphere);tr.AddNewlyCreatedDBObject(sphere,true);
                    var cylinder=new Solid3d();cylinder.SetDatabaseDefaults(db);cylinder.CreateFrustum(10,5,5,5);cylinder.TransformBy(Matrix3d.Displacement(new Vector3d(50,0,0)));ms.AppendEntity(cylinder);tr.AddNewlyCreatedDBObject(cylinder,true);
                    using(var boundary=new Polyline())
                    {
                        boundary.AddVertexAt(0,new Point2d(70,0),0,0,0);boundary.AddVertexAt(1,new Point2d(80,0),0,0,0);boundary.AddVertexAt(2,new Point2d(80,10),0,0,0);boundary.AddVertexAt(3,new Point2d(70,10),0,0,0);boundary.Closed=true;
                        var boundaries=new DBObjectCollection();boundaries.Add(boundary);
                        var regions=Region.CreateFromCurves(boundaries);
                        try {var surface=new PlaneSurface();surface.CreateFromRegion((Region)regions[0]);ms.AppendEntity(surface);tr.AddNewlyCreatedDBObject(surface,true);}
                        finally {foreach(DBObject region in regions)region.Dispose();}
                    }
                    var mesh=new SubDMesh();mesh.SetDatabaseDefaults(db);mesh.SetSubDMesh(new Point3dCollection(new[]{new Point3d(90,0,0),new Point3d(100,0,0),new Point3d(95,10,3)}),new Int32Collection(new[]{3,0,1,2}),0);ms.AppendEntity(mesh);tr.AddNewlyCreatedDBObject(mesh,true);
                    tr.Commit();
                }
                using(var tr=db.TransactionManager.StartTransaction())
                {
                    Matrix3d view=Matrix3d.PlaneToWorld(new Vector3d(1,-2,3)).Inverse();
                    Matrix3d ucs=Matrix3d.Rotation(0.3,Vector3d.XAxis,Point3d.Origin);
                    var result=FlatshotEngine.Generate(db,tr,view,ucs,new Point3d(100,200,300),new FlatshotOptions {ShowHidden=false,Tolerance=0.05},null);
                    Assert(result.Objects==5 && result.Visible>0,"Sphere/cylinder/surface/mesh/block failed");
                    var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
                    var block=(BlockTableRecord)tr.GetObject(bt[result.BlockName],OpenMode.ForRead);
                    bool inherited=false;
                    foreach(ObjectId id in block){var line=(Line)tr.GetObject(id,OpenMode.ForRead);Assert(Math.Abs(line.StartPoint.Z)<1e-8 && Math.Abs(line.EndPoint.Z)<1e-8,"Twisted view did not flatten");if(line.Color.Red==21 && line.Color.Green==180)inherited=true;}
                    Assert(inherited,"Nested block ByBlock color inheritance lost");
                    var original=(BlockReference)tr.GetObject(blockSource,OpenMode.ForRead);
                    Assert(original.Position.DistanceTo(new Point3d(110,0,0))<1e-9 && Math.Abs(original.Rotation-0.4)<1e-9,"Original block changed");
                    var ms=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
                    bool inserted=false;
                    foreach(ObjectId id in ms){var br=tr.GetObject(id,OpenMode.ForRead) as BlockReference;if(br!=null && br.BlockTableRecord==block.ObjectId){Assert(br.Position.DistanceTo(new Point3d(100,200,300))<1e-7,"Insertion point wrong");Assert(br.Normal.IsEqualTo(ucs.CoordinateSystem3d.Zaxis),"UCS normal wrong");Assert(Math.Abs(br.ScaleFactors.X-1)<1e-9 && Math.Abs(br.ScaleFactors.Y-1)<1e-9 && Math.Abs(br.ScaleFactors.Z-1)<1e-9,"Output scale is not 1:1");inserted=true;}}
                    Assert(inserted,"Output block reference missing");
                    log.AppendLine("PASS sphere, cylinder, planar surface, SubD mesh, rotated/scaled local block, ByBlock RGB, oblique view, UCS insertion, unchanged source");
                }
            }
            finally {HostApplicationServices.WorkingDatabase=previous;}
        }
    }
}
