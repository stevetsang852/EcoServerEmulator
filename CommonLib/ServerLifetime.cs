using System;
using System.Threading;

namespace CommonLib
{
    public static class ServerLifetime
    {
        public static void WaitForShutdown()
        {
            using (var shutdown = new ManualResetEvent(false))
            {
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    shutdown.Set();
                };
                shutdown.WaitOne();
            }
        }
    }
}
