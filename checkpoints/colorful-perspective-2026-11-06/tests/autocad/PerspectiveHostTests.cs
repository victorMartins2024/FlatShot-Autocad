using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.ApplicationServices;
using ColorFlatshot.Models;
using ColorFlatshot.Services;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Application;
[assembly:CommandClass(typeof(ColorfulPerspectiveTests.Tests))]
namespace ColorfulPerspectiveTests
{
    public class Tests
    {
        readonly List<string> report=new List<string>();
        string PathName{get{return Path.Combine((string)AppContext.GetData("ColorfulPerspective.TestDirectory")??Path.GetDirectoryName(typeof(Tests).Assembly.Location),"perspective-host-results.txt");}}
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);report.Add("PASS "+name);File.WriteAllLines(PathName,report);}
        static HashSet<ObjectId> Ids(Database db){using(var tr=db.TransactionManager.StartOpenCloseTransaction())return ((BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForRead)).Cast<ObjectId>().ToHashSet();}
        static P3 Point(Point3d p){return new P3(p.X,p.Y,p.Z);}
        [CommandMethod("CFPTEST")]
        public void Run()
        {
            var doc=AcApp.DocumentManager.MdiActiveDocument;var db=doc.Database;var ed=doc.Editor;
            try
            {
                Check(db.TileMode&&Ids(db).Count==0,"isolated empty Model drawing");
                AcApp.SetSystemVariable("UCSFOLLOW",0);ed.CurrentUserCoordinateSystem=Matrix3d.Identity;
                using(var tr=db.TransactionManager.StartTransaction())
                using(var view=ed.GetCurrentView())
                {
                    var styles=(DBDictionary)tr.GetObject(db.VisualStyleDictionaryId,OpenMode.ForRead);
                    if(styles.Contains("Wireframe"))view.VisualStyleId=styles.GetAt("Wireframe");
                    view.SetUcsToWorld();view.ViewDirection=new Vector3d(60,-80,60);view.Target=Point3d.Origin;
                    view.ViewTwist=.19;view.Height=35;view.Width=50;view.CenterPoint=new Point2d(0,0);view.LensLength=35;view.PerspectiveEnabled=true;
                    ed.SetCurrentView(view);tr.Commit();
                }
                ed.Regen();
                using(var before=ed.GetCurrentView())
                {
                    Check(before.PerspectiveEnabled,"AutoCAD actual perspective enabled");
                    var camera=PerspectiveFlatshotService.CaptureCamera(doc,before);
                    int viewport=Convert.ToInt32(AcApp.GetSystemVariable("CVPORT"));
                    var gs=doc.GraphicsManager.GetCurrent3dAcGsView(viewport);
                    var right=(gs.Target-gs.Position).CrossProduct(gs.UpVector).GetNormal();var up=gs.UpVector.GetNormal();
                    var center=gs.Target;
                    double stepX=gs.FieldWidth*.25,stepY=gs.FieldHeight*.25;
                    report.Add("CAMERA field="+gs.FieldWidth+","+gs.FieldHeight+" eye="+gs.Position+" target="+gs.Target+" view="+before.Width+","+before.Height);
                    var points=new[]{center,center+right*stepX,center+up*stepY,center+(gs.Target-gs.Position).GetNormal()*10+right*(stepX*.7)+up*(stepY*.5),center-right*(stepX*.6)-up*(stepY*.6)};
                    var image=points.Select(p=>camera.Project(Point(p))).ToArray();
                    var screen=points.Select(p=>ed.PointToScreen(p,viewport)).ToArray();
                    double kx=(screen[1].X-screen[0].X)/(image[1].X-image[0].X);
                    double ky=(screen[2].Y-screen[0].Y)/(image[2].Y-image[0].Y);
                    for(int i=0;i<points.Length;i++)
                    {
                        double ex=screen[0].X+(image[i].X-image[0].X)*kx,ey=screen[0].Y+(image[i].Y-image[0].Y)*ky;
                        report.Add("CAMERA sample "+i+" projected="+image[i].X+","+image[i].Y+" native="+screen[i]+" error="+(ex-screen[i].X)+","+(ey-screen[i].Y));
                        Check(Math.Abs(ex-screen[i].X)<2&&Math.Abs(ey-screen[i].Y)<2,"camera matrix matches AutoCAD PointToScreen sample "+i);
                    }
                    Check(Math.Abs(Math.Abs(kx/ky)-1)<.03,"perspective output preserves native screen aspect ratio");
                    ObjectId cube;
                    using(var tr=db.TransactionManager.StartTransaction())
                    {
                        var ms=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForWrite);
                        var layer=new LayerTableRecord{Name="CFP_BOX",Color=Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,3)};
                        var lt=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForWrite);var lid=lt.Add(layer);tr.AddNewlyCreatedDBObject(layer,true);
                        var solid=new Solid3d();solid.CreateBox(12,8,4);solid.LayerId=lid;
                        solid.Color=Autodesk.AutoCAD.Colors.Color.FromRgb(25,125,210);
                        cube=ms.AppendEntity(solid);tr.AddNewlyCreatedDBObject(solid,true);tr.Commit();
                    }
                    var options=new FlatshotOptions{InsertAsBlock=true,IncludeHiddenLines=false,PerspectiveTolerance=.01};
                    int visible=GenerateAndVerify(doc,new[]{cube},options,camera,new Point3d(25,0,0),Matrix3d.Identity,"box visible edges");
                    options.IncludeHiddenLines=true;
                    int all=GenerateAndVerify(doc,new[]{cube},options,camera,new Point3d(50,0,0),Matrix3d.Identity,"box including hidden edges");
                    Check(all>visible,"hidden edge toggle adds obscured box edges");
                    options.InsertAsBlock=false;
                    var ucs=Matrix3d.Displacement(new Vector3d(3,7,11))*Matrix3d.Rotation(.7,new Vector3d(1,2,3),Point3d.Origin);
                    GenerateAndVerify(doc,new[]{cube},options,camera,new Point3d(75,0,0),ucs,"independent curves on rotated UCS plane");
                    ObjectId sphere;
                    using(var tr=db.TransactionManager.StartTransaction())
                    {
                        var ms=(BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db),OpenMode.ForWrite);
                        var solid=new Solid3d();solid.CreateSphere(5);sphere=ms.AppendEntity(solid);tr.AddNewlyCreatedDBObject(solid,true);tr.Commit();
                    }
                    options.InsertAsBlock=true;options.IncludeHiddenLines=false;options.PerspectiveTolerance=.05;
                    Check(GenerateAndVerify(doc,new[]{sphere},options,camera,new Point3d(0,25,0),Matrix3d.Identity,"smooth sphere perspective silhouette")>=8,"curved silhouette generated with controlled tessellation");
                    using(var after=ed.GetCurrentView())
                    {
                        Check(after.PerspectiveEnabled,"perspective mode preserved after generation");
                        Check(after.ViewDirection.IsEqualTo(before.ViewDirection,new Tolerance(1e-7,1e-7))&&Math.Abs(after.ViewTwist-before.ViewTwist)<1e-7,"camera orientation preserved after generation");
                        Check(after.Target.DistanceTo(before.Target)<1e-7&&Math.Abs(after.LensLength-before.LensLength)<1e-7,"camera target and lens preserved");
                    }
                    // Parallel output must still use the analytic section path.
                    options.InsertAsBlock=true;var frame=FlatshotService.GetWorldToView(ProjectionView.Top,before);
                    var parallel=new FlatshotService().Generate(db,new[]{cube},options,frame,new Point3d(25,25,0),Matrix3d.Identity);
                    Check(parallel.CurveCount>0,"parallel projection regression remains functional");
                    report.Add("ALL PERSPECTIVE HOST TESTS PASSED");File.WriteAllLines(PathName,report);ed.WriteMessage("\nCFPTEST SUCCESS: "+PathName);
                }
            }
            catch(System.Exception ex){report.Add("FAIL "+ex);File.WriteAllLines(PathName,report);ed.WriteMessage("\nCFPTEST FAIL: "+ex);}
        }
        int GenerateAndVerify(Document doc,ObjectId[] sources,FlatshotOptions options,PerspectiveCamera camera,Point3d insertion,Matrix3d ucs,string label)
        {
            var db=doc.Database;var before=Ids(db);var result=new PerspectiveFlatshotService().Generate(doc,sources,options,camera,insertion,ucs);
            Check(result.CurveCount>0,label+": nonempty output");
            var added=Ids(db).Except(before).ToArray();
            using(var tr=db.TransactionManager.StartOpenCloseTransaction())
            {
                var curves=new List<Entity>();
                var axes=ucs.CoordinateSystem3d;var placement=Matrix3d.AlignCoordinateSystem(Point3d.Origin,Vector3d.XAxis,Vector3d.YAxis,Vector3d.ZAxis,insertion,axes.Xaxis,axes.Yaxis,axes.Zaxis);
                if(options.InsertAsBlock)
                {
                    Check(added.Length==1,label+": only final block appended");
                    var reference=(BlockReference)tr.GetObject(added[0],OpenMode.ForRead);
                    Check(reference.Position.DistanceTo(insertion)<1e-7,label+": insertion point preserved");
                    var block=(BlockTableRecord)tr.GetObject(reference.BlockTableRecord,OpenMode.ForRead);
                    curves.AddRange(block.Cast<ObjectId>().Select(id=>(Entity)tr.GetObject(id,OpenMode.ForRead)));
                }
                else curves.AddRange(added.Select(id=>(Entity)tr.GetObject(id,OpenMode.ForRead)));
                foreach(var curve in curves)
                using(var copy=(Entity)curve.Clone())
                {
                    if(!options.InsertAsBlock)copy.TransformBy(placement.Inverse());
                    var ext=copy.GeometricExtents;
                    if(Math.Abs(ext.MinPoint.Z)>1e-6||Math.Abs(ext.MaxPoint.Z)>1e-6)throw new InvalidOperationException(label+": output outside intended plane");
                }
                Check(curves.Count>0,label+": planar editable curves");
                if(sources.Length==1)
                {
                    var source=(Entity)tr.GetObject(sources[0],OpenMode.ForRead);
                    Check(curves.All(x=>x.LayerId==source.LayerId&&x.Color.Equals(source.Color)),label+": source layers and colors preserved");
                }
                if(!options.IncludeHiddenLines)Check(curves.All(x=>x.Linetype!="CF_PERSPECTIVE_HIDDEN"),label+": hidden edges omitted");
                else Check(curves.Any(x=>x.Linetype=="CF_PERSPECTIVE_HIDDEN"),label+": obscured edges classified and dashed");
            }
            return result.CurveCount;
        }
    }
}
