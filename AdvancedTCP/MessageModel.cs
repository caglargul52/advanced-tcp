using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdvancedTCP
{
    internal enum Type
    {
        ClientLogon,
        AcceptLogon,
        Blocked,
        Message
    }
    internal class MessageModel
    {
        public Type Type { get; set; }
        public string Message { get; set; }
        public string PcName { get; set; }
    }
}
