using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZwiftMetricsUI {
    public class ZwiftOutgoingUdpDataPacket {
        public int ConnectionStatusId { get; set;  }
        public int ZwiftUserId { get; set; }
        public long ZwiftWorldUnixTimestamp { get; set; }
        public long Distance { get; set; }
        public int Speed { get; set; }
        public int Cadence { get; set; }
        public int HeartRate { get; set; }
        public int Power { get; set; }
        public long ElevationGain { get; set; }
    }
}
