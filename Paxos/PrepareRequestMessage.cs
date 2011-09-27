using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Paxos
{
    [Serializable]
    public class PrepareRequestMessage : Message  
    {          
        private int csn;          
        private int psn;                    
        public PrepareRequestMessage(int csn, int psn)          
        {                  
            this.csn = csn;                  
            this.psn = psn;          
        }                    
        
        public int getPsn()          
        {                  
            return psn;          
        }                    
        public int getCsn()          
        {                  
            return csn;          
        }  
    }
}
