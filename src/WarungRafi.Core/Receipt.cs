namespace WarungRafi.Core;

public static class Receipt
{
    public static IReadOnlyList<ReceiptLine> Build(CompletedSale sale, bool copy)
    {
        var lines = new List<ReceiptLine>
        {
            new("Wedangan dan Aneka Nasi Sayur", true), new("Depan SMK Negeri 2 Surakarta MANAHAN"),
            new(sale.Order.Number), new(sale.Payment.PaidAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm 'WIB'")),
            new(copy ? "SALINAN — LUNAS" : "LUNAS", true),
            new(sale.Payment.Method == PaymentMethod.Cash ? "Pembayaran: TUNAI" : "Pembayaran: QRIS"), new("")
        };
        foreach (var item in sale.Order.Lines)
        {
            lines.Add(new(item.Name));
            lines.Add(new($"{item.Quantity} x {Money.Format(item.UnitPrice)}    {Money.Format(item.Subtotal)}"));
        }
        lines.Add(new(""));
        lines.Add(new($"TOTAL {Money.Format(sale.Order.Total)}", true));
        if (sale.Payment.Method == PaymentMethod.Cash)
        {
            lines.Add(new($"Tunai {Money.Format(sale.Payment.Tendered)}"));
            lines.Add(new($"Kembalian {Money.Format(sale.Payment.Change)}", true));
        }
        lines.Add(new("Terima kasih. Selamat menikmati!"));
        return lines;
    }
}
