// Reproducible vector geometry matching assets/workbench.svg. No downloaded artwork.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
class IconBuilder {
    static Bitmap Draw(int size){
        var b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(b)){
            g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/256f,size/256f);g.Clear(Color.Transparent);
            using(var path=new GraphicsPath())using(var bg=new SolidBrush(Color.FromArgb(24,37,56))){
                path.AddArc(8,8,104,104,180,90);path.AddArc(144,8,104,104,270,90);path.AddArc(144,144,104,104,0,90);path.AddArc(8,144,104,104,90,90);path.CloseFigure();g.FillPath(bg,path);
            }
            using(var pen=new Pen(Color.FromArgb(98,203,181),size<=24?26:22)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                g.DrawLines(pen,new[]{new PointF(56,76),new PointF(56,168),new PointF(96,168),new PointF(128,112),new PointF(160,168),new PointF(200,168),new PointF(200,76)});
            }
            using(var brush=new SolidBrush(Color.FromArgb(246,248,252)))g.FillEllipse(brush,114,98,28,28);
        }return b;
    }
    static void Main(string[] args){
        string folder=args[0];Directory.CreateDirectory(folder);int[] sizes={16,20,24,32,40,48,64,128,256};var pngs=new List<byte[]>();
        foreach(int size in sizes)using(var b=Draw(size))using(var m=new MemoryStream()){b.Save(m,ImageFormat.Png);pngs.Add(m.ToArray());if(size==256)b.Save(Path.Combine(folder,"workbench.png"),ImageFormat.Png);}
        using(var w=new BinaryWriter(File.Create(Path.Combine(folder,"workbench.ico")))){
            w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(pngs[i].Length);w.Write(offset);offset+=pngs[i].Length;}
            foreach(var png in pngs)w.Write(png);
        }
    }
}
