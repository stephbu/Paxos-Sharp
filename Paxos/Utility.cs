using System;
using System.IO;

namespace Paxos
{
    class Utility
    {
        private static readonly DateTime _epoch = new DateTime(1970, 1, 1);
        private static object _consoleLock = new object();

        public static double CurrentTimeMilliseconds()
        {
            return (DateTime.Now - _epoch).TotalMilliseconds; 
        }

        public static void WriteDebug(String s)
        {
            WriteDebug(s, false);
        }

        public static void WriteDebug(String s, bool isError)
        {
            lock (_consoleLock)
            {

                //if(!Main.isDebugging)                          
                //    return;                                            

                TextWriter output = isError ? System.Console.Error : System.Console.Out;
                output.WriteLine(s);
            }
        }
    }
}
