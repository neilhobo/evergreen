using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using DevExpress.XtraPrinting;
//using DevExpress.XtraReports.UI;

namespace Globeweigh.Reports
{
    public static class ReportHelper
    {
        //public static void AutoscaleControlText(XRControl control)
        //{
        //    float controlWidth = control.SizeF.Width;
        //    float controlHeight = control.SizeF.Height;

        //    while (MeasureTextWidthPixels(((XtraReport)control.Report.Report).ReportUnit, control.Text, control.Font) > controlWidth)
        //        control.Font = new Font(control.Font.FontFamily, control.Font.Size - 0.1f, control.Font.Style);
        //}

        //public static float MeasureTextWidthPixels(ReportUnit unit, string text, Font font)
        //{
        //    Graphics gr = Graphics.FromHwnd(IntPtr.Zero);

        //    int factor;
        //    if (unit == ReportUnit.HundredthsOfAnInch)
        //    {
        //        gr.PageUnit = GraphicsUnit.Inch;
        //        factor = 100;
        //    }
        //    else
        //    {
        //        gr.PageUnit = GraphicsUnit.Millimeter;
        //        factor = 10;
        //    }

        //    SizeF size = gr.MeasureString(text, font);
        //    gr.Dispose();

        //    float s = size.Width * factor;
        //    return s;
        //}


        //public static void AutoscaleControlTextMutiliine(XRControl control, ReportUnit reportUnit)
        //{
        //    while (GetTextHeightMutiliine(control, reportUnit) > control.HeightF)
        //    {
        //        control.Font = new Font(control.Font.FontFamily, control.Font.Size - 0.1f, control.Font.Style);
        //    }
        //}

        //private static float GetTextHeightMutiliine(XRControl label, ReportUnit reportUnit)
        //{

        //    StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
        //    format.FormatFlags = System.Drawing.StringFormatFlags.FitBlackBox | System.Drawing.StringFormatFlags.LineLimit | System.Drawing.StringFormatFlags.NoClip;

        //    String text = label.Text;
        //    SizeF textSize = SizeF.Empty;
        //    float height = 0.0F;
        //    switch (reportUnit)
        //    {
        //        case DevExpress.XtraReports.UI.ReportUnit.HundredthsOfAnInch:
        //            textSize = BrickGraphics.MeasureString(text, label.Font, (int)(label.Width / 100), format, GraphicsUnit.Inch);
        //            height = textSize.Height * 100;
        //            break;
        //        case DevExpress.XtraReports.UI.ReportUnit.TenthsOfAMillimeter:
        //            textSize = BrickGraphics.MeasureString(text, label.Font, (int)(label.Width / 10), format, GraphicsUnit.Inch);
        //            height = textSize.Height * 10;
        //            break;
        //        case DevExpress.XtraReports.UI.ReportUnit.Pixels:
        //            textSize = BrickGraphics.MeasureString(text, label.Font, (int)(label.Width), format, GraphicsUnit.Pixel);
        //            height = textSize.Height;
        //            break;
        //    }


        //    return height;
        //}

        //public static void Test()
        //{
        //}
    }
}
