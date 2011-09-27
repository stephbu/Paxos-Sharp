using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Paxos
{
    [Serializable]
    public class AcceptRequestMessage<TValue> : Message
    {
        private Proposal<TValue> proposal;
        
        public AcceptRequestMessage(Proposal<TValue> proposal)
        {
            this.proposal = proposal;
        }

        public Proposal<TValue> getProposal()
        {
            return proposal;
        }
    }
}
