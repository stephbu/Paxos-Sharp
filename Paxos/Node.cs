using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;

namespace Paxos
{
    class Node<TValue>
    {
        static BinaryFormatter formatter = new BinaryFormatter();


        // timeout per connection
        private static readonly int SOCKET_TIMEOUT = 1000;
        private static readonly double HEARTBEAT_TIMEOUT = 5000;

        // if a proposer doesn't hear back from a majority of acceptors, try again
        private static readonly double PROPOSE_TIMEOUT = 10000;

        // this is a range so that all heartbeats usually won't happen simultaneously          
        private static Random rand = new Random();
        private static readonly int heartbeatDelayMin = 1000;
        private static readonly int heartbeatDelayMax = 2000;

        private static int nextPort = 8100;


        // Instance-wide synchronization object
        private object _nodeLock = new object();

        // Node Data          
        private HashSet<NodeLocation> _nodes;
        private NodeLocation _location;
        private NodeListener _listener;
        private Heartbeat _heartbeat;
        private Dictionary<int, HeartbeatObserver> _heartbeatObservers;

        private bool isRunning;

        // Proposer Variables          
        private int currentCsn;
        private int psn;
        private Dictionary<int, int> numAcceptRequests;
        private Dictionary<int, Proposal<TValue>> proposals;
        private Dictionary<int, NodeReProposer<TValue>> reProposers;

        // Acceptor Variables          
        private Dictionary<int, int> minPsns;
        private Dictionary<int, Proposal<TValue>> maxAcceptedProposals;

        // Learner Variables
        private Dictionary<int, int> _numAcceptNotifications;
        private Dictionary<int, TValue> _chosenValues;

        public Node(String host, int port, int psnSeed)
        {
            this.psn = psnSeed;

            // when used properly, this ensures unique PSNs.                  
            this.currentCsn = 0;

            this._location = new NodeLocation(host, port, psnSeed);
            this.numAcceptRequests = new Dictionary<int, int>();
            this._numAcceptNotifications = new Dictionary<int, int>();

            this.proposals = new Dictionary<int, Proposal<TValue>>();
            this.reProposers = new Dictionary<int, NodeReProposer<TValue>>();

            this.minPsns = new Dictionary<int, int>();
            this.maxAcceptedProposals = new Dictionary<int, Proposal<TValue>>();

            this._chosenValues = new Dictionary<int, TValue>();
            this._nodes = new HashSet<NodeLocation>();

            this.isRunning = false;
        }

        public Node(int psnSeed)
            : this("localhost", nextPort++, psnSeed)
        {
        }

        public void setNodeList(HashSet<NodeLocation> s)
        {
            this._nodes = s;
        }

        /// <summary>
        /// Transitions current node into leader role
        /// </summary>
        public void BecomeLeader()
        {
            Utility.WriteDebug("I'm Leader");

            _location.becomeLeader();
            foreach (NodeLocation node in this._nodes)
            {
                node.becomeNonLeader();
            }

            // fill skipped slots                  
            int n = 0;
            int m = 0;
            List<int> proposeBuffer = new List<int>();

            // announce voiding for any proposed but unaccepted values
            while (m < _chosenValues.Count)
            {
                if (_chosenValues.ContainsKey(n))
                {
                    m++;
                }
                else
                {
                    proposeBuffer.Add(n);
                    n++;
                }

                foreach (int i in proposeBuffer)
                {
                    Propose(default(TValue), proposeBuffer[i]);
                }
            }
        }

        private void ElectNewLeader()
        {
            if (!isRunning)
                return;

            int newNum = -1;

            // find old leader and calculate new leader num                  
            foreach (NodeLocation node in _nodes)
            {
                if (node.isLeader)
                {
                    newNum = (node.getNum() + 1) % _nodes.Count();
                    break;
                }
            }

            NewLeaderNotificationMessage newLeaderNotification = new NewLeaderNotificationMessage(newNum);
            Broadcast(newLeaderNotification);
            Utility.WriteDebug("Electing new leader: " + newNum);
        }


        public ICollection<TValue> Values()
        {
            lock (_nodeLock)
            {
                return _chosenValues.Values;
            }
        }


