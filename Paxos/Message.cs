using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Paxos
{
    [Serializable]
    public abstract class Message  
    {
        protected NodeLocation _sender;

        public NodeLocation Sender
        {
            get { return this._sender; }
            set { this._sender = value; }
        }

        protected NodeLocation _receiver;
        public NodeLocation Receiver
        {
            get { return this._receiver; }
            set { this._receiver = value; }
        }
        
    }  
}
