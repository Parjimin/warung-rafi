namespace WarungRafi.Core;

public static class PaymentAlertPolicy
{
    public static bool IsFresh(ProviderPayment proof, DateTimeOffset now)
    {
        var age=now-proof.PaidAt;
        return age>=TimeSpan.Zero && age<=TimeSpan.FromSeconds(60);
    }
}
