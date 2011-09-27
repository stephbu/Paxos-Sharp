using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Paxos
{
    [Serializable]
    public class PrepareResponseMessage<TValue> : Message  
    {          
        private int csn;          
        private int minPsn;          
        private Proposal<TValue> proposal;                    

        public PrepareResponseMessage(int csn, int minPsn, Proposal<TValue> proposal)          
        {                  
            this.proposal = proposal;                  
            this.minPsn = minPsn;                  
            this.csn = csn;          
        }                    
        
        public Proposal<TValue> getProposal()          
        {                  
            return proposal;          
        }                    
        
        public int getCsn()          
        {                  
            return csn;          
        }                    
        
        public int getMinPsn()          
        {                  
            return minPsn;          
        }  
    }  
}
