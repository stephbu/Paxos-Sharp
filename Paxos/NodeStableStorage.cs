using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Paxos
{

    //For serialization reasons, this class is separate from the Node class  
    [Serializable]
    public class NodeStableStorage<TValue> 
    {  
        public Dictionary<int, int> minPsns;  
        public Dictionary<int, Proposal<TValue>> maxAcceptedProposals;    
    }  
}
