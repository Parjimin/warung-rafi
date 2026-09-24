using WarungRafi.RecoveryChecks;

if(args.Length>0 && args[0]=="--worker")
{
    await CrashWorker.Run(args[1],args[2],args[3]);
    return;
}
var suite=new RecoverySuite();
await suite.Run();
