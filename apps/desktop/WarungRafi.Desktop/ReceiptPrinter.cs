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
    public static async Task PrintAsync(CompletedSale sale,bool copy)
    {
        await Queue.WaitAsync();
        try
        {
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread=new Thread(()=>
            {
                try { PrintOnSta(sale,copy);completion.SetResult(); }
                catch(Exception error) { completion.SetException(error); }
            }) { IsBackground=true,Name="WarungRafi.ReceiptPrinter" };
            thread.SetApartmentState(ApartmentState.STA);thread.Start();await completion.Task;
        }
        finally { Queue.Release(); }
    }
    private static void PrintOnSta(CompletedSale sale,bool copy)
    {
        var printerName=Environment.GetEnvironmentVariable("WARUNG_PRINTER_NAME");
        if(string.IsNullOrWhiteSpace(printerName))throw new InvalidOperationException("Printer belum dipilih. Atur WARUNG_PRINTER_NAME sesuai nama printer Windows.");
        using var server=new LocalPrintServer();
        using var queue=server.GetPrintQueue(printerName);
        var width=58d/25.4*96;
        var document=new FlowDocument
        {
            PageWidth=width,ColumnWidth=width,PagePadding=new Thickness(7),
            FontFamily=new FontFamily("Consolas"),FontSize=11
        };
        foreach(var line in Receipt.Build(sale,copy))
            document.Blocks.Add(new Paragraph(new Run(line.Text)) { Margin=new Thickness(0,1,0,1),FontWeight=line.Emphasized?FontWeights.Bold:FontWeights.Normal });
        var dialog=new PrintDialog { PrintQueue=queue,PrintTicket=new PrintTicket { PageMediaSize=new PageMediaSize(width,1000),PageOrientation=PageOrientation.Portrait } };
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,$"Warung Rafi {sale.Order.Number}");
    }
}
