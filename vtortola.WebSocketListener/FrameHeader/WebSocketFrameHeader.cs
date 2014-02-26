﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace vtortola.WebSockets
{
    public sealed class WebSocketFrameHeader
    {
        public UInt64 ContentLength { get; private set; }
        public Boolean IsComplete { get { return Flags.FIN; } }
        public Int32 HeaderLength { get; private set; }
        public WebSocketFrameHeaderFlags Flags { get; private set; }
        public Byte[] Raw { get; set; }
        public UInt64 RemainingBytes { get; private set; }

        Byte[] _key;

        UInt64 cursor = 0;
        public Byte DecodeByte(Byte b)
        {
            RemainingBytes--;
            return (Byte)(b ^ _key[cursor++ % 4]);
        }

        public WebSocketFrameHeader()
        {
            _key = new Byte[4];
        }

        public static Boolean TryParseFrameLength(Byte[] frameStart, Int32 offset, Int32 count, out UInt64 frameLength)
        {
            Int32 header;
            UInt64 content;
            if (TryParseLengths(frameStart, offset, count, out header, out content))
            {
                frameLength = (UInt64)header + content;
                return true;
            }

            frameLength = 0;
            return false;
        }
        
        public static Boolean TryParseLengths(Byte[] frameStart, Int32 offset, Int32 count, out Int32 headerLength, out UInt64 contentLength)
        {
            contentLength = 0;
            headerLength = -1;

            if (frameStart == null || frameStart.Length < 6 || count < 6 || frameStart.Length - (offset + count) < 0)
                return false;

            Int32 value = frameStart[offset];
            value = value > 128 ? value - 128 : value;

            contentLength = (UInt64)(frameStart[offset + 1] - 128);
            headerLength = 0;

            if (contentLength <= 125)
            {
                headerLength = 6;
            }
            else if (contentLength == 126)
            {
                if (frameStart.Length < 8 || count < 8)
                    return false;

                frameStart.ReversePortion(offset + 2, 2);
                contentLength = BitConverter.ToUInt16(frameStart, 2);
                frameStart.ReversePortion(offset + 2, 2);

                headerLength = 8;
            }
            else if (contentLength == 127)
            {
                if (frameStart.Length < 14 || count < 14)
                    return false;

                frameStart.ReversePortion(offset + 2, 8);
                contentLength = (UInt64)BitConverter.ToUInt64(frameStart, 2);
                frameStart.ReversePortion(offset + 2, 8);

                headerLength = 14;
            }
            else
            {
                throw new WebSocketException("Protocol error [contentLength:" + contentLength + "]");
            }

            return true;
        }

        public static Boolean TryParse(Byte[] frameStart, Int32 offset, Int32 count, out WebSocketFrameHeader header)
        {
            header = null;

            if (frameStart == null || frameStart.Length < 6 || count < 6 || frameStart.Length - (offset + count) < 0)
                return false;

            Boolean isComplete = frameStart[offset] >= 128;

            Int32 value = frameStart[offset];
            value = value > 128 ? value - 128 : value;

            if (!Enum.IsDefined(typeof(WebSocketFrameOption), value))
                Debugger.Break();

            WebSocketFrameOption option = (WebSocketFrameOption)value;

            UInt64 contentLength;
            Int32 headerLength;

            if (!TryParseLengths(frameStart, offset, count, out headerLength, out contentLength))
                return false;
            
            header = new WebSocketFrameHeader()
            {
                ContentLength = contentLength,
                HeaderLength = headerLength,
                Flags = new WebSocketFrameHeaderFlags(frameStart,offset),
                RemainingBytes = contentLength
            };

            headerLength -= 4;
            for (int i = 0; i < 4; i++)
                header._key[i] = frameStart[offset + i + headerLength];

            return true;
        }

        public static WebSocketFrameHeader Create(Int32 count, Boolean isComplete, Boolean headerSent, WebSocketFrameOption option)
        {
            // Complete == 1
            // Partial : first 0 + code
            // Rest : first 0 + continuation
            // final : fist 1 + continuation

            List<Byte> arrayMaker = new List<Byte>();

            if(isComplete && !headerSent)
                arrayMaker.Add((Byte)(128 + (Int32)option));
            else if(isComplete && headerSent)
                arrayMaker.Add((Byte)(128 + (Int32)WebSocketFrameOption.Continuation));
            else if(!isComplete && !headerSent)
                arrayMaker.Add((Byte)(0 + (Int32)option));
            else if(!isComplete && headerSent)
                arrayMaker.Add((Byte)(0 + (Int32)WebSocketFrameOption.Continuation));

            Int32 headerLength = 2;

            if (count <= 125)
            {
                arrayMaker.Add((Byte)(count));
                headerLength = 2;
            }
            else if (count < UInt16.MaxValue)
            {
                arrayMaker.Add(126);
                Byte[] i16 = BitConverter.GetBytes((UInt16)count).Reverse().ToArray();
                arrayMaker.AddRange(i16);
                headerLength = 4;
            }
            else if ((UInt64)count < UInt64.MaxValue)
            {
                arrayMaker.Add(127);
                arrayMaker.AddRange(BitConverter.GetBytes((UInt64)count).Reverse().ToArray());
                headerLength = 10;
            }

            return new WebSocketFrameHeader()
            {
                HeaderLength = headerLength,
                ContentLength = (UInt64)count,
                Flags = new WebSocketFrameHeaderFlags(isComplete, option),
                Raw = arrayMaker.ToArray(),
                RemainingBytes = (UInt64)count
            };
        }
    }
}
