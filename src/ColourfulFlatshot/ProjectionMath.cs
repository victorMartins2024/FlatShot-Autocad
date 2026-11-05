using System;
using System.Collections.Generic;

namespace ColourfulFlatshot
{
    // Independent of AutoCAD: projected XY with Z increasing toward the viewer.
    public struct Vertex
    {
        public double X, Y, Z;
        public Vertex(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vertex At(Vertex a, Vertex b, double t)
        { return new Vertex(a.X+(b.X-a.X)*t, a.Y+(b.Y-a.Y)*t, a.Z+(b.Z-a.Z)*t); }
    }
    public struct Span
    {
        public double Start, End;
        public Span(double start, double end) { Start=start; End=end; }
    }
    public sealed class Triangle
    {
        public readonly Vertex A,B,C;
        public readonly double MinX,MaxX,MinY,MaxY,MaxZ,Area;
        public Triangle(Vertex a,Vertex b,Vertex c)
        {
            A=a;B=b;C=c;
            MinX=Math.Min(a.X,Math.Min(b.X,c.X));MaxX=Math.Max(a.X,Math.Max(b.X,c.X));
            MinY=Math.Min(a.Y,Math.Min(b.Y,c.Y));MaxY=Math.Max(a.Y,Math.Max(b.Y,c.Y));
            MaxZ=Math.Max(a.Z,Math.Max(b.Z,c.Z));
            Area=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        }
    }
    public static class ProjectionMath
    {
        private static double Cross(double ax,double ay,double bx,double by) { return ax*by-ay*bx; }
        // Restricts [lo,hi] to the linear inequality f(t) >= 0.
        private static bool Clip(double f0,double f1,ref double lo,ref double hi)
        {
            if(f0<0 && f1<0)return false;
            if(f0>=0 && f1>=0)return true;
            double t=f0/(f0-f1);
            if(f0<0)lo=Math.Max(lo,t);else hi=Math.Min(hi,t);
            return hi>lo;
        }
        public static bool OccludedSpan(Vertex p,Vertex q,Triangle tri,double tolerance,out Span span)
        {
            span=new Span();
            if(Math.Max(p.X,q.X)<tri.MinX || Math.Min(p.X,q.X)>tri.MaxX || Math.Max(p.Y,q.Y)<tri.MinY || Math.Min(p.Y,q.Y)>tri.MaxY)return false;
            double area=tri.Area;
            if(Math.Abs(area)<tolerance*tolerance)return false;
            double lo=0,hi=1;
            if(!ClipSide(tri.A,tri.B,p,q,area,ref lo,ref hi)
                || !ClipSide(tri.B,tri.C,p,q,area,ref lo,ref hi)
                || !ClipSide(tri.C,tri.A,p,q,area,ref lo,ref hi))return false;
            double d0=Depth(p,tri,area)-p.Z-tolerance;
            double d1=Depth(q,tri,area)-q.Z-tolerance;
            if(!Clip(d0,d1,ref lo,ref hi) || hi-lo<1e-10)return false;
            span=new Span(lo,hi);return true;
        }
        private static bool ClipSide(Vertex a,Vertex b,Vertex p,Vertex q,double area,ref double lo,ref double hi)
        {
            double f0=Cross(b.X-a.X,b.Y-a.Y,p.X-a.X,p.Y-a.Y)/area;
            double f1=Cross(b.X-a.X,b.Y-a.Y,q.X-a.X,q.Y-a.Y)/area;
            return Clip(f0,f1,ref lo,ref hi);
        }
        private static double Depth(Vertex p,Triangle t,double area)
        {
            double b=Cross(p.X-t.A.X,p.Y-t.A.Y,t.C.X-t.A.X,t.C.Y-t.A.Y)/area;
            double c=Cross(t.B.X-t.A.X,t.B.Y-t.A.Y,p.X-t.A.X,p.Y-t.A.Y)/area;
            return t.A.Z+(t.B.Z-t.A.Z)*b+(t.C.Z-t.A.Z)*c;
        }
        public static List<Span> Merge(List<Span> spans)
        {
            spans.Sort((a,b)=>a.Start.CompareTo(b.Start));
            var result=new List<Span>();
            foreach(var span in spans)
            {
                if(result.Count==0 || span.Start>result[result.Count-1].End+1e-10)result.Add(span);
                else {var previous=result[result.Count-1];previous.End=Math.Max(previous.End,span.End);result[result.Count-1]=previous;}
            }
            return result;
        }
    }
}
