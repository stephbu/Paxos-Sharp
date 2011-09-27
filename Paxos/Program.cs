using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Paxos
{
    class Program
    {
        public static readonly bool isDebugging = true; 
         
        private static HashSet<Node<string>> nodes; 
        private static HashSet<NodeLocation> nodeLocations; 
        private static List<String> proposeBuffer; 
        private static bool isRunning; 
         
        // Test case variables 
        private static readonly int testTime = 20; 
        public static bool slotSkippingFlag = false; 
         
        public static void Main(String[] args)
        { 
                writeDebug("Type 'help' for a list of commands"); 
                 
                isRunning = false; 
                nodes = new HashSet<Node<string>>(); 
                proposeBuffer = new List<String>(); 
                nodeLocations = new HashSet<NodeLocation>(); 
                TextReader input = Console.In; 
                
                while(true) 
                { 
                        String[] s = input.ReadLine().Split(new char[]{' '}); 
                        String cmd = s[0]; 
                        String arg = s.Length > 1 ? s[1] : null; 
                         
                        if(cmd.Equals("init", StringComparison.InvariantCultureIgnoreCase)) 
                                createNodes(int.Parse(arg)); 
                         
                        else if(cmd.Equals("start", StringComparison.InvariantCultureIgnoreCase)) 
                        { 
                                if(arg == null) 
                                        startAll(); 
                                else 
                                        start(int.Parse(arg)); 
                        } 
                         
                        else if(cmd.Equals("stop", StringComparison.InvariantCultureIgnoreCase)) 
                        { 
                                if(arg == null) 
                                        stopAll(); 
                                else 
                                        stop(int.Parse(arg)); 
                        } 
                         
                        else if(cmd.Equals("clear", StringComparison.InvariantCultureIgnoreCase)) 
                                clearStableStorage(); 
 
                        else if(cmd.Equals("print", StringComparison.InvariantCultureIgnoreCase)) 
                        { 
                                if(arg == null) 
                                        printAll(); 
                                else 
                                        print(int.Parse(arg)); 
                        } 
                         
                        else if(cmd.Equals("test", StringComparison.InvariantCultureIgnoreCase)) 
                        { 
                                if(arg == null) 
                                        writeDebug("You must specify a test case. Type 'help' for a list of commands and allowed values.", true); 
                                else 
                                { 
                                        if(arg.Equals("leaderFail", StringComparison.InvariantCultureIgnoreCase)) 
                                                testLeaderFail(); 
                                        else if(arg.Equals("cascadingLeaderFail", StringComparison.InvariantCultureIgnoreCase)) 
                                                testCascadingLeaderFail(); 
                                        else if(arg.Equals("simultaneousFail", StringComparison.InvariantCultureIgnoreCase)) 
                                                testSimultaneousFail(); 
                                        else if(arg.Equals("slotSkipping", StringComparison.InvariantCultureIgnoreCase)) 
                                                testSlotSkipping(); 
                                        else 
                                                writeDebug("Unrecognized test case. Type 'help' for a list of commands and allowed values.", true); 
                                } 
                        } 
 
                         
                        else if(cmd.Equals("propose", StringComparison.InvariantCultureIgnoreCase)) 
                                if(isRunning) 
                                        Propose(arg); 
                                else 
                                        bufferPropose(arg); 
                         
                        else if(cmd.Equals("exit", StringComparison.InvariantCultureIgnoreCase)) 
                                exit(); 
                         
                        else if(cmd.Equals("help", StringComparison.InvariantCultureIgnoreCase)) 
                        { 
                                String m = ""; 
                                m += "List of valid commands:"; 
                                m += "\n\tinit <num> - creates <num> nodes"; 
                                m += "\n\tstart [<num>] - starts the node with the number <num>. If no number specified, all will start"; 
                                m += "\n\tstop [<num>] - stops (or 'crashes') the node with the number <num>. If no number specified, all will stop"; 
                                m += "\n\tprint [<num>] - prints the learned values from the node with the number <num>. If no number specified, all will printed"; 
                                m += "\n\tclear - clears all nodes' stable storage"; 
                                m += "\n\tpropose <value> - the current leader will propose <value>"; 
                                m += "\n\ttest <value> - tests against a particular condition. Allowed values are 'leaderFail', 'cascadingLeaderFail', 'simultaneousFail', and 'slotSkipping'."; 
                                m += "\n\texit - stops all nodes and exits"; 
                                m += "\n\thelp - displays this list"; 
                                writeDebug("\n" + m + "\n"); 
                        } 
                         
                        else 
                                writeDebug("Unrecognized Command. Type 'help' for a list of commands", true); 
                } 
        } 
         
        private static void Propose(String s) 
        { 
                writeDebug("Proposing: " + s); 
                foreach(Node<string> node in nodes) 
                        if(node.IsLeader()) 
                        { 
                                node.Propose(s); 
                                break; 
                        } 
        } 
         
        private static void bufferPropose(String s) 
        { 
                writeDebug("Buffering Proposal: " + s); 
                proposeBuffer.Add(s); 
        } 
         
        private static void startAll() 
        { 
                writeDebug("Starting all nodes..."); 
 
                foreach(var node in nodes) 
                        node.Start();

                while (proposeBuffer.Count > 0)
                { 
                    string proposal = proposeBuffer[0];
                    Propose(proposal);
                    proposeBuffer.RemoveAt(0);
                }
                         
                isRunning = true; 
                 
                writeDebug("All nodes started"); 
        } 
         
        private static void start(int n) 
        { 
                writeDebug("Starting node " + n); 
                foreach(var node in nodes) 
                        if(node.GetLocationData().getNum() == n) 
                        { 
                                node.Start(); 
                                break; 
                        } 
        } 
         
        private static void stopAll() 
        { 
                writeDebug("Stopping all nodes..."); 
 
                foreach(var node in nodes) 
                        node.Stop(); 
                nodes.Clear(); 
                nodeLocations.Clear(); 
                isRunning = false; 
 
                writeDebug("All nodes stopped"); 
        } 
         
        private static void stop(int n) 
        { 
                writeDebug("Stopping node " + n); 
                foreach(var node in nodes) 
                        if(node.GetLocationData().getNum() == n) 
                        { 
                                node.Stop(); 
                                break; 
                        } 
        } 
         
        private static void createNodes(int n) 
        { 
                stopAll(); 
                 
                for(int i = 0; i < n; i++) 
                { 
                        var node = new Node<string>(i); 
                        if(i == 0) // make 0 leader 
                                node.BecomeLeader(); 
                        nodes.Add(node); 
                        nodeLocations.Add(node.GetLocationData()); 
                } 
                 
                // give node list to all nodes (statically) 
                foreach(var node in nodes) 
                        node.setNodeList(nodeLocations); 
                 
                writeDebug(n + " nodes created"); 
        } 
         
        private static void clearStableStorage() 
        { 
                foreach(var node in nodes) 
                        node.ClearStableStorage(); 
                writeDebug("Stable Storage Cleared"); 
        } 
         
        private static void printAll() 
        { 
                foreach(var node in nodes) 
                        print(node.GetLocationData().getNum()); 
        } 
         
        private static void print(int n) 
        {
            foreach (var node in nodes)
            {
                if (node.GetLocationData().getNum() == n)
                {

                    IEnumerable<string> values = node.Values();

                    String m = "List of values learned by " + node.GetLocationData().getNum() + ": ";
                    foreach (string vlu in values)
                    {
                        m += "\n\t" + vlu;
                    }

                    writeDebug("\n" + m + "\n");
                    break;
                }
            }
        } 
         
        private static void testLeaderFail() 
        { 
                //new Thread() 
                //{ 
                //        public void run() 
                //        { 
                //                // don't start timer until it's running 
                //                while(!Main.isRunning) 
                //                        yield(); // so the while loop doesn't spin too much 
                //                writeDebug("Leader Fail Test Started"); 
                                 
                //                // fail timer 
                //                long expireTime = System.currentTimeMillis() + testTime; 
                //                bool isRunning = true; 
                //                while(isRunning) 
                //                { 
                //                        if(expireTime < System.currentTimeMillis()) 
                //                        { 
                //                                Main.stop(0); 
                //                                isRunning = false; 
                //                        } 
                //                        yield(); // so the while loop doesn't spin too much 
                //                } 
                //        } 
                //}.start(); 
        } 
         
        private static void testCascadingLeaderFail() 
        { 
                //new Thread() 
                //{ 
                //        public void run() 
                //        { 
                //                // don't start timer until it's running 
                //                while(!Main.isRunning) 
                //                        yield(); // so the while loop doesn't spin too much 
                //                writeDebug("Cascading Leader Fail Test Started"); 
                                 
                //                // fail timer 
                //                for(int i = 0; i < nodes.size(); i++) 
                //                { 
                //                        long expireTime = System.currentTimeMillis() + testTime; 
                //                        bool isRunning = true; 
                //                        while(isRunning) 
                //                        { 
                //                                if(expireTime < System.currentTimeMillis()) 
                //                                { 
                //                                        Main.stop(i); 
                //                                        isRunning = false; 
                //                                } 
                //                                yield(); // so the while loop doesn't spin too much 
                //                        } 
                //                } 
                //                writeDebug("Cascading Leader Fail Test Complete"); 
                //        } 
                //}.start(); 
        } 
         
        private static void testSimultaneousFail() 
        { 
                //new Thread() 
                //{ 
                //        public void run() 
                //        { 
                //                // don't start timer until it's running 
                //                while(!Main.isRunning) 
                //                        yield(); // so the while loop doesn't spin too much 
                //                writeDebug("Simultaneous Fail Test Started"); 
                                 
                //                // fail timer 
                //                long expireTime = System.currentTimeMillis() + testTime; 
                //                bool isRunning = true; 
                //                while(isRunning) 
                //                { 
                //                        if(expireTime < System.currentTimeMillis()) 
                //                        { 
                //                                Main.stop(0); 
                //                                Main.stop(1); 
                //                                isRunning = false; 
                //                        } 
                //                        yield(); // so the while loop doesn't spin too much 
                //                } 
                //        } 
                //}.start(); 
        } 
         
        private static void testSlotSkipping() 
        { 
                // implementation is in Node.propose() 
                slotSkippingFlag = true; 
        } 
         
        private static void exit() 
        { 
                stopAll(); 
                writeDebug("Exiting"); 
                Environment.Exit(0); 
        } 

        private static void writeDebug(String s) 
        { 
                writeDebug(s, false); 
        } 
         
        private static void writeDebug(String s, bool isError) 
        { 
                if(!isDebugging) 
                        return; 
                         
                TextWriter output = isError ? System.Console.Error : System.Console.Out; 
                output.Write("*** "); 
                output.Write(s); 
                output.WriteLine(" ***"); 
        } 

    }
}