        public void Start()
        {
            lock (_nodeLock)
            {
                RecoverStableStorage();
                _listener = new NodeListener(this.Deliver, this._location);
                _listener.start();

                _heartbeat = new Heartbeat(this.Broadcast);
                _heartbeat.start();
                _heartbeatObservers = new Dictionary<int, HeartbeatObserver>();

                foreach (NodeLocation node in this._nodes)
                {
                    if (node == _location)
                    {
                        continue;
                    }
                    HeartbeatObserver nodeHeartbeatObserver = new HeartbeatObserver(this.ElectNewLeader, node);
                    nodeHeartbeatObserver.start();
                    _heartbeatObservers[node.getNum()] = nodeHeartbeatObserver;
                }
                isRunning = true;
                Utility.WriteDebug("Started");
            }
        }


        public void Stop()
        {
            lock (_nodeLock)
            {
                if (_listener != null)
                    _listener.Kill();

                _listener = null;

                if (_heartbeat != null)
                    _heartbeat.Kill();
                _heartbeat = null;

                if (_heartbeatObservers != null)
                {
                    foreach (HeartbeatObserver heartbeatListener in _heartbeatObservers.Values)
                    {
                        heartbeatListener.Kill();
                    }
                    _heartbeatObservers.Clear();
                }
                _heartbeatObservers = null;

                isRunning = false;
                Utility.WriteDebug("Stopped");
            }
        }

        public void Propose(TValue value)
        {
            // testing purposes                  
            //if(Main.slotSkippingFlag)                          
            //    currentCsn++;

            Propose(value, currentCsn++);
        }

        public void Propose(TValue value, int csn)
        {
            if (!isRunning)
                return;

            if (reProposers.ContainsKey(csn))
            {
                reProposers[csn].Kill();
                reProposers.Remove(csn);
            }

            numAcceptRequests[csn] = 0;
            Proposal<TValue> proposal = new Proposal<TValue>(csn, psn, value);
            reProposers.Add(csn, new NodeReProposer<TValue>(this.Propose, proposal));
            proposals[csn] = proposal;
            Broadcast(new PrepareRequestMessage(csn, psn));
            psn += _nodes.Count;
        }

        private void Broadcast(Message m)
        {
            if (!isRunning)
                return;

            m.Sender = _location;

            foreach (NodeLocation node in _nodes)
            {
                // immediately deliver to self                          
                if (this._location == node)
                    Deliver(m);
                // send message                          
                else
                    Unicast(node, m);
            }
        }

        private void Unicast(NodeLocation destinationNode, Message m)
        {
            if (!isRunning)
                return;

            m.Receiver = destinationNode;
            try
            {
                // Find the 1st IPV4 address for the destination node
                IPAddress targetIP = (from address in Dns.GetHostEntry(destinationNode.getHost()).AddressList where address.AddressFamily == AddressFamily.InterNetwork select address).First();

                IPEndPoint ep = new IPEndPoint(targetIP, destinationNode.getPort());
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.SendTimeout = Node<TValue>.SOCKET_TIMEOUT;
                    socket.Connect(ep);
                    using (NetworkStream outputStream = new NetworkStream(socket, false))
                    {
                        formatter.Serialize(outputStream, m);
                    }
                }
            }
            catch (SocketException e)
            {
                Utility.WriteDebug("Detected crash from " + destinationNode.getNum(), true);
                // if was leader, elect a new one and try THIS retransmission again, else, do nothing                          
                if (destinationNode.isLeader && !(m is NewLeaderNotificationMessage))
                {
                    ElectNewLeader();
                }
            }
            catch (IOException e)
            {
                Utility.WriteDebug("IOException while trying to send message!", true);
            }
        }

