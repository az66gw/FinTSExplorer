using FinTSExplorer.Service;
using Serilog;

namespace FinTSExplorer.Service
{
    public class Program
    {
        private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

        public static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            Log.Logger = new LoggerConfiguration()
                        .WriteTo.Console(outputTemplate: OutputTemplate)
                        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30, outputTemplate: OutputTemplate)
                        .CreateLogger();

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Log.Information("FinTSExplorer.Service v{Version} started.", version);

            try
            {
                var builder = Host.CreateApplicationBuilder(args);

                builder.Services.AddSerilog();
                builder.Services.AddWindowsService(options => options.ServiceName = "FinTSExplorerService");
                builder.Services.AddHostedService<Worker>();

                var host = builder.Build();

                Log.Information("Application started.");
                host.Run();
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
