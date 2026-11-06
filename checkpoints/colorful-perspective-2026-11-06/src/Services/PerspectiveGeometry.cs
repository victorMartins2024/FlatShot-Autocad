using System;
using System.Collections.Generic;
using System.Linq;

namespace ColorFlatshot.Services
{
    // Host-independent projective geometry. Depth is positive in front of the eye.
    public struct P3
    {
        public double X,Y,Z;
        public P3(double x,double y,double z){X=x;Y=y;Z=z;}
        public static P3 operator +(P3 a,P3 b){return new P3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
        public static P3 operator -(P3 a,P3 b){return new P3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
        public static P3 operator *(P3 a,double k){return new P3(a.X*k,a.Y*k,a.Z*k);}
        public double Dot(P3 b){return X*b.X+Y*b.Y+Z*b.Z;}
        public P3 Cross(P3 b){return new P3(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);}
        public double Length{get{return Math.Sqrt(Dot(this));}}
        public P3 Normal(){return this*(1.0/Math.Max(1e-300,Length));}
    }
    public struct P2
    {
        public double X,Y,InvDepth;
        public P2(double x,double y,double inverseDepth){X=x;Y=y;InvDepth=inverseDepth;}
        public static P2 Lerp(P2 a,P2 b,double t){return new P2(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.InvDepth+(b.InvDepth-a.InvDepth)*t);}
        public double Distance(P2 b){double x=X-b.X,y=Y-b.Y;return Math.Sqrt(x*x+y*y);}
    }
    public sealed class PerspectiveCamera
    {
        readonly double[] matrix;
        public readonly P3 Eye,Forward;
        public readonly double ScaleX,ScaleY,Near,Far;
        public PerspectiveCamera(double[] worldToClip,P3 eye,P3 forward,double scaleX,double scaleY,double near,double far)
        {
            if(worldToClip==null||worldToClip.Length!=16||near<=0||far<=near)throw new ArgumentException("Invalid camera");
            matrix=(double[])worldToClip.Clone();Eye=eye;Forward=forward.Normal();ScaleX=scaleX;ScaleY=scaleY;Near=near;Far=far;
        }
        public double Depth(P3 p){return (p-Eye).Dot(Forward);}
        public P2 Project(P3 p)
        {
            double d=Depth(p);
            double w=matrix[12]*p.X+matrix[13]*p.Y+matrix[14]*p.Z+matrix[15];
            if(d<=0||Math.Abs(w)<1e-300)throw new InvalidOperationException("Point behind camera");
            double x=(matrix[0]*p.X+matrix[1]*p.Y+matrix[2]*p.Z+matrix[3])/w*ScaleX;
            double y=(matrix[4]*p.X+matrix[5]*p.Y+matrix[6]*p.Z+matrix[7])/w*ScaleY;
            if(double.IsNaN(x)||double.IsInfinity(x)||double.IsNaN(y)||double.IsInfinity(y))throw new InvalidOperationException("Invalid perspective coordinate");
            return new P2(x,y,1/d);
        }
        public bool ClipSegment(ref P3 a,ref P3 b)
        {
            double da=Depth(a),db=Depth(b),lo=0,hi=1;
            if(!ClipLinear(da-Near,db-Near,ref lo,ref hi)||!ClipLinear(Far-da,Far-db,ref lo,ref hi))return false;
            var start=a;var delta=b-a;a=start+delta*lo;b=start+delta*hi;return hi-lo>1e-12;
        }
        public List<P3> ClipPolygon(IEnumerable<P3> input)
        {
            var points=input.ToList();
            foreach(bool front in new[]{true,false})
            {
                var output=new List<P3>();if(points.Count==0)return output;
                var a=points[points.Count-1];double fa=front?Depth(a)-Near:Far-Depth(a);
                foreach(var b in points)
                {
                    double fb=front?Depth(b)-Near:Far-Depth(b);
                    if((fa>=0)!=(fb>=0))output.Add(a+(b-a)*(fa/(fa-fb)));
                    if(fb>=0)output.Add(b);a=b;fa=fb;
                }
                points=output;
            }
            return points;
        }
        internal static bool ClipLinear(double a,double b,ref double lo,ref double hi)
        {
            if(a<0&&b<0)return false;
            if(a>=0&&b>=0)return true;
            double t=a/(a-b);if(a<0)lo=Math.Max(lo,t);else hi=Math.Min(hi,t);return lo<=hi;
        }
    }
    public sealed class ProjectedTriangle
    {
        public readonly P2 A,B,C;
        public readonly double MinX,MinY,MaxX,MaxY;
        readonly double area;
        public ProjectedTriangle(P2 a,P2 b,P2 c)
        {
            if(Cross(b.X-a.X,b.Y-a.Y,c.X-a.X,c.Y-a.Y)<0){var temp=b;b=c;c=temp;}
            A=a;B=b;C=c;area=Cross(b.X-a.X,b.Y-a.Y,c.X-a.X,c.Y-a.Y);
            MinX=Math.Min(a.X,Math.Min(b.X,c.X));MaxX=Math.Max(a.X,Math.Max(b.X,c.X));
            MinY=Math.Min(a.Y,Math.Min(b.Y,c.Y));MaxY=Math.Max(a.Y,Math.Max(b.Y,c.Y));
        }
        static double Cross(double ax,double ay,double bx,double by){return ax*by-ay*bx;}
        double At(P2 p)
        {
            double wa=Cross(B.X-p.X,B.Y-p.Y,C.X-p.X,C.Y-p.Y)/area;
            double wb=Cross(C.X-p.X,C.Y-p.Y,A.X-p.X,A.Y-p.Y)/area;
            return wa*A.InvDepth+wb*B.InvDepth+(1-wa-wb)*C.InvDepth;
        }
        public bool HiddenInterval(P2 start,P2 end,double worldBias,out double lo,out double hi)
        {
            lo=0;hi=1;if(area<=1e-20)return false;
            var vertices=new[]{A,B,C};
            for(int i=0;i<3;i++)
            {
                var a=vertices[i];var b=vertices[(i+1)%3];
                double f0=Cross(b.X-a.X,b.Y-a.Y,start.X-a.X,start.Y-a.Y);
                double f1=Cross(b.X-a.X,b.Y-a.Y,end.X-a.X,end.Y-a.Y);
                if(!PerspectiveCamera.ClipLinear(f0,f1,ref lo,ref hi))return false;
            }
            // Inverse depth is affine in image space for triangles AND straight edges.
            double q=Math.Max(start.InvDepth,end.InvDepth);
            double bias=Math.Max(1e-12,worldBias*q*q);
            if(!PerspectiveCamera.ClipLinear(At(start)-start.InvDepth-bias,At(end)-end.InvDepth-bias,ref lo,ref hi))return false;
            return hi-lo>1e-10;
        }
    }
    public struct PerspectiveSpan
    {
        public double Start,End;public bool Hidden;
        public PerspectiveSpan(double start,double end,bool hidden){Start=start;End=end;Hidden=hidden;}
    }
    public sealed class TriangleIndex
    {
        sealed class Node
        {
            public double MinX,MinY,MaxX,MaxY;
            public Node Left,Right;
            public ProjectedTriangle[] Items;
        }
        readonly Node root;
        public TriangleIndex(IEnumerable<ProjectedTriangle> triangles){root=Build(triangles.ToArray());}
        static Node Build(ProjectedTriangle[] items)
        {
            if(items.Length==0)return null;
            var n=new Node{MinX=items.Min(x=>x.MinX),MaxX=items.Max(x=>x.MaxX),MinY=items.Min(x=>x.MinY),MaxY=items.Max(x=>x.MaxY)};
            if(items.Length<=12){n.Items=items;return n;}
            bool xAxis=n.MaxX-n.MinX>=n.MaxY-n.MinY;
            Array.Sort(items,(a,b)=>(xAxis?a.MinX+a.MaxX:a.MinY+a.MaxY).CompareTo(xAxis?b.MinX+b.MaxX:b.MinY+b.MaxY));
            int mid=items.Length/2;n.Left=Build(items.Take(mid).ToArray());n.Right=Build(items.Skip(mid).ToArray());return n;
        }
        static IEnumerable<ProjectedTriangle> Query(Node n,double minX,double minY,double maxX,double maxY)
        {
            if(n==null||n.MaxX<minX||n.MinX>maxX||n.MaxY<minY||n.MinY>maxY)yield break;
            if(n.Items!=null){foreach(var item in n.Items)yield return item;yield break;}
            foreach(var item in Query(n.Left,minX,minY,maxX,maxY))yield return item;
            foreach(var item in Query(n.Right,minX,minY,maxX,maxY))yield return item;
        }
        public List<PerspectiveSpan> Classify(P2 a,P2 b,double bias)
        {
            var hidden=new List<PerspectiveSpan>();
            foreach(var triangle in Query(root,Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)))
            {
                double lo,hi;if(triangle.HiddenInterval(a,b,bias,out lo,out hi))hidden.Add(new PerspectiveSpan(lo,hi,true));
            }
            hidden.Sort((x,y)=>x.Start.CompareTo(y.Start));var merged=new List<PerspectiveSpan>();
            foreach(var span in hidden)
            {
                if(merged.Count==0||span.Start>merged[merged.Count-1].End+1e-9)merged.Add(span);
                else{var last=merged[merged.Count-1];last.End=Math.Max(last.End,span.End);merged[merged.Count-1]=last;}
            }
            var result=new List<PerspectiveSpan>();double cursor=0;
            foreach(var span in merged){if(span.Start>cursor+1e-9)result.Add(new PerspectiveSpan(cursor,span.Start,false));result.Add(span);cursor=span.End;}
            if(cursor<1-1e-9)result.Add(new PerspectiveSpan(cursor,1,false));return result;
        }
    }
}
