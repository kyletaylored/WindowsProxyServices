using System;
using System.ServiceProcess;

namespace WindowsProxyService
{
    static class Program
    {
        static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                var svc = new Service1();
                svc.GetType().GetMethod("OnStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(svc, new object[] { args });

                Console.WriteLine("Running (console mode). Press Enter to stop.");
                Console.ReadLine();

                svc.GetType().GetMethod("OnStop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(svc, null);
            }
            else
            {
                ServiceBase.Run(new Service1());
            }
        }
    }
}