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
        var document=new FlowDocument
        {
            PageWidth=pageWidth,PageHeight=pageHeight,ColumnWidth=double.PositiveInfinity,
            ColumnGap=0,PagePadding=padding,
            FontFamily=new FontFamily("Consolas"),FontSize=11
        };
        foreach(var line in Receipt.Build(sale,copy))
            document.Blocks.Add(new Paragraph(new Run(line.Text)) { Margin=new Thickness(0,1,0,1),FontWeight=line.Emphasized?FontWeights.Bold:FontWeights.Normal });
        var dialog=new PrintDialog { PrintQueue=queue,PrintTicket=ticket };
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,$"Warung Rafi {sale.Order.Number}");
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