        /// <summary>
        /// Serialized Node Message Pump
        /// </summary>
        /// <param name="m"></param>
        private void Deliver(Message m)
        {

            lock (_nodeLock)
            {

                if (!isRunning)
                    return;

                if (m is HeartbeatMessage)
                {
                    // drop own heartbeats
                    if (m.Sender == this._location)
                        return;

                    _heartbeatObservers[m.Sender.getNum()].ResetTimeout();
                }
                else if (m is PrepareRequestMessage)
                // Acceptor                  
                {
                    PrepareRequestMessage prepareRequest = (PrepareRequestMessage)m;
                    int csn = prepareRequest.getCsn();
                    int psn = prepareRequest.getPsn();
                    if (currentCsn <= csn)
                        currentCsn = csn + 1;
                    Utility.WriteDebug("Got Prepare Request from " + prepareRequest.Sender + ": (" + csn + ", " + psn + ")");

                    // new minPsn                          
                    if (!minPsns.ContainsKey(csn) || minPsns[csn] < psn)
                        minPsns[csn] = psn;
                    // respond                          
                    Proposal<TValue> responseProposalAccepted = maxAcceptedProposals.ContainsKey(csn) ? maxAcceptedProposals[csn] : null;
                    PrepareResponseMessage<TValue> prepareResponse = new PrepareResponseMessage<TValue>(csn, minPsns[csn], responseProposalAccepted);
                    prepareResponse.Sender = _location;
                    Unicast(prepareRequest.Sender, prepareResponse);
                    UpdateStableStorage();
                }
                else if (m is PrepareResponseMessage<TValue>)
                // Proposer                  
                {
                    PrepareResponseMessage<TValue> prepareResponse = (PrepareResponseMessage<TValue>)m;
                    Proposal<TValue> acceptedProposal = prepareResponse.getProposal();
                    int csn = prepareResponse.getCsn();
                    int minPsn = prepareResponse.getMinPsn();
                    Proposal<TValue> proposal = proposals[csn];
                    if (currentCsn <= csn)
                        currentCsn = csn + 1;
                    Utility.WriteDebug("Got Prepare Response from " + prepareResponse.Sender + ": " + csn + ", " + minPsn + ", " + (acceptedProposal == null ? "None" : acceptedProposal.toString()));
                    if (!numAcceptRequests.ContainsKey(csn))
                        // ignore if already heard from a majority                                  
                        return;
                    // if acceptors already accepted something higher, use it instead                          
                    if (acceptedProposal != null && acceptedProposal.getPsn() > proposal.getPsn())
                        proposal = acceptedProposal;
                    // if acceptors already promised something higher, use higher psn                          
                    if (minPsn > proposal.getPsn())
                    {
                        while (psn < prepareResponse.getMinPsn())
                            psn += _nodes.Count;
                        Propose(proposal.getValue(), proposal.getCsn());
                        return;
                    }
                    int n = numAcceptRequests[csn];
                    n++;

                    if (n > (_nodes.Count / 2))
                    // has heard from majority?                         
                    {
                        numAcceptRequests.Remove(csn);
                        if (reProposers.ContainsKey(csn))
                        {
                            reProposers[csn].Kill();
                            reProposers.Remove(csn);
                        }
                        AcceptRequestMessage<TValue> acceptRequest = new AcceptRequestMessage<TValue>(proposal);
                        Broadcast(acceptRequest);
                    }
                    else
                        numAcceptRequests[csn] = n;
                }
                else if (m is AcceptRequestMessage<TValue>) // Acceptor                  
                {
                    AcceptRequestMessage<TValue> acceptRequest = (AcceptRequestMessage<TValue>)m;
                    Proposal<TValue> requestedProposal = acceptRequest.getProposal();
                    int csn = requestedProposal.getCsn();
                    int psn = requestedProposal.getPsn();
                    if (currentCsn <= csn)
                        currentCsn = csn + 1;
                    Utility.WriteDebug("Got Accept Request from " + acceptRequest.Sender + ": " + requestedProposal.toString());
                    if (psn < minPsns[csn])
                        return; // ignore                                                    
                    // "accept" the proposal                          

                    if (psn > minPsns[csn])
                        minPsns[csn] = psn;

                    maxAcceptedProposals[csn] = requestedProposal;
                    Utility.WriteDebug("Accepted: " + requestedProposal.toString());

                    // Notify Learners                          
                    AcceptNotificationMessage<TValue> acceptNotification = new AcceptNotificationMessage<TValue>(requestedProposal);
                    Broadcast(acceptNotification);
                    UpdateStableStorage();
                }
                else if (m is AcceptNotificationMessage<TValue>)
                // Learner                  
                {
                    AcceptNotificationMessage<TValue> acceptNotification = (AcceptNotificationMessage<TValue>) m;
                    Proposal<TValue> acceptedProposal = acceptNotification.Proposal;
                    int csn = acceptedProposal.getCsn();
                    if (currentCsn <= csn)
                        currentCsn = csn + 1;
                    Utility.WriteDebug("Got Accept Notification from " + acceptNotification.Sender + ": " + (acceptedProposal == null ? "None" : acceptedProposal.toString()));

                    // ignore if already learned                          
                    if (_chosenValues.ContainsKey(csn))
                        return;

                    if (!_numAcceptNotifications.ContainsKey(csn))
                        _numAcceptNotifications[csn] = 0;
                    int n = _numAcceptNotifications[csn];
                    n++;
                    if (n > (_nodes.Count / 2)) // has heard from majority?                          
                    {
                        _numAcceptNotifications.Remove(csn);
                        _chosenValues[csn] = acceptedProposal.getValue();
                        Utility.WriteDebug("Learned: " + acceptedProposal.getCsn() + ", " + acceptedProposal.getValue());
                        UpdateStableStorage();
                    }
                    else
                        _numAcceptNotifications[csn] = n;
                }
                else if (m is NewLeaderNotificationMessage) // Leader Election                  
                {
                    NewLeaderNotificationMessage newLeaderNotification = (NewLeaderNotificationMessage)m;
                    int newNum = newLeaderNotification.getNum();
                    Utility.WriteDebug("Got New Leader Notification from " + newLeaderNotification.Sender + ": " + newNum);
                    // am i new leader?                          
                    if (_location.getNum() == newNum)
                        BecomeLeader();
                    // find new leader, make others non-leaders                          
                    foreach (NodeLocation node in _nodes)
                        if (node.getNum() == newNum)
                            node.becomeLeader();
                        else
                            node.becomeNonLeader();
                }
                else
                    Utility.WriteDebug("Unknown Message recieved", true);
            }
        }

