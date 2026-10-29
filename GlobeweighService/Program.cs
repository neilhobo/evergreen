using System;
using System.ServiceProcess;
using System.Threading;
using GlobeweighService;

static class Program
{
    static void Main()
    {
#if DEBUG
        AppConfig.ConnectionString =
            @"metadata=res://*/Model1.csdl|res://*/Model1.ssdl|res://*/Model1.msl;
          provider=System.Data.SqlClient;
          provider connection string=""Server=evergreen\NTPRO;Database=GbEvergreen;User Id=globeweigh;Password=ButternutSquash09;MultipleActiveResultSets=True;""";
#else
    AppConfig.ConnectionString =
        @"metadata=res://*/Model1.csdl|res://*/Model1.ssdl|res://*/Model1.msl;
          provider=System.Data.SqlClient;
          provider connection string=""Server=localhost\NTPRO;Database=GbEvergreen;User Id=globeweigh;Password=ButternutSquash09;MultipleActiveResultSets=True;""";
#endif

#if DEBUG
        ConsoleHelper.EnsureConsole();
        Console.Title = "Globeweigh Scanner Service (DEBUG MODE)";
        Console.WriteLine("Starting in console mode...\n");

        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("\nStopping...");
            e.Cancel = true;
            cts.Cancel();
        };

        ServiceHost.RunAsync(cts.Token).GetAwaiter().GetResult();
#else
    ServiceBase.Run(new BarcodeService());
#endif
    }
}