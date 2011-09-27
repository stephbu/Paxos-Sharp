using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Paxos
{
    [Serializable]
    public class NewLeaderNotificationMessage : Message
    {
        private int num;
        public NewLeaderNotificationMessage(int num)
        {
            this.num = num;
        }
        public int getNum()
        {
            return num;
        }
    }
}
