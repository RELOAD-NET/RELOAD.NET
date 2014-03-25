using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN
{
    public enum StunMessageType
    {
        /*
         *   0                 1
             2  3  4 5 6 7 8 9 0 1 2 3 4 5
            +--+--+-+-+-+-+-+-+-+-+-+-+-+-+
            |M |M |M|M|M|C|M|M|M|C|M|M|M|M|
            |11|10|9|8|7|1|6|5|4|0|3|2|1|0|
            +--+--+-+-+-+-+-+-+-+-+-+-+-+-+
            Figure 3: Format of STUN Message Type Field
        */

        /*  Binding Request
            Class = Request: 0b00
            Method = Binding: 0b000000000001
        */
        BindingRequest = 0x0001,

        /*  Binding Indication
            Class = Indication: 0b01
            Method = Binding: 0b000000000001
        */
        BindingIndication = 0x0011,

        /*  Binding Success Response
            Class = Success Response: 0b10
            Method = Binding: 0b000000000001
        */
        BindingSuccessResponse = 0x0101,

        /*  Binding Error Response
            Class = Error Response: 0b11
            Method = Binding: 0b000000000001
        */
        BindingErrorResponse = 0x0111
    }
}
