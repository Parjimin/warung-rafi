using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WarungRafi.Core;

namespace WarungRafi.Desktop;

internal static class ReceiptPrinter
{
    private static readonly SemaphoreSlim Queue=new(1,1);
    public static async Task PrintAsync(CompletedSale sale,bool copy,string? configuredPrinter=null)
    {
        await Queue.WaitAsync();
        try
        {
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread=new Thread(()=>
            {
                try { PrintOnSta(sale,copy,configuredPrinter);completion.SetResult(); }
                catch(Exception error) { completion.SetException(error); }
            }) { IsBackground=true,Name="WarungRafi.ReceiptPrinter" };
            thread.SetApartmentState(ApartmentState.STA);thread.Start();await completion.Task;
        }
        finally { Queue.Release(); }
    }
    private static void PrintOnSta(CompletedSale sale,bool copy,string? configuredPrinter)
    {
        var printerName=configuredPrinter??Environment.GetEnvironmentVariable("WARUNG_PRINTER_NAME");
        if(string.IsNullOrWhiteSpace(printerName))throw new InvalidOperationException("Printer belum dipilih. Pilih printer Windows melalui Pengaturan.");
        using var server=new LocalPrintServer();
        using var queue=server.GetPrintQueue(printerName);
        var width=58d/25.4*96;
        var requested=new PrintTicket { PageMediaSize=new PageMediaSize(width,1000),PageOrientation=PageOrientation.Portrait };
        var ticket=queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket,requested).ValidatedPrintTicket;
        var capabilities=queue.GetPrintCapabilities(ticket);
        var pageWidth=ticket.PageMediaSize?.Width??width;
        var pageHeight=ticket.PageMediaSize?.Height??1000;
        var area=capabilities.PageImageableArea;
        var padding=SafePadding(pageWidth,pageHeight,area?.OriginWidth??0,area?.OriginHeight??0,
            area?.ExtentWidth??pageWidth,area?.ExtentHeight??pageHeight);
        var document=CreateDocument(sale,copy,pageWidth,pageHeight,padding);
        var dialog=new PrintDialog { PrintQueue=queue,PrintTicket=ticket };
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,$"Warung Rafi {sale.Order.Number}");
    }
    internal static FlowDocument CreateDocument(CompletedSale sale,bool copy,double width,double height,Thickness padding)
    {
        var doc=new FlowDocument
        {
            PageWidth=width,PageHeight=height,ColumnWidth=double.PositiveInfinity,
            ColumnGap=0,PagePadding=padding,FontFamily=new FontFamily("Arial"),
            FontSize=11,Foreground=Brushes.Black,Background=Brushes.White
        };
        Paragraph Text(string value,double size=11,bool bold=false,bool center=false)
        {
            var p=new Paragraph(new Run(value)) { FontSize=size,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,
                TextAlignment=center?TextAlignment.Center:TextAlignment.Left,Margin=new Thickness(0,1,0,1),KeepTogether=true };
            doc.Blocks.Add(p);return p;
        }
        void Rule(bool ornament=false)
        {
            if(ornament) { Text("─── ◇ ◇ ◇ ───",11,false,true).Margin=new Thickness(0,5,0,5);return; }
            doc.Blocks.Add(new BlockUIContainer(new Border { Height=1,Background=Brushes.Black }) { Margin=new Thickness(0,5,0,5) });
        }
        void Pair(string label,string value,double size=11,bool bold=false)
        {
            var table=new Table { CellSpacing=0,Margin=new Thickness(0,1,0,1),FontSize=size,
                FontWeight=bold?FontWeights.Bold:FontWeights.Normal };
            table.Columns.Add(new TableColumn { Width=new GridLength(1,GridUnitType.Star) });
            table.Columns.Add(new TableColumn { Width=new GridLength(1,GridUnitType.Star) });
            var row=new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run(label)) { Margin=new Thickness(0) }) { Padding=new Thickness(0) });
            row.Cells.Add(new TableCell(new Paragraph(new Run(value)) { Margin=new Thickness(0),TextAlignment=TextAlignment.Right }) { Padding=new Thickness(0) });
            var group=new TableRowGroup();group.Rows.Add(row);table.RowGroups.Add(group);doc.Blocks.Add(table);
        }
        void Contact(bool instagram,string value)
        {
            var p=new Paragraph { TextAlignment=TextAlignment.Center,Margin=new Thickness(0,3,0,0),FontSize=10 };
            p.Inlines.Add(new InlineUIContainer(ContactIcon(instagram)) { BaselineAlignment=BaselineAlignment.Center });
            p.Inlines.Add(new Run("  "+value));doc.Blocks.Add(p);
        }
        Rule(true);
        Text("WEDANGAN",19,true,true);
        Text("DAN ANEKA NASI SAYUR",11,true,true);
        Text("MURAH",19,true,true);
        Rule(true);
        Text("Depan SMK Negeri 2 Surakarta",10,false,true);
        Text("MANAHAN",10,false,true).Margin=new Thickness(0,1,0,8);
        Text("Pesanan",10);
        Text(sale.Order.Number,11,true);
        var paid=sale.Payment.PaidAt.ToOffset(TimeSpan.FromHours(7));
        Pair("Tanggal",paid.ToString("dd/MM/yyyy"));
        Pair("Jam",paid.ToString("HH:mm")+" WIB");
        Pair("Metode",sale.Payment.Method==PaymentMethod.Cash?"TUNAI":"QRIS");
        if(copy)Text("SALINAN",11,true,true);
        Rule();Text("PESANAN KAMU",11,true);Rule();
        foreach(var item in sale.Order.Lines)
        {
            Text(item.Name,11,true).Margin=new Thickness(0,4,0,1);
            Pair($"{item.Quantity} × {Money.Format(item.UnitPrice)}",Money.Format(item.Subtotal),10);
        }
        Rule();Pair("TOTAL BAYAR",Money.Format(sale.Order.Total),13,true);Rule();
        if(sale.Payment.Method==PaymentMethod.Cash)
        {
            Pair("Tunai",Money.Format(sale.Payment.Tendered));
            Pair("Kembalian",Money.Format(sale.Payment.Change),11,true);
        }
        Text("LUNAS",11,true,true).Margin=new Thickness(0,5,0,8);
        Text("Terima kasih!",11,true,true);
        Text("Selamat menikmati.",10,false,true).Margin=new Thickness(0,1,0,7);
        Contact(false,"+62 851-5650-4119");
        Contact(true,"@muh_rafi875");
        Rule(true);
        return doc;
    }
    private static Image ContactIcon(bool instagram)
    {
        // Vector outlines remain sharp and monochrome on the thermal printer;
        // no emoji font or external image download is required.
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            var pen=new Pen(Brushes.Black,1.3);
            if(instagram)
            {
                dc.DrawRoundedRectangle(null,pen,new Rect(1,1,12,12),3,3);
                dc.DrawEllipse(null,pen,new Point(7,7),2.7,2.7);
                dc.DrawEllipse(Brushes.Black,null,new Point(10.5,3.6),0.8,0.8);
            }
            else
            {
                dc.DrawGeometry(null,pen,Geometry.Parse("M 2,10 A 6,6 0 1 1 5,12 L 1,13 Z"));
                dc.DrawGeometry(null,new Pen(Brushes.Black,1.8) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round },
                    Geometry.Parse("M 4,4 L 4.8,5.5 L 4.3,6 Q 5.5,8.5 8,9 L 8.6,8.3 L 10,9"));
            }
        }
        return new Image { Source=new DrawingImage(visual.Drawing),Width=14,Height=14,Stretch=Stretch.Uniform };
    }
    // WPF coordinates are physical-page coordinates. Keep text inside the driver's
    // imageable rectangle, plus a guard for thermal drivers that report no margins.
    internal static Thickness SafePadding(double width,double height,double x,double y,double printableWidth,double printableHeight)
    {
        const double mm=96d/25.4;
        var left=Math.Max(5*mm,Math.Max(0,x)+mm);
        var rightEdge=Math.Min(width-5*mm,Math.Max(0,x)+printableWidth-mm);
        rightEdge=Math.Min(rightEdge,left+48*mm);
        var top=Math.Max(2*mm,Math.Max(0,y)+mm);
        var bottom=Math.Max(2*mm,height-(Math.Max(0,y)+printableHeight)+mm);
        if(rightEdge-left<25*mm||height-top-bottom<20*mm)
            throw new InvalidOperationException("Area cetak printer terlalu kecil. Pilih ukuran kertas struk 58 mm di pengaturan printer Windows.");
        return new Thickness(left,top,width-rightEdge,bottom);
    }
}
