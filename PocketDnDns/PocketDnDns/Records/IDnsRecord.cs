using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace PocketDnDns.Records
{
    public interface IDnsRecord
    {
        RecordHeader DnsHeader { get; }
        string Answer { get; }
        //short DataLength { get; }
        string ErrorMsg { get; }
        //string Name { get; }
        //NsClass NsClass { get; }
        //NsType NsType { get; }
        //int TimeToLive { get; }
        //void ParseRecordHeader(ref MemoryStream ms);
        void ParseRecord(ref MemoryStream ms);
        string ToString();
    }
}