        public NodeLocation GetLocationData()
        {
            return _location;
        }

        public bool IsLeader()
        {
            return _location.isLeader;
        }

        public override String ToString()
        {
            return _location.ToString();
        }

        private void RecoverStableStorage()
        {
            lock (_nodeLock)
            {
                Stream input = null;
                NodeStableStorage<TValue> stableStorage;
                try
                {
                    string path = "stableStorage/" + ToString() + ".bak";
                    if (!File.Exists(path))
                    {
                        Utility.WriteDebug("No stable storage found");
                        return;
                    }

                    using (input = File.OpenRead(path))
                    {
                        stableStorage = (NodeStableStorage<TValue>)formatter.Deserialize(input);
                    }

                    minPsns = stableStorage.minPsns;
                    maxAcceptedProposals = stableStorage.maxAcceptedProposals;
                }
                catch (IOException e)
                {
                    Utility.WriteDebug("Problem reading from stable storage!", true);
                }
                catch (Exception e)
                {
                    Utility.WriteDebug("ClassNotFoundException while reading from stable storage!", true);
                }
            }
        }

        private void UpdateStableStorage()
        {
            lock (_nodeLock)
            {
                NodeStableStorage<TValue> stableStorage = new NodeStableStorage<TValue>();
                stableStorage.minPsns = minPsns;
                stableStorage.maxAcceptedProposals = maxAcceptedProposals;
                Stream outputStream = null;

                try
                {
                    string storageDirectory = "stableStorage";
                    if (!Directory.Exists(storageDirectory))
                        Directory.CreateDirectory(storageDirectory);
                    // TODO: Handle directory creation errors       
                    // TODO: Use path combine to create new filestream output.
                    // TODO: Move storage directory to a parameter on Node
                    // TODO: Change to use storage directory from Node

                    string path = "stableStorage/" + ToString() + ".bak";
                    using (outputStream = File.OpenWrite(path))
                    {
                        formatter.Serialize(outputStream, stableStorage);
                    }
                }
                catch (IOException e)
                {
                    Utility.WriteDebug(e.ToString(), true);
                }
            }
        }

        public void ClearStableStorage()
        {
            lock (_nodeLock)
            {
                string path = "stableStorage/" + ToString() + ".bak";
                if (File.Exists(path))
                {
                    // TODO: Error handling failure cases
                    File.Delete(path);
                }
            }
        }

        private class Heartbeat : ThreadHost
        {
            private bool _isRunning;
            private double _lastHeartbeatTimestamp;
            private Action<Message> _broadcast;

            public Heartbeat(Action<Message> broadcast)
            {
                this._isRunning = true;
                this._lastHeartbeatTimestamp = Utility.CurrentTimeMilliseconds();
                this._broadcast = broadcast;
            }


