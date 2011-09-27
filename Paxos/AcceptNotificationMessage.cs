using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Paxos
{
    [Serializable]
    public class AcceptNotificationMessage<TValue> : Message  
    {
        public AcceptNotificationMessage(Proposal<TValue> proposal)
        {
            this.proposal = proposal;
        }
        
        private Proposal<TValue> proposal;

        public Proposal<TValue> Proposal
        {
            get { return proposal; }
            set { proposal = value; }
        }        
    }  
}
