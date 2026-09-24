using System.Globalization;

namespace WarungRafi.Core;

public enum OrderStatus { Draft, Held, Completed, Cancelled }
public enum PaymentMethod { Cash, QrisManual }
public sealed record Product(string Id, string Name, string Category, long Price, bool Available = true, string? ImageUrl = null);
public sealed record OrderLine(string ProductId, string Name, string Category, long UnitPrice, int Quantity)
{
    public long Subtotal => checked(UnitPrice * Quantity);
    public string PriceLabel => Money.Format(UnitPrice);
    public string SubtotalLabel => Money.Format(Subtotal);
}
public sealed record Order(string Id, string Number, long Version, OrderStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, string CustomerLabel, OrderLine[] Lines, string? CancellationReason = null)
{
    public long Total => Lines.Aggregate(0L, (total, line) => checked(total + line.Subtotal));
    public string TotalLabel => Money.Format(Total);
    public string Summary => string.Join(", ", Lines.Take(3).Select(x => $"{x.Quantity} {x.Name}"));
    public static Order New() => New(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
    public static Order New(string id, DateTimeOffset now) => new(id,
        $"WR-{now.ToOffset(TimeSpan.FromHours(7)):yyMMdd}-{id[..8].ToUpperInvariant()}",
        0, OrderStatus.Draft, now, now, "", []);
}
public sealed record Payment(string OrderId, PaymentMethod Method, long Amount, long Tendered, long Change, DateTimeOffset PaidAt);
public sealed record CompletedSale(Order Order, Payment Payment);
public sealed record OutboxEvent(string Id, string AggregateId, long Version, string Payload, DateTimeOffset CreatedAt);
public sealed record ProviderPayment(long Sequence, string TransactionId, long Amount, DateTimeOffset PaidAt);
public sealed record CatalogSnapshot(long Version, Product[] Products);
public sealed record DailySales(long Cash, long Qris, long Count);
public sealed record ReceiptLine(string Text, bool Emphasized = false);

public static class Money
{
    public static string Format(long amount) => "Rp" + amount.ToString("N0", CultureInfo.GetCultureInfo("id-ID"));
    public static long ParseInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (text.Any(c => !char.IsAsciiDigit(c))) throw new ArgumentException("Isi nominal dengan angka saja.");
        if (!long.TryParse(text, out var amount) || amount > 1_000_000_000) throw new ArgumentException("Nominal terlalu besar.");
        return amount;
    }
}
