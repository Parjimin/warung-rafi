namespace WarungRafi.Core;

public static class OrderRules
{
    public static void Validate(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if(!Enum.IsDefined(order.Status) || order.Version<0 || !Guid.TryParseExact(order.Id,"N",out _) ||
            string.IsNullOrWhiteSpace(order.Number) || order.Number.Length>80 || order.CustomerLabel is null || order.CustomerLabel.Length>60 ||
            order.Lines is null || order.Lines.Length>300)
            throw new ArgumentException("Data pesanan tidak valid.");
        var ids=new HashSet<string>();
        foreach(var line in order.Lines)
        {
            if(line is null || string.IsNullOrWhiteSpace(line.ProductId) || line.ProductId.Length>80 || !ids.Add(line.ProductId) ||
                string.IsNullOrWhiteSpace(line.Name) || line.Name.Length>60 || string.IsNullOrWhiteSpace(line.Category) ||
                line.UnitPrice is <1 or >1_000_000_000 || line.Quantity is <1 or >9999)
                throw new ArgumentException("Rincian pesanan tidak valid.");
        }
        if(order.Status is OrderStatus.Held or OrderStatus.Completed && order.Lines.Length==0)
            throw new ArgumentException("Pesanan ini harus memiliki rincian.");
        if(order.Status==OrderStatus.Cancelled && string.IsNullOrWhiteSpace(order.CancellationReason))
            throw new ArgumentException("Alasan pembatalan wajib diisi.");
        _=order.Total;
    }
    public static Order Add(Order order, Product product)
    {
        Editable(order);
        if (!product.Available) throw new InvalidOperationException("Menu ini sedang habis.");
        if (product.Price <= 0 || product.Price > 1_000_000_000) throw new ArgumentException("Harga menu tidak valid.");
        var lines = order.Lines.ToList();
        var index = lines.FindIndex(x => x.ProductId == product.Id);
        if (index < 0) lines.Add(new(product.Id, product.Name, product.Category, product.Price, 1));
        else
        {
            if (lines[index].Quantity >= 9999) throw new InvalidOperationException("Jumlah item terlalu besar.");
            lines[index] = lines[index] with { Quantity = lines[index].Quantity + 1 };
        }
        return Changed(order, lines.ToArray());
    }

    public static Order Reduce(Order order, string productId)
    {
        Editable(order);
        var lines = order.Lines.Select(x => x.ProductId == productId ? x with { Quantity = x.Quantity - 1 } : x)
            .Where(x => x.Quantity > 0).ToArray();
        return Changed(order, lines);
    }

    public static Order Hold(Order order, string label)
    {
        Editable(order);
        if (order.Lines.Length == 0) throw new InvalidOperationException("Pilih makanan terlebih dahulu.");
        return order with { Status = OrderStatus.Held, CustomerLabel = label.Trim()[..Math.Min(label.Trim().Length, 60)], UpdatedAt = DateTimeOffset.UtcNow };
    }

    public static Order Resume(Order order)
    {
        Editable(order);
        return order with { Status = OrderStatus.Draft, UpdatedAt = DateTimeOffset.UtcNow };
    }

    public static Order Cancel(Order order, string reason)
    {
        Editable(order);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Alasan pembatalan wajib diisi.");
        return order with { Status = OrderStatus.Cancelled, CancellationReason = reason.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
    }

    public static CompletedSale Complete(Order order, PaymentMethod method, long tendered)
    {
        Validate(order);
        Editable(order);
        if(!Enum.IsDefined(method))throw new ArgumentException("Metode pembayaran tidak valid.");
        if (order.Lines.Length == 0 || order.Total <= 0) throw new InvalidOperationException("Pesanan masih kosong.");
        if (method == PaymentMethod.Cash && tendered < order.Total) throw new InvalidOperationException("Uang diterima masih kurang.");
        var now = DateTimeOffset.UtcNow;
        var paid = method == PaymentMethod.Cash ? tendered : order.Total;
        return new(order with { Status = OrderStatus.Completed, UpdatedAt = now },
            new(order.Id, method, order.Total, paid, checked(paid - order.Total), now));
    }

    private static Order Changed(Order order, OrderLine[] lines)
    {
        var changed = order with { Lines = lines, UpdatedAt = DateTimeOffset.UtcNow };
        _ = changed.Total;
        return changed;
    }
    private static void Editable(Order order)
    {
        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new InvalidOperationException("Pesanan ini sudah selesai atau dibatalkan.");
    }
}
