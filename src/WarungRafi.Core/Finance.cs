using System.Security.Cryptography;
using System.Text.Json;

namespace WarungRafi.Core;

public sealed record CashSession(string Id,DateTimeOffset OpenedAt,long OpeningCash,string OpenedBy,
    DateTimeOffset? ClosedAt=null,long? CountedCash=null,long? ExpectedAtClose=null,string ClosingNote="")
{
    public long? Difference=>CountedCash-ExpectedAtClose;
}
public sealed record CashPosition(CashSession Session,long CashSales,long QrisSales,long CashIn,long CashOut,long CashRefunds,long SalesRefunds,long SaleCount)
{
    public long Expected=>checked(Session.OpeningCash+CashSales+CashIn-CashOut-CashRefunds);
    public long Gross=>checked(CashSales+QrisSales);
}
public enum RefundState { Requested,Completed,Failed }
public enum RefundChannel { Cash,External }
public sealed record Refund(string Id,string OrderId,long Amount,RefundChannel Channel,string Reason,
    RefundState State,DateTimeOffset RequestedAt,DateTimeOffset? CompletedAt=null,string? SessionId=null,
    string ApprovedBy="",string Reference="",string FailureReason="");
public sealed record FinanceEvent(string Id,long Sequence,string Kind,DateTimeOffset OccurredAt,
    string? SessionId,string? OrderId,long Amount,long CashDelta,string Actor,string Reason,JsonElement Details);

public static class FinanceRules
{
    public static void Amount(long value,bool allowZero=false)
    { if(value<(allowZero?0:1)||value>1_000_000_000)throw new ArgumentException("Nominal harus berupa rupiah bulat antara "+(allowZero?"0":"1")+" dan 1 miliar."); }
    public static void Id(string value)
    { if(!Guid.TryParseExact(value,"N",out _))throw new ArgumentException("Identitas tindakan tidak valid."); }
    public static string Note(string value)
    { var clean=value.Trim();if(clean.Length is <3 or >200)throw new ArgumentException("Isi keterangan 3–200 karakter.");return clean; }
    public static long EstimateFee(long amount,int basisPoints)
    {
        if(amount<=0||amount>999_999_999_999||basisPoints is <0 or >10000)throw new ArgumentException("Tarif/nominal tidak valid.");
        return checked((long)decimal.Round(amount*(decimal)basisPoints/10000,0,MidpointRounding.AwayFromZero));
    }
}

public static class ManagerPin
{
    public static string Hash(string pin)
    {
        if(pin.Length is <6 or >12 || pin.Any(c=>!char.IsAsciiDigit(c)))throw new ArgumentException("PIN harus 6–12 angka.");
        var salt=RandomNumberGenerator.GetBytes(16);
        var hash=Rfc2898DeriveBytes.Pbkdf2(pin,salt,600000,HashAlgorithmName.SHA256,32);
        return $"pbkdf2-sha256$600000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string? encoded,string pin)
    {
        if(encoded is null||pin.Length is <6 or >12||pin.Any(c=>!char.IsAsciiDigit(c)))return false;
        try
        {
            var parts=encoded.Split('$');
            if(parts.Length!=4||parts[0]!="pbkdf2-sha256"||parts[1]!="600000")return false;
            var salt=Convert.FromBase64String(parts[2]);var expected=Convert.FromBase64String(parts[3]);
            if(salt.Length!=16||expected.Length!=32)return false;
            return CryptographicOperations.FixedTimeEquals(expected,Rfc2898DeriveBytes.Pbkdf2(pin,salt,600000,HashAlgorithmName.SHA256,32));
        }
        catch(FormatException){return false;}
    }
}
