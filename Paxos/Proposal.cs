using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Paxos
{
    [Serializable]    
    public class Proposal<TValue>  
    {          
        private int csn;          
        private int psn;          
        private TValue value;                    
        
        public Proposal(int csn, int psn, TValue value)          
        {                  
            this.csn = csn;                  
            this.psn = psn;                  
            this.value = value;          
        }                    
        
        public int getCsn()          
        {                  
            return csn;          
        }                    
        
        public int getPsn()          
        {                  
            return psn;          
        }                    
        
        public TValue getValue()          
        {                  
            return value;          
        }                    
        
        public String toString()          
        {                  
            return "{" + csn + ", " + psn + ", " + value.ToString() + "}";          
        }  
    }  
}
