using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.RecoveryChecks;

internal static class CrashWorker
{
    public static async Task Run(string stage,string path,string orderId)
    {
        var store=new LocalStore(path,connection=>
        {
            connection.CreateFunction("pause_for_parent",Pause);
            TestDatabase.Execute(connection,"PRAGMA cache_size=1; PRAGMA cache_spill=ON; PRAGMA wal_autocheckpoint=0;");
            var sql=stage switch
            {
                "during-save" => "CREATE TEMP TRIGGER stop_save AFTER INSERT ON main.outbox BEGIN INSERT INTO test_spill VALUES(zeroblob(200000)); SELECT pause_for_parent(); END;",
                "before-payment-commit" => "CREATE TEMP TRIGGER stop_payment AFTER INSERT ON main.payments BEGIN INSERT INTO test_spill VALUES(zeroblob(200000)); SELECT pause_for_parent(); END;",
                "during-ack" => "CREATE TEMP TRIGGER stop_ack AFTER UPDATE OF acknowledged ON main.outbox BEGIN INSERT INTO test_spill VALUES(zeroblob(200000)); SELECT pause_for_parent(); END;",
                "after-payment-commit" => "SELECT 1;",
                _ => throw new ArgumentException("Unknown stage")
            };
            TestDatabase.Execute(connection,sql);
        });
        var order=await store.OrderAsync(orderId)??throw new Exception("Missing test order");
        if(stage=="during-save")await store.SaveAsync(OrderRules.Add(order,TestDatabase.Product));
        else if(stage=="during-ack")await store.AcknowledgeAsync((await store.PendingAsync()).Select(x=>x.Id).ToArray());
        else
        {
            await store.CompleteAsync(order.Id,order.Version,PaymentMethod.Cash,10000);
            if(stage=="after-payment-commit")Pause();
        }
        throw new Exception("Worker should have been killed at its rendezvous.");
    }
    private static int Pause()
    {
        Console.WriteLine("READY_TO_KILL");Console.Out.Flush();
        using var signal=new ManualResetEvent(false);signal.WaitOne();
        return 0;
    }
    public static async Task KillAt(string stage,string path,string orderId)
    {
        var info=new ProcessStartInfo(Environment.ProcessPath??throw new Exception("Missing executable path"))
        {
            UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true
        };
        if(System.IO.Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach(var argument in new[]{"--worker",stage,path,orderId})info.ArgumentList.Add(argument);
        using var process=Process.Start(info)??throw new Exception("Worker did not start");
        var error=process.StandardError.ReadToEndAsync();
        try
        {
            var marker=await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if(marker!="READY_TO_KILL")throw new Exception($"Worker {stage} exited before checkpoint: {await error}");
            // Kill only the child started above. No graceful shutdown/disposal can run in it.
            process.Kill(entireProcessTree:true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if(process.ExitCode==0)throw new Exception("Expected forced termination");
        }
        finally
        {
            if(!process.HasExited) { process.Kill(entireProcessTree:true);await process.WaitForExitAsync(); }
        }
    }
}