            public override void Run()
            {
                Random _rand = new Random();

                int delay = rand.Next(heartbeatDelayMax - heartbeatDelayMin) + heartbeatDelayMin;
                
                while (_isRunning)
                {
                    if (delay < Utility.CurrentTimeMilliseconds() - _lastHeartbeatTimestamp)
                    {
                        this._broadcast(new HeartbeatMessage());
                        this._lastHeartbeatTimestamp = Utility.CurrentTimeMilliseconds();
                        delay = rand.Next(heartbeatDelayMax - heartbeatDelayMin) + heartbeatDelayMin;
                    }
                    yield(); // so the while loop doesn't spin too much                          
                }
            }


            public void Kill()
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// Watches for Heartbeat failure
        /// </summary>
        /// <remarks>Can force leadership elections if the failed node is leader.</remarks>
        private class HeartbeatObserver : ThreadHost
        {
            private bool _isRunning;
            private double _lastHeartbeatTimestamp;
            private NodeLocation _nodeLocationData;
            private Action _electNewLeader;

            public HeartbeatObserver(Action electNewLeader, NodeLocation nodeLocationData)
            {
                this._isRunning = true;
                this._lastHeartbeatTimestamp = Utility.CurrentTimeMilliseconds();
                this._nodeLocationData = nodeLocationData;
                this._electNewLeader = electNewLeader;
            }

            public void ResetTimeout()
            {
                _lastHeartbeatTimestamp = Utility.CurrentTimeMilliseconds();
            }

            public override void Run()
            {
                while (_isRunning)
                {
                    if (HEARTBEAT_TIMEOUT < Utility.CurrentTimeMilliseconds() - _lastHeartbeatTimestamp)
                    {
                        Utility.WriteDebug("Detected crash from " + _nodeLocationData.getNum() + " (heartbeat)", true);
                        // if was leader, elect a new one                                          
                        if (_nodeLocationData.isLeader)
                            this._electNewLeader();
                        _lastHeartbeatTimestamp = Utility.CurrentTimeMilliseconds();
                    }
                    yield();

                    // so the while loop doesn't spin too much                          
                }
            }

            public void Kill()
            {
                _isRunning = false;
            }
        }

        private class NodeListener : ThreadHost
        {
            private bool _isRunning;
            private Socket _serverSocket;
            private Action<Message> _deliver;

            public NodeListener(Action<Message> deliver, NodeLocation location)
            {
                this._deliver = deliver;
                _isRunning = true;
                try
                {
                    _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IPAddress hostIP = IPAddress.Any;
                    IPEndPoint ep = new IPEndPoint(hostIP, location.getPort());
                    _serverSocket.Bind(ep);
                    // TODO: Set a sensible backlog queue length
                    _serverSocket.Listen(1000);
                }
                catch (IOException e)
                {
                    Utility.WriteDebug("IOException while trying to listen!", true);
                }
            }

            public override void Run()
            {
                Socket socket = null;
                NetworkStream input = null;
                using (_serverSocket)
                {
                    while (_isRunning)
                    {
                        try
                        {
                            using (socket = _serverSocket.Accept())
                            {
                                input = new NetworkStream(socket, false);
                                this._deliver((Message)formatter.Deserialize(input));
                            }
                        }
                        catch (IOException e)
                        {
                            Utility.WriteDebug("IOException while trying to accept connection!", true);
                        }
                        catch (Exception e)
                        {
                            Utility.WriteDebug(e.ToString(), true);
                        }
                    }
                }

            }

            public void Kill()
            {
                _isRunning = false;
            }
        }

        private class NodeReProposer<TValue> : ThreadHost
        {
            private bool isRunning;
            private double expireTime;
            private Proposal<TValue> proposal;
            private Action<TValue, int> _propose;

            public NodeReProposer(Action<TValue, int> propose, Proposal<TValue> proposal)
            {
                this.isRunning = true;
                this._propose = propose;
                this.proposal = proposal;
            }

            public override void Run()
            {
                expireTime = Utility.CurrentTimeMilliseconds() + PROPOSE_TIMEOUT;
                while (isRunning)
                {
                    if (expireTime < Utility.CurrentTimeMilliseconds())
                    {
                        this._propose(proposal.getValue(), proposal.getCsn());
                        this.Kill();
                    }
                    yield();
                    // so the while loop doesn't spin too much                          
                }
            }

            public void Kill()
            {
                isRunning = false;
            }
        }
    }
}
