using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Paxos
{

    // For serialization reasons, this class is separate from the Node class  
    [Serializable]
    public class NodeLocation 
    {  
        private String host;  
        private int port;  
        private int num;  
        public bool isLeader {get;set;}    
        
        public NodeLocation(String host, int port, int num)  
        {  
            this.host = host;  
            this.port = port;  
            this.num = num;  
            this.isLeader = false;  
        }    
        
        public void becomeLeader()  
        {  
            isLeader = true;  
        }    
        
        public void becomeNonLeader()  
        {  
            isLeader = false;  
        }    
        
        
        public String getHost()  
        {  
            return host;  
        }    
        
        public int getPort()  
        {  
            return port;  
        }    
        
        public int getNum()  
        {  
            return num;  
        }    
        
        public override String ToString()  
        {  
            return num.ToString();  
        }  
    }  
}
